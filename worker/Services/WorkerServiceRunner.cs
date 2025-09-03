#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

using System.Text.Json;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Infrastructure.Adapters;

namespace WorkerService.Services;

public sealed class WorkerServiceRunner
{
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = Microsoft.Extensions.Logging.LoggerFactory
        .Create(b => b.AddSimpleConsole())
        .CreateLogger("worker");

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
                options.HubUrl,
                options.RedisUrl,
                options.NodeId,
                NodeSecret = redact(Environment.GetEnvironmentVariable("NODE_SECRET")),
                PublicWs =
                    new
                    {
                        Host = options.PublicWsHost ?? "(unset)",
                        Port = options.PublicWsPort ?? "(unset)",
                        Scheme = options.PublicWsScheme
                    },
                Sidecar =
                    new { Script = options.SidecarScript, ReadyTimeoutSeconds = options.SidecarReadyTimeoutSeconds },
                Backpressure = new { LogChannelCapacity = options.WebSocketLogChannelCapacity, LogDropPolicy = options.WebSocketLogDropPolicy.ToString(), ProxyChannelCapacity = options.WebSocketProxyChannelCapacity, ProxyDropPolicy = options.WebSocketProxyDropPolicy.ToString() },
                Labels = options.Labels.ToDictionary(k => k.Key, v => v.Value),
                PoolConfig = options.PoolConfig.ToDictionary(k => k.Key, v => v.Value)
            };
            var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
            Logger.LogInformation("[worker] Startup diagnostics (effective config):\n{json}", json);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[worker] Startup diagnostics failed: {message}", ex.Message);
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

            Logger.LogInformation("[worker] Playwright startup: package={pkg}, envVersion={envVersion}, installedVersion={installedVersion}", envPkg, envVer, installedVer);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[worker] Playwright startup: failed to detect installed version: {message}", ex.Message);
        }

        // Compose dependencies
        var redisOptions = ConfigurationOptions.Parse(options.RedisUrl, true);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 3;
        redisOptions.KeepAlive = 15;
        int GetInt(string key, int def)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? Math.Max(0, v) : def;
        }
        redisOptions.ConnectTimeout = GetInt("REDIS_CONNECT_TIMEOUT_MS", 5000);
        redisOptions.SyncTimeout = GetInt("REDIS_SYNC_TIMEOUT_MS", 5000);
        redisOptions.AsyncTimeout = GetInt("REDIS_ASYNC_TIMEOUT_MS", 5000);
        redisOptions.ReconnectRetryPolicy = new ExponentialRetry(5000);

        var mux = await ConnectionMultiplexer.ConnectAsync(redisOptions);
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
