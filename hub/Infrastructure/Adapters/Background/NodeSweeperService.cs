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

using System.Globalization;
using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

// Background service that sweeps stale nodes and prunes stale available entries
/// <summary>
///     A background service responsible for sweeping stale nodes and pruning stale available entries
///     in the Playwright Hub infrastructure.
/// </summary>
/// <remarks>
///     This service periodically scans and validates node entries stored in Redis. Any stale or
///     invalid nodes are removed as part of its operation. It uses configuration values
///     to determine the node timeout and sweeper execution interval. Optionally, it can also
///     remove expired nodes based on configuration settings. The service runs until explicitly
///     cancelled.
/// </remarks>
public sealed class NodeSweeperService(IDatabase db, IConnectionMultiplexer mux, IConfiguration config, Microsoft.Extensions.Logging.ILogger<NodeSweeperService> logger)
    : BackgroundService
{
    /// <summary>
    ///     Executes the main logic of the NodeSweeperService, which periodically scans and prunes
    ///     stale or expired node entries from the Redis database.
    /// </summary>
    /// <param name="stoppingToken">A token that signals the task to stop execution.</param>
    /// <returns>A task that represents the asynchronous execution operation of the service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nodeTimeoutSeconds = int.TryParse(config["HUB_NODE_TIMEOUT"], out var t) ? t : 60;
        var sweeperExpire = string.Equals(config["HUB_SWEEPER_EXPIRE"], "true", StringComparison.OrdinalIgnoreCase);

        var nodeTimeout = TimeSpan.FromSeconds(nodeTimeoutSeconds);
        var sweeperInterval = TimeSpan.FromSeconds(20);

        logger.LogInformation("[Sweeper] Starting. interval={interval}s timeout={timeout}s expire={expire}", sweeperInterval.TotalSeconds.ToString("F0"), nodeTimeout.TotalSeconds.ToString("F0"), sweeperExpire ? "on" : "off");

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTime.UtcNow;
            var scanned = 0;
            var expired = 0;
            var errors = 0;

            try
            {
                var nodeIds = db.SetMembers("nodes").Select(x => x.ToString()).ToArray();
                scanned = nodeIds.Length;

                foreach (var nodeId in nodeIds)
                {
                    try
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // If an alive TTL key is present, consider the node healthy.
                        var aliveKey = RedisKeys.NodeAlive(nodeId);
                        if (await db.KeyExistsAsync(aliveKey))
                        {
                            continue;
                        }

                        var key = RedisKeys.Node(nodeId);
                        var lastSeenVal = await db.HashGetAsync(key, "LastSeen");

                        // Parse using round-trip ISO 8601 to preserve UTC
                        var lastSeenStr = lastSeenVal.ToString();
                        var parsed = DateTimeOffset.TryParseExact(
                            lastSeenStr,
                            "o",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var lastSeenDto);

                        var missingOrStale =
                            lastSeenVal.IsNullOrEmpty ||
                            !parsed ||
                            DateTime.UtcNow - lastSeenDto.UtcDateTime > nodeTimeout;

                        // Small tolerance: if clocks are skewed and lastSeen is in the future, do not expire
                        var clockSkewFuture = parsed && lastSeenDto.UtcDateTime > DateTime.UtcNow.AddSeconds(5);

                        if (missingOrStale && !clockSkewFuture)
                        {
                            // Double-check liveness before deleting to avoid racing a fresh heartbeat
                            if (await db.KeyExistsAsync(aliveKey))
                            {
                                continue;
                            }

                            // If the node still has available entries, treat it as alive and refresh a short TTL
                            if (await HasAvailableEntriesForNodeAsync(nodeId))
                            {
                                await db.StringSetAsync(aliveKey, "1", TimeSpan.FromSeconds(30));
                                logger.LogInformation("[Sweeper] Skipping expiration of node={nodeId} due to active available entries; refreshed TTL=30s", nodeId);
                                continue;
                            }

                            if (sweeperExpire)
                            {
                                logger.LogInformation("[Sweeper] Expiring node={nodeId} lastSeen={lastSeen}", nodeId, string.IsNullOrEmpty(lastSeenStr) ? "<missing>" : lastSeenStr);

                                await db.SetRemoveAsync("nodes", nodeId);
                                await db.KeyDeleteAsync(key);
                                await PruneAvailableEntriesForNodeAsync(nodeId);
                                await PruneInuseEntriesForNodeAsync(nodeId);

                                expired++;
                            }
                            else
                            {
                                await db.StringSetAsync(aliveKey, "1", TimeSpan.FromSeconds(30));
                                logger.LogInformation("[Sweeper] Would expire node={nodeId} (expiration disabled); refreshed TTL=30s", nodeId);
                            }
                        }
                    }
                    catch (Exception exNode)
                    {
                        errors++;
                        logger.LogWarning(exNode, "[Sweeper] Error while processing node {nodeId}: {message}", nodeId, exNode.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogWarning(ex, "[Sweeper] Loop error");
            }

            var elapsedMs = (int)(DateTime.UtcNow - tickStart).TotalMilliseconds;
            logger.LogInformation("[Sweeper] Tick done: scanned={scanned} expired={expired} errors={errors} took={ms}ms", scanned, expired, errors, elapsedMs);

            try
            {
                await Task.Delay(sweeperInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // graceful exit
                break;
            }
        }
    }

    /// <summary>
    ///     Removes stale available entries associated with the specified node from the database.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node whose stale available entries are to be pruned.</param>
    /// <returns>A task that represents the asynchronous pruning operation.</returns>
    private async Task PruneAvailableEntriesForNodeAsync(string nodeId)
    {
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var nodePattern = $"\"nodeId\":\"{nodeId}\"";
        foreach (var rk in server.Keys(pattern: RedisKeys.AvailablePrefix + "*"))
        {
            var key = rk.ToString();
            var list = db.ListRange(key);
            foreach (var item in list)
            {
                if (item.ToString().Contains(nodePattern, StringComparison.Ordinal))
                {
                    await db.ListRemoveAsync(key, item);
                    logger.LogInformation("[Sweeper] Pruned stale entry from {key} for node {nodeId}", key, nodeId);
                }
            }
        }
    }

    /// <summary>
    ///     Checks if the specified node has available entries in the system.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node to check for available entries.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation.
    ///     The task result contains a boolean indicating whether the node has available entries.
    /// </returns>
    private async Task<bool> HasAvailableEntriesForNodeAsync(string nodeId)
    {
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var nodePattern = $"\"nodeId\":\"{nodeId}\"";
        foreach (var rk in server.Keys(pattern: RedisKeys.AvailablePrefix + "*"))
        {
            var key = rk.ToString();
            var list = await db.ListRangeAsync(key);
            if (list.Any(item => item.ToString().Contains(nodePattern, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Removes orphaned in-use entries associated with the specified node from Redis and
    ///     clears lightweight browser mappings (browser_run:/browser_test:) when encountered.
    ///     This prevents capacity from being stuck when a worker disappears.
    /// </summary>
    private async Task PruneInuseEntriesForNodeAsync(string nodeId)
    {
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var nodePattern = $"\"nodeId\":\"{nodeId}\"";
        foreach (var rk in server.Keys(pattern: RedisKeys.InUsePrefix + "*"))
        {
            var key = rk.ToString();
            var list = await db.ListRangeAsync(key);
            foreach (var item in list)
            {
                var s = item.ToString();
                if (s.Contains(nodePattern, StringComparison.Ordinal))
                {
                    await db.ListRemoveAsync(key, item);

                    // Decrement in-flight concurrency for this label because the in-use entry is gone
                    try
                    {
                        var labelKey = key.StartsWith(RedisKeys.InUsePrefix, StringComparison.Ordinal) ? key[RedisKeys.InUsePrefix.Length..] : key;
                        PlaywrightHub.Infrastructure.Web.EndpointCapacityQueue.OnFinished(labelKey);
                    }
                    catch { }

                    // Best-effort: remove browser mappings if browserId is present in the JSON blob
                    try
                    {
                        using var doc = JsonDocument.Parse(s);
                        if (doc.RootElement.TryGetProperty("browserId", out var bidEl) &&
                            bidEl.ValueKind == JsonValueKind.String)
                        {
                            var browserId = bidEl.GetString();
                            if (!string.IsNullOrWhiteSpace(browserId))
                            {
                                try { await db.KeyDeleteAsync(RedisKeys.BrowserRun(browserId)); }
                                catch { }

                                try { await db.KeyDeleteAsync(RedisKeys.BrowserTest(browserId)); }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    logger.LogInformation("[Sweeper] Pruned orphaned in-use entry from {key} for node {nodeId}", key, nodeId);
                }
            }
        }
    }
}
