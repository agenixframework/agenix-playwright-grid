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
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class WebServerHost
{
    private readonly IDatabase _db;
    private readonly IMetricsPort _metrics;
    private readonly WorkerOptions _options;
    private readonly PoolManager _pool;

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

        app.UseMetricServer();
        app.UseHttpMetrics();

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                appCts.Cancel();
            }
            catch { }

            try { _pool.CleanupAllAsync().GetAwaiter().GetResult(); }
            catch { }

            try { _pool.KillAll(); }
            catch { }
        });

        app.MapPost("/borrow/{labelKey}", async (string labelKey, HttpRequest req) =>
        {
            if (!req.Headers.TryGetValue("x-node-secret", out var s) || s.FirstOrDefault() != _options.NodeNodeSecret)
            {
                return Results.Unauthorized();
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

        app.UseWebSockets();

        // Public Playwright WS proxy
        // This endpoint accepts client WebSocket connections and forwards them to the internal
        // Playwright browser server (wsEndpoint from launchServer). While proxying, it assembles
        // text frames into full protocol messages and forwards them (non-blocking) to the Hub
        // via HTTP POST /results/browser/{browserId}/commands. This is how we intercept the
        // Playwright protocol without modifying Playwright itself.
        app.Map("/ws/{browserId}", async (HttpContext ctx, string browserId) =>
        {
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
            upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

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
                void Forward(string direction, string message)
                {
                    try
                    {
                        var hubUrl = _options.HubUrl?.TrimEnd('/') ?? "";
                        if (string.IsNullOrEmpty(hubUrl))
                        {
                            return;
                        }

                        var url = $"{hubUrl}/results/browser/{browserId}/commands";
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
                                client.DefaultRequestHeaders.Remove("x-hub-secret");
                                client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret",
                                    _options.NodeSecret);
                                // Add correlation header if runId is known for this browserId
                                if (!string.IsNullOrWhiteSpace(runId))
                                {
                                    client.DefaultRequestHeaders.Remove("Correlation-Id");
                                    client.DefaultRequestHeaders.TryAddWithoutValidation("Correlation-Id", runId);
                                }

                                var payload = new { text = message, direction, ts = DateTime.UtcNow.ToString("O") };
                                var json = JsonSerializer.Serialize(payload);
                                using var content =
                                    new StringContent(json, Encoding.UTF8, "application/json");
                                await client.PostAsync(url, content, ctx.RequestAborted);
                            }
                            catch { }
                        }, ctx.RequestAborted);
                    }
                    catch { }
                }

                var t1 = Pump(downstream, upstream, cts.Token, msg => Forward("c2s", msg));
                var t2 = Pump(upstream, downstream, cts.Token, msg => Forward("s2c", msg));
                await Task.WhenAny(t1, t2);
            }
            finally
            {
                try { await cts.CancelAsync(); }
                catch { }

                _pool.MarkConnectionEnd(browserId);
            }

            return;

            static async Task Pump(WebSocket src, WebSocket dst, CancellationToken ct, Action<string>? onTextMessage)
            {
                var buffer = new byte[32 * 1024];
                var sb = new StringBuilder();
                while (true)
                {
                    var result = await src.ReceiveAsync(buffer, ct);
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

                    // Forward the frame as-is first to minimize latency
                    await dst.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType,
                        result.EndOfMessage, ct);

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
        });

        await app.RunAsync("http://0.0.0.0:5000");
    }
}
