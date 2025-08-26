using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using Prometheus;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

public static class EndpointMappingExtensions
{
    public static void MapHubEndpoints(this WebApplication app)
    {
        var config = app.Configuration;
        var services = app.Services;

        var db = services.GetRequiredService<IDatabase>();
        var mux = services.GetRequiredService<IConnectionMultiplexer>();
        var resultsStore = services.GetRequiredService<IResultsStore>();
        var resultsHubCtx = services.GetRequiredService<IHubContext<ResultsHub, IResultsClient>>();

        var hubRunnerSecret = config["HUB_RUNNER_SECRET"] ?? "runner-secret";
        var hubNodeSecret = config["HUB_NODE_SECRET"] ?? "node-secret";
        var nodeTimeoutSeconds = int.TryParse(config["HUB_NODE_TIMEOUT"], out var t) ? t : 60;
        var dashboardUrl = config["DASHBOARD_URL"] ?? "http://localhost:3001";

        // Borrow matching configuration
        var enableTrailingFallback =
            !bool.TryParse(config["HUB_BORROW_TRAILING_FALLBACK"], out var tf) || tf; // default true
        var enablePrefixExpand = !bool.TryParse(config["HUB_BORROW_PREFIX_EXPAND"], out var pe) || pe; // default true
        var enableWildcards = bool.TryParse(config["HUB_BORROW_WILDCARDS"], out var wc) && wc; // default false

        // Metrics used by endpoints
        var borrowRequests = Metrics.CreateCounter(
            "hub_borrow_requests_total",
            "Total borrow requests",
            new CounterConfiguration { LabelNames = ["label"] });

        var borrowLatency = Metrics.CreateHistogram(
            "hub_borrow_latency_seconds",
            "Borrow latency",
            new HistogramConfiguration { LabelNames = ["label"] });

        var poolAvailableGauge = Metrics.CreateGauge(
            "hub_pool_available_total",
            "Available endpoints",
            new GaugeConfiguration { LabelNames = ["label"] });

        // Simple Lua scripts operate on available:{labelKey}
        var luaFindPop = @"
local listKey = KEYS[1]
local inuseKey = KEYS[2]
local len = redis.call('LLEN', listKey)
if len == 0 then return nil end
local item = redis.call('LPOP', listKey)
if item then redis.call('RPUSH', inuseKey, item); return item end
return nil
";

        var luaReturn = @"
local inuse = KEYS[1]
local avail = KEYS[2]
local browserId = ARGV[1]
local list = redis.call('LRANGE', inuse, 0, -1)
for i,item in ipairs(list) do
  if string.find(item, browserId, 1, true) then
    redis.call('LREM', inuse, 1, item)
    redis.call('RPUSH', avail, item)
    return item
  end
end
return nil
";

        // Node register (nodes set and node:{nodeId} hash)
        app.MapPost("/node/register", async (HttpRequest req) =>
        {
            var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var startedAt = DateTime.UtcNow;

            try
            {
                if (!CheckSecret(req, "x-hub-secret", hubNodeSecret))
                {
                    Console.WriteLine($"[Register] 401 Unauthorized from {remoteIp}");
                    return Results.Unauthorized();
                }

                Dictionary<string, object?>? body;
                try
                {
                    body = await req.ReadFromJsonAsync<Dictionary<string, object?>>();
                }
                catch (JsonException jex)
                {
                    Console.WriteLine($"[Register] 400 Invalid JSON from {remoteIp}: {jex.Message}");
                    return Results.BadRequest("invalid JSON");
                }

                if (body is null)
                {
                    Console.WriteLine($"[Register] 400 Empty body from {remoteIp}");
                    return Results.BadRequest("empty body");
                }

                var nodeIdRaw = body.GetValueOrDefault("NodeId")?.ToString();
                if (string.IsNullOrWhiteSpace(nodeIdRaw) && body.TryGetValue("nodeId", out var nidTok))
                {
                    nodeIdRaw = nidTok?.ToString();
                }

                var nodeId = string.IsNullOrWhiteSpace(nodeIdRaw) ? null : nodeIdRaw.Trim();
                if (string.IsNullOrEmpty(nodeId))
                {
                    Console.WriteLine($"[Register] 400 Missing NodeId from {remoteIp}");
                    return Results.BadRequest("missing NodeId");
                }

                var apps = Array.Empty<string>();
                if (body.TryGetValue("Apps", out var a) || body.TryGetValue("apps", out a))
                {
                    apps = a switch
                    {
                        JsonElement { ValueKind: JsonValueKind.Array } je => je.Deserialize<string[]>() ?? [],
                        JsonElement jeStr when jeStr.ValueKind == JsonValueKind.String =>
                            (jeStr.GetString() ?? string.Empty).Split(',',
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        string s => s.Split(',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        _ => apps
                    };
                }

                var capacity = int.TryParse(body.GetValueOrDefault("Capacity")?.ToString(), out var c) ? c : 1;
                var labels =
                    (body.GetValueOrDefault("Labels") as JsonElement?)?.Deserialize<Dictionary<string, string>>() ??
                    new Dictionary<string, string>();

                var key = $"node:{nodeId}";
                var existed = await db.KeyExistsAsync(key);

                // Optional base URL where the worker exposes its HTTP endpoints
                string? baseUrl = null;
                if (body.TryGetValue("BaseUrl", out var bu) || body.TryGetValue("baseUrl", out bu))
                {
                    baseUrl = bu?.ToString();
                }

                var meta = new Dictionary<string, string?>
                {
                    ["Apps"] = string.Join(',', apps),
                    ["Capacity"] = Math.Max(0, capacity).ToString(),
                    ["Labels"] = JsonSerializer.Serialize(labels),
                    ["LastSeen"] = DateTime.UtcNow.ToString("o"),
                    ["BaseUrl"] = baseUrl
                };

                foreach (var kv in meta)
                {
                    await db.HashSetAsync(key, kv.Key, kv.Value);
                }

                await db.SetAddAsync("nodes", nodeId);

                // Provide an initial alive TTL to prevent immediate Sweeper expiry after registration
                var ttlSeconds = Math.Max(90, nodeTimeoutSeconds);
                await db.StringSetAsync($"node_alive:{nodeId}", "1", TimeSpan.FromSeconds(ttlSeconds));

                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                var sampleApps = string.Join(',', apps.Take(3));
                var appPreview = apps.Length <= 3 ? sampleApps : $"{sampleApps}(+{apps.Length - 3})";

                Console.WriteLine(
                    $"[Register] {(existed ? "update" : "new")} nodeId={nodeId} apps={apps.Length}:{appPreview} capacity={capacity} labels={labels.Count} ip={remoteIp} in {elapsedMs}ms");

                return Results.Ok(new { registered = nodeId });
            }
            catch (Exception ex)
            {
                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                Console.WriteLine($"[Register] 500 Error from {remoteIp} after {elapsedMs}ms: {ex}");
                return Results.Problem("registration failed");
            }
        });

        // Borrow expects { "labelKey": "App:Chromium:Staging" }
        app.MapPost("/session/borrow", async (HttpRequest req) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            var body = await req.ReadFromJsonAsync<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            if (!body.TryGetValue("labelKey", out var labelKey) || string.IsNullOrEmpty(labelKey))
            {
                return Results.BadRequest("missing labelKey");
            }

            // Metrics use requested label key for request/latency tracking
            borrowRequests.WithLabels(labelKey).Inc();
            using var tmr = borrowLatency.WithLabels(labelKey).NewTimer();

            // Helper that attempts to pop one item from a specific label list
            async Task<JsonElement?> TryBorrowForAsync(string candidate)
            {
                // Maintenance gate: if pool is under maintenance, try to auto-clear if finished;
                // otherwise skip this candidate (treat as unavailable).
                try
                {
                    if (await db.KeyExistsAsync($"maintenance:{candidate}"))
                    {
                        var targetStr = await db.StringGetAsync($"maintenance:target:{candidate}");
                        if (!targetStr.IsNullOrEmpty && long.TryParse(targetStr.ToString(), out var target))
                        {
                            var availNow = await db.ListLengthAsync($"available:{candidate}");
                            var inuseNow = await db.ListLengthAsync($"inuse:{candidate}");
                            if (inuseNow == 0 && availNow == target)
                            {
                                try
                                {
                                    await db.KeyDeleteAsync($"maintenance:{candidate}");
                                    await db.KeyDeleteAsync($"maintenance:target:{candidate}");
                                    await db.KeyDeleteAsync($"maintenance:snap_avail:{candidate}");
                                    await db.KeyDeleteAsync($"maintenance:snap_inuse:{candidate}");
                                    await db.KeyDeleteAsync($"maintenance:since:{candidate}");
                                }
                                catch { }
                            }
                        }

                        // If still under maintenance, skip borrowing from this candidate
                        if (await db.KeyExistsAsync($"maintenance:{candidate}"))
                        {
                            return null;
                        }
                    }
                }
                catch { }

                var listKey = $"available:{candidate}";
                var inuseKey = $"inuse:{candidate}";
                var res = await db.ScriptEvaluateAsync(luaFindPop, new RedisKey[] { listKey, inuseKey }, []);
                if (res.IsNull)
                {
                    return null;
                }

                // Update gauge for the actual matched label
                var listLenght = await db.ListLengthAsync(listKey);
                poolAvailableGauge.WithLabels(candidate).Set(listLenght);

                // Keep as JsonElement so we can both inspect fields and return it as the response object later.
                using var doc = JsonDocument.Parse(res.ToString());
                return doc.RootElement.Clone();
            }

            // 1) Exact match first
            var item = await TryBorrowForAsync(labelKey);
            if (item is not null)
            {
                // Optional results emission if runId provided via header or query
                var runId = req.Headers["x-run-id"].FirstOrDefault() ?? req.Query["runId"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(runId))
                {
                    var now = DateTime.UtcNow;
                    var ev = new CommandLogEventDto
                    {
                        RunId = runId,
                        TimestampUtc = now,
                        Kind = "Borrow",
                        Message = $"Borrowed for {labelKey}",
                        Props = new Dictionary<string, string> { ["labelKey"] = labelKey }
                    };

                    await resultsStore.AppendCommandAsync(ev);

                    // Upsert and enrich run summary with metadata from borrowed item
                    var run = await resultsStore.GetRunAsync(runId) ?? new ResultRunSummaryDto
                    {
                        RunId = runId,
                        App = labelKey.Split(':').FirstOrDefault() ?? "",
                        Browser = labelKey.Split(':').Skip(1).FirstOrDefault() ?? "Chromium",
                        Env = labelKey.Split(':').Skip(2).FirstOrDefault() ?? "",
                        StartedAtUtc = now
                    };
                    run.Status = "Running";

                    try
                    {
                        string? nodeId = null, browserVer = null, region = null, os = null, pwVer = null;
                        if (item.Value.TryGetProperty("nodeId", out var nodeEl) &&
                            nodeEl.ValueKind == JsonValueKind.String)
                        {
                            nodeId = nodeEl.GetString();
                        }

                        if (item.Value.TryGetProperty("browserVersion", out var bvEl) &&
                            bvEl.ValueKind == JsonValueKind.String)
                        {
                            browserVer = bvEl.GetString();
                        }

                        if (item.Value.TryGetProperty("labels", out var labelsEl) &&
                            labelsEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in labelsEl.EnumerateObject())
                            {
                                if (string.Equals(p.Name, "region", StringComparison.OrdinalIgnoreCase) &&
                                    p.Value.ValueKind == JsonValueKind.String)
                                {
                                    region = p.Value.GetString();
                                }

                                if (string.Equals(p.Name, "os", StringComparison.OrdinalIgnoreCase) &&
                                    p.Value.ValueKind == JsonValueKind.String)
                                {
                                    os = p.Value.GetString();
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(nodeId))
                        {
                            var pwVal = await db.HashGetAsync($"node:{nodeId}", "PlaywrightVersion");
                            if (!pwVal.IsNullOrEmpty)
                            {
                                pwVer = pwVal.ToString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(region) || !string.IsNullOrWhiteSpace(os))
                        {
                            run = run with { Region = region ?? run.Region, OS = os ?? run.OS };
                        }

                        if (!string.IsNullOrWhiteSpace(nodeId))
                        {
                            run.WorkerNodeId = nodeId;
                        }

                        if (!string.IsNullOrWhiteSpace(pwVer))
                        {
                            run.PlaywrightVersion = pwVer;
                        }

                        if (!string.IsNullOrWhiteSpace(browserVer))
                        {
                            run.BrowserVersion = browserVer;
                        }
                    }
                    catch { }

                    await resultsStore.UpsertRunAsync(run);

                    CommandLogEventDto? serverEv = null;
                    try
                    {
                        string? ws = null, bt = null, args = null, bid = null;
                        if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl) &&
                            wsEl.ValueKind == JsonValueKind.String)
                        {
                            ws = wsEl.GetString();
                        }

                        if (item.Value.TryGetProperty("browserType", out var btEl) &&
                            btEl.ValueKind == JsonValueKind.String)
                        {
                            bt = btEl.GetString();
                        }

                        if (item.Value.TryGetProperty("args", out var argsEl) &&
                            argsEl.ValueKind == JsonValueKind.String)
                        {
                            args = argsEl.GetString();
                        }

                        if (item.Value.TryGetProperty("browserId", out var bidEl) &&
                            bidEl.ValueKind == JsonValueKind.String)
                        {
                            bid = bidEl.GetString();
                        }

                        // Map browserId to runId so worker-sourced command logs can be attributed
                        if (!string.IsNullOrWhiteSpace(bid))
                        {
                            await db.StringSetAsync($"browser_run:{bid}", runId, TimeSpan.FromHours(6));
                        }

                        serverEv = new CommandLogEventDto
                        {
                            RunId = runId,
                            TimestampUtc = now.AddMilliseconds(-1),
                            Kind = "ServerLaunch",
                            Message = "Playwright server started",
                            Props = new Dictionary<string, string>()
                        };
                        if (!string.IsNullOrWhiteSpace(ws))
                        {
                            serverEv.Props["wsEndpoint"] = ws;
                        }

                        if (!string.IsNullOrWhiteSpace(bt))
                        {
                            serverEv.Props["browserType"] = bt;
                        }

                        if (!string.IsNullOrWhiteSpace(args))
                        {
                            serverEv.Props["args"] = args;
                        }

                        if (!string.IsNullOrWhiteSpace(bid))
                        {
                            serverEv.Props["browserId"] = bid;
                        }

                        if (!string.IsNullOrWhiteSpace(run.PlaywrightVersion))
                        {
                            serverEv.Props["playwrightVersion"] = run.PlaywrightVersion;
                        }

                        if (!string.IsNullOrWhiteSpace(run.BrowserVersion))
                        {
                            serverEv.Props["browserVersion"] = run.BrowserVersion;
                        }

                        serverEv.Props["labelKey"] = labelKey;
                        await resultsStore.AppendCommandAsync(serverEv);
                    }
                    catch { }

                    var toSend = serverEv is not null ? new[] { serverEv, ev } : new[] { ev };
                    await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(toSend);
                    await resultsHubCtx.Clients.Group($"run:{runId}").RunUpdate(run);
                }

                return Results.Ok(item);
            }

            // Build candidate list according to configuration
            var candidates = new List<string>();

            // 2) Trailing-segment fallback: drop trailing segments from the requested label
            if (enableTrailingFallback)
            {
                var s = labelKey;
                while (true)
                {
                    var idx = s.LastIndexOf(':');
                    if (idx <= 0)
                    {
                        break;
                    }

                    s = s[..idx];
                    if (!string.Equals(s, labelKey, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(s);
                    }
                }
            }

            // 3) Prefix expansion: treat missing trailing segments as ANY by matching more specific pools
            if (enablePrefixExpand)
            {
                var pattern = $"available:{labelKey}:*";
                foreach (var ep in mux.GetEndPoints())
                {
                    var server = mux.GetServer(ep);
                    if (server is null || !server.IsConnected)
                    {
                        continue;
                    }

                    foreach (var key in server.Keys(pattern: pattern))
                    {
                        var k = key.ToString();
                        if (k.StartsWith("available:", StringComparison.Ordinal))
                        {
                            var label = k["available:".Length..];
                            if (!candidates.Contains(label, StringComparer.OrdinalIgnoreCase))
                            {
                                candidates.Add(label);
                            }
                        }
                    }
                }
            }

            // 4) Wildcards: allow '*' in any segment if enabled
            if (enableWildcards && labelKey.Contains('*'))
            {
                var pattern = $"available:{labelKey}";
                foreach (var ep in mux.GetEndPoints())
                {
                    var server = mux.GetServer(ep);
                    if (server is null || !server.IsConnected)
                    {
                        continue;
                    }

                    foreach (var key in server.Keys(pattern: pattern))
                    {
                        var k = key.ToString();
                        if (k.StartsWith("available:", StringComparison.Ordinal))
                        {
                            var label = k["available:".Length..];
                            if (!candidates.Contains(label, StringComparer.OrdinalIgnoreCase))
                            {
                                candidates.Add(label);
                            }
                        }
                    }
                }
            }

            // Sort candidates by specificity: prefer those closest to the requested key (fewest extra segments)
            candidates.Sort((a, b) =>
            {
                int Segments(string s)
                {
                    return s.Split(':').Length;
                }

                var da = Math.Abs(Segments(a) - Segments(labelKey));
                var dbb = Math.Abs(Segments(b) - Segments(labelKey));
                var cmp = da.CompareTo(dbb);
                return cmp != 0 ? cmp : string.Compare(a, b, StringComparison.Ordinal);
            });

            foreach (var c in candidates)
            {
                item = await TryBorrowForAsync(c);
                if (item is not null)
                {
                    var runId = req.Headers["x-run-id"].FirstOrDefault() ?? req.Query["runId"].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(runId))
                    {
                        var now = DateTime.UtcNow;
                        var ev = new CommandLogEventDto
                        {
                            RunId = runId,
                            TimestampUtc = now,
                            Kind = "Borrow",
                            Message = $"Borrowed for {labelKey} via fallback {c}",
                            Props = new Dictionary<string, string> { ["labelKey"] = labelKey, ["matchedLabel"] = c }
                        };
                        await resultsStore.AppendCommandAsync(ev);
                        var run = await resultsStore.GetRunAsync(runId) ?? new ResultRunSummaryDto
                        {
                            RunId = runId!,
                            App = labelKey.Split(':').FirstOrDefault() ?? "",
                            Browser = labelKey.Split(':').Skip(1).FirstOrDefault() ?? "Chromium",
                            Env = labelKey.Split(':').Skip(2).FirstOrDefault() ?? "",
                            StartedAtUtc = now
                        };
                        run.Status = "Running";

                        try
                        {
                            string? nodeId = null, browserVer = null, region = null, os = null, pwVer = null;
                            if (item.Value.TryGetProperty("nodeId", out var nodeEl) &&
                                nodeEl.ValueKind == JsonValueKind.String)
                            {
                                nodeId = nodeEl.GetString();
                            }

                            if (item.Value.TryGetProperty("browserVersion", out var bvEl) &&
                                bvEl.ValueKind == JsonValueKind.String)
                            {
                                browserVer = bvEl.GetString();
                            }

                            if (item.Value.TryGetProperty("labels", out var labelsEl) &&
                                labelsEl.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var p in labelsEl.EnumerateObject())
                                {
                                    if (string.Equals(p.Name, "region", StringComparison.OrdinalIgnoreCase) &&
                                        p.Value.ValueKind == JsonValueKind.String)
                                    {
                                        region = p.Value.GetString();
                                    }

                                    if (string.Equals(p.Name, "os", StringComparison.OrdinalIgnoreCase) &&
                                        p.Value.ValueKind == JsonValueKind.String)
                                    {
                                        os = p.Value.GetString();
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(nodeId))
                            {
                                var pwVal = await db.HashGetAsync($"node:{nodeId}", "PlaywrightVersion");
                                if (!pwVal.IsNullOrEmpty)
                                {
                                    pwVer = pwVal.ToString();
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(region) || !string.IsNullOrWhiteSpace(os))
                            {
                                run = run with { Region = region ?? run.Region, OS = os ?? run.OS };
                            }

                            if (!string.IsNullOrWhiteSpace(nodeId))
                            {
                                run.WorkerNodeId = nodeId;
                            }

                            if (!string.IsNullOrWhiteSpace(pwVer))
                            {
                                run.PlaywrightVersion = pwVer;
                            }

                            if (!string.IsNullOrWhiteSpace(browserVer))
                            {
                                run.BrowserVersion = browserVer;
                            }
                        }
                        catch { }

                        await resultsStore.UpsertRunAsync(run);

                        CommandLogEventDto? serverEv = null;
                        try
                        {
                            string? ws = null, bt = null, args = null, bid = null;
                            if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl) &&
                                wsEl.ValueKind == JsonValueKind.String)
                            {
                                ws = wsEl.GetString();
                            }

                            if (item.Value.TryGetProperty("browserType", out var btEl) &&
                                btEl.ValueKind == JsonValueKind.String)
                            {
                                bt = btEl.GetString();
                            }

                            if (item.Value.TryGetProperty("args", out var argsEl) &&
                                argsEl.ValueKind == JsonValueKind.String)
                            {
                                args = argsEl.GetString();
                            }

                            if (item.Value.TryGetProperty("browserId", out var bidEl) &&
                                bidEl.ValueKind == JsonValueKind.String)
                            {
                                bid = bidEl.GetString();
                            }

                            // Map browserId to runId so worker-sourced command logs can be attributed
                            if (!string.IsNullOrWhiteSpace(bid))
                            {
                                await db.StringSetAsync($"browser_run:{bid}", runId!, TimeSpan.FromHours(6));
                            }

                            serverEv = new CommandLogEventDto
                            {
                                RunId = runId!,
                                TimestampUtc = now.AddMilliseconds(-1),
                                Kind = "ServerLaunch",
                                Message = "Playwright server started",
                                Props = new Dictionary<string, string>()
                            };
                            if (!string.IsNullOrWhiteSpace(ws))
                            {
                                serverEv.Props["wsEndpoint"] = ws;
                            }

                            if (!string.IsNullOrWhiteSpace(bt))
                            {
                                serverEv.Props["browserType"] = bt;
                            }

                            if (!string.IsNullOrWhiteSpace(args))
                            {
                                serverEv.Props["args"] = args!;
                            }

                            if (!string.IsNullOrWhiteSpace(bid))
                            {
                                serverEv.Props["browserId"] = bid!;
                            }

                            if (!string.IsNullOrWhiteSpace(run.PlaywrightVersion))
                            {
                                serverEv.Props["playwrightVersion"] = run.PlaywrightVersion!;
                            }

                            if (!string.IsNullOrWhiteSpace(run.BrowserVersion))
                            {
                                serverEv.Props["browserVersion"] = run.BrowserVersion!;
                            }

                            serverEv.Props["labelKey"] = labelKey;
                            serverEv.Props["matchedLabel"] = c;
                            await resultsStore.AppendCommandAsync(serverEv);
                        }
                        catch { }

                        var toSend = serverEv is not null ? new[] { serverEv, ev } : new[] { ev };
                        await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(toSend);
                        await resultsHubCtx.Clients.Group($"run:{runId}").RunUpdate(run);
                    }

                    return Results.Ok(item);
                }
            }

            return Results.Problem($"No browser available for {labelKey}", statusCode: 503);
        });

        // Return { "labelKey": "...", "browserId": "..." }
        app.MapPost("/session/return", async (HttpRequest req) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            var body = await req.ReadFromJsonAsync<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            if (!body.TryGetValue("labelKey", out var labelKey) || !body.TryGetValue("browserId", out var browserId))
            {
                return Results.BadRequest("missing labelKey|browserId");
            }

            var inuseKey = $"inuse:{labelKey}";
            var availKey = $"available:{labelKey}";

            var res = await db.ScriptEvaluateAsync(luaReturn, [inuseKey, availKey], [browserId]);
            // Update availability gauge regardless of idempotent outcomes
            var listLenght = await db.ListLengthAsync(availKey);
            poolAvailableGauge.WithLabels(labelKey).Set(listLenght);

            // Request sidecar recycle on the worker so the instance is torn down and replenished with a fresh one
            // This aligns with the policy of not reusing the same browser across multiple borrowers.
            try { await db.StringSetAsync($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2)); }
            catch { }

            if (res.IsNull)
            {
                // Treat return as idempotent: if browserId is not in the in-use list, consider it already returned
                return Results.Ok(new { returned = browserId, note = "already returned" });
            }

            // Optional results emission if runId provided
            var runId2 = req.Headers["x-run-id"].FirstOrDefault() ?? req.Query["runId"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(runId2))
            {
                var now = DateTime.UtcNow;
                var ev = new CommandLogEventDto
                {
                    RunId = runId2!,
                    TimestampUtc = now,
                    Kind = "Return",
                    Message = $"Returned browser {browserId} for {labelKey}",
                    Props = new Dictionary<string, string> { ["labelKey"] = labelKey, ["browserId"] = browserId }
                };
                await resultsStore.AppendCommandAsync(ev);

                var run = await resultsStore.GetRunAsync(runId2) ?? new ResultRunSummaryDto
                {
                    RunId = runId2,
                    App = labelKey.Split(':').FirstOrDefault() ?? "",
                    Browser = labelKey.Split(':').Skip(1).FirstOrDefault() ?? "Chromium",
                    Env = labelKey.Split(':').Skip(2).FirstOrDefault() ?? "",
                    StartedAtUtc = now
                };
                // Mark run complete on return when attributed by runId
                run.CompletedAtUtc = now;
                run.Status = run.Failed > 0 ? "Failed" : "Passed";
                await resultsStore.UpsertRunAsync(run);
                await resultsHubCtx.Clients.Group($"run:{runId2}").CommandLogChunk(new[] { ev });
                await resultsHubCtx.Clients.Group($"run:{runId2}").RunUpdate(run);

                // Clear browserId->runId mapping on return
                try { await db.KeyDeleteAsync($"browser_run:{browserId}"); }
                catch { }

                try { await db.KeyDeleteAsync($"browser_test:{browserId}"); }
                catch { }
            }

            return Results.Ok(new { returned = browserId });
        });

        // ResultsHub + Results HTTP endpoints
        app.MapHub<PoolHub>("/ws");
        app.MapHub<ResultsHub>("/results-ws");

        app.MapGet("/", () => Results.Redirect("/dashboard"));
        app.MapGet("/dashboard", () => Results.Redirect(dashboardUrl));

        // Results HTTP endpoints
        app.MapGet("/results", async (HttpRequest req) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 100 : take;
            var status = req.Query["status"].FirstOrDefault();
            var appQ = req.Query["app"].FirstOrDefault();
            var browser = req.Query["browser"].FirstOrDefault();
            var env = req.Query["env"].FirstOrDefault();
            var runs = await resultsStore.GetRunsAsync(skip, take, status, appQ, browser, env);
            return Results.Ok(runs);
        });

        app.MapGet("/results/count", async (HttpRequest req) =>
        {
            var status = req.Query["status"].FirstOrDefault();
            var appQ = req.Query["app"].FirstOrDefault();
            var browser = req.Query["browser"].FirstOrDefault();
            var env = req.Query["env"].FirstOrDefault();
            var count = await resultsStore.GetRunsCountAsync(status, appQ, browser, env);
            return Results.Ok(new { count });
        });

        app.MapGet("/results/{runId}", async (string runId) =>
        {
            var run = await resultsStore.GetRunAsync(runId);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        // Tests endpoint
        app.MapGet("/results/{runId}/tests", async (HttpRequest req, string runId) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 200 : take;
            var status = req.Query["status"].FirstOrDefault();
            var items = await resultsStore.GetTestsAsync(runId, skip, take, status);
            return Results.Ok(items);
        });

        app.MapGet("/results/{runId}/commands", async (HttpRequest req, string runId) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 200 : take;
            var items = await resultsStore.GetCommandsAsync(runId, skip, take);
            return Results.Ok(items);
        });

        app.MapGet("/results/{runId}/commands/count", async (string runId) =>
        {
            var count = await resultsStore.GetCommandCountAsync(runId);
            return Results.Ok(new { count });
        });

        // Admin: mark run stopped and attempt to release any borrowed browsers for this run
        app.MapPost("/results/{runId}/stop", async (HttpRequest req, string runId) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            var now = DateTime.UtcNow;
            var released = 0;

            // Try to find borrowed browser(s) and their labelKey via stored commands
            var commands = await resultsStore.GetCommandsAsync(runId, 0, 5000);
            var candidates = new List<(string labelKey, string browserId)>();
            foreach (var cmd in commands)
            {
                if (!string.Equals(cmd.Kind, "ServerLaunch", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (cmd.Props is null)
                {
                    continue;
                }

                if (!cmd.Props.TryGetValue("browserId", out var bid) || string.IsNullOrWhiteSpace(bid))
                {
                    continue;
                }

                // Prefer matchedLabel (actual) then labelKey
                var labelKey = cmd.Props.TryGetValue("matchedLabel", out var ml) && !string.IsNullOrWhiteSpace(ml)
                    ? ml
                    : cmd.Props.TryGetValue("labelKey", out var lk)
                        ? lk ?? string.Empty
                        : string.Empty;
                if (string.IsNullOrWhiteSpace(labelKey))
                {
                    continue;
                }

                candidates.Add((labelKey, bid));
            }

            foreach (var (labelKey, browserId) in candidates.Distinct())
            {
                try
                {
                    var inuseKey = $"inuse:{labelKey}";
                    var availKey = $"available:{labelKey}";
                    var res = await db.ScriptEvaluateAsync(luaReturn, new RedisKey[] { inuseKey, availKey },
                        new RedisValue[] { browserId });
                    if (!res.IsNull)
                    {
                        released++;
                        // clean mappings
                        try { await db.KeyDeleteAsync($"browser_run:{browserId}"); }
                        catch { }

                        try { await db.KeyDeleteAsync($"browser_test:{browserId}"); }
                        catch { }
                    }

                    // request sidecar recycle on the worker (idempotent, short TTL)
                    try { await db.StringSetAsync($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2)); }
                    catch { }
                }
                catch { }
            }

            // Update run status
            var run = await resultsStore.GetRunAsync(runId) ?? new ResultRunSummaryDto
            {
                RunId = runId,
                StartedAtUtc = now
            };
            run.Status = "Stopped";
            run.CompletedAtUtc = now;
            await resultsStore.UpsertRunAsync(run);

            // Log and broadcast
            var ev = new CommandLogEventDto
            {
                RunId = runId,
                TimestampUtc = now,
                Kind = "Stop",
                Message = $"Run marked as Stopped. Released {released} borrow(s).",
                Props = new Dictionary<string, string>()
            };
            await resultsStore.AppendCommandAsync(ev);
            await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(new[] { ev });
            await resultsHubCtx.Clients.Group($"run:{runId}").RunUpdate(run);

            return Results.Ok(new { stopped = runId, released });
        });

        // Admin: restart all pre-warmed browsers for a pool (available and in-use)
        app.MapPost("/admin/pools/{labelKey}/restart", async (HttpRequest req, string labelKey) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            // Enter maintenance and snapshot current counts
            var availKey = $"available:{labelKey}";
            var inuseKey = $"inuse:{labelKey}";
            var availLen = await db.ListLengthAsync(availKey);
            var inuseLen = await db.ListLengthAsync(inuseKey);
            var target = availLen + inuseLen;
            var ttl = TimeSpan.FromMinutes(30);
            try
            {
                await db.StringSetAsync($"maintenance:{labelKey}", "1", ttl);
                await db.StringSetAsync($"maintenance:target:{labelKey}", target.ToString(CultureInfo.InvariantCulture),
                    ttl);
                await db.StringSetAsync($"maintenance:snap_avail:{labelKey}",
                    availLen.ToString(CultureInfo.InvariantCulture), ttl);
                await db.StringSetAsync($"maintenance:snap_inuse:{labelKey}",
                    inuseLen.ToString(CultureInfo.InvariantCulture), ttl);
                await db.StringSetAsync($"maintenance:since:{labelKey}",
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ttl);
            }
            catch { }

            var scheduled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in new[] { availKey, inuseKey })
            {
                try
                {
                    var items = await db.ListRangeAsync(key);
                    foreach (var item in items)
                    {
                        var s = item.ToString();
                        var m = System.Text.RegularExpressions.Regex.Match(s, "\"browserId\":\"(?<id>[^\"]+)\"");
                        if (m.Success)
                        {
                            var bid = m.Groups["id"].Value;
                            if (scheduled.Add(bid))
                            {
                                try { await db.StringSetAsync($"recycle:{bid}", "1", TimeSpan.FromMinutes(2)); }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            return Results.Ok(new
            {
                labelKey,
                scheduled = scheduled.Count,
                maintenance = new { active = true, target, avail = availLen, inuse = inuseLen }
            });
        });

        // Test attribution from client: set current testId for a browserId
        app.MapPost("/results/browser/{browserId}/test", async (HttpRequest req, string browserId) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            var body = await req.ReadFromJsonAsync<Dictionary<string, string?>>() ?? new Dictionary<string, string?>();
            if (!body.TryGetValue("testId", out var testId) || string.IsNullOrWhiteSpace(testId))
            {
                return Results.BadRequest("missing testId");
            }

            // Ensure browser->run mapping if provided in body and missing in Redis
            var runMapKey = $"browser_run:{browserId}";
            var runIdVal = await db.StringGetAsync(runMapKey);
            if (runIdVal.IsNullOrEmpty && body.TryGetValue("runId", out var providedRunId) &&
                !string.IsNullOrWhiteSpace(providedRunId))
            {
                await db.StringSetAsync(runMapKey, providedRunId!, TimeSpan.FromHours(6));
                runIdVal = providedRunId;
            }

            // Map current testId and TTL. Mapping is per-browser session; it's also cleared on return.
            await db.StringSetAsync($"browser_test:{browserId}", testId!, TimeSpan.FromHours(6));

            // If we can attribute to a run, persist and broadcast a TestUpdate for nicer UI
            if (!runIdVal.IsNullOrEmpty)
            {
                var runId = runIdVal.ToString();
                var title = body.GetValueOrDefault("title") ?? testId;
                var file = body.GetValueOrDefault("file") ?? string.Empty;
                var project = body.GetValueOrDefault("project");
                var status = body.GetValueOrDefault("status") ?? "Running";

                var dto = new ResultTestCaseDto
                {
                    RunId = runId!,
                    TestId = testId!,
                    Title = title ?? string.Empty,
                    File = file,
                    Project = project,
                    Status = status,
                    DurationMs = 0
                };
                await resultsStore.UpsertTestAsync(dto);
                await resultsHubCtx.Clients.Group($"run:{runId}").TestUpdate(dto);
            }

            return Results.Ok(new { mapped = browserId, testId });
        });

        // Worker-sourced Playwright command logs by browserId
        app.MapPost("/results/browser/{browserId}/commands", async (HttpRequest req, string browserId) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubNodeSecret))
            {
                return Results.Unauthorized();
            }

            var runIdVal = await db.StringGetAsync($"browser_run:{browserId}");
            if (runIdVal.IsNullOrEmpty)
            {
                return Results.Accepted(); // No run attribution yet
            }

            var runId = runIdVal.ToString();

            // Get current test attribution (if any)
            RedisValue currentTestVal;
            try { currentTestVal = await db.StringGetAsync($"browser_test:{browserId}"); }
            catch { currentTestVal = RedisValue.Null; }

            var currentTestId = currentTestVal.IsNullOrEmpty ? null : currentTestVal.ToString();

            JsonDocument? doc = null;
            try { doc = await JsonDocument.ParseAsync(req.Body); }
            catch { return Results.BadRequest("invalid JSON"); }

            using var _ = doc;

            var list = new List<CommandLogEventDto>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var (text, dir, ts) = ExtractLog(el);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    list.Add(ToEvent(runId, browserId, currentTestId, text, dir, ts, "worker"));
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("items", out var itemsEl) &&
                    itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsEl.EnumerateArray())
                    {
                        var (text, dir, ts) = ExtractLog(el);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        list.Add(ToEvent(runId, browserId, currentTestId, text, dir, ts, "worker"));
                    }
                }
                else
                {
                    var (text, dir, ts) = ExtractLog(doc.RootElement);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(ToEvent(runId, browserId, currentTestId, text, dir, ts, "worker"));
                    }
                }
            }

            if (list.Count == 0)
            {
                return Results.NoContent();
            }

            foreach (var ev in list)
            {
                await resultsStore.AppendCommandAsync(ev);
            }

            await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(list.ToArray());
            return Results.Ok(new { accepted = list.Count });

            static (string text, string? dir, DateTime? ts) ExtractLog(JsonElement el)
            {
                try
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        return (el.GetString() ?? string.Empty, null, null);
                    }

                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        string? text = null, dir = null;
                        DateTime? ts = null;
                        if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            text = t.GetString();
                        }

                        if (el.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String)
                        {
                            dir = d.GetString();
                        }

                        if (el.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(tsEl.GetString(), out var parsed))
                        {
                            ts = parsed;
                        }

                        return (text ?? string.Empty, dir, ts);
                    }
                }
                catch { }

                return (string.Empty, null, null);
            }

            static CommandLogEventDto ToEvent(string runId, string browserId, string? testId, string text, string? dir,
                DateTime? ts, string source)
            {
                if (text.Length > 4000)
                {
                    text = text.Substring(0, 4000);
                }

                return new CommandLogEventDto
                {
                    RunId = runId,
                    TimestampUtc = ts ?? DateTime.UtcNow,
                    Kind = "PwProtocol",
                    Message = text,
                    Props = new Dictionary<string, string>
                    {
                        ["browserId"] = browserId,
                        ["direction"] = string.IsNullOrWhiteSpace(dir) ? "" : dir,
                        ["source"] = source
                    },
                    TestId = string.IsNullOrWhiteSpace(testId) ? null : testId
                };
            }
        });

        // Runner-sourced Playwright API/protocol logs (pw:api) by browserId
        // Accepts HUB_RUNNER_SECRET so test runners can forward their client-side logs
        app.MapPost("/results/browser/{browserId}/api-logs", async (HttpRequest req, string browserId) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            var runIdVal = await db.StringGetAsync($"browser_run:{browserId}");
            if (runIdVal.IsNullOrEmpty)
            {
                return Results.Accepted();
            }

            var runId = runIdVal.ToString();

            RedisValue currentTestVal;
            try { currentTestVal = await db.StringGetAsync($"browser_test:{browserId}"); }
            catch { currentTestVal = RedisValue.Null; }

            var currentTestId = currentTestVal.IsNullOrEmpty ? null : currentTestVal.ToString();

            JsonDocument? doc;
            try { doc = await JsonDocument.ParseAsync(req.Body); }
            catch { return Results.BadRequest("invalid JSON"); }

            using var _2 = doc;

            var list = new List<CommandLogEventDto>();
            switch (doc.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var (text, dir, ts) = ExtractRunnerLog(el);
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                continue;
                            }

                            list.Add(ToRunnerEvent(runId, browserId, currentTestId, text, dir, ts));
                        }

                        break;
                    }
                case JsonValueKind.Object when doc.RootElement.TryGetProperty("items", out var itemsEl) &&
                                               itemsEl.ValueKind == JsonValueKind.Array:
                    {
                        foreach (var el in itemsEl.EnumerateArray())
                        {
                            var (text, dir, ts) = ExtractRunnerLog(el);
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                continue;
                            }

                            list.Add(ToRunnerEvent(runId, browserId, currentTestId, text, dir, ts));
                        }

                        break;
                    }
                case JsonValueKind.Object:
                    {
                        var (text, dir, ts) = ExtractRunnerLog(doc.RootElement);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            list.Add(ToRunnerEvent(runId, browserId, currentTestId, text, dir, ts));
                        }

                        break;
                    }
            }

            if (list.Count == 0)
            {
                return Results.NoContent();
            }

            foreach (var ev in list)
            {
                await resultsStore.AppendCommandAsync(ev);
            }

            await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(list.ToArray());
            return Results.Ok(new { accepted = list.Count });

            static (string text, string? dir, DateTime? ts) ExtractRunnerLog(JsonElement el)
            {
                try
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        return (el.GetString() ?? string.Empty, null, null);
                    }

                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        string? text = null;
                        DateTime? ts = null;
                        string? dir = null;
                        if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            text = t.GetString();
                        }

                        if (el.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(tsEl.GetString(), out var parsed))
                        {
                            ts = parsed;
                        }

                        if (el.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String)
                        {
                            dir = d.GetString();
                        }

                        return (text ?? string.Empty, dir, ts);
                    }
                }
                catch { }

                return (string.Empty, null, null);
            }

            static CommandLogEventDto ToRunnerEvent(string runId, string browserId, string? testId, string text,
                string? dir, DateTime? ts)
            {
                if (text.Length > 4000)
                {
                    text = text.Substring(0, 4000);
                }

                return new CommandLogEventDto
                {
                    RunId = runId,
                    TimestampUtc = ts ?? DateTime.UtcNow,
                    Kind = "PwProtocol",
                    Message = text,
                    Props = new Dictionary<string, string>
                    {
                        ["browserId"] = browserId,
                        ["direction"] = string.IsNullOrWhiteSpace(dir) ? "" : dir,
                        ["source"] = "runner"
                    },
                    TestId = string.IsNullOrWhiteSpace(testId) ? null : testId
                };
            }
        });

        // Demo seeding endpoint to quickly generate a sample run
        app.MapGet("/results/demo", async () =>
        {
            var runId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var run = new ResultRunSummaryDto
            {
                RunId = runId,
                App = "demo-app",
                Browser = "Chromium",
                Env = "local",
                Region = "",
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Status = "Running",
                TotalTests = 3,
                Passed = 2,
                Failed = 1,
                StartedAtUtc = now,
                WorkerNodeId = "demo-node",
                PlaywrightVersion = "1.45.0",
                BrowserVersion = "117.0"
            };

            await resultsStore.UpsertRunAsync(run);

            var evs = new List<CommandLogEventDto>
            {
                new()
                {
                    RunId = runId,
                    TimestampUtc = now,
                    Kind = "ServerLaunch",
                    Message = "Playwright server started",
                    Props =
                        new Dictionary<string, string>
                        {
                            ["wsEndpoint"] = "ws://demo/ws", ["args"] = "--disable-gpu --no-sandbox"
                        }
                },
                new()
                {
                    RunId = runId,
                    TimestampUtc = now.AddSeconds(1),
                    Kind = "Borrow",
                    Message = "Borrowed Chromium endpoint",
                    Props =
                        new Dictionary<string, string>
                        {
                            ["labelKey"] = "demo:Chromium:local", ["browserId"] = "demo-browser-1"
                        }
                },
                new()
                {
                    RunId = runId,
                    TimestampUtc = now.AddSeconds(5),
                    Kind = "Return",
                    Message = "Returned Chromium endpoint",
                    Props = new Dictionary<string, string>
                    {
                        ["labelKey"] = "demo:Chromium:local", ["browserId"] = "demo-browser-1"
                    }
                }
            };

            foreach (var e in evs)
            {
                await resultsStore.AppendCommandAsync(e);
            }

            // Broadcast to any connected dashboard clients
            await resultsHubCtx.Clients.All.RunUpdate(run);
            await resultsHubCtx.Clients.All.CommandLogChunk(evs.ToArray());

            return Results.Ok(new { runId });
        });

        // Diagnostics endpoint (requires runner secret)
        app.MapGet("/diagnostics", async (HttpRequest req) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            var reader = services.GetRequiredService<IPoolStateReader>();
            var state = await reader.GetStateAsync();

            var ver = typeof(EndpointMappingExtensions).Assembly.GetName().Version?.ToString() ?? "";

            var dto = new HubDiagnosticsDto
            {
                HubConfig = new HubEffectiveConfigDto
                {
                    RedisUrl = config["REDIS_URL"] ?? "redis:6379",
                    BorrowTrailingFallback = enableTrailingFallback,
                    BorrowPrefixExpand = enablePrefixExpand,
                    BorrowWildcards = enableWildcards,
                    NodeTimeoutSeconds = nodeTimeoutSeconds,
                    DashboardUrl = dashboardUrl,
                    Version = ver
                },
                Workers = state.Workers,
                Now = DateTime.UtcNow
            };

            return Results.Ok(dto);
        });

        // Hub environment variables (masked); requires runner secret
        app.MapGet("/diagnostics/env", (HttpRequest req) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            static bool IsSecret(string key)
            {
                key = key.ToUpperInvariant();
                return key.Contains("SECRET") || key.Contains("PASSWORD") || key.Contains("TOKEN");
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
            {
                var k = de.Key?.ToString();
                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                var v = de.Value?.ToString() ?? string.Empty;
                dict[k] = IsSecret(k) && !string.IsNullOrEmpty(v) ? "***" : v;
            }

            return Results.Ok(new { env = dict, now = DateTime.UtcNow });
        });

        // Proxy to worker environment variables via hub (runner secret required). Hub forwards node secret to worker.
        app.MapGet("/diagnostics/worker-env/{nodeId}", async (HttpRequest req, string nodeId) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubRunnerSecret))
            {
                return Results.Unauthorized();
            }

            try
            {
                var key = $"node:{nodeId}";
                var baseUrl = (await db.HashGetAsync(key, "BaseUrl")).ToString();
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    baseUrl = $"http://{nodeId}:5000";
                }

                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Remove("x-hub-secret");
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", hubNodeSecret);

                var url = $"{baseUrl.TrimEnd('/')}/diagnostics/env";
                var resp = await client.GetAsync(url);
                var text = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    return Results.StatusCode((int)resp.StatusCode);
                }

                // Pass-through payload from worker
                return Results.Text(text, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        app.MapGet("/nodes", () => Results.Ok(db.SetMembers("nodes").Select(x => x.ToString())));
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    private static bool CheckSecret(HttpRequest req, string header, string expected)
    {
        return req.Headers.TryGetValue(header, out var h) && h.FirstOrDefault() == expected;
    }
}
