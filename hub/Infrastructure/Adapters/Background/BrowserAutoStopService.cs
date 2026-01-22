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
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.Audit;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using PlaywrightHub.Infrastructure.Metrics;
using PlaywrightHub.Infrastructure.Services;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

/// <summary>
///     Periodically scans for long-running inactive test items and auto-stops them,
///     releasing any borrowed browsers back to the pool and notifying subscribers.
///     Only processes Test and Scenario item types (the only types that borrow browsers).
///     Runs every 5 minutes by default.
///     Formerly BrowserCleanupService - renamed to BrowserAutoStopService for clarity (cleanup implies deletion).
/// </summary>
public sealed class BrowserAutoStopService(
    IResultsStore resultsStore,
    IEventPublisher eventPublisher,
    IHubContext<ResultsHub, IResultsClient> resultsHub,
    IHubContext<LaunchesHub, ILaunchesClient> launchesHub,
    IConfiguration config,
    IAuditStore auditStore,
    TestRunMetrics metrics,
    IBrowserPoolService browserPool,
    ILogger<BrowserAutoStopService> logger,
    ChunkedLogger<BrowserAutoStopService> chunkedLogger
) : BackgroundService
{
    private readonly IConfiguration _config = config;
    private readonly IResultsStore _resultsStore = resultsStore;
    private readonly IEventPublisher _eventPublisher = eventPublisher;
    private readonly IHubContext<ResultsHub, IResultsClient> _resultsHub = resultsHub;
    private readonly IHubContext<LaunchesHub, ILaunchesClient> _launchesHub = launchesHub;
    private readonly IAuditStore _auditStore = auditStore;
    private readonly TestRunMetrics _metrics = metrics;
    private readonly IBrowserPoolService _browserPool = browserPool;
    private readonly ILogger<BrowserAutoStopService> _logger = logger;
    private readonly ChunkedLogger<BrowserAutoStopService> _chunkedLogger = chunkedLogger;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = int.TryParse(_config["AGENIX_HUB_CLEANUP_INTERVAL_MINUTES"], out var im)
            ? Math.Max(1, im)
            : 5;
        var inactivityMinutes = int.TryParse(_config["AGENIX_HUB_INACTIVITY_THRESHOLD_MINUTES"], out var ia)
            ? Math.Max(1, ia)
            : 30;
        var maxHours = int.TryParse(_config["AGENIX_HUB_MAX_RUN_DURATION_HOURS"], out var mh)
            ? Math.Max(1, mh)
            : 3;
        var batchSize = int.TryParse(_config["AGENIX_HUB_CLEANUP_BATCH_SIZE"], out var bs)
            ? Math.Max(1, bs)
            : 50;
        var debug = bool.TryParse(_config["AGENIX_HUB_CLEANUP_DEBUG"], out var dbg) && dbg;

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var inactivity = TimeSpan.FromMinutes(inactivityMinutes);

        // Determine max duration
        TimeSpan maxDuration;
        if (int.TryParse(_config["AGENIX_HUB_MAX_RUN_DURATION_MINUTES"], out var maxMinutesOverride) &&
            maxMinutesOverride > 0)
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

        // Log service start
        _chunkedLogger.LogMilestone(
            EventCodes.BrowserAutoStop.ScanStarted, // BST01
            "interval={Interval}m inactivity={Inactivity}m max={Max} batch={Batch}",
            interval.TotalMinutes, inactivity.TotalMinutes, maxDisplay, batchSize);

        _logger.LogInformation(
            "[BrowserAutoStop] Starting. interval={interval}m inactivity={inactivity}m max={max} batch={batch}",
            interval.TotalMinutes, inactivity.TotalMinutes, maxDisplay, batchSize);

        // Initial startup delay (30 seconds)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Main loop
        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTime.UtcNow;
            int scanned = 0, processed = 0, releasedTotal = 0, errors = 0;
            int skipIdle = 0, skipDuration = 0, skipNoOutstanding = 0;

            var inputs = new Dictionary<string, object>
            {
                ["intervalMinutes"] = interval.TotalMinutes,
                ["inactivityMinutes"] = inactivity.TotalMinutes,
                ["maxDuration"] = maxDisplay,
                ["batchSize"] = batchSize,
                ["debug"] = debug
            };

            using var operation = (dynamic)_chunkedLogger.BeginOperation("BrowserAutoStopTick", inputs);

            try
            {
                // Get active test items with browser sessions (Test and Scenario types only)
                var candidates = await _resultsStore.GetActiveTestItemsAsync(
                    0,
                    batchSize,
                    ["Queued", "Running"],
                    ["Test", "Scenario"] // Only types that borrow browsers
                );

                scanned = candidates.Count;

                // Event: Active items retrieved
                _chunkedLogger.LogMilestone(
                    EventCodes.BrowserAutoStop.ActiveItemsRetrieved, // BST02
                    "count={Count}",
                    scanned);

                foreach (var testItem in candidates)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    if (processed >= batchSize)
                        break;

                    try
                    {
                        // Log item selected
                        _chunkedLogger.LogMilestone(
                            EventCodes.BrowserAutoStop.ItemSelected, // BST03
                            "itemId={ItemId} name={Name}",
                            testItem.Id, testItem.Name);

                        // Check inactivity and duration
                        var cmds = await _resultsStore.GetCommandsAsync(testItem.Id.ToString(), 0, 5000);
                        var lastActivityDateTime = cmds.Count > 0
                            ? cmds.Max(c => c.TimestampUtc)
                            : testItem.StartTime.UtcDateTime;
                        var now = DateTimeOffset.UtcNow;

                        var inactiveLongEnough = now - lastActivityDateTime >= inactivity;
                        var overMaxDuration = now - testItem.StartTime >= maxDuration;

                        if (!(inactiveLongEnough || overMaxDuration))
                        {
                            // Track skip reasons
                            if (!inactiveLongEnough) skipIdle++;
                            if (!overMaxDuration) skipDuration++;
                            continue;
                        }

                        // Inactivity check completed
                        _chunkedLogger.LogMilestone(
                            EventCodes.BrowserAutoStop.InactivityMet, // BST11
                            "itemId={ItemId} inactiveMs={InactiveMs}",
                            testItem.Id, (now - lastActivityDateTime).TotalMilliseconds);

                        // Find outstanding browsers from command logs
                        var outstanding = await FindOutstandingBrowsersAsync(testItem, cmds);

                        if (outstanding.Count == 0)
                        {
                            skipNoOutstanding++;
                            continue;
                        }

                        // Outstanding browsers detected
                        _chunkedLogger.LogMilestone(
                            EventCodes.BrowserAutoStop.OutstandingBrowsersDetected, // BST23
                            "itemId={ItemId} browserCount={Count}",
                            testItem.Id, outstanding.Count);

                        // Return browsers
                        var released = await ReturnBrowsersAsync(testItem, outstanding);
                        releasedTotal += released;

                        // SignalR notification (if configured)
                        if (testItem.LaunchId != Guid.Empty)
                        {
                            await _launchesHub.Clients.Group($"launch:{testItem.LaunchId}")
                                .LaunchUpdated(testItem.ProjectKey ?? "unknown", testItem.LaunchId);

                            _chunkedLogger.LogMilestone(
                                EventCodes.BrowserAutoStop.SignalRNotificationSent, // BST40
                                "launchId={LaunchId} itemId={ItemId}",
                                testItem.LaunchId, testItem.Id);
                        }

                        // Event publication
                        var autoStopEvent = new TestItemAutoStoppedEvent
                        {
                            ItemId = testItem.Id,
                            LaunchId = testItem.LaunchId,
                            AutoStopReason = inactiveLongEnough ? "Inactivity" : "MaxDuration",
                            ReleasedBrowserCount = released
                        };

                        await _eventPublisher.PublishTestItemEventAsync(autoStopEvent);

                        _chunkedLogger.LogMilestone(
                            EventCodes.BrowserAutoStop.EventPublished, // BST41
                            "itemId={ItemId} launchId={LaunchId} reason={Reason}",
                            testItem.Id, testItem.LaunchId, autoStopEvent.AutoStopReason);

                        processed++;
                    }
                    catch (Exception exItem)
                    {
                        errors++;
                        _logger.LogWarning(exItem, "[BrowserAutoStop] Failed to process item {itemId}: {error}",
                            testItem.Id, exItem.Message);
                        // Continue processing other items
                    }
                }

                // Log completion
                var outputs = new Dictionary<string, object>
                {
                    ["scanned"] = scanned,
                    ["processed"] = processed,
                    ["released"] = releasedTotal,
                    ["errors"] = errors,
                    ["skipIdle"] = skipIdle,
                    ["skipDuration"] = skipDuration,
                    ["skipNoOutstanding"] = skipNoOutstanding
                };

                operation.SetOutputs(outputs);
                operation.Complete();

                _chunkedLogger.LogMilestone(
                    EventCodes.BrowserAutoStop.BatchCompleted, // BST51
                    "processed={Processed} released={Released} errors={Errors} duration={DurationMs}ms",
                    processed, releasedTotal, errors,
                    (int)(DateTime.UtcNow - tickStart).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected, DependencyName.ResultsStore);

                _logger.LogError(ex, "[BrowserAutoStop] Batch processing failed: {error}", ex.Message);

                _chunkedLogger.LogMilestone(
                    EventCodes.BrowserAutoStop.BatchCompleted, // BST51 (with failure)
                    "error={Error} processed={Processed}",
                    ex.Message, processed);
            }

            // Wait before next iteration
            await Task.Delay(interval, stoppingToken);
        }
    }

    private Task<List<string>> FindOutstandingBrowsersAsync(TestItemDto testItem,
        IReadOnlyList<CommandLogEventDto> cmds)
    {
        _chunkedLogger.LogMilestone(
            EventCodes.BrowserAutoStop.CommandLogAnalysisStarted, // BST20
            "itemId={ItemId} commandCount={Count}",
            testItem.Id, cmds.Count);

        // Parse commands to find launch/return
        var launchCommands = cmds.Where(c =>
            c.Kind.Equals("ServerLaunch", StringComparison.OrdinalIgnoreCase) &&
            c.Props?.ContainsKey("browserId") == true).ToList();

        var returnCommands = cmds.Where(c =>
            c.Kind.Equals("Return", StringComparison.OrdinalIgnoreCase) &&
            c.Props?.ContainsKey("browserId") == true).ToList();

        foreach (var launchCmd in launchCommands)
        {
            _chunkedLogger.LogMilestone(
                EventCodes.BrowserAutoStop.LaunchCommandFound, // BST21
                "itemId={ItemId} browserId={BrowserId}",
                testItem.Id, launchCmd.Props?["browserId"]);
        }

        foreach (var returnCmd in returnCommands)
        {
            _chunkedLogger.LogMilestone(
                EventCodes.BrowserAutoStop.ReturnCommandFound, // BST22
                "itemId={ItemId} browserId={BrowserId}",
                testItem.Id, returnCmd.Props?["browserId"]);
        }

        // Outstanding = launched but not returned
        var launchedBrowsers = launchCommands.Select(c => c.Props!["browserId"]);
        var returnedBrowsers = returnCommands.Select(c => c.Props!["browserId"]).ToHashSet();

        var outstanding = launchedBrowsers.Where(bid => !returnedBrowsers.Contains(bid)).ToList();

        _chunkedLogger.LogMilestone(
            EventCodes.BrowserAutoStop.OutstandingBrowsersDetected, // BST23
            "itemId={ItemId} count={Count}",
            testItem.Id, outstanding.Count);

        return Task.FromResult(outstanding);
    }

    private async Task<int> ReturnBrowsersAsync(TestItemDto testItem, List<string> browserIds)
    {
        var released = 0;

        _chunkedLogger.LogMilestone(
            EventCodes.BrowserAutoStop.BrowserReturnStarted, // BST30
            "itemId={ItemId} browserCount={Count}",
            testItem.Id, browserIds.Count);

        foreach (var browserId in browserIds)
        {
            try
            {
                await _browserPool.ReturnBrowserAsync(
                    browserId,
                    testItem.WorkerNodeId ?? "unknown",
                    testItem.LabelKey ?? "unknown"
                );

                released++;

                _chunkedLogger.LogMilestone(
                    EventCodes.BrowserAutoStop.BrowserReturned, // BST31
                    "itemId={ItemId} browserId={BrowserId}",
                    testItem.Id, browserId);
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.BrowserAutoStop.BrowserReturnFailed, // BST32
                    ex,
                    "itemId={ItemId} browserId={BrowserId} error={Error}",
                    testItem.Id, browserId, ex.Message);

                _logger.LogWarning(ex, "[BrowserAutoStop] Failed to return browser {browserId} for item {itemId}",
                    browserId, testItem.Id);
            }
        }

        _chunkedLogger.LogMilestone(
            EventCodes.BrowserAutoStop.BrowserReturnStarted, // BST30 end
            "itemId={ItemId} released={Released}",
            testItem.Id, released);

        return released;
    }

    /// <summary>
    ///     Converts TestItemDto to ResultRunSummaryDto for backward compatibility with legacy APIs.
    ///     TestItemDto be the new hierarchical model, ResultRunSummaryDto be the legacy flat model.
    /// </summary>
    private static ResultRunSummaryDto ConvertToRunSummary(TestItemDto testItem)
    {
        // Extract app/browser/env from attributes (a legacy model stored these as top-level fields)
        string? app = null, browser = null, env = null, region = null;
        if (testItem.Attributes != null)
        {
            foreach (var attr in testItem.Attributes)
            {
                var lower = attr.ToLowerInvariant();
                if (lower.StartsWith("app:"))
                {
                    app = attr[4..];
                }
                else if (lower.StartsWith("browser:"))
                {
                    browser = attr[8..];
                }
                else if (lower.StartsWith("env:"))
                {
                    env = attr[4..];
                }
                else if (lower.StartsWith("region:"))
                {
                    region = attr[7..];
                }
            }
        }

        return new ResultRunSummaryDto
        {
            RunId = testItem.Id.ToString(),
            RunName = testItem.Name, // Preserve test item name
            LaunchId = testItem.LaunchId,
            ParentItemId = testItem.ParentItemId,

            // ItemType - CRITICAL: Preserve the item type from TestItemDto
            ItemType = testItem.ItemType,

            // Browser session fields
            BrowserId = testItem.BrowserId,
            WebSocketEndpoint = testItem.WebSocketEndpoint,
            Browser = testItem.BrowserType ?? browser ?? string.Empty,
            WorkerNodeId = testItem.WorkerNodeId,

            // Status fields (separated in a new model)
            Status = testItem.Status ?? "Unknown",
            SessionStatus = testItem.SessionStatus,
            ComputedStatus = testItem.ComputedStatus,

            // Timing (convert DateTimeOffset to DateTime)
            StartedAtUtc = testItem.StartTime.UtcDateTime,
            CompletedAtUtc = testItem.FinishTime?.UtcDateTime,

            // Legacy attribute fields (non-nullable, provide empty string fallback)
            App = app ?? string.Empty,
            Env = env ?? string.Empty,
            Region = region,
            Attributes = testItem.Attributes ?? [],

            // Test aggregations (note: ResultRunSummaryDto uses different property names)
            TotalTests = testItem.TotalTests,
            Passed = testItem.PassedTests,
            Failed = testItem.FailedTests,
            Skipped = testItem.SkippedTests,
            TimedOut = testItem.TimedoutTests
        };
    }
}
