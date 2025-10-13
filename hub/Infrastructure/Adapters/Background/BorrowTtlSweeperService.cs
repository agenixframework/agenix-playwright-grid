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

using System.Text.Json;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using PlaywrightHub.Infrastructure.Web;
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
    ChunkedLogger<BorrowTtlSweeperService> chunkedLogger,
    IResultsStore? resultsStore = null,
    IEventPublisher? eventPublisher = null,
    IHubContext<ResultsHub, IResultsClient>? resultsHub = null
)
    : BackgroundService
{
    private const string LuaReturn = @"
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = int.TryParse(config["AGENIX_HUB_BORROW_TTL_SWEEP_SECONDS"], out var s)
            ? Math.Max(5, s)
            : 10;

        var leadershipEnabled = string.Equals(
            config["AGENIX_HUB_SWEEPER_LEADERSHIP"], "true", StringComparison.OrdinalIgnoreCase);

        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var leaseSeconds = int.TryParse(config["AGENIX_HUB_SWEEPER_LEASE_SECONDS"], out var ls) ? Math.Max(5, ls) : 30;
        var instanceId = !string.IsNullOrWhiteSpace(config["AGENIX_HUB_INSTANCE_ID"])
            ? config["AGENIX_HUB_INSTANCE_ID"]!
            : $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = RedisKeys.SweeperLeader("borrow_ttl");

        logger.LogInformation("[BorrowTTL] Starting. sweepInterval={IntervalSeconds}s", intervalSeconds);
        // Initial startup delay
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTime.UtcNow;
            int processed = 0, returned = 0, errors = 0;

            using var operation = (dynamic)chunkedLogger.BeginOperation("BorrowTtlSweeperService.SweepExpiredBorrows");
            try
            {
                // Leader election (if enabled)
                if (leadershipEnabled)
                {
                    var leaseAcquired = await db.StringSetAsync(leaderKey, instanceId, TimeSpan.FromSeconds(leaseSeconds),
                        When.NotExists);

                    chunkedLogger.LogMilestone(
                        EventCodes.BorrowTtlSweeper.ScanStarted, // BRT01
                        "leader={IsLeader} lease={LeaseSeconds}s",
                        leaseAcquired, leaseSeconds);

                    if (!leaseAcquired)
                    {
                        operation.Complete();
                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
                        continue;
                    }
                }
                else
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.BorrowTtlSweeper.ScanStarted, // BRT01
                        "leader=disabled");
                }

                // Scan Redis for expired borrows
                foreach (var key in server.Keys(pattern: "session:*"))
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var sessionKey = key.ToString();
                    var browserId = sessionKey.Substring("session:".Length);

                    var (p, r, e) = await ProcessSessionAsync(sessionKey, browserId, stoppingToken);
                    processed += p;
                    returned += r;
                    errors += e;
                }

                // Set operation outputs
                var outputs = new Dictionary<string, object>
                {
                    ["processed"] = processed,
                    ["returned"] = returned,
                    ["errors"] = errors,
                    ["durationMs"] = (int)(DateTime.UtcNow - tickStart).TotalMilliseconds
                };

                operation.SetOutputs(outputs);
                operation.Complete();

                chunkedLogger.LogMilestone(
                    EventCodes.BorrowTtlSweeper.ScanCompleted, // BRT02
                    "processed={Processed} returned={Returned} errors={Errors} duration={DurationMs}ms",
                    processed, returned, errors, (int)(DateTime.UtcNow - tickStart).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected, DependencyName.Redis);

                chunkedLogger.LogMilestone(
                    EventCodes.BorrowTtlSweeper.ScanCompleted, // BRT02 (with failure)
                    ex,
                    "error={Error} processed={Processed}",
                    ex.Message, processed);
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    private async Task<(int Processed, int Returned, int Errors)> ProcessSessionAsync(
        string sessionKey,
        string browserId,
        CancellationToken stoppingToken)
    {
        int processed = 0, returned = 0, errors = 0;
        var localChunkedLogger = new ChunkedLogger(logger, nameof(BorrowTtlSweeperService) + ".ProcessSession");
        try
        {
            localChunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.SessionKeyFound, // BRT03
                "sessionKey={Key} browserId={BrowserId}",
                sessionKey, browserId);

            // Check TTL
            localChunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.TtlCheckStarted, // BRT10
                "browserId={BrowserId}",
                browserId);

            var hasLease = await db.KeyExistsAsync(RedisKeys.BorrowTtl(browserId));
            var hasIdle = await db.KeyExistsAsync($"borrow_idle:{browserId}");
            if (hasLease && hasIdle)
            {
                // Still valid, skip
                localChunkedLogger.LogMilestone(
                    EventCodes.BorrowTtlSweeper.TtlStillValid, // BRT12
                    "browserId={BrowserId}",
                    browserId);

                processed++;
                return (processed, returned, errors);
            }

            // TTL expired
            localChunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.TtlExpired, // BRT11
                "browserId={BrowserId}",
                browserId);

            // Load session metadata
            var entries = await db.HashGetAllAsync(sessionKey);

            if (entries is null || entries.Length == 0)
            {
                localChunkedLogger.LogMilestone(
                    EventCodes.BorrowTtlSweeper.SessionMetadataEmpty, // BRT21
                    "sessionKey={Key}",
                    sessionKey);

                // Clean up stray key
                try { await db.KeyDeleteAsync(sessionKey); }
                catch (Exception exClean)
                {
                    logger.LogWarning(exClean, "[BorrowTTL] Failed to delete stray key {SessionKey}", sessionKey);
                }

                processed++;
                return (processed, returned, errors);
            }

            localChunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.SessionMetadataLoaded, // BRT20
                "sessionKey={Key} entries={Count}",
                sessionKey, entries.Length);

            // Extract labelKey
            var labelEntry = entries.FirstOrDefault(e => e.Name == "labelKey");
            var labelKey = labelEntry.Value.IsNull ? null :
                labelEntry.Value.IsNullOrEmpty ? null : labelEntry.Value.ToString();

            int returnedCount = 0;
            if (!string.IsNullOrWhiteSpace(labelKey))
            {
                // Return browser
                localChunkedLogger.LogMilestone(
                    EventCodes.BorrowTtlSweeper.BrowserReturnStarted, // BRT30
                    "browserId={BrowserId} labelKey={LabelKey}",
                    browserId, labelKey);

                returnedCount = await ReturnBrowserAsync(labelKey, browserId);
                returned += returnedCount;

                // Signal fair queue and decrement in-flight for this label
                try { EndpointCapacityQueue.OnFinished(labelKey); }
                catch (Exception exFinished)
                {
                    logger.LogWarning(exFinished, "[BorrowTTL] Failed to call OnFinished for {LabelKey}", labelKey);
                }

                try { EndpointCapacityQueue.Signal(labelKey); }
                catch (Exception exSignal)
                {
                    logger.LogWarning(exSignal, "[BorrowTTL] Failed to signal capacity queue for {LabelKey}", labelKey);
                }

                // Notify about auto-return
                var runId = await GetRunIdFromBrowserAsync(browserId);
                if (!string.IsNullOrWhiteSpace(runId) && (eventPublisher != null || resultsHub != null))
                {
                    await PublishAutoReturnEvent(runId, browserId, labelKey);
                }

                localChunkedLogger.LogMilestone(
                    EventCodes.BorrowTtlSweeper.BrowserReturned, // BRT31
                    "browserId={BrowserId} labelKey={LabelKey} returned={ReturnedCount}",
                    browserId, labelKey, returnedCount);
            }
            else
            {
                // malformed session; delete
                try { await db.KeyDeleteAsync(sessionKey); }
                catch (Exception exClean)
                {
                    logger.LogWarning(exClean, "[BorrowTTL] Failed to delete malformed session key {SessionKey}", sessionKey);
                }
            }

            // Cleanup keys regardless
            await CleanupSessionKeysAsync(browserId, sessionKey);

            // request to recycle on worker
            try { await db.StringSetAsync($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2)); }
            catch (Exception exRecycle)
            {
                logger.LogWarning(exRecycle, "[BorrowTTL] Failed to set recycle flag for {BrowserId}", browserId);
            }

            processed++;
            return (processed, returned, errors);
        }
        catch (Exception ex)
        {
            errors++;

            localChunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.BrowserReturnFailed, // BRT32
                ex,
                "browserId={BrowserId} error={Error}",
                browserId, ex.Message);

            logger.LogWarning(ex, "[BorrowTTL] Failed to process session {sessionKey}", sessionKey);

            return (processed, returned, errors);
        }
    }

    private async Task<int> ReturnBrowserAsync(string labelKey, string browserId)
    {
        var inuseKey = RedisKeys.InUse(labelKey);
        var availKey = RedisKeys.Available(labelKey);
        var res = await db.ScriptEvaluateAsync(LuaReturn, [inuseKey, availKey], [browserId]);
        return res.IsNull ? 0 : 1;
    }

    private async Task<string?> GetRunIdFromBrowserAsync(string browserId)
    {
        try
        {
            var v = await db.StringGetAsync(RedisKeys.BrowserRun(browserId));
            return v.IsNullOrEmpty ? null : v.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[BorrowTTL] Failed to get runId for browser {BrowserId}", browserId);
            return null;
        }
    }

    private async Task PublishAutoReturnEvent(string runId, string browserId, string labelKey)
    {
        var now = DateTime.UtcNow;
        var message = $"Auto-released browser {browserId} for {labelKey} (TTL expired)";
        var props = new Dictionary<string, string>
        {
            ["labelKey"] = labelKey,
            ["browserId"] = browserId
        };

        // Publish AutoReturn command event
        if (eventPublisher != null)
        {
            var evt = new CommandEvent
            {
                EventType = "CommandAppended",
                RunId = runId,
                TimestampUtc = now,
                DataJson = JsonSerializer.Serialize(new
                {
                    kind = "AutoReturn",
                    message,
                    propsJson = JsonSerializer.Serialize(props)
                }),
                CorrelationId = Guid.NewGuid().ToString()
            };
            await eventPublisher.PublishCommandEventAsync(evt);

            chunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.CommandEventPublished, // BRT40
                "runId={RunId} browserId={BrowserId}",
                runId, browserId);
        }

        // Still send to SignalR for real-time UI updates
        if (resultsHub != null)
        {
            var evReturn = new CommandLogEventDto
            {
                RunId = runId,
                TimestampUtc = now,
                Kind = "AutoReturn",
                Message = message,
                Props = props
            };
            await resultsHub.Clients.Group($"run:{runId}").CommandLogChunk([evReturn]);

            chunkedLogger.LogMilestone(
                EventCodes.BorrowTtlSweeper.SignalRNotificationSent, // BRT41
                "runId={RunId} browserId={BrowserId}",
                runId, browserId);
        }

        // Optionally mark run AutoStopped if no outstanding browsers remain
        await CheckAndAutoStopRunAsync(runId, browserId, now);
    }

    private async Task CheckAndAutoStopRunAsync(string runId, string browserId, DateTime now)
    {
        if (resultsStore == null) return;

        try
        {
            var cmds = await resultsStore.GetCommandsAsync(runId, 0, 5000);
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
                else if (string.Equals(cmd.Kind, "Return", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(cmd.Kind, "AutoReturn", StringComparison.OrdinalIgnoreCase))
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
                var run = await resultsStore.GetRunAsync(runId);
                if (run is not null && run.Status != "AutoStopped" && run.SessionStatus != "AutoStopped" && run.Status != "Completed")
                {
                    run.SessionStatus = "AutoStopped";
                    run.Status = "AutoStopped";
                    run.CompletedAtUtc = now;
                    try { run.Reason = "Lease expired (TTL)"; }
                    catch (Exception exReason)
                    {
                        logger.LogWarning(exReason, "[BorrowTTL] Failed to set Reason for run {RunId}", runId);
                    }

                    await resultsStore.UpsertRunAsync(run);

                    chunkedLogger.LogMilestone(
                        EventCodes.BorrowTtlSweeper.RunAutoStopped, // BRT50
                        "runId={RunId}",
                        runId);
                }
            }
        }
        catch (Exception exAttribution)
        {
            logger.LogWarning(exAttribution, "[BorrowTTL] Failed to check/update run status for {RunId}", runId);
        }
    }

    private async Task CleanupSessionKeysAsync(string browserId, string sessionKey)
    {
        var keysToDelete = new[]
        {
            $"browser_run:{browserId}",
            $"browser_test:{browserId}",
            RedisKeys.BorrowTtl(browserId),
            $"borrow_idle:{browserId}",
            sessionKey
        };

        foreach (var key in keysToDelete)
        {
            try { await db.KeyDeleteAsync(key); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[BorrowTTL] Failed to delete key {Key}", key);
            }
        }
    }
}
