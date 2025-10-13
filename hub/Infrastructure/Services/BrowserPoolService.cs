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
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Service implementation for browser pool operations.
///     Extracted from EndpointMappingExtensions.cs for reusability across test run and legacy borrow endpoints.
/// </summary>
public sealed class BrowserPoolService : IBrowserPoolService
{
    // Lua scripts for atomic operations (plain strings for ScriptEvaluateAsync)
    private const string LuaFindPopScript = @"
local listKey = KEYS[1]
local inuseKey = KEYS[2]
local len = redis.call('LLEN', listKey)
if len == 0 then return nil end
local item = redis.call('LPOP', listKey)
if item then redis.call('RPUSH', inuseKey, item); return item end
return nil
";

    // Atomically move item from inuse back to available by value
    private const string LuaReturnToPoolScript = @"
local inuseKey = KEYS[1]
local availKey = KEYS[2]
local browserEntry = ARGV[1]
local removed = redis.call('LREM', inuseKey, 1, browserEntry)
if removed > 0 then
    redis.call('RPUSH', availKey, browserEntry)
    return 1
end
return 0
";

    // Atomically move item from inuse back to available (for quarantine skip)
    private const string LuaSkipQuarantinedScript = @"
local inuseKey = KEYS[1]
local availKey = KEYS[2]
local browserEntry = ARGV[1]
local removed = redis.call('LREM', inuseKey, 1, browserEntry)
if removed > 0 then
    redis.call('RPUSH', availKey, browserEntry)
    return 1
end
return 0
";

    private readonly ChunkedLogger _chunkedLogger;
    private readonly IConfiguration _config;
    private readonly IDatabase _db;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<BrowserPoolService> _logger;
    private readonly IResultsStore _resultsStore;

    public BrowserPoolService(
        IDatabase db,
        IResultsStore resultsStore,
        IEventPublisher eventPublisher,
        ILogger<BrowserPoolService> logger,
        IConfiguration config,
        ChunkedLogger chunkedLogger)
    {
        _db = db;
        _resultsStore = resultsStore;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _config = config;
        _chunkedLogger = chunkedLogger;
    }

    public async Task<BrowserBorrowResult> TryBorrowBrowserAsync(
        string labelKey,
        string runId,
        string? runName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = new Dictionary<string, object>
        {
            ["labelKey"] = labelKey,
            ["runId"] = runId,
            ["runName"] = runName ?? "null"
        };

        // Link to a parent HTTP operation if one exists
        var parentOpId = OperationContext.Current?.OperationId;
        using var operation = (dynamic)_chunkedLogger.BeginOperation("TryBorrowBrowser", inputs, parentOperationId: parentOpId);

        try
        {
            // Parse and normalize label key
            if (!LabelKey.TryParseDetailed(labelKey, out var parsed, out var parseError))
            {
                operation.Fail(
                    new ArgumentException($"Invalid labelKey: {parseError}"),
                    ErrorType.Validation);
                return BrowserBorrowResult.FailureResult($"Invalid labelKey: {parseError}", 400);
            }

            labelKey = parsed!.Normalized;

            // Event code: Borrow requested
            _chunkedLogger.LogMilestone(EventCodes.BrowserPool.BorrowRequested,
                "Browser borrow requested",
                labelKey, runId);

            // Check maintenance mode
            try
            {
                if (await _db.KeyExistsAsync(RedisKeys.MaintenanceFlag(labelKey)))
                {
                    _chunkedLogger.LogInformation(null, "Pool {LabelKey} is under maintenance", labelKey);
                    return BrowserBorrowResult.MaintenanceResult(labelKey);
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogWarning(ex, null, "Failed to check maintenance mode for {LabelKey}", labelKey);
            }

            // Attempt to borrow from the pool
            var item = await TryBorrowFromPoolAsync(labelKey, cancellationToken);
            if (item == null)
            {
                operation.Fail(
                    new InvalidOperationException($"No browser capacity available for label: {labelKey}"),
                    ErrorType.NotFound);
                return BrowserBorrowResult.FailureResult(
                    $"No browser capacity available for label: {labelKey}",
                    503);
            }

            // Extract browser session details
            string? browserId = null, wsEndpoint = null, browserType = null, nodeId = null;
            string? browserVersion = null, playwrightVersion = null, regionOs = null;

            try
            {
                if (item.Value.TryGetProperty("browserId", out var bidEl) &&
                    bidEl.ValueKind == JsonValueKind.String)
                {
                    browserId = bidEl.GetString();
                }

                if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl) &&
                    wsEl.ValueKind == JsonValueKind.String)
                {
                    wsEndpoint = wsEl.GetString();
                }

                if (item.Value.TryGetProperty("browserType", out var btEl) &&
                    btEl.ValueKind == JsonValueKind.String)
                {
                    browserType = btEl.GetString();
                }

                if (item.Value.TryGetProperty("nodeId", out var nodeEl) &&
                    nodeEl.ValueKind == JsonValueKind.String)
                {
                    nodeId = nodeEl.GetString();
                }

                if (item.Value.TryGetProperty("browserVersion", out var bvEl) &&
                    bvEl.ValueKind == JsonValueKind.String)
                {
                    browserVersion = bvEl.GetString();
                }

                // Get additional metadata from node (Playwright version, Region/OS)
                if (!string.IsNullOrWhiteSpace(nodeId))
                {
                    var pwVal = await _db.HashGetAsync($"node:{nodeId}", "PlaywrightVersion");
                    if (!pwVal.IsNullOrEmpty)
                    {
                        playwrightVersion = pwVal.ToString();
                    }

                    var regionVal = await _db.HashGetAsync($"node:{nodeId}", "RegionOs");
                    if (!regionVal.IsNullOrEmpty)
                    {
                        regionOs = regionVal.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // Silently ignore - non-critical metadata extraction failure
            }

            // Map browserId to runId for attribution
            if (!string.IsNullOrWhiteSpace(browserId))
            {
                try
                {
                    await _db.StringSetAsync($"browser_run:{browserId}", runId, TimeSpan.FromHours(6));
                }
                catch (Exception)
                {
                    // Silently ignore - non-critical attribution mapping failure
                }
            }

            // Publish borrow command event
            try
            {
                var evt = new CommandEvent
                {
                    EventType = "CommandAppended",
                    RunId = runId,
                    TimestampUtc = DateTime.UtcNow,
                    DataJson = JsonSerializer.Serialize(new
                    {
                        kind = "Borrow",
                        message = $"Borrowed browser for {labelKey}",
                        propsJson =
                            JsonSerializer.Serialize(new Dictionary<string, string> { ["labelKey"] = labelKey })
                    }),
                    CorrelationId = Guid.NewGuid().ToString()
                };
                await _eventPublisher.PublishCommandEventAsync(evt, null, cancellationToken);
            }
            catch (Exception)
            {
                // Silently ignore - non-critical event publishing failure
            }

            // Calculate TTLs from configuration
            var borrowTtlSeconds = int.TryParse(_config["AGENIX_HUB_BORROW_TTL_SECONDS"], out var bttl) ? Math.Max(60, bttl) : 900;
            var idleTimeoutSeconds = int.TryParse(_config["AGENIX_HUB_IDLE_TIMEOUT_SECONDS"], out var idle) ? Math.Max(10, idle) : 120;
            var maxSessionTtlSeconds = int.TryParse(_config["AGENIX_HUB_MAX_SESSION_TTL_SECONDS"], out var maxTtl) ? Math.Max(60, maxTtl) : 24 * 60 * 60;

            var expiresAt = DateTime.UtcNow.AddSeconds(borrowTtlSeconds);

            // Store session metadata in Redis
            if (!string.IsNullOrWhiteSpace(browserId))
            {
                try
                {
                    // 1. Set the initial borrow lease (lease)
                    await _db.StringSetAsync(RedisKeys.BorrowTtl(browserId), "1", TimeSpan.FromSeconds(borrowTtlSeconds));

                    // 2. Set the initial idle timeout (refreshed by worker on activity)
                    await _db.StringSetAsync($"borrow_idle:{browserId}", "1", TimeSpan.FromSeconds(idleTimeoutSeconds));

                    // 3. Store session registration (metadata) with a safety TTL
                    var sessionKey = $"session:{browserId}";
                    var fields = new HashEntry[]
                    {
                        new("browserId", browserId), new("labelKey", labelKey), new("runId", runId),
                        new("nodeId", nodeId ?? string.Empty), new("borrowedAtUtc", DateTime.UtcNow.ToString("o")),
                        new("ttlSeconds", borrowTtlSeconds.ToString())
                    };
                    await _db.HashSetAsync(sessionKey, fields);
                    await _db.KeyExpireAsync(sessionKey, TimeSpan.FromSeconds(maxSessionTtlSeconds));
                }
                catch (Exception ex)
                {
                    _chunkedLogger.LogWarning(ex, null, "Failed to persist session metadata for browser {BrowserId}", browserId);
                }
            }

            // Event code: Browser allocated successfully
            _chunkedLogger.LogMilestone(EventCodes.BrowserPool.BrowserAllocated,
                "Browser allocated successfully",
                browserId!, nodeId!);

            operation.SetOutputs(new Dictionary<string, object>
            {
                ["browserId"] = browserId ?? "unknown",
                ["nodeId"] = nodeId ?? "unknown",
                ["browserType"] = browserType ?? "unknown",
                ["wsEndpoint"] = wsEndpoint ?? "unknown",
                ["labelKey"] = labelKey,
                ["ttlSeconds"] = borrowTtlSeconds
            });

            return BrowserBorrowResult.SuccessResult(
                browserId ?? string.Empty,
                wsEndpoint ?? string.Empty,
                browserType,
                nodeId,
                nodeId, // workerNodeId is same as nodeId
                playwrightVersion,
                browserVersion,
                regionOs,
                expiresAt);
        }
        catch (RedisException ex)
        {
            var context = OperationContext.Current;
            _chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Redis);
            throw;
        }
        catch (TimeoutException ex)
        {
            var context = OperationContext.Current;
            _chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            var context = OperationContext.Current;
            _chunkedLogger.FailOperation(context, ex, ErrorType.Unexpected);
            throw;
        }
    }

    public async Task ReturnBrowserAsync(
        string browserId,
        string? nodeId,
        string? finalStatus = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = new Dictionary<string, object>
        {
            ["browserId"] = browserId ?? "null",
            ["nodeId"] = nodeId ?? "null",
            ["finalStatus"] = finalStatus ?? "null"
        };

        // Link to parent HTTP operation if one exists
        var parentOpId = OperationContext.Current?.OperationId;
        using var operation = (dynamic)_chunkedLogger.BeginOperation("ReturnBrowser", inputs, parentOperationId: parentOpId);

        try
        {
            if (string.IsNullOrWhiteSpace(browserId))
            {
                operation.Fail(
                    new ArgumentException("browserId is required"),
                    ErrorType.Validation);
                _chunkedLogger.LogWarning(null, "Attempted to return browser with null/empty browserId");
                return;
            }

            // Get label key from session metadata
            string? labelKey = null;
            try
            {
                var sessionKey = $"session:{browserId}";
                var labelVal = await _db.HashGetAsync(sessionKey, "labelKey");
                if (!labelVal.IsNullOrEmpty)
                {
                    labelKey = labelVal.ToString();
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogWarning(ex, null, "Failed to retrieve session labelKey for browser {BrowserId}", browserId);
            }

            if (string.IsNullOrWhiteSpace(labelKey))
            {
                _chunkedLogger.LogWarning(null, "Cannot return browser {BrowserId}: labelKey not found in session", browserId);
                return;
            }

            // Normalize label key
            if (!LabelKey.TryParse(labelKey, out var parsed))
            {
                _chunkedLogger.LogWarning(null, "Invalid labelKey {LabelKey} for browser {BrowserId}", labelKey, browserId);
                return;
            }

            labelKey = parsed!.Normalized;

            // Event code: Return requested
            _chunkedLogger.LogMilestone(EventCodes.BrowserPool.ReturnRequested,
                "Browser return requested",
                browserId, labelKey);

            // Return browser to the pool using a Lua script
            var inuseKey = RedisKeys.InUse(labelKey);
            var availKey = RedisKeys.Available(labelKey);

            try
            {
                // Find the browser entry in the in-use list
                // This is complex because we need the full JSON entry, not just the browserId
                var inuseItems = await _db.ListRangeAsync(inuseKey);
                string? browserEntry = null;

                foreach (var item in inuseItems)
                {
                    if (item.IsNullOrEmpty)
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(item.ToString());
                        if (doc.RootElement.TryGetProperty("browserId", out var bidEl) &&
                            bidEl.ValueKind == JsonValueKind.String &&
                            bidEl.GetString() == browserId)
                        {
                            browserEntry = item.ToString();
                            break;
                        }
                    }
                    catch
                    {
                        // Ignore parse errors
                    }
                }

                if (browserEntry != null)
                {
                    // Atomically move from in-use to available using a Lua script
                    var result = await _db.ScriptEvaluateAsync(LuaReturnToPoolScript,
                        [inuseKey, availKey],
                        [browserEntry]);

                    if (!result.IsNull && (int)result > 0)
                    {
                        _chunkedLogger.LogMilestone(EventCodes.BrowserPool.BrowserReturned,
                            "Returned browser {BrowserId} to pool for label {LabelKey}",
                            browserId, labelKey);
                    }
                    else
                    {
                        _chunkedLogger.LogWarning(null,
                            "Browser {BrowserId} not found in in-use list during atomic return (already returned or expired)",
                            browserId);
                    }
                }
                else
                {
                    _chunkedLogger.LogInformation(null,
                        "Browser {BrowserId} not found in in-use list (already returned or expired)",
                        browserId);
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogError(ex, null, "Failed to return browser {BrowserId} to pool", browserId);
            }

            // Clean up session metadata
            try
            {
                await _db.KeyDeleteAsync($"borrow_ttl:{browserId}");
                await _db.KeyDeleteAsync($"borrow_idle:{browserId}");
                await _db.KeyDeleteAsync($"session:{browserId}");
                await _db.KeyDeleteAsync($"browser_run:{browserId}");
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogWarning(ex, null, "Failed to clean up session metadata for browser {BrowserId}", browserId);
            }

            // Request sidecar recycle
            try
            {
                await _db.StringSetAsync($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2));
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogWarning(ex, null, "Failed to request recycle for browser {BrowserId}", browserId);
            }

            operation.SetOutputs(new Dictionary<string, object>
            {
                ["browserId"] = browserId,
                ["labelKey"] = labelKey ?? "unknown",
                ["nodeId"] = nodeId ?? "unknown"
            });
        }
        catch (RedisException ex)
        {
            var context = OperationContext.Current;
            _chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Redis);
            throw;
        }
        catch (TimeoutException ex)
        {
            var context = OperationContext.Current;
            _chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            var context = OperationContext.Current;
            _chunkedLogger.FailOperation(context, ex, ErrorType.Unexpected);
            throw;
        }
    }

    /// <summary>
    ///     Attempts to borrow a browser from the specified pool.
    ///     Handles maintenance mode and quarantined nodes.
    /// </summary>
    private async Task<JsonElement?> TryBorrowFromPoolAsync(string labelKey, CancellationToken cancellationToken)
    {
        var availKey = RedisKeys.Available(labelKey);
        var inuseKey = RedisKeys.InUse(labelKey);

        // Pop from available and push to in-use atomically
        // Also check for quarantined nodes and skip them
        for (var attempts = 0; attempts < 128; attempts++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                var res = await _db.ScriptEvaluateAsync(LuaFindPopScript,
                    [availKey, inuseKey],
                    []);
                if (res.IsNull)
                {
                    return null;
                }

                var json = res.ToString();
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check if the node is quarantined
                if (root.TryGetProperty("nodeId", out var nodeEl) &&
                    nodeEl.ValueKind == JsonValueKind.String)
                {
                    var nodeId = nodeEl.GetString();
                    if (!string.IsNullOrWhiteSpace(nodeId))
                    {
                        var qTtl = await _db.KeyTimeToLiveAsync($"node:quarantine:{nodeId}");
                        if (qTtl is not null)
                        {
                            // Node is quarantined, atomically move back to available and try next
                            await _db.ScriptEvaluateAsync(LuaSkipQuarantinedScript,
                                [inuseKey, availKey],
                                [json]);
                            continue;
                        }
                    }
                }

                // Valid browser found
                return root.Clone();
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogError(ex, null, "Error while borrowing from pool {LabelKey}", labelKey);
                return null;
            }
        }

        _chunkedLogger.LogWarning(null, "Exhausted retry attempts borrowing from pool {LabelKey}", labelKey);
        return null;
    }
}
