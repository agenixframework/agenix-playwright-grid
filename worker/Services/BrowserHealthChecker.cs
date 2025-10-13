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
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Infrastructure.Adapters;

namespace WorkerService.Services;

/// <summary>
///     Background service that periodically checks browser health via Playwright protocol.
///     Detects hung/unresponsive browsers and triggers recycling before client requests fail.
/// </summary>
public sealed class BrowserHealthChecker : BackgroundService
{
    private readonly IDatabase _db;
    private readonly ChunkedLogger<BrowserHealthChecker> _logger;
    private readonly IMetricsPort _metrics;
    private readonly WorkerOptions _options;
    private readonly PoolManager _poolManager;
    private readonly IPlaywrightProtocolClientFactory _clientFactory;

    // Track consecutive failures per browserId
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.OrdinalIgnoreCase);

    public BrowserHealthChecker(
        PoolManager poolManager,
        IDatabase db,
        WorkerOptions options,
        IMetricsPort metrics,
        ChunkedLogger<BrowserHealthChecker> logger,
        IPlaywrightProtocolClientFactory? clientFactory = null)
    {
        _poolManager = poolManager;
        _db = db;
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _clientFactory = clientFactory ?? new PlaywrightProtocolClientFactory();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Feature disabled by default (opt-in)
        var enabled = Environment.GetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_ENABLED");
        if (!bool.TryParse(enabled, out var isEnabled) || !isEnabled)
        {
            _logger.LogInformation(null, "[HealthCheck] Browser health checks disabled (set AGENIX_WORKER_HEALTH_CHECK_ENABLED=true to enable)");
            return;
        }

        var intervalEnv = Environment.GetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS");
        var interval = int.TryParse(intervalEnv, out var i) && i >= 10 ? i : 30;

        var timeoutEnv = Environment.GetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS");
        var timeout = int.TryParse(timeoutEnv, out var t) && t >= 1 ? t : 5;

        var thresholdEnv = Environment.GetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD");
        var failureThreshold = int.TryParse(thresholdEnv, out var thresh) && thresh >= 1 ? thresh : 3;

        _logger.LogInformation(
            EventCodes.BrowserHealth.LoopStarted,
            "[HealthCheck][{NodeId}] Browser health checks enabled (interval={Interval}s, timeout={Timeout}s, threshold={Threshold})",
            _options.NodeId, interval, timeout, failureThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Perform check first, then delay (don't wait before first check)
                await CheckAllBrowsersAsync(TimeSpan.FromSeconds(timeout), failureThreshold, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, EventCodes.BrowserHealth.LoopError, "[HealthCheck][{NodeId}] Health check loop error: {Message}",
                    _options.NodeId, ex.Message);
            }
        }
    }

    private async Task CheckAllBrowsersAsync(TimeSpan timeout, int failureThreshold, CancellationToken ct)
    {
        using var op = _logger.BeginOperation("BrowserHealthCheckLoop", new Dictionary<string, object>
        {
            ["NodeId"] = _options.NodeId,
            ["Timeout"] = timeout.TotalSeconds,
            ["Threshold"] = failureThreshold
        });

        var checkedCount = 0;
        var healthyCount = 0;
        var unhealthyCount = 0;
        var recycledCount = 0;

        foreach (var labelEntry in _poolManager.Pools)
        {
            var labelKey = labelEntry.Key;
            var browserMap = labelEntry.Value;

            foreach (var browserEntry in browserMap.ToArray())
            {
                var browserId = browserEntry.Key;
                var slot = browserEntry.Value;

                // Skip browsers with active client connections (don't interrupt active tests)
                if (_poolManager.HasActiveConnection(browserId))
                {
                    continue;
                }

                checkedCount++;

                // Perform health check
                var isHealthy = await CheckBrowserHealthAsync(browserId, slot.InternalWs, timeout, ct);

                if (isHealthy)
                {
                    healthyCount++;
                    // Reset failure count on success
                    _failureCounts.Remove(browserId);
                    _metrics.RecordBrowserHealthCheck(_options.NodeId, labelKey, slot.BrowserType, success: true);
                }
                else
                {
                    unhealthyCount++;
                    // Increment failure count
                    var failures = _failureCounts.GetValueOrDefault(browserId, 0) + 1;
                    _failureCounts[browserId] = failures;
                    _metrics.RecordBrowserHealthCheck(_options.NodeId, labelKey, slot.BrowserType, success: false);

                    _logger.LogWarning(
                        EventCodes.BrowserHealth.CheckFailed,
                        "[HealthCheck][{NodeId}] Browser {BrowserId} ({BrowserType}, {LabelKey}) health check failed (failures={Failures}/{Threshold})",
                        _options.NodeId, browserId, slot.BrowserType, labelKey, failures, failureThreshold);

                    // Trigger recycling if threshold reached
                    if (failures >= failureThreshold)
                    {
                        _logger.LogError(
                            null,
                            EventCodes.BrowserHealth.RecycleTriggered,
                            "[HealthCheck][{NodeId}] Browser {BrowserId} ({BrowserType}, {LabelKey}) failed {Failures} consecutive health checks - triggering recycle",
                            _options.NodeId, browserId, slot.BrowserType, labelKey, failures);

                        try
                        {
                            // Set recycle flag for ReconcileLoop to pick up
                            // Store timestamp (Unix seconds) as value for latency tracking
                            // Reduced TTL from 5min to 1min for faster flag expiration
                            // (Option C: Optimize Integration - better coordination)
                            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            await _db.StringSetAsync(RedisKeys.Recycle(browserId), timestamp.ToString(), TimeSpan.FromMinutes(1));
                            recycledCount++;
                            _failureCounts.Remove(browserId); // Reset counter after triggering recycle
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, EventCodes.BrowserHealth.RecycleFailed, "[HealthCheck][{NodeId}] Failed to set recycle flag for {BrowserId}: {Message}",
                                _options.NodeId, browserId, ex.Message);
                        }
                    }
                }
            }
        }

        op.SetOutputs(new Dictionary<string, object>
        {
            ["Checked"] = checkedCount,
            ["Healthy"] = healthyCount,
            ["Unhealthy"] = unhealthyCount,
            ["Recycled"] = recycledCount
        });

        if (checkedCount > 0)
        {
            _logger.LogInformation(
                EventCodes.BrowserHealth.LoopCompleted,
                "[HealthCheck][{NodeId}] Checked {Checked} browsers: {Healthy} healthy, {Unhealthy} unhealthy, {Recycled} recycled",
                _options.NodeId, checkedCount, healthyCount, unhealthyCount, recycledCount);
        }
        else
        {
            _logger.LogDebug(null, "[HealthCheck][{NodeId}] No browsers to check (all have active connections)",
                _options.NodeId);
        }

        op.Complete();
    }

    private async Task<bool> CheckBrowserHealthAsync(string browserId, string wsEndpoint, TimeSpan timeout,
        CancellationToken ct)
    {
        using var op = _logger.BeginOperation("CheckBrowserHealth", new Dictionary<string, object>
        {
            ["BrowserId"] = browserId,
            ["WsEndpoint"] = wsEndpoint,
            ["Timeout"] = timeout.TotalSeconds
        });

        _logger.LogInformation(EventCodes.BrowserHealth.CheckStarted,
            "[HealthCheck][{NodeId}] Starting health check for browser {BrowserId}",
            _options.NodeId, browserId);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = _clientFactory.CreateClient();

            // Connect to browser's WebSocket
            _logger.LogDebug(null, "[HealthCheck][{NodeId}] Connecting to {WsEndpoint}...", _options.NodeId, wsEndpoint);
            await client.ConnectAsync(wsEndpoint, timeout, ct);

            // Send Browser.version command (lightweight protocol call)
            _logger.LogDebug(null, "[HealthCheck][{NodeId}] Sending Browser.version command...", _options.NodeId);
            var response = await client.SendCommandAsync("Browser.version", timeout, ct);

            sw.Stop();

            // Health check succeeds if we get any response
            var isHealthy = !string.IsNullOrWhiteSpace(response);

            if (isHealthy)
            {
                _logger.LogInformation(EventCodes.BrowserHealth.CheckPassed,
                    "[HealthCheck][{NodeId}] Browser {BrowserId} health check passed ({ElapsedMs}ms)",
                    _options.NodeId, browserId, sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(EventCodes.BrowserHealth.CheckFailed,
                    "[HealthCheck][{NodeId}] Browser {BrowserId} health check failed - no response ({ElapsedMs}ms)",
                    _options.NodeId, browserId, sw.ElapsedMilliseconds);
            }

            await client.CloseAsync(ct);

            // Record latency metric
            _metrics.RecordBrowserHealthCheckDuration(_options.NodeId, sw.Elapsed.TotalSeconds);

            op.SetOutputs(new Dictionary<string, object>
            {
                ["Healthy"] = isHealthy,
                ["ElapsedMs"] = sw.ElapsedMilliseconds,
                ["Response"] = response ?? ""
            });
            op.Complete();

            return isHealthy;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                EventCodes.BrowserHealth.CheckException,
                "[HealthCheck][{NodeId}] Browser {BrowserId} health check exception after {ElapsedMs}ms: {Message}",
                _options.NodeId, browserId, sw.ElapsedMilliseconds, ex.Message);

            _metrics.RecordBrowserHealthCheckDuration(_options.NodeId, sw.Elapsed.TotalSeconds);

            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Playwright);

            return false;
        }
    }
}
