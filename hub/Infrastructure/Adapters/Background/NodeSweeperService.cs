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

using System.Globalization;
using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Shared.Logging;
using PlaywrightHub.Infrastructure.Web;
using Prometheus;
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
public sealed class NodeSweeperService(
    IDatabase db,
    IConnectionMultiplexer mux,
    IConfiguration config,
    ILogger<NodeSweeperService> logger,
    ChunkedLogger<NodeSweeperService> chunkedLogger)
    : BackgroundService
{
    private static readonly Counter QuarantineEvents = Prometheus.Metrics.CreateCounter(
        "hub_node_quarantine_total",
        "Total number of worker quarantine activations",
        new CounterConfiguration { LabelNames = ["reason"] });

    private static readonly Gauge QuarantinedNodes = Prometheus.Metrics.CreateGauge(
        "hub_nodes_quarantined",
        "Current number of workers under quarantine");

    /// <summary>
    ///     Executes the main logic of the NodeSweeperService, which periodically scans and prunes
    ///     stale or expired node entries from the Redis database.
    /// </summary>
    /// <param name="stoppingToken">A token that signals the task to stop execution.</param>
    /// <returns>A task that represents the asynchronous execution operation of the service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Configuration loading (existing)
        var nodeTimeoutSeconds = int.TryParse(config["AGENIX_HUB_NODE_TIMEOUT"], out var t) ? t : 60;
        var sweeperExpire = string.Equals(config["AGENIX_HUB_SWEEPER_EXPIRE"], "true",
            StringComparison.OrdinalIgnoreCase);
        var quarantineSeconds = int.TryParse(config["AGENIX_HUB_NODE_QUARANTINE_SECONDS"], out var qs)
            ? Math.Max(10, qs) : 120;
        var sweeperIntervalSeconds = int.TryParse(
            config["AGENIX_HUB_NODE_SWEEP_INTERVAL_SECONDS"], out var si) ? Math.Max(5, si) : 20;

        var nodeTimeout = TimeSpan.FromSeconds(nodeTimeoutSeconds);
        var sweeperInterval = TimeSpan.FromSeconds(sweeperIntervalSeconds);

        // Initial startup delay (moved inside loop for consistency)
        logger.LogInformation("[Sweeper] Starting. interval={interval}s timeout={timeout}s expire={expire}",
            sweeperInterval.TotalSeconds.ToString("F0"), nodeTimeout.TotalSeconds.ToString("F0"),
            sweeperExpire ? "on" : "off");

        var leadershipEnabled = string.Equals(
            config["AGENIX_HUB_SWEEPER_LEADERSHIP"], "true", StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(config["AGENIX_HUB_SWEEPER_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls) : 30;
        var instanceId = !string.IsNullOrWhiteSpace(config["AGENIX_HUB_INSTANCE_ID"])
            ? config["AGENIX_HUB_INSTANCE_ID"]!
            : $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = RedisKeys.SweeperLeader("nodes");

        if (leadershipEnabled)
        {
            logger.LogInformation(
                "[SweeperLeader] Enabled for NodeSweeper. key={leaderKey} leaseSeconds={leaseSeconds} instance={instanceId}",
                leaderKey, leaseSeconds, instanceId);
        }

        // Initial startup delay
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Main loop
        while (!stoppingToken.IsCancellationRequested)
        {
            // Begin operation for this sweep iteration
            using var operation = chunkedLogger.BeginOperation("NodeSweeperService.SweepStaleNodes");
            try
            {
                // Leader election (if enabled)
                if (leadershipEnabled)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.NodeSweeper.LeaderElectionStarted, // NSR01
                        "leaderKey={Key} instance={Instance}",
                        leaderKey, instanceId);

                    var leaseAcquired = await db.StringSetAsync(leaderKey, instanceId,
                        TimeSpan.FromSeconds(leaseSeconds), When.NotExists);

                    if (!leaseAcquired)
                    {
                        // Check if we're already the leader
                        var currentLeader = await db.StringGetAsync(leaderKey);
                        if (currentLeader == instanceId)
                        {
                            // Extend lease - already leader
                            await db.StringSetAsync(leaderKey, instanceId,
                                TimeSpan.FromSeconds(leaseSeconds));

                            chunkedLogger.LogMilestone(
                                EventCodes.NodeSweeper.LeaderLockRenewed, // NSR03
                                "instance={Instance}",
                                instanceId);
                        }
                        else
                        {
                            chunkedLogger.LogMilestone(
                                EventCodes.NodeSweeper.LeaderLockFailed, // NSR04
                                "reason=AnotherInstanceIsLeader currentLeader={CurrentLeader}",
                                currentLeader);

                            // Wait and retry
                            await Task.Delay(sweeperInterval, stoppingToken);
                            continue;
                        }
                    }
                    else
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.NodeSweeper.LeaderLockAcquired, // NSR02
                            "instance={Instance}",
                            instanceId);
                    }
                }

                // Execute sweep
                var (scanned, expired, quarantined, errors) = await ExecuteSweepAsync(
                    nodeTimeout, sweeperExpire, quarantineSeconds, stoppingToken);

                // Set operation outputs for structured logging
                var outputs = new Dictionary<string, object>
                {
                    ["scanned"] = scanned,
                    ["expired"] = expired,
                    ["quarantined"] = quarantined,
                    ["errors"] = errors
                };

                operation.SetOutputs(outputs);

                if (errors > 0)
                {
                    operation.Fail(
                        new InvalidOperationException($"{errors} nodes failed to process"),
                        ErrorType.DependencyFailure,
                        DependencyName.Redis);
                }
                else
                {
                    operation.Complete();
                }

                // Wait before next sweep
                await Task.Delay(sweeperInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log error but continue loop
                var context = OperationContext.Current;
                if (context != null)
                {
                    chunkedLogger.FailOperation(context, ex, ErrorType.Unexpected, DependencyName.Hub);
                }

                logger.LogError(ex, "[Sweeper] Loop error - will retry in {interval}s",
                    sweeperInterval.TotalSeconds);

                await Task.Delay(sweeperInterval, stoppingToken);
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
        var prunedCount = 0;

        foreach (var rk in server.Keys(pattern: RedisKeys.AvailablePrefix + "*"))
        {
            var key = rk.ToString();
            var list = await db.ListRangeAsync(key);
            foreach (var item in list)
            {
                if (item.ToString().Contains(nodePattern, StringComparison.Ordinal))
                {
                    await db.ListRemoveAsync(key, item);
                    prunedCount++;
                }
            }
        }

        // Log milestone if any entries were pruned
        if (prunedCount > 0)
        {
            chunkedLogger.LogMilestone(
                EventCodes.NodeSweeper.AvailableEntriesPruned, // NSR20
                "nodeId={NodeId} count={Count}",
                nodeId, prunedCount);
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
        var prunedCount = 0;

        foreach (var rk in server.Keys(pattern: RedisKeys.InUsePrefix + "*"))
        {
            var key = rk.ToString();
            var list = await db.ListRangeAsync(key);
            foreach (var item in list)
            {
                if (item.ToString().Contains(nodePattern, StringComparison.Ordinal))
                {
                    await db.ListRemoveAsync(key, item);
                    prunedCount++;
                }
            }
        }

        if (prunedCount > 0)
        {
            chunkedLogger.LogMilestone(
                EventCodes.NodeSweeper.InuseEntriesPruned, // NSR21
                "nodeId={NodeId} count={Count}",
                nodeId, prunedCount);
        }
    }

    private async Task<(int scanned, int expired, int quarantined, int errors)>
        ExecuteSweepAsync(
            TimeSpan nodeTimeout,
            bool sweeperExpire,
            int quarantineSeconds,
            CancellationToken stoppingToken)
    {
        var sweepLogger = new ChunkedLogger(logger, nameof(NodeSweeperService) + ".Sweep");
        sweepLogger.LogMilestone(
            EventCodes.NodeSweeper.ScanningStarted, // NSR10
            "timeout={Timeout}s expire={Expire}",
            nodeTimeout.TotalSeconds, sweeperExpire);

        var scanned = 0;
        var expired = 0;
        var quarantined = 0;
        var errors = 0;

        try
        {
            // Get all nodes
            var nodeIds = (await db.SetMembersAsync("nodes"))
                .Select(x => x.ToString())
                .ToArray();

            scanned = nodeIds.Length;
            sweepLogger.LogMilestone(
                EventCodes.NodeSweeper.NodesRetrieved, // NSR11
                "count={Count}",
                scanned);

            // Process each node
            foreach (var nodeId in nodeIds)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                sweepLogger.LogMilestone(
                    EventCodes.NodeSweeper.NodeProcessingStarted, // NSR12
                    "nodeId={NodeId}",
                    nodeId);

                var (nodeExpired, nodeQuarantined, nodeErrors) = await ProcessNodeAsync(sweepLogger, nodeId, nodeTimeout, sweeperExpire, quarantineSeconds);
                expired += nodeExpired;
                quarantined += nodeQuarantined;
                errors += nodeErrors;
            }
        }
        catch (Exception ex)
        {
            // Errors already logged in ProcessNodeAsync
            sweepLogger.LogMilestone(
                EventCodes.NodeSweeper.ScanningFailed, // NSR31
                ex,
                "error={Error}",
                ex.Message);
        }

        sweepLogger.LogMilestone(
            EventCodes.NodeSweeper.ScanningCompleted, // NSR40
            "scanned={Scanned} expired={Expired} quarantined={Quarantined} errors={Errors}",
            scanned, expired, quarantined, errors);

        await UpdateMetricsAsync();

        return (scanned, expired, quarantined, errors);
    }

    private async Task<(int expired, int quarantined, int errors)> ProcessNodeAsync(
        ChunkedLogger sweepLogger,
        string nodeId,
        TimeSpan nodeTimeout,
        bool sweeperExpire,
        int quarantineSeconds)
    {
        var expired = 0;
        var quarantined = 0;
        var errors = 0;

        try
        {
            // Check if node has alive key
            var aliveKey = RedisKeys.NodeAlive(nodeId);
            if (await db.KeyExistsAsync(aliveKey))
            {
                // Node is healthy, skip
                return (expired, quarantined, errors);
            }

            // Get last seen timestamp
            var key = RedisKeys.Node(nodeId);
            var lastSeenVal = await db.HashGetAsync(key, "LastSeen");
            if (lastSeenVal.IsNullOrEmpty)
            {
                // Missing data = stale
                logger.LogWarning("[Sweeper] Node {nodeId} missing LastSeen data", nodeId);
                await ExpireNodeAsync(nodeId);
                expired++;
                return (expired, quarantined, errors);
            }

            // Parse timestamp
            var lastSeenStr = lastSeenVal.ToString();
            var parsed = DateTimeOffset.TryParseExact(
                lastSeenStr,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastSeenDto);

            if (!parsed)
            {
                logger.LogWarning("[Sweeper] Node {nodeId} invalid LastSeen format: {lastSeen}", nodeId, lastSeenStr);
                await ExpireNodeAsync(nodeId);
                expired++;
                return (expired, quarantined, errors);
            }

            var age = DateTime.UtcNow - lastSeenDto.UtcDateTime;
            var clockSkewFuture = lastSeenDto.UtcDateTime > DateTime.UtcNow.AddSeconds(5);

            if (age > nodeTimeout && !clockSkewFuture)
            {
                // Check alive key again (race condition protection)
                if (await db.KeyExistsAsync(aliveKey))
                {
                    logger.LogDebug("[Sweeper] Node {nodeId} became alive during validation", nodeId);
                    return (expired, quarantined, errors);
                }

                // Check quarantine key
                var qKey = RedisKeys.NodeQuarantine(nodeId);
                var qTtl = await db.KeyTimeToLiveAsync(qKey);

                // If expiration enabled and quarantine expired/not set, expire the node
                if (sweeperExpire && qTtl is null)
                {
                    logger.LogInformation("[Sweeper] Expiring node={nodeId} (lastSeen={lastSeen})", nodeId,
                        lastSeenDto);
                    await ExpireNodeAsync(nodeId);
                    expired++;

                    sweepLogger.LogMilestone(
                        EventCodes.NodeSweeper.NodeExpiredRemoved, // NSR16
                        "nodeId={NodeId} lastSeen={LastSeen}",
                        nodeId, lastSeenDto);
                    return (expired, quarantined, errors);
                }

                // Check if already quarantined or need to quarantine
                if (qTtl is null)
                {
                    // Not quarantined - quarantine it
                    await db.StringSetAsync(qKey, "1",
                        TimeSpan.FromSeconds(quarantineSeconds));

                    logger.LogWarning("[Sweeper] Quarantined node={nodeId} for {seconds}s (lastSeen={lastSeen})",
                        nodeId, quarantineSeconds, lastSeenDto);

                    quarantined++;

                    sweepLogger.LogMilestone(
                        EventCodes.NodeSweeper.NodeQuarantined, // NSR15
                        "nodeId={NodeId} duration={Duration}s lastSeen={LastSeen}",
                        nodeId, quarantineSeconds, lastSeenDto);
                }

                // Remove available/inuse entries while quarantined
                await PruneAvailableEntriesForNodeAsync(nodeId);
            }
        }
        catch (TimeoutException exTimeout)
        {
            errors++;
            logger.LogWarning(exTimeout, "[Sweeper] Timeout while processing node {nodeId}", nodeId);

            sweepLogger.LogMilestone(
                EventCodes.NodeSweeper.RedisTimeout, // NSR32
                "nodeId={NodeId} error={Error}",
                nodeId, exTimeout.Message);
        }
        catch (RedisException exRedis)
        {
            errors++;
            logger.LogWarning(exRedis, "[Sweeper] Redis error while processing node {nodeId}", nodeId);

            sweepLogger.LogMilestone(
                EventCodes.NodeSweeper.NodeProcessingFailed, // NSR30
                "nodeId={NodeId} error={Error}",
                nodeId, exRedis.Message);
        }
        catch (Exception exNode)
        {
            errors++;
            logger.LogWarning(exNode, "[Sweeper] Error while processing node {nodeId}: {message}", nodeId,
                exNode.Message);

            sweepLogger.LogMilestone(
                EventCodes.NodeSweeper.NodeProcessingFailed, // NSR30
                "nodeId={NodeId} error={Error}",
                nodeId, exNode.Message);
        }

        return (expired, quarantined, errors);
    }

    private async Task ExpireNodeAsync(string nodeId)
    {
        await db.SetRemoveAsync("nodes", nodeId);
        await db.KeyDeleteAsync(RedisKeys.Node(nodeId));
        await PruneAvailableEntriesForNodeAsync(nodeId);
        await PruneInuseEntriesForNodeAsync(nodeId);
    }

    private Task UpdateMetricsAsync()
    {
        try
        {
            var server = mux.GetServer(mux.GetEndPoints()[0]);
            var qCount = server.Keys(pattern: RedisKeys.NodeQuarantinePrefix + "*").Count();
            QuarantinedNodes.Set(qCount);

            chunkedLogger.LogMilestone(
                EventCodes.NodeSweeper.QuarantineGaugeUpdated, // NSR41
                "quarantinedCount={Count}",
                qCount);
        }
        catch (Exception exGauge)
        {
            logger.LogWarning(exGauge, "[Sweeper] Failed to update quarantined nodes gauge");
        }

        return Task.CompletedTask;
    }
}
