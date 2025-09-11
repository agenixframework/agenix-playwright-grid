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

using Agenix.PlaywrightGrid.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

/// <summary>
///     Background sweeper that auto-returns borrowed sessions whose TTL/lease has expired.
///     Persists and consults session:* hashes to recover context after Hub restarts.
/// </summary>
public sealed class BorrowTtlSweeperService(
    IDatabase db,
    IConnectionMultiplexer mux,
    IConfiguration config,
    ILogger<BorrowTtlSweeperService> logger,
    IResultsStore? resultsStore = null,
    IHubContext<ResultsHub, IResultsClient>? resultsHub = null
)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = int.TryParse(config["HUB_BORROW_TTL_SWEEP_SECONDS"], out var s) ? Math.Max(5, s) : 10;
        var server = mux.GetServer(mux.GetEndPoints()[0]);
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

        logger.LogInformation("[BorrowTTL] Starting. sweepInterval={IntervalSeconds}s", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            var processed = 0;
            var returned = 0;
            var errors = 0;
            try
            {
                foreach (var key in server.Keys(pattern: "session:*"))
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var sessionKey = key.ToString();
                    var browserId = sessionKey.Substring("session:".Length);
                    try
                    {
                        // if both lease key and idle key still exist -> not expired
                        var hasLease = await db.KeyExistsAsync(RedisKeys.BorrowTtl(browserId));
                        var hasIdle = await db.KeyExistsAsync($"borrow_idle:{browserId}");
                        if (hasLease && hasIdle)
                        {
                            processed++;
                            continue;
                        }

                        // No lease key -> TTL expired. Load session record to know labelKey and attempt return.
                        var entries = await db.HashGetAllAsync(sessionKey);
                        if (entries is null || entries.Length == 0)
                        {
                            // Nothing to do; clean stray session key
                            try { await db.KeyDeleteAsync(sessionKey); }
                            catch { }

                            processed++;
                            continue;
                        }

                        var labelEntry = entries.FirstOrDefault(e => e.Name == "labelKey");
                        string? labelKey = labelEntry.Value.IsNull ? null : (labelEntry.Value.IsNullOrEmpty ? null : labelEntry.Value.ToString());
                        if (!string.IsNullOrWhiteSpace(labelKey))
                        {
                            var inuseKey = RedisKeys.InUse(labelKey!);
                            var availKey = RedisKeys.Available(labelKey!);
                            var res = await db.ScriptEvaluateAsync(luaReturn, new RedisKey[] { inuseKey, availKey },
                                new RedisValue[] { browserId });
                            if (!res.IsNull)
                            {
                                returned++;
                                // Signal fair queue and decrement in-flight for this label
                                try { PlaywrightHub.Infrastructure.Web.EndpointCapacityQueue.OnFinished(labelKey); } catch { }
                                try { PlaywrightHub.Infrastructure.Web.EndpointCapacityQueue.Signal(labelKey); } catch { }

                                // Attempt to attribute and update run state
                                string? runId = null;
                                try
                                {
                                    var v = await db.StringGetAsync(RedisKeys.BrowserRun(browserId));
                                    if (!v.IsNullOrEmpty) runId = v.ToString();
                                }
                                catch { }

                                if (!string.IsNullOrWhiteSpace(runId) && resultsStore is not null && resultsHub is not null)
                                {
                                    try
                                    {
                                        var now = DateTime.UtcNow;
                                        // Append AutoReturn event
                                        var evReturn = new CommandLogEventDto
                                        {
                                            RunId = runId!,
                                            TimestampUtc = now,
                                            Kind = "AutoReturn",
                                            Message = $"Auto-released browser {browserId} for {labelKey} (TTL expired)",
                                            Props = new Dictionary<string, string>
                                            {
                                                ["labelKey"] = labelKey,
                                                ["browserId"] = browserId
                                            }
                                        };
                                        await resultsStore.AppendCommandAsync(evReturn);
                                        await resultsHub.Clients.Group($"run:{runId}").CommandLogChunk([evReturn]);

                                        // Optionally mark run AutoStopped if no outstanding browsers remain
                                        try
                                        {
                                            var cmds = await resultsStore.GetCommandsAsync(runId!, 0, 5000);
                                            var launched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                            var returnedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                            foreach (var cmd in cmds)
                                            {
                                                if (cmd.Props is null) continue;
                                                if (string.Equals(cmd.Kind, "ServerLaunch", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (cmd.Props.TryGetValue("browserId", out var bid) && !string.IsNullOrWhiteSpace(bid))
                                                    {
                                                        launched.Add(bid);
                                                    }
                                                }
                                                else if (string.Equals(cmd.Kind, "Return", StringComparison.OrdinalIgnoreCase) || string.Equals(cmd.Kind, "AutoReturn", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (cmd.Props.TryGetValue("browserId", out var rb) && !string.IsNullOrWhiteSpace(rb))
                                                    {
                                                        returnedSet.Add(rb);
                                                    }
                                                }
                                            }
                                            returnedSet.Add(browserId); // count this TTL-based return
                                            var outstanding = launched.Where(b => !returnedSet.Contains(b)).ToList();

                                            if (outstanding.Count == 0)
                                            {
                                                var run = await resultsStore.GetRunAsync(runId!);
                                                if (run is not null)
                                                {
                                                    run.Status = "AutoStopped";
                                                    run.CompletedAtUtc = now;
                                                    try { run.Reason = "Lease expired (TTL)"; } catch { }
                                                    await resultsStore.UpsertRunAsync(run);
                                                    await resultsHub.Clients.Group($"run:{runId}").RunUpdate(run);
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                    catch { }
                                }

                                // cleanup mappings
                                try { await db.KeyDeleteAsync($"browser_run:{browserId}"); } catch { }
                                try { await db.KeyDeleteAsync($"browser_test:{browserId}"); } catch { }

                                try { await db.KeyDeleteAsync(RedisKeys.BorrowTtl(browserId)); } catch { }
                                try { await db.KeyDeleteAsync($"borrow_idle:{browserId}"); } catch { }
                                try { await db.KeyDeleteAsync(sessionKey); } catch { }

                                // request recycle on worker
                                try { await db.StringSetAsync($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2)); }
                                catch { }
                            }
                            else
                            {
                                // Could not find in inuse list; still perform attribution + cleanup
                                string? runId = null;
                                try
                                {
                                    var v = await db.StringGetAsync(RedisKeys.BrowserRun(browserId));
                                    if (!v.IsNullOrEmpty) runId = v.ToString();
                                }
                                catch { }

                                if (!string.IsNullOrWhiteSpace(runId) && resultsStore is not null && resultsHub is not null)
                                {
                                    try
                                    {
                                        var now = DateTime.UtcNow;
                                        var evReturn = new CommandLogEventDto
                                        {
                                            RunId = runId!,
                                            TimestampUtc = now,
                                            Kind = "AutoReturn",
                                            Message = $"Auto-released browser {browserId} for {labelKey} (TTL expired)",
                                            Props = new Dictionary<string, string>
                                            {
                                                ["labelKey"] = labelKey,
                                                ["browserId"] = browserId
                                            }
                                        };
                                        await resultsStore.AppendCommandAsync(evReturn);
                                        await resultsHub.Clients.Group($"run:{runId}").CommandLogChunk([evReturn]);

                                        // Heuristic: attempt to stop run if no outstanding by logs
                                        try
                                        {
                                            var cmds = await resultsStore.GetCommandsAsync(runId!, 0, 5000);
                                            var launched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                            var returnedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                            foreach (var cmd in cmds)
                                            {
                                                if (cmd.Props is null) continue;
                                                if (string.Equals(cmd.Kind, "ServerLaunch", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (cmd.Props.TryGetValue("browserId", out var bid) && !string.IsNullOrWhiteSpace(bid))
                                                    {
                                                        launched.Add(bid);
                                                    }
                                                }
                                                else if (string.Equals(cmd.Kind, "Return", StringComparison.OrdinalIgnoreCase) || string.Equals(cmd.Kind, "AutoReturn", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (cmd.Props.TryGetValue("browserId", out var rb) && !string.IsNullOrWhiteSpace(rb))
                                                    {
                                                        returnedSet.Add(rb);
                                                    }
                                                }
                                            }
                                            returnedSet.Add(browserId);
                                            var outstanding = launched.Where(b => !returnedSet.Contains(b)).ToList();

                                            if (outstanding.Count == 0)
                                            {
                                                var run = await resultsStore.GetRunAsync(runId!);
                                                if (run is not null)
                                                {
                                                    run.Status = "AutoStopped";
                                                    run.CompletedAtUtc = now;
                                                    try { run.Reason = "Lease expired (TTL)"; } catch { }
                                                    await resultsStore.UpsertRunAsync(run);
                                                    await resultsHub.Clients.Group($"run:{runId}").RunUpdate(run);
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                    catch { }
                                }

                                // Clean keys regardless
                                try { await db.KeyDeleteAsync($"browser_run:{browserId}"); } catch { }
                                try { await db.KeyDeleteAsync($"browser_test:{browserId}"); } catch { }
                                try { await db.KeyDeleteAsync(RedisKeys.BorrowTtl(browserId)); } catch { }
                                try { await db.KeyDeleteAsync($"borrow_idle:{browserId}"); } catch { }
                                try { await db.KeyDeleteAsync(sessionKey); } catch { }
                            }
                        }
                        else
                        {
                            // malformed session; delete
                            try { await db.KeyDeleteAsync(sessionKey); }
                            catch { }
                        }

                        processed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        logger.LogError(ex, "[BorrowTTL] Error while processing {SessionKey}", sessionKey);
                    }
                }
            }
            catch (Exception exOuter)
            {
                errors++;
                logger.LogError(exOuter, "[BorrowTTL] Sweep error");
            }

            var took = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            logger.LogInformation("[BorrowTTL] Tick done processed={Processed} returned={Returned} errors={Errors} took={TookMs}ms", processed, returned, errors, took);

            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
