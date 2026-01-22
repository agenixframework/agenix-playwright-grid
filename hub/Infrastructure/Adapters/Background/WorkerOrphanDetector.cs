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

using Agenix.PlaywrightGrid.Shared.Logging;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

/// <summary>
/// Background service that detects workers with expired heartbeats
/// and cleans up their orphaned sidecar processes.
/// Uses leader election to ensure only one hub instance runs detection.
/// </summary>
public sealed class WorkerOrphanDetector : BackgroundService
{
    private readonly IDatabase _redis;
    private readonly ILogger<WorkerOrphanDetector> _logger;
    private readonly ChunkedLogger<WorkerOrphanDetector> _chunkedLogger;
    private readonly string _hubId;

    // Configuration
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaderLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly long HeartbeatExpirySeconds = 300; // 5 minutes

    // Redis keys
    private const string LeaderKey = "orphan_detector:leader";
    private const string WorkerHeartbeatPattern = "worker:*:heartbeat";
    private const string WorkerPidsKey = "worker:{0}:pids";
    private const string PidMetadataKey = "pid:{0}:metadata";

    public WorkerOrphanDetector(
        IDatabase redis,
        ILogger<WorkerOrphanDetector> logger,
        ChunkedLogger<WorkerOrphanDetector> chunkedLogger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _chunkedLogger = chunkedLogger ?? throw new ArgumentNullException(nameof(chunkedLogger));
        _hubId = Environment.MachineName + "_" + Environment.ProcessId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Log service start
        _chunkedLogger.LogMilestone(
            EventCodes.OrphanDetector.LeaderElectionStarted, // ORP01
            "hubId={HubId} scanInterval={ScanInterval}s leaseDuration={LeaseDuration}s",
            _hubId, ScanInterval.TotalSeconds, LeaderLeaseDuration.TotalSeconds);

        // Initial startup delay (30s to let workers register)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Main loop - each iteration = one background operation
        while (!stoppingToken.IsCancellationRequested)
        {
            using var operation = _chunkedLogger.BeginOperation("WorkerOrphanDetector:Scan");
            try
            {
                // Leader election
                _chunkedLogger.LogMilestone(
                    EventCodes.OrphanDetector.LeaderElectionStarted, // ORP01
                    "attempt=Acquire");

                var isLeader = await TryAcquireLeaderLockAsync(stoppingToken);

                if (!isLeader)
                {
                    _chunkedLogger.LogMilestone(
                        EventCodes.OrphanDetector.LeaderLockFailed, // ORP31
                        "reason=AnotherHubIsLeader");

                    await Task.Delay(LeaderLeaseDuration, stoppingToken);
                    continue;
                }

                _chunkedLogger.LogMilestone(
                    EventCodes.OrphanDetector.LeaderLockAcquired, // ORP02
                    "hubId={HubId}",
                    _hubId);

                // Scan for expired workers
                _chunkedLogger.LogMilestone(
                    EventCodes.OrphanDetector.ScanningStarted, // ORP10
                    "expiryThreshold={Expiry}s",
                    HeartbeatExpirySeconds);

                var (expiredWorkers, totalOrphanedPids) = await ScanForExpiredWorkersAsync(stoppingToken);

                // Operation completion
                var outputs = new Dictionary<string, object>
                {
                    ["expiredWorkers"] = expiredWorkers,
                    ["orphanedPidsCleaned"] = totalOrphanedPids,
                    ["scanComplete"] = true
                };

                operation.SetOutputs(outputs);

                if (expiredWorkers > 0)
                {
                    operation.Complete();
                }
                else
                {
                    _chunkedLogger.LogDebug(null, "No expired workers found");
                }

                // Sleep before next scan
                await Task.Delay(ScanInterval, stoppingToken);

                // Renew leader lock during next iteration
            }
            catch (Exception ex) when (ex is RedisException || ex.Message.Contains("Redis"))
            {
                operation.Fail(
                    ex,
                    ErrorType.DependencyFailure,
                    DependencyName.Redis);

                _logger.LogError(ex, "[WorkerOrphanDetector] Redis failure - retrying in {Delay}s",
                    ScanInterval.TotalSeconds);

                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);

                _logger.LogError(ex, "[WorkerOrphanDetector] Unexpected error - retrying in {Delay}s",
                    ScanInterval.TotalSeconds);

                await Task.Delay(ScanInterval, stoppingToken);
            }
        }

        // Service stopping
        _logger.LogInformation("[WorkerOrphanDetector] Stopped");
    }

    /// <summary>Try to acquire leader lock using Redis SET NX with TTL</summary>
    private async Task<bool> TryAcquireLeaderLockAsync(CancellationToken ct)
    {
        try
        {
            var acquired = await _redis.StringSetAsync(
                LeaderKey,
                _hubId,
                expiry: LeaderLeaseDuration,
                when: When.NotExists
            );

            if (acquired)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.OrphanDetector.LeaderLockAcquired, // ORP02
                    "hubId={HubId}",
                    _hubId);
                return true;
            }

            // Check if we're already the leader (our lock still valid)
            var currentLeader = await _redis.StringGetAsync(LeaderKey);
            if (currentLeader == _hubId)
            {
                // Extend our lease
                await _redis.StringSetAsync(LeaderKey, _hubId, expiry: LeaderLeaseDuration);

                _chunkedLogger.LogMilestone(
                    EventCodes.OrphanDetector.LeaderLockRenewed, // ORP03
                    "hubId={HubId}",
                    _hubId);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.LeaderLockFailed, // ORP31
                ex,
                "error={Error}",
                ex.Message);

            return false;
        }
    }

    /// <summary>Scan for workers with expired heartbeats</summary>
    private async Task<(int expiredWorkers, int totalOrphanedPids)> ScanForExpiredWorkersAsync(CancellationToken ct)
    {
        var expiredWorkers = 0;
        var totalOrphanedPids = 0;

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var server = _redis.Multiplexer.GetServer(_redis.Multiplexer.GetEndPoints().First());
            var heartbeatKeys = server.Keys(pattern: WorkerHeartbeatPattern).ToList();

            if (heartbeatKeys.Count == 0)
            {
                _chunkedLogger.LogDebug(null, "No worker heartbeats found");
                return (0, 0);
            }

            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.ScanningStarted, // ORP10
                "heartbeatCount={Count}",
                heartbeatKeys.Count);

            foreach (var heartbeatKey in heartbeatKeys)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var lastHeartbeat = await _redis.StringGetAsync(heartbeatKey);

                    if (lastHeartbeat.IsNullOrEmpty)
                        continue;

                    var heartbeatTime = long.Parse(lastHeartbeat!);
                    var age = now - heartbeatTime;

                    // Heartbeat expired (older than 5 minutes)
                    if (age > HeartbeatExpirySeconds)
                    {
                        var workerId = ExtractWorkerId(heartbeatKey.ToString());

                        _chunkedLogger.LogMilestone(
                            EventCodes.OrphanDetector.ScanningHeartbeatExpired, // ORP11
                            "workerId={WorkerId} age={Age}s",
                            workerId, age);

                        var orphanedPids = await KillWorkerPidsAsync(workerId, ct);

                        if (orphanedPids > 0)
                        {
                            _chunkedLogger.LogMilestone(
                                EventCodes.OrphanDetector.ScanningOrphanedPidsFound, // ORP12
                                "workerId={WorkerId} orphanedPids={Count}",
                                workerId, orphanedPids);
                        }

                        expiredWorkers++;
                        totalOrphanedPids += orphanedPids;
                    }
                }
                catch (Exception ex)
                {
                    _chunkedLogger.LogMilestone(
                        EventCodes.OrphanDetector.DetectFailed, // ORP30
                        ex,
                        "error={Error} heartbeatKey={Key}",
                        ex.Message, heartbeatKey);
                }
            }

            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.ScanningComplete, // ORP13
                "expiredWorkers={Expired} orphanedPids={Pids}",
                expiredWorkers, totalOrphanedPids);

            return (expiredWorkers, totalOrphanedPids);
        }
        catch (Exception ex)
        {
            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.DetectFailed, // ORP30
                ex,
                "error={Error}");

            return (0, 0);
        }
    }

    /// <summary>Clean up Redis PID entries for dead worker</summary>
    private async Task<int> KillWorkerPidsAsync(string workerId, CancellationToken ct)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.OrphanDetector.PidCleanupStarted, // ORP20
            "workerId={WorkerId}",
            workerId);

        var cleanedCount = 0;
        try
        {
            var pidsKey = string.Format(WorkerPidsKey, workerId);
            var pids = await _redis.SetMembersAsync(pidsKey);

            if (pids.Length == 0)
            {
                _chunkedLogger.LogDebug(null, "No PIDs found for worker {WorkerId}", workerId);
                await CleanupWorkerKeysAsync(workerId);
                return 0;
            }

            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.ScanningOrphanedPidsFound, // ORP12
                "workerId={WorkerId} pidCount={Count}",
                workerId, pids.Length);

            // Clean up each PID
            foreach (var pidValue in pids)
            {
                if (ct.IsCancellationRequested)
                    break;

                var pid = (int)pidValue;
                try
                {
                    var metadataKey = string.Format(PidMetadataKey, pid);
                    // Remove PID from worker's set
                    await _redis.SetRemoveAsync(pidsKey, pid);
                    // Delete PID metadata
                    await _redis.KeyDeleteAsync(metadataKey);
                    cleanedCount++;

                    _chunkedLogger.LogMilestone(
                        EventCodes.OrphanDetector.PidCleaned, // ORP21
                        "workerId={WorkerId} pid={Pid}",
                        workerId, pid);
                }
                catch (Exception ex)
                {
                    _chunkedLogger.LogMilestone(
                        EventCodes.OrphanDetector.PidCleanupFailed, // ORP22
                        ex,
                        "error={Error} workerId={WorkerId} pid={Pid}",
                        ex.Message, workerId, pid);
                }
            }

            // Cleanup worker keys
            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.WorkerKeysCleanupStarted, // ORP23
                "workerId={WorkerId}",
                workerId);

            await CleanupWorkerKeysAsync(workerId);

            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.WorkerKeysCleaned, // ORP24
                "workerId={WorkerId} cleanedCount={Count}",
                workerId, cleanedCount);

            return cleanedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkerOrphanDetector] Failed to clean PIDs for worker {WorkerId}: {Message}",
                workerId, ex.Message);
            return cleanedCount;
        }
    }

    private async Task CleanupWorkerKeysAsync(string workerId)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.OrphanDetector.WorkerKeysCleanupStarted, // ORP23
            "workerId={WorkerId}",
            workerId);
        try
        {
            var pidsKey = string.Format(WorkerPidsKey, workerId);
            var heartbeatKey = $"worker:{workerId}:heartbeat";
            var metadataKey = $"worker:{workerId}:metadata";
            var transaction = _redis.CreateTransaction();
            await transaction.KeyDeleteAsync(pidsKey);
            await transaction.KeyDeleteAsync(heartbeatKey);
            await transaction.KeyDeleteAsync(metadataKey);
            await transaction.ExecuteAsync();
            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.WorkerKeysCleaned, // ORP24
                "workerId={WorkerId}",
                workerId);
        }
        catch (Exception ex)
        {
            _chunkedLogger.LogMilestone(
                EventCodes.OrphanDetector.DetectFailed, // ORP30
                ex,
                "error={Error} workerId={WorkerId}",
                ex.Message, workerId);
        }
    }

    /// <summary>
    /// Extract worker ID from a Redis heartbeat key.
    /// Key format: "worker:{workerId}:heartbeat"
    /// </summary>
    private static string ExtractWorkerId(string heartbeatKey)
    {
        var parts = heartbeatKey.Split(':');
        return parts.Length >= 2 ? parts[1] : heartbeatKey;
    }
}
