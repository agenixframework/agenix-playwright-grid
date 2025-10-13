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

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Shared.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class WebServerHost
{
    private static readonly Counter WsLogDroppedCounter = Metrics.CreateCounter(
        "worker_ws_log_dropped_messages_total",
        "Dropped Playwright WS log messages due to backpressure",
        "node", "direction", "policy", "reason");

    private static readonly Counter WsProxyDroppedCounter = Metrics.CreateCounter(
        "worker_ws_proxy_dropped_frames_total",
        "Dropped WebSocket frames in proxy due to backpressure",
        "node", "direction", "policy", "reason");

    // New: per-worker WS concurrency metrics
    private static readonly Gauge WsConnectionsCurrentGauge = Metrics.CreateGauge(
        "worker_ws_connections_current",
        "Current number of client WebSocket connections handled by this worker",
        "node");

    private static readonly Gauge WsConnectionsLimitGauge = Metrics.CreateGauge(
        "worker_ws_connections_limit",
        "Configured maximum concurrent client WebSocket connections for this worker (0 = unlimited)",
        "node");

    private static readonly Gauge WsConnectionsHeadroomGauge = Metrics.CreateGauge(
        "worker_ws_connections_headroom",
        "Headroom: (limit - current). If unlimited, set to -1",
        "node");

    private static readonly Gauge WsSaturationGauge = Metrics.CreateGauge(
        "worker_ws_saturation",
        "WS saturation ratio current/limit in [0,1]. For unlimited limit, set to 0",
        "node");

    private static readonly Counter WsConnectionsRejectedCounter = Metrics.CreateCounter(
        "worker_ws_connections_rejected_total",
        "Rejected client WebSocket connections due to per-worker max cap",
        "node", "reason");

    private readonly IDatabase _db;
    private readonly IMetricsPort _metrics;
    private readonly WorkerOptions _options;
    private readonly PoolManager _pool;
    private readonly PidRedisManager? _pidRedisManager;
    private readonly ChunkedLogger<WebServerHost>? _chunkedLogger;
    private volatile bool _acceptingBorrows = true;
    private volatile bool _shuttingDown;

    // Tracks current WS connections for per-worker cap enforcement
    private long _wsCurrentConnections;

    public WebServerHost(WorkerOptions options, IMetricsPort metrics, PoolManager pool, IDatabase db, PidRedisManager? pidRedisManager = null, ChunkedLogger<WebServerHost>? chunkedLogger = null)
    {
        _options = options;
        _metrics = metrics;
        _pool = pool;
        _db = db;
        _pidRedisManager = pidRedisManager;
        _chunkedLogger = chunkedLogger;
    }

    private void UpdateWsGauges()
    {
        try
        {
            var node = _options.NodeId;
            var current = Interlocked.Read(ref _wsCurrentConnections);
            var limit = _options.WebSocketMaxConnections;
            var headroom = limit > 0 ? Math.Max(0, limit - (int)current) : -1;
            var saturation = limit > 0 && limit >= current && limit > 0 ? current / (double)limit : 0.0;

            WsConnectionsCurrentGauge.WithLabels(node).Set(current);
            WsConnectionsLimitGauge.WithLabels(node).Set(limit);
            WsConnectionsHeadroomGauge.WithLabels(node).Set(headroom);
            WsSaturationGauge.WithLabels(node).Set(saturation);
        }
        catch
        {
            /* metrics failures should not impact data path */
        }
    }

    public async Task RunAsync(string[] args, CancellationTokenSource appCts)
    {
        using var op = _chunkedLogger?.BeginOperation("WebServerLifecycle", new Dictionary<string, object>
        {
            ["NodeId"] = _options.NodeId
        });
        _chunkedLogger?.LogMilestone(EventCodes.WebServer.ServerStarting, "Web server starting for node {NodeId}", _options.NodeId);

        var builder = WebApplication.CreateBuilder(args);

        // Ensure only Serilog is used for logging to prevent duplication and interleaving
        builder.Logging.ClearProviders();
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        // Suppress verbose framework logs like OkObjectResult JSON writing and EndpointMiddleware exec logs
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        // Apply environment-driven log levels (global + per-category overrides)
        LoggingConfigurator.ApplyFromEnvironment(builder.Logging, builder.Configuration);

        // OpenTelemetry setup (env-driven exporters)
        const string workerServiceName = "playwright-worker";
        var workerServiceVersion = typeof(WebServerHost).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var enableOtlp = string.Equals(builder.Configuration["ENABLE_OTLP"], "1", StringComparison.OrdinalIgnoreCase);
        var enablePromOtel = string.Equals(builder.Configuration["ENABLE_PROMETHEUS_OTEL"], "1",
            StringComparison.OrdinalIgnoreCase);
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
        var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(workerServiceName, serviceVersion: workerServiceVersion, serviceInstanceId: _options.NodeId);


        builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(workerServiceName, serviceVersion: workerServiceVersion,
                serviceInstanceId: _options.NodeId))
            .WithTracing(t =>
            {
                t.SetResourceBuilder(resourceBuilder);
                t.AddAspNetCoreInstrumentation();
                t.AddHttpClientInstrumentation();
                if (enableOtlp)
                {
                    t.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                            ? OtlpExportProtocol.HttpProtobuf
                            : OtlpExportProtocol.Grpc;
                    });
                }
            })
            .WithMetrics(m =>
            {
                m.SetResourceBuilder(resourceBuilder);
                m.AddAspNetCoreInstrumentation();
                m.AddRuntimeInstrumentation();
                if (enableOtlp)
                {
                    m.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                            ? OtlpExportProtocol.HttpProtobuf
                            : OtlpExportProtocol.Grpc;
                    });
                }
            });


        var app = builder.Build();
        var logger = app.Logger;

        // Log configuration dump
        try
        {
            var configData = new Dictionary<string, object>
            {
                ["NodeId"] = _options.NodeId,
                ["HubUrl"] = _options.HubUrl,
                ["RedisUrl"] = _options.RedisUrl,
                ["PublicWsHost"] = _options.PublicWsHost ?? "(auto)",
                ["PublicWsPort"] = _options.PublicWsPort ?? "0",
                ["Labels"] = _options.Labels,
                ["PoolConfig"] = _options.PoolConfig
            };
            _chunkedLogger?.LogMilestone(EventCodes.WebServer.ConfigurationDumped, "Effective worker configuration");
            _chunkedLogger?.LogInformation(null, "Configuration details: {Config}", JsonSerializer.Serialize(configData));
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogWarning(ex, null, "Failed to log configuration dump: {Message}", ex.Message);
        }

        app.UseMetricServer();
        app.UseHttpMetrics();

        // Initialize WS limit/headroom gauges at startup
        UpdateWsGauges();

        _chunkedLogger?.LogMilestone(EventCodes.WebServer.EndpointsRegistered, "API endpoints registered");

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = string.Join(", ", app.Urls);
            _chunkedLogger?.LogMilestone(EventCodes.WebServer.ListeningAddresses, "Web server listening on: {Addresses}", addresses);
            _chunkedLogger?.LogMilestone(EventCodes.WebServer.ServerStarted, "Web server started for node {NodeId}", _options.NodeId);
        });

        app.Lifetime.ApplicationStopping.Register(async () =>
        {
            using var shutdownOp = _chunkedLogger?.BeginOperation("WebServerShutdown");
            _chunkedLogger?.LogMilestone(EventCodes.WebServer.ServerStopping, "ApplicationStopping: initiating graceful drain");
            _shuttingDown = true;
            _acceptingBorrows = false;

            // Proactively drop TTL keys for any active sessions so Hub sweeper can reclaim
            try
            {
                foreach (var bid in _pool.GetActiveBrowserIds())
                {
                    try { await _db.KeyDeleteAsync(RedisKeys.BorrowTtl(bid)); }
                    catch { }

                    try { await _db.KeyDeleteAsync($"borrow_idle:{bid}"); }
                    catch { }
                }
            }
            catch { }

            try { await appCts.CancelAsync(); }
            catch { }

            // Drain active WebSocket sessions up to a timeout
            var drainSeconds = 0;
            try
            {
                drainSeconds =
                    int.TryParse(Environment.GetEnvironmentVariable("WORKER_DRAIN_TIMEOUT_SECONDS"), out var s)
                        ? Math.Max(0, s)
                        : 30; // default 30s
            }
            catch { drainSeconds = 30; }

            var until = DateTime.UtcNow.AddSeconds(drainSeconds);
            try
            {
                while (DateTime.UtcNow < until)
                {
                    if (!_poolHasAnyActiveConnections())
                    {
                        break;
                    }

                    Thread.Sleep(250);
                }
            }
            catch { }

            try { _pool.CleanupAllAsync().GetAwaiter().GetResult(); }
            catch { }

            // Always kill all sidecar processes to prevent orphans
            // (regardless of whether active connections remain)
            try
            {
                if (_poolHasAnyActiveConnections())
                {
                    _chunkedLogger?.LogWarning(null, "Drain timeout reached; forcing sidecar shutdown");
                }
                else
                {
                    _chunkedLogger?.LogMilestone(EventCodes.WebServer.ServerStopped, "Graceful drain complete; killing all sidecars");
                }

                _pool.KillAll();

                // Layer 3: Cleanup Redis PID tracking after killing all sidecars
                if (_pidRedisManager != null)
                {
                    await _pidRedisManager.CleanupAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[worker] Failed to kill sidecars during shutdown");
            }
        });

        bool _poolHasAnyActiveConnections()
        {
            try { return _pool.HasAnyActiveConnections(); }
            catch { return false; }
        }

        app.MapPost("/borrow/{labelKey}", async (string labelKey, HttpRequest req) =>
        {
            using var op = _chunkedLogger?.BeginOperation("BorrowRequest", new Dictionary<string, object>
            {
                ["LabelKey"] = labelKey
            });
            _chunkedLogger?.LogMilestone(EventCodes.WebServer.RequestReceived, "Borrow requested for label {LabelKey}", labelKey);

            if (!req.Headers.TryGetValue("x-node-secret", out var s) || s.FirstOrDefault() != _options.NodeNodeSecret)
            {
                _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Unauthorized borrow request for label {LabelKey}", labelKey);
                return Results.Unauthorized();
            }

            if (!_acceptingBorrows)
            {
                try { req.HttpContext.Response.Headers.Append("Retry-After", "30"); }
                catch { }

                _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Borrow rejected (node not accepting borrows) for label {LabelKey}", labelKey);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // Respect maintenance mode: deny borrow while maintenance is active for this label
            try
            {
                if (_db.KeyExists(RedisKeys.MaintenanceFlag(labelKey)))
                {
                    _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Borrow rejected (maintenance mode) for label {LabelKey}", labelKey);
                    return Results.StatusCode(503);
                }
            }
            catch { }

            if (!_pool.TryGetFirstSlot(labelKey, out var browserId, out var slot))
            {
                _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Borrow failed (no slots available) for label {LabelKey}", labelKey);
                return Results.StatusCode(503);
            }

            _metrics.IncrementBorrow(_options.NodeId, labelKey);
            var availableKey = RedisKeys.Available(labelKey);
            _metrics.SetPoolAvailable(_options.NodeId, labelKey, await _db.ListLengthAsync(availableKey));

            var respObj = new
            {
                nodeId = _options.NodeId,
                browserId,
                webSocketEndpoint = slot.PublicWs,
                browserType = slot.BrowserType,
                labels = _options.Labels.ToDictionary(k => k.Key, v => v.Value)
            };

            _chunkedLogger?.LogMilestone(EventCodes.WebServer.RequestProcessed, "Borrow successful for label {LabelKey}, browser {BrowserId}", labelKey, browserId);
            op?.Complete();
            return Results.Ok(respObj);
        });

        // Admin: orchestrate a safe sidecar upgrade on this node (graceful drain + restart)
        app.MapPost("/admin/sidecar/upgrade", async (HttpRequest req) =>
        {
            using var op = _chunkedLogger?.BeginOperation("SidecarUpgrade");
            _chunkedLogger?.LogMilestone(EventCodes.WebServer.RequestReceived, "Sidecar upgrade requested");

            if (!req.Headers.TryGetValue("x-node-secret", out var s) || s.FirstOrDefault() != _options.NodeNodeSecret)
            {
                _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Unauthorized sidecar upgrade request");
                return Results.Unauthorized();
            }

            // Stop local direct borrows early
            _acceptingBorrows = false;

            var started = DateTime.UtcNow;
            var removed = 0;
            var scheduledRecycle = 0;

            _chunkedLogger?.LogMilestone(EventCodes.WebServer.RequestProcessed, "Sidecar upgrade initiated: withdrawing from Redis and scheduling recycle");
            op?.Complete();

            // 1) Withdraw local availability from Redis so Hub won't assign new sessions to this node
            foreach (var labelKey in _options.PoolConfig.Keys)
            {
                var availKey = RedisKeys.Available(labelKey);
                try
                {
                    var items = await _db.ListRangeAsync(availKey);
                    foreach (var item in items)
                    {
                        var sItem = item.ToString();
                        if (sItem.Contains($"\"nodeId\":\"{_options.NodeId}\"", StringComparison.Ordinal))
                        {
                            await _db.ListRemoveAsync(availKey, item);
                            removed++;
                        }
                    }
                }
                catch { }
            }

            // 2) Schedule recycle for local sidecars that are not actively serving a WS connection
            foreach (var kv in _pool.Pools)
            {
                var map = kv.Value;
                foreach (var bid in map.Keys)
                {
                    try
                    {
                        if (!_pool.HasActiveConnection(bid))
                        {
                            await _db.StringSetAsync(RedisKeys.Recycle(bid), "1", TimeSpan.FromMinutes(5));
                            scheduledRecycle++;
                        }
                    }
                    catch { }
                }
            }

            // 3) Drain active sessions up to a timeout
            int drainSeconds;
            try
            {
                drainSeconds =
                    int.TryParse(Environment.GetEnvironmentVariable("WORKER_DRAIN_TIMEOUT_SECONDS"), out var s2)
                        ? Math.Max(0, s2)
                        : 30;
            }
            catch { drainSeconds = 30; }

            var until = DateTime.UtcNow.AddSeconds(drainSeconds);
            try
            {
                while (DateTime.UtcNow < until)
                {
                    if (!_pool.HasAnyActiveConnections())
                    {
                        break;
                    }

                    await Task.Delay(250);
                }
            }
            catch { }

            // 4) If still active after timeout, force-kill sidecars (as in shutdown path)
            if (_pool.HasAnyActiveConnections())
            {
                try { _pool.KillAll(); }
                catch { }
            }

            // 5) Warm pools again (fresh sidecars; repopulate availability)
            await _pool.InitializeAsync();

            var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            return Results.Ok(new { status = "ok", removed, scheduledRecycle, elapsedMs });
        });

        app.MapPost("/return/{labelKey}", async (string labelKey, HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("x-node-secret", out var s) || s.FirstOrDefault() != _options.NodeNodeSecret)
            {
                return Results.Unauthorized();
            }

            var body =
                await req.ReadFromJsonAsync<ConcurrentDictionary<string, string>>() ??
                new ConcurrentDictionary<string, string>();
            if (!body.TryGetValue("browserId", out _))
            {
                return Results.BadRequest();
            }

            var availableKey = RedisKeys.Available(labelKey);
            _metrics.SetPoolAvailable(_options.NodeId, labelKey, await _db.ListLengthAsync(availableKey));
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/health", () =>
        {
            _chunkedLogger?.LogDebug(EventCodes.WebServer.RequestReceived, "Health check requested");
            return Results.Ok(new { node = _options.NodeId, pools = _pool.Pools.Keys.ToArray() });
        });

        app.MapGet("/health/ready", async () =>
        {
            _chunkedLogger?.LogDebug(EventCodes.WebServer.RequestReceived, "Readiness check requested");
            if (_shuttingDown)
            {
                _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Readiness check failed: shutting down");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var timeoutMs = int.TryParse(Environment.GetEnvironmentVariable("REDIS_HEALTH_TIMEOUT_MS"), out var ms)
                    ? Math.Max(100, ms)
                    : 1000;
                var sw = Stopwatch.StartNew();
                var pingTask = _db.PingAsync();
                var completed = await Task.WhenAny(pingTask, Task.Delay(timeoutMs));
                if (completed != pingTask)
                {
                    _chunkedLogger?.LogWarning(EventCodes.WebServer.RequestFailed, "Readiness check failed: Redis ping timeout");
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                await pingTask; // propagate any errors
                sw.Stop();
                return Results.Ok(new { status = "ready", redis = new { pingMs = sw.ElapsedMilliseconds } });
            }
            catch (Exception ex)
            {
                _chunkedLogger?.LogError(ex, EventCodes.WebServer.RequestFailed, "Readiness check failed: {Message}", ex.Message);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });

        // Sidecar health snapshot (per label and browserId)
        app.MapGet("/health/sidecars", () =>
        {
            var now = DateTime.UtcNow;
            var backoffs = _pool.GetBackoffAll();
            var labels = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var labelEntry in _pool.Pools)
            {
                var labelKey = labelEntry.Key;
                var arr = new List<object>();
                foreach (var kv in labelEntry.Value)
                {
                    var browserId = kv.Key;
                    var slot = kv.Value;
                    bool alive;
                    int pid;
                    try { alive = !slot.Proc.HasExited; }
                    catch { alive = false; }

                    try { pid = slot.Proc.Id; }
                    catch { pid = 0; }

                    var uptime = now - slot.StartedAt;
                    arr.Add(new
                    {
                        browserId,
                        slot.BrowserType,
                        pid,
                        alive,
                        startedAtUtc = slot.StartedAt,
                        uptimeSeconds = (int)Math.Max(0, uptime.TotalSeconds),
                        wsInternal = slot.InternalWs,
                        wsPublic = slot.PublicWs
                    });
                }

                object? backoffObj = null;
                if (backoffs.TryGetValue(labelKey, out var bo))
                {
                    backoffObj = new
                    {
                        bo.failures,
                        bo.lastFailureUtc,
                        nextDelaySeconds = (int)bo.nextDelay.TotalSeconds
                    };
                }

                labels[labelKey] = new { slots = arr, backoff = backoffObj };
            }

            return Results.Ok(new { nodeId = _options.NodeId, labels });
        });

        // Diagnostics: expose filtered environment variables for this worker
        app.MapGet("/diagnostics/env", (HttpRequest req) =>
        {
            // Allow access with either hub secret (from hub) or node-node secret
            var ok = (req.Headers.TryGetValue("x-hub-secret", out var hs) && hs.FirstOrDefault() == _options.NodeSecret)
                     || (req.Headers.TryGetValue("x-node-secret", out var ns) &&
                         ns.FirstOrDefault() == _options.NodeNodeSecret);
            if (!ok)
            {
                return Results.Unauthorized();
            }

            static bool IsSecret(string key)
            {
                key = key.ToUpperInvariant();
                return key.Contains("SECRET") || key.Contains("PASSWORD") || key.Contains("TOKEN");
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
            {
                var k = de.Key?.ToString();
                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                var v = de.Value?.ToString() ?? string.Empty;
                dict[k] = IsSecret(k) && !string.IsNullOrEmpty(v) ? "***" : v;
            }

            var payload = new { nodeId = _options.NodeId, env = dict, now = DateTime.UtcNow };
            return Results.Ok(payload);
        });

        // Internal: Kill orphaned PID (called by hub's WorkerOrphanDetector)
        app.MapPost("/api/worker/internal/kill-pid/{pid:int}", async (int pid, HttpRequest req) =>
        {
            // Require hub secret or node-node secret for security
            var ok = (req.Headers.TryGetValue("x-hub-secret", out var hs) && hs.FirstOrDefault() == _options.NodeSecret)
                     || (req.Headers.TryGetValue("x-node-secret", out var ns) &&
                         ns.FirstOrDefault() == _options.NodeNodeSecret);
            if (!ok)
            {
                return Results.Unauthorized();
            }

            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                logger.LogInformation("[KillPid] Killed PID {Pid}", pid);

                // Untrack from Redis if PidRedisManager is available
                if (_pidRedisManager != null)
                {
                    await _pidRedisManager.UntrackPidAsync(pid);
                }

                return Results.Ok(new { pid, killed = true });
            }
            catch (ArgumentException)
            {
                // Process doesn't exist - already dead
                logger.LogDebug("[KillPid] PID {Pid} not found (already dead)", pid);
                return Results.Ok(new { pid, killed = false, reason = "not_found" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[KillPid] Failed to kill PID {Pid}", pid);
                return Results.Problem($"Failed to kill PID {pid}: {ex.Message}");
            }
        });

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(_options.WebSocketPingIntervalSeconds)
        });

        // Public Playwright WS proxy
        // This endpoint accepts client WebSocket connections and forwards them to the internal
        // Playwright browser server (wsEndpoint from launchServer). While proxying, it assembles
        // text frames into full protocol messages and forwards them (non-blocking) to the Hub
        // via HTTP POST /results/browser/{browserId}/commands. This is how we intercept the
        // Playwright protocol without modifying Playwright itself.
        app.Map("/ws/{browserId}", async (HttpContext ctx, string browserId) =>
        {
            using var scopeWs = LoggingScopes.Begin(logger, browserId: browserId);
            logger.LogInformation("WS connect start {BrowserId}", browserId);

            // Track and enforce the per-worker WS connection cap
            var reservedWsSlot = false;

            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("Expected WebSocket upgrade");
                return;
            }

            if (!_pool.TryFindSlotById(browserId, out var slot))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync("Unknown browserId");
                return;
            }

            // Reserve a connection slot and enforce per-worker cap
            var limit = _options.WebSocketMaxConnections;
            var currentAfterInc = Interlocked.Increment(ref _wsCurrentConnections);
            reservedWsSlot = true;
            if (limit > 0 && currentAfterInc > limit)
            {
                Interlocked.Decrement(ref _wsCurrentConnections);
                reservedWsSlot = false;
                try { WsConnectionsRejectedCounter.WithLabels(_options.NodeId, "saturated").Inc(); }
                catch { }

                UpdateWsGauges();
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsync("WebSocket capacity exhausted on worker");
                return;
            }

            UpdateWsGauges();

            // Try to resolve runId for this browser from Redis mapping to propagate correlation
            string? runId = null;
            string? runName = null;
            try
            {
                var v = await _db.StringGetAsync(RedisKeys.BrowserRun(browserId));
                if (!v.IsNullOrEmpty)
                {
                    runId = v.ToString();
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(runId))
                        {
                            var rnVal = await _db.StringGetAsync(RedisKeys.ResultsRunName(runId));
                            if (!rnVal.IsNullOrEmpty)
                            {
                                runName = rnVal.ToString();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            using var upstream = new ClientWebSocket();
            upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(_options.WebSocketPingIntervalSeconds);
            // Best-effort: include run context for the sidecar
            try
            {
                if (!string.IsNullOrWhiteSpace(runId))
                {
                    upstream.Options.SetRequestHeader("x-run-id", runId);
                }

                if (!string.IsNullOrWhiteSpace(runName))
                {
                    upstream.Options.SetRequestHeader("x-run-name", runName);
                }
            }
            catch { }

            try
            {
                await upstream.ConnectAsync(new Uri(slot.InternalWs), ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                // Release reserved slot on upstream failure
                if (reservedWsSlot)
                {
                    Interlocked.Decrement(ref _wsCurrentConnections);
                    UpdateWsGauges();
                    reservedWsSlot = false;
                }

                ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
                await ctx.Response.WriteAsync($"Upstream connect failed: {ex.Message}");
                return;
            }

            var wsAccept =
                new WebSocketAcceptContext { DangerousEnableCompression = _options.WebSocketCompressionEnabled };
            WebSocket? downstream;
            try
            {
                downstream = await ctx.WebSockets.AcceptWebSocketAsync(wsAccept);
            }
            catch (Exception ex)
            {
                // Release reserved slot on accept failure
                if (reservedWsSlot)
                {
                    Interlocked.Decrement(ref _wsCurrentConnections);
                    UpdateWsGauges();
                    reservedWsSlot = false;
                }

                ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
                await ctx.Response.WriteAsync($"WebSocket accept failed: {ex.Message}");
                return;
            }

            // Mark active connection for this browserId to prevent mid-session recycle
            _pool.MarkConnectionStart(browserId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);

            // Enrich logs with run context once available
            using var scopeRunCtx = LoggingScopes.Begin(logger, runId, browserId, runName);

            try
            {
                // WS log backpressure: bounded channel + drop policy, single consumer HTTP poster
                var logChannel = Channel.CreateBounded<(string direction, string text)>(
                    new BoundedChannelOptions(_options.WebSocketLogChannelCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait // we manage drops explicitly
                    });
                var hubBase = _options.HubUrl.TrimEnd('/') ?? "";
                var postUrl = string.IsNullOrEmpty(hubBase) ? null : $"{hubBase}/results/browser/{browserId}/commands";
                var dropPolicyLabel = _options.WebSocketLogDropPolicy == WorkerOptions.WsDropPolicy.DropOldest
                    ? "DropOldest"
                    : "DropNewest";

                void EnqueueLog(string direction, string message)
                {
                    try
                    {
                        if (postUrl is null)
                        {
                            return;
                        }

                        var item = (direction, message);
                        if (!logChannel.Writer.TryWrite(item))
                        {
                            if (_options.WebSocketLogDropPolicy == WorkerOptions.WsDropPolicy.DropOldest)
                            {
                                if (logChannel.Reader.TryRead(out _))
                                {
                                    WsLogDroppedCounter.WithLabels(_options.NodeId, direction, dropPolicyLabel, "full")
                                        .Inc();
                                }

                                if (!logChannel.Writer.TryWrite(item))
                                {
                                    WsLogDroppedCounter.WithLabels(_options.NodeId, direction, dropPolicyLabel, "full")
                                        .Inc();
                                }
                            }
                            else
                            {
                                WsLogDroppedCounter.WithLabels(_options.NodeId, direction, dropPolicyLabel, "full")
                                    .Inc();
                            }
                        }
                    }
                    catch
                    {
                        // ignore enqueue errors
                    }
                }

                var logConsumer = Task.Run(async () =>
                {
                    if (postUrl is null)
                    {
                        return;
                    }

                    try
                    {
                        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
                        client.DefaultRequestHeaders.Remove("x-hub-secret");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", _options.NodeSecret);
                        if (!string.IsNullOrWhiteSpace(runId))
                        {
                            client.DefaultRequestHeaders.Remove("Correlation-Id");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Correlation-Id", runId!);
                            client.DefaultRequestHeaders.Remove("x-run-id");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("x-run-id", runId!);
                        }

                        if (!string.IsNullOrWhiteSpace(runName))
                        {
                            client.DefaultRequestHeaders.Remove("x-run-name");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("x-run-name", runName!);
                        }

                        while (await logChannel.Reader.WaitToReadAsync(cts.Token))
                        {
                            while (logChannel.Reader.TryRead(out var item))
                            {
                                try
                                {
                                    var payload = new { item.text, item.direction, ts = DateTime.UtcNow.ToString("O") };
                                    var json = JsonSerializer.Serialize(payload);
                                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                                    await client.PostAsync(postUrl, content, cts.Token);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch
                                {
                                    // on post-failure, drop silently
                                    WsLogDroppedCounter.WithLabels(_options.NodeId, item.direction, dropPolicyLabel,
                                        "canceled").Inc();
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // normal on shutdown
                    }
                    catch
                    {
                        // ignore
                    }
                }, cts.Token);

                var lastActivity = DateTime.UtcNow;

                void Touch()
                {
                    try { lastActivity = DateTime.UtcNow; }
                    catch { }
                }

                var idleTimeout = TimeSpan.FromSeconds(_options.WebSocketIdleTimeoutSeconds);

                // WS proxy backpressure: bounded channels for both directions
                var proxyDropPolicyLabel = _options.WebSocketProxyDropPolicy == WorkerOptions.WsDropPolicy.DropOldest
                    ? "DropOldest"
                    : "DropNewest";
                var c2sChannel = Channel.CreateBounded<Frame>(
                    new BoundedChannelOptions(_options.WebSocketProxyChannelCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        FullMode = BoundedChannelFullMode.Wait // we handle drops explicitly
                    });
                var s2cChannel = Channel.CreateBounded<Frame>(
                    new BoundedChannelOptions(_options.WebSocketProxyChannelCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                var dropOldestPolicy = _options.WebSocketProxyDropPolicy == WorkerOptions.WsDropPolicy.DropOldest;

                bool TryEnqueue(string direction, Channel<Frame> ch, Frame item)
                {
                    if (ch.Writer.TryWrite(item))
                    {
                        return true;
                    }

                    if (dropOldestPolicy)
                    {
                        if (ch.Reader.TryRead(out _))
                        {
                            WsProxyDroppedCounter.WithLabels(_options.NodeId, direction, proxyDropPolicyLabel, "full")
                                .Inc();
                        }

                        if (!ch.Writer.TryWrite(item))
                        {
                            WsProxyDroppedCounter.WithLabels(_options.NodeId, direction, proxyDropPolicyLabel, "full")
                                .Inc();
                            return false;
                        }

                        return true;
                    }

                    WsProxyDroppedCounter.WithLabels(_options.NodeId, direction, proxyDropPolicyLabel, "full").Inc();
                    return false;
                }

                // Start readers (assemble frames, enforce limits, log text), writers forward frames
                var t1 = PumpRead(downstream, upstream, c2sChannel, "c2s", cts.Token, msg => EnqueueLog("c2s", msg),
                    _options.WebSocketMaxMessageBytes, Touch, TryEnqueue);
                var t2 = PumpRead(upstream, downstream, s2cChannel, "s2c", cts.Token, msg => EnqueueLog("s2c", msg),
                    _options.WebSocketMaxMessageBytes, Touch, TryEnqueue);
                var w1 = ForwardWriter(c2sChannel, upstream, cts.Token);
                var w2 = ForwardWriter(s2cChannel, downstream, cts.Token);

                var borrowIdleTtl = TimeSpan.FromSeconds(Math.Max(10, _options.BorrowIdleTimeoutSeconds));
                var lastIdleRefresh = DateTime.MinValue;
                var idleWatch = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            try { await Task.Delay(TimeSpan.FromSeconds(1), cts.Token); }
                            catch { }

                            // Refresh borrow_idle TTL at most once per second while connection active
                            if ((DateTime.UtcNow - lastIdleRefresh).TotalSeconds >= 1)
                            {
                                try { await _db.KeyExpireAsync($"borrow_idle:{browserId}", borrowIdleTtl); }
                                catch { }

                                lastIdleRefresh = DateTime.UtcNow;
                            }

                            if (DateTime.UtcNow - lastActivity > idleTimeout)
                            {
                                try
                                {
                                    await downstream.CloseAsync(WebSocketCloseStatus.NormalClosure, "Idle timeout",
                                        CancellationToken.None);
                                }
                                catch { }

                                try
                                {
                                    await upstream.CloseAsync(WebSocketCloseStatus.NormalClosure, "Idle timeout",
                                        CancellationToken.None);
                                }
                                catch { }

                                try { await cts.CancelAsync(); }
                                catch { }

                                break;
                            }
                        }
                    }
                    catch { }
                }, cts.Token);

                await Task.WhenAny(t1, t2, idleWatch);
            }
            finally
            {
                try { await cts.CancelAsync(); }
                catch { }

                // On proxied WS close, delete TTL/idle keys so Hub can reclaim promptly
                try { await _db.KeyDeleteAsync(RedisKeys.BorrowTtl(browserId)); }
                catch { }

                try { await _db.KeyDeleteAsync($"borrow_idle:{browserId}"); }
                catch { }

                _pool.MarkConnectionEnd(browserId);

                // Release reserved WS slot and update metrics
                if (reservedWsSlot)
                {
                    Interlocked.Decrement(ref _wsCurrentConnections);
                    UpdateWsGauges();
                }

                // On client WS disconnect, attempt to auto-return the browser to the Hub to finalize the run
                try
                {
                    if (_pool.TryFindLabelByBrowserId(browserId, out var labelKey) &&
                        !string.IsNullOrWhiteSpace(labelKey))
                    {
                        var hubBase = _options.HubUrl?.TrimEnd('/') ?? string.Empty;
                        if (!string.IsNullOrEmpty(hubBase))
                        {
                            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
                            client.DefaultRequestHeaders.Remove("x-hub-secret");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", _options.NodeSecret);
                            if (!string.IsNullOrWhiteSpace(runId))
                            {
                                // propagate correlation for attribution
                                client.DefaultRequestHeaders.Remove("Correlation-Id");
                                client.DefaultRequestHeaders.TryAddWithoutValidation("Correlation-Id", runId!);
                                client.DefaultRequestHeaders.Remove("x-run-id");
                                client.DefaultRequestHeaders.TryAddWithoutValidation("x-run-id", runId!);
                            }

                            if (!string.IsNullOrWhiteSpace(runName))
                            {
                                client.DefaultRequestHeaders.Remove("x-run-name");
                                client.DefaultRequestHeaders.TryAddWithoutValidation("x-run-name", runName!);
                            }

                            var url = $"{hubBase}/session/return";
                            var payload = new { labelKey, browserId };
                            var json = JsonSerializer.Serialize(payload);
                            using var content = new StringContent(json, Encoding.UTF8, "application/json");
                            try { await client.PostAsync(url, content, cts.Token); }
                            catch
                            {
                                /* swallow errors; cleanup is best-effort */
                            }
                        }
                    }
                }
                catch { }
            }

            return;


            static async Task PumpRead(WebSocket src, WebSocket dst, Channel<Frame> outChannel, string direction,
                CancellationToken ct,
                Action<string>? onTextMessage, int maxMessageBytes, Action? onAnyActivity,
                Func<string, Channel<Frame>, Frame, bool> tryEnqueue)
            {
                var buffer = new byte[32 * 1024];
                var sb = new StringBuilder();
                var currentMessageBytes = 0;
                while (true)
                {
                    var result = await src.ReceiveAsync(buffer, ct);
                    onAnyActivity?.Invoke();

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await dst.CloseAsync(src.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                src.CloseStatusDescription, ct);
                        }
                        catch { }

                        break;
                    }

                    // Track aggregated message size across frames without allowing integer overflow
                    if (result.Count < 0 || maxMessageBytes - currentMessageBytes < result.Count)
                    {
                        // Close both ends due to MessageTooBig
                        try
                        {
                            await src.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large",
                                CancellationToken.None);
                        }
                        catch { }

                        try
                        {
                            await dst.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large",
                                CancellationToken.None);
                        }
                        catch { }

                        break;
                    }

                    currentMessageBytes += result.Count;

                    // Enqueue the frame to the channel (copy buffer slice)
                    try
                    {
                        var data = new byte[result.Count];
                        Buffer.BlockCopy(buffer, 0, data, 0, result.Count);
                        var frame = new Frame(data, result.MessageType, result.EndOfMessage);
                        tryEnqueue(direction, outChannel, frame);
                    }
                    catch { }

                    if (result.EndOfMessage)
                    {
                        currentMessageBytes = 0; // reset for the next message
                    }

                    // Capture Playwright protocol text messages (assembled on EndOfMessage)
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        try
                        {
                            var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            sb.Append(chunk);
                            if (result.EndOfMessage)
                            {
                                var full = sb.ToString();
                                sb.Clear();
                                if (!string.IsNullOrWhiteSpace(full))
                                {
                                    try { onTextMessage?.Invoke(full); }
                                    catch { }
                                }
                            }
                        }
                        catch
                        {
                            /* ignore capture errors */
                        }
                    }
                }
            }

            static async Task ForwardWriter(Channel<Frame> channel, WebSocket dst, CancellationToken ct)
            {
                try
                {
                    while (await channel.Reader.WaitToReadAsync(ct))
                    {
                        while (channel.Reader.TryRead(out var frame))
                        {
                            try
                            {
                                await dst.SendAsync(new ArraySegment<byte>(frame.Data), frame.MessageType,
                                    frame.EndOfMessage, ct);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch
                            {
                                // destination likely closed; exit
                                return;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // normal on shutdown
                }
            }
        });

        // Background: watch for Hub-triggered node upgrade command
        _ = Task.Run(async () =>
        {
            while (!appCts.IsCancellationRequested)
            {
                try
                {
                    var key = RedisKeys.NodeUpgrade(_options.NodeId);
                    if (await _db.KeyExistsAsync(key))
                    {
                        try { await _db.KeyDeleteAsync(key); }
                        catch { }

                        // Stop local direct borrows early
                        _acceptingBorrows = false;

                        var removed = 0;
                        var scheduledRecycle = 0;

                        // Withdraw local availability
                        foreach (var labelKey in _options.PoolConfig.Keys)
                        {
                            var availKey = RedisKeys.Available(labelKey);
                            try
                            {
                                var items = await _db.ListRangeAsync(availKey);
                                foreach (var item in items)
                                {
                                    var sItem = item.ToString();
                                    if (sItem.Contains($"\"nodeId\":\"{_options.NodeId}\"", StringComparison.Ordinal))
                                    {
                                        await _db.ListRemoveAsync(availKey, item);
                                        removed++;
                                    }
                                }
                            }
                            catch { }
                        }

                        // Schedule recycle for idle sidecars
                        foreach (var kv in _pool.Pools)
                        {
                            var map = kv.Value;
                            foreach (var bid in map.Keys)
                            {
                                try
                                {
                                    if (!_pool.HasActiveConnection(bid))
                                    {
                                        await _db.StringSetAsync(RedisKeys.Recycle(bid), "1", TimeSpan.FromMinutes(5));
                                        scheduledRecycle++;
                                    }
                                }
                                catch { }
                            }
                        }

                        // Drain active sessions up to timeout
                        int drainSeconds;
                        try
                        {
                            drainSeconds =
                                int.TryParse(Environment.GetEnvironmentVariable("WORKER_DRAIN_TIMEOUT_SECONDS"),
                                    out var s2)
                                    ? Math.Max(0, s2)
                                    : 30;
                        }
                        catch { drainSeconds = 30; }

                        var until = DateTime.UtcNow.AddSeconds(drainSeconds);
                        try
                        {
                            while (DateTime.UtcNow < until)
                            {
                                if (!_pool.HasAnyActiveConnections())
                                {
                                    break;
                                }

                                await Task.Delay(250, appCts.Token);
                            }
                        }
                        catch { }

                        if (_pool.HasAnyActiveConnections())
                        {
                            try { _pool.KillAll(); }
                            catch { }
                        }

                        await _pool.InitializeAsync();
                    }
                }
                catch { }

                try { await Task.Delay(1000, appCts.Token); }
                catch { }
            }
        }, appCts.Token);

        // Read port from the environment variable or use default
        var httpPort = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:5000";

        // Link app shutdown to appCts cancellation
        using var registration = appCts.Token.Register(() =>
        {
            try { app.StopAsync().GetAwaiter().GetResult(); }
            catch { }
        });

        await app.RunAsync(httpPort);
    }

    private readonly record struct Frame(byte[] Data, WebSocketMessageType MessageType, bool EndOfMessage);
}
