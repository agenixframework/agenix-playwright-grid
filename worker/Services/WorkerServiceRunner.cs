using System.Text.Json;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Infrastructure.Adapters;

namespace WorkerService.Services;

public sealed class WorkerServiceRunner
{
    public async Task RunAsync(string[] args)
    {
        // Load options
        var options = WorkerOptions.FromEnvironment();

        // Startup diagnostics: effective worker configuration (secrets redacted)
        try
        {
            // Redact secrets
            string redact(string? s)
            {
                return string.IsNullOrEmpty(s) ? "" : "***";
            }

            var diag = new
            {
                HubUrl = options.HubUrl,
                RedisUrl = options.RedisUrl,
                NodeId = options.NodeId,
                NodeSecret = redact(Environment.GetEnvironmentVariable("NODE_SECRET")),
                PublicWs =
                    new
                    {
                        Host = options.PublicWsHost ?? "(unset)",
                        Port = options.PublicWsPort ?? "(unset)",
                        Scheme = options.PublicWsScheme
                    },
                Sidecar =
                    new
                    {
                        Script = options.SidecarScript,
                        ReadyTimeoutSeconds = options.SidecarReadyTimeoutSeconds
                    },
                Labels = options.Labels.ToDictionary(k => k.Key, v => v.Value),
                PoolConfig = options.PoolConfig.ToDictionary(k => k.Key, v => v.Value)
            };
            var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("[worker] Startup diagnostics (effective config):\n" + json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[worker] Startup diagnostics failed: {ex.Message}");
        }

        // Startup info: log configured Playwright version and installed NPM version
        try
        {
            var envPkg = Environment.GetEnvironmentVariable("PLAYWRIGHT_PACKAGE") ?? "playwright";
            var envVer = Environment.GetEnvironmentVariable("PLAYWRIGHT_VERSION") ?? "(not set)";
            var baseDir = AppContext.BaseDirectory;
            var pkgJsonPath = Path.Combine(baseDir, "node_modules", envPkg, "package.json");
            var installedVer = "(unknown)";
            if (File.Exists(pkgJsonPath))
            {
                var jsonText = await File.ReadAllTextAsync(pkgJsonPath);
                using var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.TryGetProperty("version", out var v))
                {
                    installedVer = v.GetString() ?? installedVer;
                }
            }

            Console.WriteLine(
                $"[worker] Playwright startup: package={envPkg}, envVersion={envVer}, installedVersion={installedVer}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[worker] Playwright startup: failed to detect installed version: {ex.Message}");
        }

        // Compose dependencies
        var mux = await ConnectionMultiplexer.ConnectAsync(options.RedisUrl);
        var db = mux.GetDatabase();
        IMetricsPort metrics = new PrometheusMetrics();
        IHubClient hub = new HubHttpClient();
        var sidecarLauncher = new SidecarLauncher(options);

        // Services
        var pool = new PoolManager(options, db, metrics, sidecarLauncher);
        var heartbeat = new HeartbeatService(options, db);
        var registrar = new NodeRegistrar(hub, options);
        var webHost = new WebServerHost(options, metrics, pool, db);

        // Cancellation for background loops
        using var cts = new CancellationTokenSource();

        // Start heartbeats early to avoid premature expiry during warmup
        await heartbeat.HeartbeatOnceAsync();
        _ = heartbeat.HeartbeatLoopAsync(cts.Token);

        // Warm pools and start background loops
        await pool.InitializeAsync();
        await registrar.RegisterAsync();
        _ = pool.ReconcileLoopAsync(cts.Token);

        // Run web app (blocks until shutdown)
        await webHost.RunAsync(args, cts);

        // Ensure background loops are signaled to stop
        try { await Task.Delay(100, cts.Token); }
        catch
        {
            // ignored
        }
    }
}
