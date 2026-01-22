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
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Shared.Logging;
using StackExchange.Redis;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class HeartbeatService(
    WorkerOptions options,
    IDatabase db,
    ChunkedLogger<HeartbeatService>? chunkedLogger = null)
{
    private readonly ChunkedLogger<HeartbeatService>? _chunkedLogger = chunkedLogger;

    private DateTimeOffset _lastHeartbeatTime = DateTimeOffset.UtcNow;
    private Func<Task>? _onGapDetectedCallback;

    /// <summary>
    ///     Sets the callback to invoke when a timer gap is detected (system sleep/wake).
    ///     This callback should trigger worker re-registration.
    /// </summary>
    public void SetGapDetectedCallback(Func<Task> callback)
    {
        _onGapDetectedCallback = callback;
    }

    public async Task HeartbeatOnceAsync()
    {
        using var op = _chunkedLogger?.BeginOperation("HeartbeatOnce", new Dictionary<string, object>
        {
            ["nodeId"] = options.NodeId
        });

        try
        {
            var key = RedisKeys.Node(options.NodeId);
            var nowIso = DateTime.UtcNow.ToString("o");
            await db.HashSetAsync(key, "LastSeen", nowIso);
            var lblsJson = JsonSerializer.Serialize(options.Labels.ToDictionary(k => k.Key, v => v.Value));
            await db.HashSetAsync(key, "Labels", lblsJson);
            var capacity = options.PoolConfig.Values.Sum();
            await db.HashSetAsync(key, "Capacity", capacity.ToString());
            await db.SetAddAsync("nodes", options.NodeId);
            await db.StringSetAsync(RedisKeys.NodeAlive(options.NodeId), "1", TimeSpan.FromSeconds(90));

            _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatTick,
                "One-time heartbeat successful for {NodeId} (capacity={Capacity})",
                options.NodeId, capacity);

            op?.Complete();
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatFailed, ex,
                "One-time heartbeat failed: {message}", ex.Message);
            op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
        }
    }

    public async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var hbInterval = TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds);
        var gapThreshold = TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds * 2);

        _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatStarted,
            "Heartbeat loop started", options.NodeId, hbInterval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            using var op = _chunkedLogger?.BeginOperation("HeartbeatTick", new Dictionary<string, object>
            {
                ["nodeId"] = options.NodeId
            });

            try
            {
                // FAST PATH: Detect timer gap (system sleep/wake scenario)
                var now = DateTimeOffset.UtcNow;
                var gap = now - _lastHeartbeatTime;

                if (gap > gapThreshold)
                {
                    _chunkedLogger?.LogWarning(EventCodes.Worker.HeartbeatGapDetected,
                        "Detected timer gap of {GapSeconds}s (expected <{ThresholdSeconds}s). System may have slept. Triggering re-registration...",
                        gap.TotalSeconds,
                        gapThreshold.TotalSeconds);

                    // Trigger re-registration via callback
                    if (_onGapDetectedCallback != null)
                    {
                        try
                        {
                            await _onGapDetectedCallback();
                        }
                        catch (Exception cbEx)
                        {
                            _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatFailed, cbEx,
                                "Error invoking gap detection callback: {message}", cbEx.Message);
                        }
                    }
                    else
                    {
                        _chunkedLogger?.LogWarning(null,
                            "[Heartbeat] Gap detected but no callback registered. Re-registration will not occur.");
                    }
                }

                // Normal heartbeat
                var key = RedisKeys.Node(options.NodeId);
                var nowIso = DateTime.UtcNow.ToString("o");
                await db.HashSetAsync(key, "LastSeen", nowIso);
                var lblsJson = JsonSerializer.Serialize(options.Labels.ToDictionary(k => k.Key, v => v.Value));
                await db.HashSetAsync(key, "Labels", lblsJson);
                var capacity = options.PoolConfig.Values.Sum();
                await db.HashSetAsync(key, "Capacity", capacity.ToString());
                await db.SetAddAsync("nodes", options.NodeId);
                await db.StringSetAsync(RedisKeys.NodeAlive(options.NodeId), "1", TimeSpan.FromSeconds(90));

                // Update last heartbeat time after a successful heartbeat
                _lastHeartbeatTime = now;

                // Log successful heartbeat tick (similar to hub's background services)
                _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatTick,
                    "Heartbeat tick done: nodeId={NodeId} capacity={Capacity}", options.NodeId, capacity);

                op?.SetOutputs(new Dictionary<string, object>
                {
                    ["lastSeen"] = nowIso,
                    ["capacity"] = capacity
                });
                op?.Complete();
            }
            catch (Exception ex)
            {
                _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatFailed, ex,
                    "Heartbeat tick failed: {message}", ex.Message);
                op?.Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
            }

            try
            {
                await Task.Delay(hbInterval, ct);
            }
            catch
            {
                // Ignored - cancellation or task delay error
            }
        }

        _chunkedLogger?.LogMilestone(EventCodes.Worker.HeartbeatStopped,
            "Heartbeat loop stopped", options.NodeId);
    }
}
