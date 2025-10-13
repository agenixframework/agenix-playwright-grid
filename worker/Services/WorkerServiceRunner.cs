#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Reflection;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Infrastructure.Adapters;

namespace WorkerService.Services;

public class WorkerServiceRunner(IServiceProvider? serviceProvider)
{
    public WorkerServiceRunner() : this(null) { }

    private readonly ChunkedLogger<WorkerServiceRunner>? _chunkedLogger = serviceProvider?.GetService<ChunkedLogger<WorkerServiceRunner>>();

    // Instance fields for services that need to be accessible for re-registration
    private NodeRegistrar? _registrar;
    private PoolManager? _pool;
    private IMetricsPort? _metrics;
    private WorkerOptions? _options;
    private Microsoft.Extensions.Logging.ILogger? _logger;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    public async Task RunAsync(string[] args)
    {
        using var startupOp = _chunkedLogger?.BeginOperation("WorkerStartup");
        _chunkedLogger?.LogMilestone(EventCodes.System.BootstrapStarted, "Worker bootstrap started");

        // Configure Serilog from appsettings.json
        var configuration = serviceProvider?.GetService<Microsoft.Extensions.Configuration.IConfiguration>() ?? new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile(
                $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                true)
            .AddEnvironmentVariables()
            .Build();

        if (serviceProvider == null)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
        }

        var loggerFactory = serviceProvider?.GetService<Microsoft.Extensions.Logging.ILoggerFactory>() ?? LoggerFactory.Create(builder => builder.AddSerilog());
        _logger = loggerFactory.CreateLogger("worker");
        var logger = _logger; // Keep local variable for backward compatibility

        // Load options
        var options = WorkerOptions.FromEnvironment();
        _options = options; // Store for re-registration

        // Startup diagnostics: effective worker configuration (secrets redacted)
        try
        {
            // Redact secrets
            string redact(string? s)
            {
                return string.IsNullOrEmpty(s) ? "" : "***";
            }

            static string GetInformationalVersion(Type t)
            {
                try
                {
                    var asm = t.Assembly;
                    var aiv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    return aiv?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            var version = GetInformationalVersion(typeof(WorkerServiceRunner));

            static string TruncVer(string? v)
            {
                const int max = 15; // "1.0.1-preview.3".Length
                if (string.IsNullOrEmpty(v))
                {
                    return v ?? string.Empty;
                }

                return v.Length <= max ? v : v[..max];
            }

            var versionShort = TruncVer(version);

            var diag = new
            {
                Version = versionShort,
                options.HubUrl,
                options.RedisUrl,
                options.NodeId,
                NodeSecret = redact(Environment.GetEnvironmentVariable("AGENIX_WORKER_NODE_SECRET")),
                PublicWs =
                    new
                    {
                        Host = options.PublicWsHost ?? "(unset)",
                        Port = options.PublicWsPort ?? "(unset)",
                        Scheme = options.PublicWsScheme
                    },
                Sidecar =
                    new { Script = options.SidecarScript, ReadyTimeoutSeconds = options.SidecarReadyTimeoutSeconds },
                Backpressure =
                    new
                    {
                        LogChannelCapacity = options.WebSocketLogChannelCapacity,
                        LogDropPolicy = options.WebSocketLogDropPolicy.ToString(),
                        ProxyChannelCapacity = options.WebSocketProxyChannelCapacity,
                        ProxyDropPolicy = options.WebSocketProxyDropPolicy.ToString()
                    },
                Labels = options.Labels.ToDictionary(k => k.Key, v => v.Value),
                PoolConfig = options.PoolConfig.ToDictionary(k => k.Key, v => v.Value)
            };
            var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
            _chunkedLogger?.LogInformation(null, "[worker] Startup diagnostics (effective config):\n{json}", json);
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogError(ex, null, "[worker] Startup diagnostics failed: {message}", ex.Message);
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

            _chunkedLogger?.LogInformation(EventCodes.Worker.PlaywrightLaunched,
                "[worker] Playwright startup: package={pkg}, envVersion={envVersion}, installedVersion={installedVersion}",
                envPkg, envVer, installedVer);
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogWarning(ex, null, "[worker] Playwright startup: failed to detect installed version: {message}",
                ex.Message);
        }

        // Compose dependencies
        static bool IsTrue(string? v)
        {
            return !string.IsNullOrEmpty(v) && (string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase));
        }

        var redisUrl = options.RedisUrl;
        var mode = (Environment.GetEnvironmentVariable("REDIS_MODE") ?? string.Empty).Trim().ToLowerInvariant();
        var useCluster = IsTrue(Environment.GetEnvironmentVariable("REDIS_USE_CLUSTER")) || mode == "cluster";
        var useSentinel = IsTrue(Environment.GetEnvironmentVariable("REDIS_USE_SENTINEL")) || mode == "sentinel";
        if (useCluster && useSentinel)
        {
            useCluster = false; // prefer sentinel
        }

        ConfigurationOptions ApplyCommon(ConfigurationOptions opts)
        {
            opts.AbortOnConnectFail = false;
            opts.ConnectRetry = 3;
            opts.KeepAlive = 15;

            int GetInt(string key, int def)
            {
                return int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? Math.Max(0, v) : def;
            }

            opts.ConnectTimeout = GetInt("REDIS_CONNECT_TIMEOUT_MS", 5000);
            opts.SyncTimeout = GetInt("REDIS_SYNC_TIMEOUT_MS", 5000);
            opts.AsyncTimeout = GetInt("REDIS_ASYNC_TIMEOUT_MS", 5000);
            opts.ReconnectRetryPolicy = new ExponentialRetry(5000);

            var sslExplicit = IsTrue(Environment.GetEnvironmentVariable("REDIS_SSL"));
            var isRediss = redisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);
            if (sslExplicit || isRediss)
            {
                opts.Ssl = true;
                var sslHost = Environment.GetEnvironmentVariable("REDIS_SSL_HOST");
                if (!string.IsNullOrWhiteSpace(sslHost))
                {
                    opts.SslHost = sslHost;
                }
            }

            return opts;
        }

        IDatabase db;
        IConnectionMultiplexer mux;
        if (useSentinel)
        {
            var sentinels = Environment.GetEnvironmentVariable("REDIS_SENTINELS");
            var masterName = Environment.GetEnvironmentVariable("REDIS_SENTINEL_MASTER");
            if (string.IsNullOrWhiteSpace(sentinels) || string.IsNullOrWhiteSpace(masterName))
            {
                throw new InvalidOperationException(
                    "REDIS_MODE=sentinel requires REDIS_SENTINELS and REDIS_SENTINEL_MASTER.");
            }

            var sentinelOptions = new ConfigurationOptions { CommandMap = CommandMap.Sentinel };
            foreach (var ep in sentinels.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                sentinelOptions.EndPoints.Add(ep.Trim());
            }

            ApplyCommon(sentinelOptions);
            var sentinelPassword = Environment.GetEnvironmentVariable("REDIS_SENTINEL_PASSWORD");
            if (!string.IsNullOrEmpty(sentinelPassword))
            {
                sentinelOptions.Password = sentinelPassword;
            }

            await using var sentinelMux = await ConnectionMultiplexer.ConnectAsync(sentinelOptions);
            var sentinelEndpoints = sentinelMux.GetEndPoints();
            if (sentinelEndpoints.Length == 0)
            {
                throw new InvalidOperationException("No reachable Sentinel endpoints.");
            }

            var sentinelServer = sentinelMux.GetServer(sentinelEndpoints[0]);
            var masterEp = await sentinelServer.SentinelGetMasterAddressByNameAsync(masterName);
            if (masterEp is null)
            {
                throw new InvalidOperationException($"Sentinel did not return a master for '{masterName}'.");
            }

            var baseOptions = ConfigurationOptions.Parse(redisUrl, true);
            var finalOptions = new ConfigurationOptions();
            finalOptions.EndPoints.Add(masterEp);
            finalOptions.User = baseOptions.User;
            finalOptions.Password = baseOptions.Password;
            finalOptions.DefaultDatabase = baseOptions.DefaultDatabase;
            ApplyCommon(finalOptions);

            mux = await ConnectionMultiplexer.ConnectAsync(finalOptions);
            db = mux.GetDatabase();
            _chunkedLogger?.LogMilestone(EventCodes.Database.DatabaseReady, "Redis connection established (Sentinel mode)");
        }
        else
        {
            var optionsRedis = ConfigurationOptions.Parse(redisUrl, true);
            ApplyCommon(optionsRedis);
            mux = await ConnectionMultiplexer.ConnectAsync(optionsRedis);
            db = mux.GetDatabase();
            _chunkedLogger?.LogMilestone(EventCodes.Database.DatabaseReady, "Redis connection established (Standalone/Cluster mode)");
        }

        IMetricsPort metrics = new PrometheusMetrics();
        _metrics = metrics; // Store for re-registration

        var chunkedOptions = serviceProvider?.GetService<ChunkedLoggerOptions>() ?? new ChunkedLoggerOptions();

        IHubClient hub = serviceProvider?.GetService<IHubClient>() ??
                         new HubHttpClient(
                             new ChunkedLogger<HubHttpClient>(loggerFactory.CreateLogger<HubHttpClient>(), chunkedOptions),
                             serviceProvider?.GetRequiredService<IHttpClientFactory>() ?? throw new InvalidOperationException("IHttpClientFactory is required"));

        var sidecarLauncher = serviceProvider?.GetService<ISidecarLauncher>() ?? new SidecarLauncher(options);

        var pidRedisManager = new PidRedisManager(db, options.NodeId,
            serviceProvider?.GetService<ChunkedLogger<PidRedisManager>>() ??
            new ChunkedLogger<PidRedisManager>(loggerFactory.CreateLogger<PidRedisManager>(), chunkedOptions));

        // Services - store pool and registrar as instance fields for re-registration
        _pool = new PoolManager(
            options,
            db,
            metrics,
            sidecarLauncher,
            loggerFactory.CreateLogger<PoolManager>(),
            serviceProvider?.GetService<ChunkedLogger<PoolManager>>(),
            pidRedisManager);
        _registrar = new NodeRegistrar(hub, options);
        var pool = _pool; // Keep local variable for backward compatibility
        var registrar = _registrar;
        var heartbeat = new HeartbeatService(options, db,
            serviceProvider?.GetService<ChunkedLogger<HeartbeatService>>() ??
            new ChunkedLogger<HeartbeatService>(loggerFactory.CreateLogger<HeartbeatService>(), chunkedOptions));
        var registrationVerifier = new WorkerRegistrationVerifier(
            this,
            options,
            hub,
            serviceProvider?.GetService<ChunkedLogger<WorkerRegistrationVerifier>>());
        var healthChecker = new BrowserHealthChecker(
            pool,
            db,
            options,
            metrics,
            serviceProvider?.GetService<ChunkedLogger<BrowserHealthChecker>>() ??
            new ChunkedLogger<BrowserHealthChecker>(loggerFactory.CreateLogger<BrowserHealthChecker>(),
                serviceProvider?.GetService<ChunkedLoggerOptions>() ?? new ChunkedLoggerOptions()),
            serviceProvider?.GetService<IPlaywrightProtocolClientFactory>());
        var webHost = new WebServerHost(
            options,
            metrics,
            pool,
            db,
            pidRedisManager,
            serviceProvider?.GetService<ChunkedLogger<WebServerHost>>() ??
            new ChunkedLogger<WebServerHost>(loggerFactory.CreateLogger<WebServerHost>(),
                serviceProvider?.GetService<ChunkedLoggerOptions>() ?? new ChunkedLoggerOptions()));
        var diskMon = new DiskUsageMonitor(options);

        // Cancellation for background loops
        using var cts = new CancellationTokenSource();

        // Register a gap detection callback for timer gap detection (FAST PATH)
        heartbeat.SetGapDetectedCallback(async () => await EnsureRegisteredAsync("gap_detection"));

        // Start heartbeats early to avoid premature expiry during warmup
        await heartbeat.HeartbeatOnceAsync();
        _ = heartbeat.HeartbeatLoopAsync(cts.Token);

        // Start periodic registration verification (SLOW PATH - backup to timer gap detection)
        _ = registrationVerifier.StartAsync(cts.Token);

        // Start a browser health checker (opt-in via AGENIX_WORKER_HEALTH_CHECK_ENABLED)
        _ = healthChecker.StartAsync(cts.Token);

        // Warm pools and start background loops
        await pool.InitializeAsync();
        _chunkedLogger?.LogMilestone(EventCodes.Worker.RegistrationStarted, "Registering worker with hub...");
        await registrar.RegisterAsync();
        _chunkedLogger?.LogMilestone(EventCodes.Worker.RegistrationConfirmed, "Worker registered successfully");
        _ = pool.ReconcileLoopAsync(cts.Token);
        _ = diskMon.RunAsync(cts.Token);

        _chunkedLogger?.LogMilestone(EventCodes.System.BootstrapCompleted, "Worker startup completed");

        // Run web app (blocks until shutdown)
        await webHost.RunAsync(args, cts);

        // Ensure background loops are signaled to stop
        try
        {
            await registrationVerifier.StopAsync(CancellationToken.None);
            await healthChecker.StopAsync(CancellationToken.None);
            await Task.Delay(100, cts.Token);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    ///     Ensures the worker is registered with the hub and pools are initialized.
    ///     This method is idempotent and thread-safe via SemaphoreSlim lock.
    ///     Called by HeartbeatService (timer gap detection) and WorkerRegistrationVerifier (periodic verification).
    /// </summary>
    /// <param name="trigger">Re-registration trigger: "gap_detection" or "periodic_verification"</param>
    public virtual async Task EnsureRegisteredAsync(string trigger = "periodic_verification")
    {
        if (_registrar == null || _pool == null || _logger == null || _metrics == null || _options == null)
        {
            throw new InvalidOperationException(
                "WorkerServiceRunner not initialized. EnsureRegisteredAsync can only be called after RunAsync has started.");
        }

        using var op = _chunkedLogger?.BeginOperation("ReRegistration", new Dictionary<string, object> { ["Trigger"] = trigger });
        await _registrationLock.WaitAsync();
        try
        {
            _chunkedLogger?.LogWarning(EventCodes.Worker.RegistrationStarted, "Detected worker expiration or timer gap (trigger={Trigger}). Re-registering with hub...", trigger);

            // Step 1: Re-register with hub
            await _registrar.RegisterAsync();
            _chunkedLogger?.LogMilestone(EventCodes.Worker.RegistrationConfirmed, "Successfully re-registered with hub");

            // Step 2: Re-warm browser pools
            await _pool.InitializeAsync();
            _chunkedLogger?.LogMilestone(EventCodes.BrowserPool.PoolInitialized, "Successfully re-initialized browser pools");

            // Step 3: Record success metric
            _metrics.IncrementReRegistration(_options.NodeId, trigger);
            op?.Complete();
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogError(ex, EventCodes.Worker.RegistrationFailed, "Failed to re-register worker (trigger={Trigger}): {Message}", trigger, ex.Message);
            op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Hub);

            // Record error metric
            if (_metrics != null && _options != null)
            {
                _metrics.IncrementReRegistrationError(_options.NodeId, trigger);
            }

            throw;
        }
        finally
        {
            _registrationLock.Release();
        }
    }
}
