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

using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

/// <summary>
///     Periodically scans for long-running inactive runs and auto-stops them,
///     releasing any borrowed browsers back to the pool and notifying subscribers.
/// </summary>
public sealed class RunCleanupService(
    IResultsStore resultsStore,
    IDatabase db,
    IHubContext<ResultsHub, IResultsClient> resultsHub,
    IConfiguration config
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = int.TryParse(config["HUB_CLEANUP_INTERVAL_MINUTES"], out var im)
            ? Math.Max(1, im)
            : 5; // default for testing
        var inactivityMinutes = int.TryParse(config["HUB_INACTIVITY_THRESHOLD_MINUTES"], out var ia)
            ? Math.Max(1, ia)
            : 30;
        var maxHours = int.TryParse(config["HUB_MAX_RUN_DURATION_HOURS"], out var mh)
            ? Math.Max(1, mh)
            : 3;
        var batchSize = int.TryParse(config["HUB_CLEANUP_BATCH_SIZE"], out var bs)
            ? Math.Max(1, bs)
            : 50;
        var debug = bool.TryParse(config["HUB_CLEANUP_DEBUG"], out var dbg) && dbg;

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var inactivity = TimeSpan.FromMinutes(inactivityMinutes);

        // Allow finer control via minutes override; if not set, fall back to hours
        TimeSpan maxDuration;
        if (int.TryParse(config["HUB_MAX_RUN_DURATION_MINUTES"], out var maxMinutesOverride) && maxMinutesOverride > 0)
        {
            maxDuration = TimeSpan.FromMinutes(maxMinutesOverride);
        }
        else
        {
            maxDuration = TimeSpan.FromHours(maxHours);
        }

        var maxDisplay = maxDuration.TotalHours >= 1
            ? $"{maxDuration.TotalHours:F1}h"
            : $"{maxDuration.TotalMinutes:F0}m";

        Console.WriteLine(
            $"[Cleanup] Starting. interval={interval.TotalMinutes:F0}m inactivity>={inactivity.TotalMinutes:F0}m max={maxDisplay} batch={batchSize}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTime.UtcNow;
            int scanned = 0, processed = 0, releasedTotal = 0, errors = 0;
            int skipIdle = 0, skipDuration = 0, skipNoOutstanding = 0;
            try
            {
                // Get running and in-progress runs
                var running = await resultsStore.GetRunsAsync(0, 1000, "Running");
                var inprog = await resultsStore.GetRunsAsync(0, 1000, "InProgress");
                var candidatesAll = running.Concat(inprog)
                    .OrderBy(r => r.StartedAtUtc)
                    .ToList();

                scanned = candidatesAll.Count;

                foreach (var run in candidatesAll)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (processed >= batchSize)
                    {
                        break;
                    }

                    try
                    {
                        // Determine last activity timestamp
                        var cmds = await resultsStore.GetCommandsAsync(run.RunId, 0, 5000);
                        var lastActivity = cmds.Count > 0
                            ? cmds.Max(c => c.TimestampUtc)
                            : run.StartedAtUtc;
                        var now = DateTime.UtcNow;

                        var inactiveLongEnough = now - lastActivity >= inactivity;
                        var overMaxDuration = now - run.StartedAtUtc >= maxDuration;
                        // Change this to match the auto-stopping criteria
                        if (!(inactiveLongEnough || overMaxDuration))
                        {
                            if (!inactiveLongEnough)
                            {
                                skipIdle++;
                            }

                            if (!overMaxDuration)
                            {
                                skipDuration++;
                            }

                            if (debug)
                            {
                                var reasons = new List<string>();
                                if (!inactiveLongEnough)
                                {
                                    reasons.Add("idle<inactivity");
                                }

                                if (!overMaxDuration)
                                {
                                    reasons.Add("duration<max");
                                }

                                Console.WriteLine(
                                    $"[Cleanup] Skip run {run.RunId} reason={string.Join(", ", reasons)} lastActivity={lastActivity:o} started={run.StartedAtUtc:o}");
                            }

                            continue;
                        }

                        // Identify browser borrow/return evidence from command logs
                        var borrowMap =
                            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // browserId -> labelKey
                        var launched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var cmd in cmds)
                        {
                            if (cmd.Props is null)
                            {
                                continue;
                            }

                            if (string.Equals(cmd.Kind, "ServerLaunch", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!cmd.Props.TryGetValue("browserId", out var bid) || string.IsNullOrWhiteSpace(bid))
                                {
                                    continue;
                                }

                                var labelKey = cmd.Props.TryGetValue("matchedLabel", out var ml) &&
                                               !string.IsNullOrWhiteSpace(ml)
                                    ? ml!
                                    : cmd.Props.TryGetValue("labelKey", out var lk)
                                        ? lk ?? string.Empty
                                        : string.Empty;
                                if (string.IsNullOrWhiteSpace(labelKey))
                                {
                                    continue;
                                }

                                launched.Add(bid);
                                borrowMap[bid] = labelKey;
                            }
                            else if (string.Equals(cmd.Kind, "Return", StringComparison.OrdinalIgnoreCase))
                            {
                                if (cmd.Props.TryGetValue("browserId", out var rb) && !string.IsNullOrWhiteSpace(rb))
                                {
                                    returned.Add(rb);
                                }
                            }
                        }

                        // Outstanding browsers are launches that were never returned (by commands view)
                        var outstanding = launched.Where(bid => !returned.Contains(bid)).ToList();

                        // If no outstanding evidence, skip this run (doesn't match hanging criteria)
                        if (outstanding.Count == 0)
                        {
                            skipNoOutstanding++;
                            if (debug)
                            {
                                Console.WriteLine($"[Cleanup] Skip run {run.RunId} reason=no-outstanding-browsers");
                            }

                            continue;
                        }

                        // Attempt to release any still in-use instances in Redis (idempotent)
                        var released = 0;
                        foreach (var browserId in outstanding.Distinct())
                        {
                            if (!borrowMap.TryGetValue(browserId, out var labelKey) ||
                                string.IsNullOrWhiteSpace(labelKey))
                            {
                                continue;
                            }

                            try
                            {
                                var inuseKey = $"inuse:{labelKey}";
                                var availKey = $"available:{labelKey}";
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
                                var res = await db.ScriptEvaluateAsync(luaReturn, new RedisKey[] { inuseKey, availKey },
                                    new RedisValue[] { browserId });
                                if (!res.IsNull)
                                {
                                    released++;
                                    releasedTotal++;

                                    // request sidecar recycle on the worker (short TTL)
                                    try { db.StringSet($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2)); }
                                    catch { }

                                    // record AutoReturn
                                    var evReturn = new CommandLogEventDto
                                    {
                                        RunId = run.RunId,
                                        TimestampUtc = DateTime.UtcNow,
                                        Kind = "AutoReturn",
                                        Message = $"Auto-released browser {browserId} for {labelKey}",
                                        Props = new Dictionary<string, string>
                                        {
                                            ["labelKey"] = labelKey,
                                            ["browserId"] = browserId
                                        }
                                    };
                                    await resultsStore.AppendCommandAsync(evReturn);
                                    await resultsHub.Clients.Group($"run:{run.RunId}").CommandLogChunk([evReturn]);

                                    // clear mappings
                                    try { db.KeyDelete($"browser_run:{browserId}"); }
                                    catch { }

                                    try { db.KeyDelete($"browser_test:{browserId}"); }
                                    catch { }
                                }
                            }
                            catch
                            {
                                /* ignore per-item errors */
                            }
                        }

                        // Proceed to mark auto-stopped when either condition is met and there are outstanding browsers
                        if ((inactiveLongEnough || overMaxDuration) && outstanding.Count >= 1)
                        {
                            var now2 = DateTime.UtcNow;
                            var updated = run with { };
                            updated.Status = "AutoStopped";
                            updated.CompletedAtUtc = now2;
                            try
                            {
                                var prop = typeof(ResultRunSummaryDto).GetProperty("Reason");
                                prop?.SetValue(updated, "Auto-terminated due to inactivity");
                            }
                            catch { }

                            await resultsStore.UpsertRunAsync(updated);

                            var evStop = new CommandLogEventDto
                            {
                                RunId = run.RunId,
                                TimestampUtc = now2,
                                Kind = "AutoStop",
                                Message =
                                    $"Run auto-stopped due to inactivity. Outstanding={outstanding.Count} Released={released}.",
                                Props = new Dictionary<string, string>()
                            };
                            await resultsStore.AppendCommandAsync(evStop);

                            await resultsHub.Clients.Group($"run:{run.RunId}").CommandLogChunk([evStop]);
                            await resultsHub.Clients.Group($"run:{run.RunId}").RunUpdate(updated);

                            processed++;
                        }
                    }
                    catch (Exception exRun)
                    {
                        errors++;
                        Console.WriteLine($"[Cleanup] Error processing run {run.RunId}: {exRun.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"[Cleanup] Loop error: {ex}");
            }

            var tookMs = (int)(DateTime.UtcNow - tickStart).TotalMilliseconds;
            if (debug)
            {
                Console.WriteLine(
                    $"[Cleanup] Tick: scanned={scanned} processed={processed} released={releasedTotal} errors={errors} skipIdle={skipIdle} skipDuration={skipDuration} skipNoOutstanding={skipNoOutstanding} took={tookMs}ms");
            }
            else
            {
                Console.WriteLine(
                    $"[Cleanup] Tick: scanned={scanned} processed={processed} released={releasedTotal} errors={errors} took={tookMs}ms");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch { break; }
        }
    }
}
