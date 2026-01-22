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

using System.Diagnostics;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using StackExchange.Redis;

namespace WorkerService.Infrastructure;

/// <summary>
/// Redis-based PID tracking for multi-hub deployments.
/// Provides centralized state management for the worker process lifecycle.
/// </summary>
public class PidRedisManager : IDisposable
{
    private readonly IDatabase _redis;
    private readonly string _workerId;
    private readonly ChunkedLogger<PidRedisManager> _logger;
    private readonly Timer _heartbeatTimer;
    private bool _disposed;

    // Redis key prefixes
    private const string WorkerPidsKey = "worker:{0}:pids";
    private const string WorkerHeartbeatKey = "worker:{0}:heartbeat";
    private const string WorkerMetadataKey = "worker:{0}:metadata";
    private const string PidMetadataKey = "pid:{0}:metadata";

    // Heartbeat settings
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTTL = TimeSpan.FromMinutes(5);

    public PidRedisManager(IDatabase redis, string workerId, ChunkedLogger<PidRedisManager> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _workerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Start a heartbeat timer
        _heartbeatTimer = new Timer(
            callback: _ => SendHeartbeatAsync().GetAwaiter().GetResult(),
            state: null,
            dueTime: TimeSpan.Zero, // Send it immediately
            period: HeartbeatInterval
        );

        _logger.LogMilestone(EventCodes.Redis.ClientInitialized,
            "PidRedisManager started for worker {WorkerId}. Endpoint={Endpoint}, DbIndex={DbIndex}",
            _workerId, _redis.Multiplexer.Configuration.Split(',')[0], _redis.Database);
    }

    /// <summary>
    /// Initialize on worker startup - detect and kill orphaned processes from the previous run.
    /// Returns list of PIDs that were tracked in Redis for this worker.
    /// </summary>
    public async Task<List<int>> InitializeAsync()
    {
        using var op = _logger.BeginOperation("PidRedisManager.Initialize", new Dictionary<string, object>
        {
            ["WorkerId"] = _workerId
        });

        try
        {
            var pidsKey = string.Format(WorkerPidsKey, _workerId);
            var trackedPids = await _redis.SetMembersAsync(pidsKey);

            var pidList = trackedPids.Select(p => (int)p).ToList();

            if (pidList.Count == 0)
            {
                _logger.LogInformation(EventCodes.Redis.SetOperationSuccess,
                    "No tracked PIDs found for worker {WorkerId}. Key={Key}", _workerId, pidsKey);
                op.Complete();
                return pidList;
            }

            _logger.LogMilestone(EventCodes.Redis.SetOperationSuccess,
                "Found {Count} tracked PIDs for worker {WorkerId}. Key={Key}, Pids={Pids}",
                pidList.Count, _workerId, pidsKey, string.Join(", ", pidList));

            op.SetOutputs(new Dictionary<string, object>
            {
                ["PidCount"] = pidList.Count
            });

            op.Complete();
            return pidList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Redis.SetOperationFailed, "Failed to initialize: {Message}", ex.Message);
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
            return new List<int>();
        }
    }

    /// <summary>
    /// Track a new sidecar PID in Redis.
    /// Stores PID in a worker's set and creates metadata entry.
    /// </summary>
    public async Task TrackPidAsync(int pid, string browserType, string labelKey)
    {
        using var op = _logger.BeginOperation("PidRedisManager.TrackPid", new Dictionary<string, object>
        {
            ["Pid"] = pid,
            ["BrowserType"] = browserType,
            ["LabelKey"] = labelKey
        });

        try
        {
            var pidsKey = string.Format(WorkerPidsKey, _workerId);
            var pidMetadataKey = string.Format(PidMetadataKey, pid);

            _logger.LogDebug(EventCodes.Redis.TransactionStarted,
                "Starting transaction to track PID {Pid}. Key={Key}, MetadataKey={MetadataKey}",
                pid, pidsKey, pidMetadataKey);

            var transaction = _redis.CreateTransaction();

            // Queue operations (don't await yet - transaction pattern)
            var task1 = transaction.SetAddAsync(pidsKey, pid);

            // Store PID metadata
            var metadata = new Dictionary<string, string>
            {
                ["worker_id"] = _workerId,
                ["browser_type"] = browserType,
                ["label_key"] = labelKey,
                ["start_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ["node_id"] = Environment.MachineName
            };

            var task2 = transaction.StringSetAsync(pidMetadataKey, JsonSerializer.Serialize(metadata));

            // Execute transaction (now await the queued operations)
            var success = await transaction.ExecuteAsync();

            if (success)
            {
                _logger.LogMilestone(EventCodes.Redis.TransactionCommitted,
                    "Tracked PID {Pid} for worker {WorkerId} ({BrowserType}, {LabelKey})",
                    pid, _workerId, browserType, labelKey);
                op.Complete();
            }
            else
            {
                _logger.LogWarning(EventCodes.Redis.TransactionFailed,
                    "Failed to track PID {Pid} - transaction failed. Key={Key}", pid, pidsKey);
                op.Fail(new Exception("Redis transaction failed"), ErrorType.Conflict, DependencyName.Redis);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Redis.TransactionFailed, "Failed to track PID {Pid}: {Message}", pid, ex.Message);
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
        }
    }

    /// <summary>
    /// Untrack a sidecar PID from Redis when it exits normally.
    /// Removes PID from a worker's set and deletes metadata.
    /// </summary>
    public async Task UntrackPidAsync(int pid)
    {
        using var op = _logger.BeginOperation("PidRedisManager.UntrackPid", new Dictionary<string, object>
        {
            ["Pid"] = pid
        });

        try
        {
            var pidsKey = string.Format(WorkerPidsKey, _workerId);
            var pidMetadataKey = string.Format(PidMetadataKey, pid);

            _logger.LogDebug(EventCodes.Redis.TransactionStarted,
                "Starting transaction to untrack PID {Pid}. Key={Key}, MetadataKey={MetadataKey}",
                pid, pidsKey, pidMetadataKey);

            var transaction = _redis.CreateTransaction();

            // Queue operations (don't await yet - transaction pattern)
            var task1 = transaction.SetRemoveAsync(pidsKey, pid);
            var task2 = transaction.KeyDeleteAsync(pidMetadataKey);

            // Execute transaction (now await the queued operations)
            var success = await transaction.ExecuteAsync();

            if (success)
            {
                _logger.LogMilestone(EventCodes.Redis.TransactionCommitted,
                    "Untracked PID {Pid} for worker {WorkerId}", pid, _workerId);
                op.Complete();
            }
            else
            {
                _logger.LogWarning(EventCodes.Redis.TransactionFailed,
                    "Failed to untrack PID {Pid} - transaction failed. Key={Key}", pid, pidsKey);
                op.Fail(new Exception("Redis transaction failed"), ErrorType.Conflict, DependencyName.Redis);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Redis.TransactionFailed, "Failed to untrack PID {Pid}: {Message}", pid, ex.Message);
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
        }
    }

    /// <summary>
    /// Detect and kill orphaned sidecar processes.
    /// Called on worker startup to clean up processes from the previous run.
    /// </summary>
    public async Task<int> DetectAndKillOrphansAsync(List<int> trackedPids)
    {
        using var op = _logger.BeginOperation("PidRedisManager.DetectAndKillOrphans", new Dictionary<string, object>
        {
            ["TrackedPidCount"] = trackedPids.Count
        });

        var killedCount = 0;

        try
        {
            _logger.LogInformation(null, "Checking {Count} tracked PIDs for orphans", trackedPids.Count);

            foreach (var pid in trackedPids)
            {
                try
                {
                    // Check if a process is still running
                    if (!IsProcessRunning(pid))
                    {
                        _logger.LogDebug(null, "PID {Pid} not running - cleaning up Redis", pid);
                        await UntrackPidAsync(pid);
                        continue;
                    }

                    // Verify it's actually a sidecar process
                    if (!IsSidecarProcess(pid))
                    {
                        _logger.LogWarning(null, "PID {Pid} is not a sidecar process - skipping", pid);
                        await UntrackPidAsync(pid);
                        continue;
                    }

                    // It's an orphaned sidecar - kill it
                    _logger.LogWarning(null, "Killing orphaned sidecar PID {Pid}", pid);
                    KillProcess(pid);
                    await UntrackPidAsync(pid);
                    killedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, null, "Failed to process PID {Pid}: {Message}", pid, ex.Message);
                }
            }

            if (killedCount > 0)
            {
                _logger.LogWarning(null, "Killed {Count} orphaned sidecar processes", killedCount);
            }
            else
            {
                _logger.LogInformation(null, "No orphaned processes found");
            }

            op.SetOutputs(new Dictionary<string, object>
            {
                ["KilledCount"] = killedCount
            });
            op.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, null, "Failed to detect/kill orphans: {Message}", ex.Message);
            op.Fail(ex, ErrorType.Unexpected);
        }

        return killedCount;
    }

    /// <summary>
    /// Send heartbeat to Redis to indicate the worker is alive.
    /// Called automatically by a timer every 30 seconds.
    /// </summary>
    internal async Task SendHeartbeatAsync()
    {
        if (_disposed) return;

        try
        {
            var heartbeatKey = string.Format(WorkerHeartbeatKey, _workerId);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await _redis.StringSetAsync(heartbeatKey, timestamp, expiry: HeartbeatTTL);

            _logger.LogDebug(EventCodes.Redis.HeartbeatSent,
                "Sent heartbeat for worker {WorkerId}. Key={Key}, Timestamp={Timestamp}",
                _workerId, heartbeatKey, timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Redis.KeyOperationFailed,
                "Failed to send heartbeat for worker {WorkerId}: {Message}", _workerId, ex.Message);
        }
    }

    /// <summary>
    /// Clean up all PIDs and metadata for this worker on shutdown.
    /// Called by WebServerHost during graceful shutdown.
    /// </summary>
    public async Task CleanupAsync()
    {
        using var op = _logger.BeginOperation("PidRedisManager.Cleanup", new Dictionary<string, object>
        {
            ["WorkerId"] = _workerId
        });

        try
        {
            var pidsKey = string.Format(WorkerPidsKey, _workerId);
            var trackedPids = await _redis.SetMembersAsync(pidsKey);

            _logger.LogInformation(null, "Cleaning up {Count} PIDs for worker {WorkerId}", trackedPids.Length, _workerId);

            // Delete all PID metadata + worker keys in a transaction
            _logger.LogDebug(EventCodes.Redis.TransactionStarted, "Starting cleanup transaction for worker {WorkerId}", _workerId);
            var transaction = _redis.CreateTransaction();

            // Queue delete operations for PID metadata (don't await yet - transaction pattern)
            var metadataDeleteTasks = new List<Task>();
            foreach (var pid in trackedPids)
            {
                var pidMetadataKey = string.Format(PidMetadataKey, (int)pid);
                metadataDeleteTasks.Add(transaction.KeyDeleteAsync(pidMetadataKey));
            }

            // Queue delete operations for worker keys (don't await yet - transaction pattern)
            var workerKeyDeleteTasks = new List<Task>
            {
                transaction.KeyDeleteAsync(pidsKey),
                transaction.KeyDeleteAsync(string.Format(WorkerHeartbeatKey, _workerId)),
                transaction.KeyDeleteAsync(string.Format(WorkerMetadataKey, _workerId))
            };

            // Execute transaction (now await all queued operations)
            var success = await transaction.ExecuteAsync();

            if (success)
            {
                _logger.LogMilestone(EventCodes.Redis.TransactionCommitted,
                    "Cleanup complete for worker {WorkerId} ({Count} PIDs)",
                    _workerId, trackedPids.Length);
                op.Complete();
            }
            else
            {
                _logger.LogWarning(EventCodes.Redis.TransactionFailed, "Cleanup transaction failed for worker {WorkerId}", _workerId);
                op.Fail(new Exception("Cleanup transaction failed"), ErrorType.Conflict, DependencyName.Redis);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Redis.TransactionFailed, "Failed to cleanup: {Message}", ex.Message);
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
        }
    }

    /// <summary>
    /// Check if a process with the given PID is currently running.
    /// </summary>
    private bool IsProcessRunning(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            return false;
        }
        catch
        {
            // Other errors (access denied, etc.)
            return false;
        }
    }

    /// <summary>
    /// Verify if a process is actually a Node.js sidecar process.
    /// Prevents killing unrelated processes if PID is reused.
    /// </summary>
    private bool IsSidecarProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            var processName = proc.ProcessName.ToLowerInvariant();

            // Check if it's a node process
            if (!processName.Contains("node"))
                return false;

            // Try to get a command line (platform-specific)
            try
            {
                var cmdLine = GetProcessCommandLine(proc);
                return cmdLine != null && cmdLine.Contains("launch_playwright_server");
            }
            catch
            {
                // If we can't get the command line, just check the process name
                return true; // Conservative: assume it's a sidecar
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the process command line (platform-specific implementation).
    /// </summary>
    private string? GetProcessCommandLine(Process proc)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: Use WMI or process.StartInfo (not reliable)
                return null;
            }

            // Unix/Linux/macOS: Use ps command
            var psProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {proc.Id} -o command=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            psProc.Start();
            var output = psProc.StandardOutput.ReadToEnd();
            psProc.WaitForExit();

            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Kill a process by PID.
    /// </summary>
    private void KillProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            _logger.LogInformation(null, "Killed process {Pid}", pid);
        }
        catch (ArgumentException)
        {
            // Process doesn't exist - already dead
            _logger.LogDebug(null, "Process {Pid} already dead", pid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, null, "Failed to kill process {Pid}: {Message}", pid, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _heartbeatTimer.Dispose();
        _logger.LogInformation(null, "Disposed for worker {WorkerId}", _workerId);
    }
}
