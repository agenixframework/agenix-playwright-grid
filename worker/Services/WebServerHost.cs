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

using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using Microsoft.Extensions.Logging;

namespace WorkerService.Services;

public sealed class WebServerHost
{
    private readonly record struct Frame(byte[] Data, WebSocketMessageType MessageType, bool EndOfMessage);
    private static readonly Counter WsLogDroppedCounter = Metrics.CreateCounter(
        "worker_ws_log_dropped_messages_total",
        "Dropped Playwright WS log messages due to backpressure",
        "node", "direction", "policy", "reason");
    private static readonly Counter WsProxyDroppedCounter = Metrics.CreateCounter(
        "worker_ws_proxy_dropped_frames_total",
        "Dropped WebSocket frames in proxy due to backpressure",
        "node", "direction", "policy", "reason");
    private readonly IDatabase _db;
    private readonly IMetricsPort _metrics;
    private readonly WorkerOptions _options;
    private readonly PoolManager _pool;
    private volatile bool _acceptingBorrows = true;
    private volatile bool _shuttingDown;

    public WebServerHost(WorkerOptions options, IMetricsPort metrics, PoolManager pool, IDatabase db)
    {
        _options = options;
        _metrics = metrics;
        _pool = pool;
        _db = db;
    }

    public async Task RunAsync(string[] args, CancellationTokenSource appCts)
    {
        var builder = WebApplication.CreateBuilder(args);
        // Suppress verbose framework logs like OkObjectResult JSON writing and EndpointMiddleware exec logs
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        // Apply environment-driven log levels (global + per-category overrides)
        LoggingConfigurator.ApplyFromEnvironment(builder.Logging, builder.Configuration);

        // OpenTelemetry setup (env-driven exporters)
        var workerServiceName = "playwright-worker";
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

        app.UseMetricServer();
        app.UseHttpMetrics();

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            _shuttingDown = true;
            _acceptingBorrows = false;
            try { logger.LogInformation("[worker] ApplicationStopping: initiating graceful drain"); } catch { }

            try { appCts.Cancel(); } catch { }

            // Drain active WebSocket sessions up to a timeout
            int drainSeconds = 0;
            try
            {
                drainSeconds = int.TryParse(Environment.GetEnvironmentVariable("WORKER_DRAIN_TIMEOUT_SECONDS"), out var s)
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

            try { _pool.CleanupAllAsync().GetAwaiter().GetResult(); } catch { }

            // If sessions are still active after timeout, force-kill sidecars
            if (_poolHasAnyActiveConnections())
            {
                try { logger.LogWarning("[worker] Drain timeout reached; forcing sidecar shutdown"); } catch { }
                try { _pool.KillAll(); } catch { }
            }
        });

        bool _poolHasAnyActiveConnections()
        {
            try { return _pool.HasAnyActiveConnections(); }
            catch { return false; }
        }

        app.MapPost("/borrow/{labelKey}", async (string labelKey, HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("x-node-secret", out var s) || s.FirstOrDefault() != _options.NodeNodeSecret)
            {
                return Results.Unauthorized();
            }

            if (!_acceptingBorrows)
            {
                try { req.HttpContext.Response.Headers.Append("Retry-After", "30"); } catch { }
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // Respect maintenance mode: deny borrow while maintenance is active for this label
            try
            {
                if (_db.KeyExists($"maintenance:{labelKey}"))
                {
                    return Results.StatusCode(503);
                }
            }
            catch { }

            if (!_pool.TryGetFirstSlot(labelKey, out var browserId, out var slot))
            {
                return Results.StatusCode(503);
            }

            _metrics.IncrementBorrow(_options.NodeId, labelKey);
            var availableKey = $"available:{labelKey}";
            _metrics.SetPoolAvailable(_options.NodeId, labelKey, await _db.ListLengthAsync(availableKey));

            var respObj = new
            {
                nodeId = _options.NodeId,
                browserId,
                webSocketEndpoint = slot.PublicWs,
                browserType = slot.BrowserType,
                labels = _options.Labels.ToDictionary(k => k.Key, v => v.Value)
            };
            return Results.Ok(respObj);
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

            var availableKey = $"available:{labelKey}";
            _metrics.SetPoolAvailable(_options.NodeId, labelKey, await _db.ListLengthAsync(availableKey));
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/health", () => Results.Ok(new { node = _options.NodeId, pools = _pool.Pools.Keys.ToArray() }));
        app.MapGet("/health/ready", async () =>
        {
            if (_shuttingDown)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            try
            {
                var timeoutMs = int.TryParse(Environment.GetEnvironmentVariable("REDIS_HEALTH_TIMEOUT_MS"), out var ms)
                    ? Math.Max(100, ms)
                    : 1000;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var pingTask = _db.PingAsync();
                var completed = await Task.WhenAny(pingTask, Task.Delay(timeoutMs));
                if (completed != pingTask)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
                await pingTask; // propagate any errors
                sw.Stop();
                return Results.Ok(new { status = "ready", redis = new { pingMs = sw.ElapsedMilliseconds } });
            }
            catch
            {
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
                    try { alive = !slot.Proc.HasExited; } catch { alive = false; }
                    try { pid = slot.Proc.Id; } catch { pid = 0; }
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
                        failures = bo.failures,
                        lastFailureUtc = bo.lastFailureUtc,
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
            using var _scopeWs = WorkerService.Infrastructure.LoggingScopes.Begin(logger, browserId: browserId);
            logger.LogInformation("WS connect start {BrowserId}", browserId);
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

            using var upstream = new ClientWebSocket();
            upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(_options.WebSocketPingIntervalSeconds);

            try
            {
                await upstream.ConnectAsync(new Uri(slot.InternalWs), ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
                await ctx.Response.WriteAsync($"Upstream connect failed: {ex.Message}");
                return;
            }

            using var downstream = await ctx.WebSockets.AcceptWebSocketAsync();

            // Mark active connection for this browserId to prevent mid-session recycle
            _pool.MarkConnectionStart(browserId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);

            // Try to resolve runId for this browser from Redis mapping to propagate correlation
            string? runId = null;
            try
            {
                var v = await _db.StringGetAsync($"browser_run:{browserId}");
                if (!v.IsNullOrEmpty)
                {
                    runId = v.ToString();
                }
            }
            catch { }

            try
            {
                // WS log backpressure: bounded channel + drop policy, single consumer HTTP poster
                var logChannel = Channel.CreateBounded<(string direction, string text)>(new BoundedChannelOptions(_options.WebSocketLogChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait // we manage drops explicitly
                });
                var hubBase = _options.HubUrl?.TrimEnd('/') ?? "";
                var postUrl = string.IsNullOrEmpty(hubBase) ? null : $"{hubBase}/results/browser/{browserId}/commands";
                var dropPolicyLabel = _options.WebSocketLogDropPolicy == WorkerOptions.WsDropPolicy.DropOldest ? "DropOldest" : "DropNewest";

                void EnqueueLog(string direction, string message)
                {
                    try
                    {
                        if (postUrl is null) return;
                        var item = (direction, message);
                        if (!logChannel.Writer.TryWrite(item))
                        {
                            if (_options.WebSocketLogDropPolicy == WorkerOptions.WsDropPolicy.DropOldest)
                            {
                                if (logChannel.Reader.TryRead(out _))
                                {
                                    WsLogDroppedCounter.WithLabels(_options.NodeId, direction, dropPolicyLabel, "full").Inc();
                                }
                                if (!logChannel.Writer.TryWrite(item))
                                {
                                    WsLogDroppedCounter.WithLabels(_options.NodeId, direction, dropPolicyLabel, "full").Inc();
                                }
                            }
                            else
                            {
                                WsLogDroppedCounter.WithLabels(_options.NodeId, direction, dropPolicyLabel, "full").Inc();
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
                    if (postUrl is null) return;
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
                        }

                        while (await logChannel.Reader.WaitToReadAsync(cts.Token))
                        {
                            while (logChannel.Reader.TryRead(out var item))
                            {
                                try
                                {
                                    var payload = new { text = item.text, direction = item.direction, ts = DateTime.UtcNow.ToString("O") };
                                    var json = JsonSerializer.Serialize(payload);
                                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                                    await client.PostAsync(postUrl, content, cts.Token);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch
                                {
                                    // on post failure, drop silently
                                    WsLogDroppedCounter.WithLabels(_options.NodeId, item.direction, dropPolicyLabel, "canceled").Inc();
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
                void Touch() { try { lastActivity = DateTime.UtcNow; } catch { } }

                var idleTimeout = TimeSpan.FromSeconds(_options.WebSocketIdleTimeoutSeconds);

                // WS proxy backpressure: bounded channels for both directions
                var proxyDropPolicyLabel = _options.WebSocketProxyDropPolicy == WorkerOptions.WsDropPolicy.DropOldest ? "DropOldest" : "DropNewest";
                var c2sChannel = Channel.CreateBounded<Frame>(new BoundedChannelOptions(_options.WebSocketProxyChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait // we handle drops explicitly
                });
                var s2cChannel = Channel.CreateBounded<Frame>(new BoundedChannelOptions(_options.WebSocketProxyChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

                var dropOldestPolicy = _options.WebSocketProxyDropPolicy == WorkerOptions.WsDropPolicy.DropOldest;

                bool TryEnqueue(string direction, Channel<Frame> ch, Frame item)
                {
                    if (ch.Writer.TryWrite(item)) return true;
                    if (dropOldestPolicy)
                    {
                        if (ch.Reader.TryRead(out _))
                        {
                            WsProxyDroppedCounter.WithLabels(_options.NodeId, direction, proxyDropPolicyLabel, "full").Inc();
                        }
                        if (!ch.Writer.TryWrite(item))
                        {
                            WsProxyDroppedCounter.WithLabels(_options.NodeId, direction, proxyDropPolicyLabel, "full").Inc();
                            return false;
                        }
                        return true;
                    }
                    else
                    {
                        WsProxyDroppedCounter.WithLabels(_options.NodeId, direction, proxyDropPolicyLabel, "full").Inc();
                        return false;
                    }
                }

                // Start readers (assemble frames, enforce limits, log text), writers forward frames
                var t1 = PumpRead(downstream, upstream, c2sChannel, "c2s", cts.Token, msg => EnqueueLog("c2s", msg), _options.WebSocketMaxMessageBytes, Touch, TryEnqueue);
                var t2 = PumpRead(upstream, downstream, s2cChannel, "s2c", cts.Token, msg => EnqueueLog("s2c", msg), _options.WebSocketMaxMessageBytes, Touch, TryEnqueue);
                var w1 = ForwardWriter(c2sChannel, upstream, cts.Token);
                var w2 = ForwardWriter(s2cChannel, downstream, cts.Token);

                var idleWatch = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            try { await Task.Delay(TimeSpan.FromSeconds(1), cts.Token); } catch { }
                            if (DateTime.UtcNow - lastActivity > idleTimeout)
                            {
                                try { await downstream.CloseAsync(WebSocketCloseStatus.NormalClosure, "Idle timeout", CancellationToken.None); } catch { }
                                try { await upstream.CloseAsync(WebSocketCloseStatus.NormalClosure, "Idle timeout", CancellationToken.None); } catch { }
                                try { await cts.CancelAsync(); } catch { }
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

                _pool.MarkConnectionEnd(browserId);
            }

            return;


            static async Task PumpRead(WebSocket src, WebSocket dst, Channel<Frame> outChannel, string direction, CancellationToken ct,
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
                    if (result.Count < 0 || (maxMessageBytes - currentMessageBytes) < result.Count)
                    {
                        // Close both ends due to MessageTooBig
                        try { await src.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None); } catch { }
                        try { await dst.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None); } catch { }
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
                                    try { onTextMessage?.Invoke(full); } catch { }
                                }
                            }
                        }
                        catch { /* ignore capture errors */ }
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
                                await dst.SendAsync(new ArraySegment<byte>(frame.Data), frame.MessageType, frame.EndOfMessage, ct);
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

        await app.RunAsync("http://0.0.0.0:5000");
    }
}
