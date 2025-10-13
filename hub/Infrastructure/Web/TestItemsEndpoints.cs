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

using System.Text.Json.Serialization;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Services;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     REST API endpoints for test item hierarchy navigation.
///     Supports querying individual items, loading recursive trees, and filtering by item type.
///     Also handles test item creation with browser borrowing and finishing with browser return.
/// </summary>
public static class TestItemsEndpoints
{
    public static void MapTestItemsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/test-items")
            .WithTags("Test Items (Hierarchy)")
            .WithOpenApi();

        // POST /api/test-items
        group.MapPost("", StartTestItem)
            .WithName("StartTestItem")
            .WithSummary("Create a new test item and borrow browser if needed")
            .Produces<TestItemCreatedResponse>(201)
            .ProducesValidationProblem()
            .Produces(503);

        // PUT /api/test-items/{id}/finish
        group.MapPut("/{id:guid}/finish", FinishTestItem)
            .WithName("FinishTestItem")
            .WithSummary("Finish a test item and return browser to pool")
            .Produces<MessageResponse>()
            .Produces(404);

        // GET /api/test-items/{id}
        group.MapGet("/{id:guid}", GetTestItem)
            .WithName("GetTestItem")
            .WithSummary("Get a single test item by ID (without children)")
            .Produces<TestItemDto>()
            .Produces(404);

        // GET /api/test-items/{id}/children
        group.MapGet("/{id:guid}/children", GetChildItems)
            .WithName("GetChildItems")
            .WithSummary("Get direct children of a test item (one level only)")
            .Produces<List<TestItemDto>>()
            .Produces(404);

        // GET /api/test-items/{id}/tree
        group.MapGet("/{id:guid}/tree", GetTestItemTree)
            .WithName("GetTestItemTree")
            .WithSummary("Get test item with full recursive child hierarchy")
            .Produces<TestItemDto>()
            .Produces(404);

        // GET /api/test-items/{id}/history
        group.MapGet("/{id:guid}/history", GetTestItemHistory)
            .WithName("GetTestItemHistory")
            .WithSummary("Get test execution history across multiple launches")
            .Produces<List<TestHistoryItemDto>>()
            .Produces(404);

        // GET /api/test-items/{id}/logs
        group.MapGet("/{id:guid}/logs", GetTestItemLogs)
            .WithName("GetTestItemLogs")
            .WithSummary("Get log items for a test item")
            .Produces<List<LogItemDto>>()
            .Produces(404);

        // GET /api/test-items/{id}/logs-with-steps
        group.MapGet("/{id:guid}/logs-with-steps", GetTestItemLogsWithSteps)
            .WithName("GetTestItemLogsWithSteps")
            .WithSummary("Get hierarchical log items with step headers for a test item")
            .Produces<List<HierarchicalLogEntryDto>>()
            .Produces(404);

        // GET /api/test-items/by-number/{launchId}/{number} - numeric URL resolution
        group.MapGet("/by-number/{launchId:guid}/{number:long}", GetTestItemByNumber)
            .WithName("GetTestItemByNumber")
            .WithSummary("Resolve test item db_id to UUID (hierarchical URLs)")
            .Produces<TestItemDto>()
            .Produces(404);

        // PUT /api/test-items/{id} - Update test item metadata
        group.MapPut("/{id:guid}", UpdateTestItem)
            .WithName("UpdateTestItem")
            .WithSummary("Update test item name, description, and attributes")
            .Produces<MessageResponse>()
            .Produces(404);

        // PATCH /api/test-items/{id}/status - Update test item status only
        group.MapPatch("/{id:guid}/status", UpdateTestItemStatus)
            .WithName("UpdateTestItemStatus")
            .WithSummary("Update test item computed status")
            .Produces<MessageResponse>()
            .Produces(404);
    }

    /// <summary>
    ///     Retrieves a single test item without loading its children.
    ///     Useful for checking item existence and basic metadata.
    ///     Triggers background artifact prefetching to warm the cache.
    /// </summary>
    private static async Task<IResult> GetTestItem(
        [FromRoute] Guid id,
        [FromServices] IResultsStore store,
        [FromServices] IArtifactPrefetchService prefetchService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetTestItem");

        var item = await store.GetTestItemAsync(id);

        if (item == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "testItemId={TestItemId}", id);

            return ProblemDetailsHelpers.NotFound(
                $"Test item {id} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Trigger fire-and-forget artifact prefetch (non-blocking, best-effort)
        _ = Task.Run(async () =>
        {
            try
            {
                await prefetchService.PrefetchArtifactsAsync(id);
            }
            catch
            {
                // Errors already logged in prefetch service - don't propagate to endpoint
            }
        });

        return Results.Ok(item);
    }

    /// <summary>
    ///     Retrieves direct children of a test item (one level only).
    ///     Example: Get all steps belonging to a test, or all tests belonging to a scenario.
    ///     Supports optional filtering by item type.
    ///     Triggers batch artifact prefetching for parent and all children.
    /// </summary>
    private static async Task<IResult> GetChildItems(
        [FromRoute] Guid id,
        [FromQuery] string? itemType,
        [FromServices] IResultsStore store,
        [FromServices] IArtifactPrefetchService prefetchService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetChildItems");

        // First verify parent exists
        var parent = await store.GetTestItemAsync(id);
        if (parent == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "parentId={ParentId}", id);

            return ProblemDetailsHelpers.NotFound(
                $"Parent test item {id} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var children = await store.GetChildItemsAsync(id, itemType);

        // Trigger batch artifact prefetch for parent + children (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                var itemIds = new List<Guid> { id }; // Include parent
                itemIds.AddRange(children.Select(c => c.Id));
                await prefetchService.PrefetchArtifactsForItemsAsync(itemIds);
            }
            catch
            {
                // Errors already logged in prefetch service
            }
        });

        return Results.Ok(new
        {
            parentId = id,
            parentName = parent.Name,
            parentItemType = parent.ItemType,
            childCount = children.Count,
            children
        });
    }

    /// <summary>
    ///     Retrieves a test item with its entire child hierarchy loaded recursively.
    ///     Supports configurable max depth to prevent infinite recursion.
    ///     Example: Load a Scenario with all its Steps, or a Suite with all Tests and their Steps.
    ///     Triggers batch artifact prefetching for all items in the tree.
    /// </summary>
    private static async Task<IResult> GetTestItemTree(
        [FromRoute] Guid id,
        [FromQuery] int? maxDepth,
        [FromServices] IResultsStore store,
        [FromServices] IArtifactPrefetchService prefetchService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetTestItemTree");

        // Validate and constrain max depth
        var depth = maxDepth ?? 5; // Default to 5 levels
        if (depth < 1)
        {
            depth = 1;
        }

        if (depth > 10)
        {
            depth = 10; // Cap at 10 to prevent performance issues
        }

        var tree = await store.GetTestItemWithChildrenAsync(id, depth);

        if (tree == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "testItemId={TestItemId}", id);

            return ProblemDetailsHelpers.NotFound(
                $"Test item {id} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Calculate tree statistics
        var stats = CalculateTreeStatistics(tree);

        // Trigger batch artifact prefetch for all items in tree (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                var itemIds = CollectItemIds(tree);
                await prefetchService.PrefetchArtifactsForItemsAsync(itemIds);
            }
            catch
            {
                // Errors already logged in prefetch service
            }
        });

        return Results.Ok(new { item = tree, statistics = stats });
    }

    /// <summary>
    ///     Calculates statistics for a test item tree.
    /// </summary>
    private static object CalculateTreeStatistics(TestItemDto root)
    {
        var totalItems = 0;
        var itemsByType = new Dictionary<string, int>();
        var maxDepth = 0;

        void CountItems(TestItemDto item, int depth)
        {
            totalItems++;
            maxDepth = Math.Max(maxDepth, depth);

            itemsByType.TryAdd(item.ItemType, 0);
            itemsByType[item.ItemType]++;

            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    CountItems(child, depth + 1);
                }
            }
        }

        CountItems(root, 0);

        return new { totalItems, maxDepthReached = maxDepth, itemsByType };
    }

    /// <summary>
    ///     Collects all test item IDs from a tree (recursive traversal).
    ///     Used for batch artifact prefetching when loading hierarchical test items.
    /// </summary>
    internal static List<Guid> CollectItemIds(TestItemDto root)
    {
        var itemIds = new List<Guid> { root.Id };

        void CollectFromChildren(TestItemDto item)
        {
            if (item.Children == null)
            {
                return;
            }

            foreach (var child in item.Children)
            {
                itemIds.Add(child.Id);
                CollectFromChildren(child);
            }
        }

        CollectFromChildren(root);
        return itemIds;
    }

    /// <summary>
    ///     Creates a new test item and borrows a browser if the item type requires it.
    ///     Item types that borrow browsers: Test, Scenario, Story.
    ///     Item types that don't borrow: Step, BeforeTest, AfterTest, etc. (use parent's browser).
    /// </summary>
    private static async Task<IResult> StartTestItem(
        [FromBody] StartTestItemRequest? request,
        [FromServices] IResultsStore store,
        [FromServices] IBrowserPoolService browserPool,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILogger<IResultsStore> logger,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] IConfiguration config,
        [FromServices] IConnectionMultiplexer redis,
        [FromServices] ChunkedLogger chunkedLogger,
        HttpContext httpContext)
    {
        // Begin operation with inputs for tracking
        var inputs = new Dictionary<string, object>
        {
            ["launchUuid"] = request?.LaunchUuid ?? "",
            ["name"] = request?.Name ?? "",
            ["type"] = request?.Type.ToString() ?? "",
            ["labelKey"] = request?.LabelKey ?? "",
            ["parentItemId"] = request?.ParentItemId ?? ""
        };

        // Link to parent HTTP operation if one exists
        var parentOpId = OperationContext.Current?.OperationId;
        using var operation = (dynamic)chunkedLogger.BeginOperation("StartTestItem", inputs, parentOperationId: parentOpId);

        try
        {
            // Check if model binding succeeded
            if (request == null)
            {
                chunkedLogger.LogError(null, null, "StartTestItem failed: request body is null or could not be deserialized");
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["Invalid request body. Please check that the JSON is valid and all required fields are provided."]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            chunkedLogger.LogDebug(null,
                "StartTestItem called with LaunchUuid={LaunchUuid}, Name={Name}, Type={Type}, LabelKey={LabelKey}, ParentItemId={ParentItemId}, AttributesCount={AttributesCount}",
                request.LaunchUuid, request.Name, request.Type, request.LabelKey, request.ParentItemId,
                request.Attributes?.Count ?? 0);

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.LaunchUuid))
            {
                chunkedLogger.LogWarning(null, "StartTestItem validation failed: LaunchUuid is required");
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["LaunchUuid"] = ["LaunchUuid is required"]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                chunkedLogger.LogWarning(null, "StartTestItem validation failed: Name is required");
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["Name"] = ["Name is required"]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Type is enum, so it's always valid (cannot be null or empty)

            // Validate launch exists and is not in the terminal state
            var launchId = Guid.Parse(request.LaunchUuid);
            var (isTerminal, launchStatus) = await IsLaunchInTerminalStateAsync(launchId, db, redis);

            if (launchStatus == null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Launch {launchId} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            if (isTerminal)
            {
                return ProblemDetailsHelpers.Conflict(
                    $"Cannot create test item: Launch {launchId} is already in terminal state '{launchStatus}'",
                    eventCode: EventCodes.Launch.LaunchAlreadyFinished,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Log milestone: Request validated successfully
            chunkedLogger.LogMilestone("ITEM01", "Test item validated");

            var itemId = Guid.NewGuid();
            var startTime = request.StartTime;

            // Compute canonical test case ID and hash for history tracking
            var testCaseId = await TestItemIdentityHelper.ComputeCanonicalIdAsync(
                db,
                launchId,
                request.ParentItemId,
                request.TestCaseId,
                request.CodeReference,
                request.Name);
            var testCaseHash = TestItemIdentityHelper.ComputeHash(testCaseId);

            // Determine if this item type needs a browser session
            // Only Test and Scenario types borrow browsers (actual test execution)
            // All other types (Suite, Story, Step, hooks) do NOT borrow browsers
            var needsBrowser = request.Type is TestItemType.Test or TestItemType.Scenario;

            string? browserId = null;
            string? webSocketEndpoint = null;
            string? browserType = null;
            string? workerNodeId = null;
            string? playwrightVersion = null;
            string? browserVersion = null;
            string? regionOs = null;
            var sessionStatus = "Queued";

            // Borrow browser if needed
            if (needsBrowser && !string.IsNullOrWhiteSpace(request.LabelKey))
            {
                // Log milestone: Browser borrow requested
                chunkedLogger.LogMilestone("POOL01", "Browser borrow requested");

                chunkedLogger.LogMilestone("POOL02",
                    "Borrowing browser for test item {ItemId} with labelKey {LabelKey}",
                    itemId, request.LabelKey);

                var borrowResult = await browserPool.TryBorrowBrowserAsync(
                    request.LabelKey,
                    itemId.ToString(),
                    request.Name,
                    TimeSpan.FromSeconds(120),
                    CancellationToken.None);

                if (!borrowResult.Success)
                {
                    chunkedLogger.LogWarning(null, "Failed to borrow browser for test item {ItemId}: {Error}",
                        itemId, borrowResult.ErrorMessage);

                    // If maintenance mode, do NOT create an item in the database (fail fast)
                    if (borrowResult.IsMaintenance)
                    {
                        return ProblemDetailsHelpers.ServiceUnavailable(
                            "BrowserPool",
                            borrowResult.ErrorMessage ?? "Pool is under maintenance",
                            eventCode: EventCodes.BrowserPool.BorrowFailed,
                            instance: httpContext.Request.Path,
                            traceId: httpContext.TraceIdentifier);
                    }

                    // Create the item in Aborted state for other borrow failures (e.g. no capacity)
                    sessionStatus = "Aborted";
                    var abortedItem = new ResultRunSummaryDto
                    {
                        RunId = itemId.ToString(),
                        LaunchId = Guid.Parse(request.LaunchUuid),
                        ParentItemId = request.ParentItemId != null ? Guid.Parse(request.ParentItemId) : null,
                        ItemType = request.Type.ToString(),
                        RunName = request.Name,
                        Description = request.Description,
                        Attributes = ConvertItemAttributesToStringArray(request.Attributes),
                        TestCaseId = testCaseId,
                        TestCaseHash = testCaseHash,
                        SessionStatus = sessionStatus,
                        ComputedStatus = "Errored",
                        StartedAtUtc = startTime
                    };
                    await PublishOrWriteTestItemAsync(eventPublisher, store, abortedItem, config, logger);

                    return ProblemDetailsHelpers.ServiceUnavailable(
                        "BrowserPool",
                        borrowResult.ErrorMessage ?? "Failed to borrow browser",
                        eventCode: EventCodes.BrowserPool.BorrowFailed,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                browserId = borrowResult.BrowserId;
                webSocketEndpoint = borrowResult.WebSocketEndpoint;
                browserType = borrowResult.BrowserType;
                workerNodeId = borrowResult.WorkerNodeId;
                playwrightVersion = borrowResult.PlaywrightVersion;
                browserVersion = borrowResult.BrowserVersion;
                regionOs = borrowResult.RegionOs;
                sessionStatus = "Running";

                // Log milestone: Browser acquired successfully with detailed info
                chunkedLogger.LogMilestone("POOL03",
                    "Browser acquired: {BrowserId} (version: {BrowserVersion}, Playwright: {PlaywrightVersion})",
                    browserId, browserVersion, playwrightVersion);
            }

            // Create the test item in the database
            var testItem = new ResultRunSummaryDto
            {
                RunId = itemId.ToString(),
                LaunchId = Guid.Parse(request.LaunchUuid),
                ParentItemId = request.ParentItemId != null ? Guid.Parse(request.ParentItemId) : null,
                ItemType = request.Type.ToString(),
                RunName = request.Name,
                Description = request.Description,
                Attributes = ConvertItemAttributesToStringArray(request.Attributes),
                CodeRef = request.CodeReference,
                TestCaseId = testCaseId,
                TestCaseHash = testCaseHash,
                SessionStatus = sessionStatus,
                ComputedStatus = needsBrowser ? "InProgress" : null,
                BrowserId = browserId,
                WebSocketEndpoint = webSocketEndpoint,
                BrowserType = browserType,
                WorkerNodeId = workerNodeId,
                PlaywrightVersion = playwrightVersion,
                BrowserVersion = browserVersion,
                RegionOs = regionOs,
                StartedAtUtc = startTime
            };

            chunkedLogger.LogMilestone("ITEM01",
                "Persisting test item: PlaywrightVersion={PwVer}, BrowserVersion={BrVer}, RegionOs={Region}",
                playwrightVersion, browserVersion, regionOs);

            await PublishOrWriteTestItemAsync(eventPublisher, store, testItem, config, logger);

            // Log milestone: Test item persisted to database
            chunkedLogger.LogMilestone("ITEM02", "Test item persisted");

            // Fire-and-forget: Update launch activity (redundant with trigger, but provides fallback)
            _ = Task.Run(async () =>
            {
                try
                {
                    await store.UpdateLaunchActivityAsync(launchId);
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogWarning(ex, null, "Failed to update launch activity for launch {LaunchId}", launchId);
                }
            });

            // Set operation outputs before successful return
            operation.SetOutputs(new Dictionary<string, object>
            {
                ["itemId"] = itemId.ToString(),
                ["browserId"] = browserId ?? "none",
                ["workerNode"] = workerNodeId ?? "none",
                ["sessionStatus"] = sessionStatus
            });

            return Results.Created(
                $"/api/test-items/{itemId}",
                new TestItemCreatedResponse
                {
                    Uuid = itemId.ToString(),
                    SessionStatus = sessionStatus,
                    BrowserId = browserId ?? string.Empty,
                    WebSocketEndpoint = webSocketEndpoint ?? string.Empty,
                    BrowserType = browserType ?? string.Empty,
                    WorkerNodeId = workerNodeId ?? string.Empty,
                    PlaywrightVersion = playwrightVersion ?? string.Empty,
                    BrowserVersion = browserVersion ?? string.Empty,
                    CodeRef = request.CodeReference,
                    TestCaseId = testCaseId
                });
        }
        catch (NpgsqlException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context!, ex, ErrorType.DependencyFailure, DependencyName.Database);
            throw;
        }
        catch (RedisException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context!, ex, ErrorType.DependencyFailure, DependencyName.Redis);
            throw;
        }
        catch (TimeoutException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context!, ex, ErrorType.Timeout, DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context!, ex, ErrorType.Unexpected);
            throw;
        }
    }

    /// <summary>
    ///     Finishes a test item and returns the browser to the pool if one was borrowed.
    ///     Updates the test item's status, end time, and returns the browser if applicable.
    /// </summary>
    private static async Task<IResult> FinishTestItem(
        [FromRoute] Guid id,
        [FromBody] FinishTestItemRequest request,
        [FromServices] IResultsStore store,
        [FromServices] IBrowserPoolService browserPool,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILogger<IResultsStore> logger,
        [FromServices] IConnectionMultiplexer redis,
        [FromServices] ChunkedLogger chunkedLogger,
        HttpContext httpContext)
    {
        var inputs = new Dictionary<string, object>
        {
            ["itemId"] = id.ToString(),
            ["computedStatus"] = request.Status.ToString()
        };

        // Link to parent HTTP operation if one exists
        var parentOpId = OperationContext.Current?.OperationId;
        using var operation = (dynamic)chunkedLogger.BeginOperation("FinishTestItem", inputs, parentOperationId: parentOpId);

        try
        {
            // Get the existing test item
            var existingRun = await store.GetRunAsync(id.ToString());
            if (existingRun == null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Test item {id} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Validate launch is not in the terminal state
            if (existingRun.LaunchId.HasValue)
            {
                var (isTerminal, launchStatus) = await IsLaunchInTerminalStateAsync(
                    existingRun.LaunchId.Value, db, redis);

                if (launchStatus == null)
                {
                    return ProblemDetailsHelpers.NotFound(
                        $"Launch {existingRun.LaunchId.Value} not found",
                        eventCode: EventCodes.Launch.LaunchNotFound,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                if (isTerminal)
                {
                    return ProblemDetailsHelpers.Conflict(
                        $"Cannot finish test item: Launch {existingRun.LaunchId.Value} is already in terminal state '{launchStatus}'",
                        eventCode: EventCodes.Launch.LaunchAlreadyFinished,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }
            }

            var endTime = request.EndTime;
            var computedStatus = request.Status.ToString();

            var browserReturned = false;
            // If this item had a browser, return it to the pool
            if (!string.IsNullOrWhiteSpace(existingRun.BrowserId) &&
                !string.IsNullOrWhiteSpace(existingRun.WorkerNodeId))
            {
                chunkedLogger.LogMilestone(EventCodes.BrowserPool.ReturnRequested,
                    "Returning browser {BrowserId} from test item {ItemId} with status {Status}",
                    existingRun.BrowserId, id, computedStatus);

                try
                {
                    await browserPool.ReturnBrowserAsync(
                        existingRun.BrowserId,
                        existingRun.WorkerNodeId,
                        computedStatus);

                    chunkedLogger.LogMilestone(EventCodes.BrowserPool.BrowserReturned,
                        "Successfully returned browser {BrowserId} for test item {ItemId}",
                        existingRun.BrowserId, id);

                    browserReturned = true;
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.BrowserPool.ReturnFailed,
                        ex,
                        "Failed to return browser {BrowserId} for test item {ItemId}",
                        existingRun.BrowserId, id);
                    // Continue with updating the item even if browser return fails
                }
            }

            // Update the test item with finish details
            await store.UpsertRunAsync(new ResultRunSummaryDto
            {
                RunId = id.ToString(),
                LaunchId = existingRun.LaunchId,
                ParentItemId = existingRun.ParentItemId,
                ItemType = existingRun.ItemType,
                RunName = existingRun.RunName,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? existingRun.Description
                    : request.Description,
                Attributes = existingRun.Attributes,
                CodeRef = existingRun.CodeRef,
                TestCaseId = existingRun.TestCaseId,
                TestCaseHash = existingRun.TestCaseHash,
                SessionStatus = "Completed",
                ComputedStatus = computedStatus,
                BrowserId = existingRun.BrowserId,
                WebSocketEndpoint = existingRun.WebSocketEndpoint,
                BrowserType = existingRun.BrowserType,
                WorkerNodeId = existingRun.WorkerNodeId,
                PlaywrightVersion = existingRun.PlaywrightVersion,
                BrowserVersion = existingRun.BrowserVersion,
                RegionOs = existingRun.RegionOs,
                StartedAtUtc = existingRun.StartedAtUtc,
                CompletedAtUtc = endTime
            });

            chunkedLogger.LogMilestone(EventCodes.TestItem.ItemFinished,
                "Test item finished and persisted with status {Status}",
                computedStatus);

            // Fire-and-forget: Update launch activity (redundant with trigger, but provides fallback)
            if (existingRun.LaunchId.HasValue)
            {
                var launchId = existingRun.LaunchId.Value;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await store.UpdateLaunchActivityAsync(launchId);
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogWarning(ex, null, "Failed to update launch activity for launch {LaunchId}", launchId);
                    }
                });
            }

            operation.SetOutputs(new Dictionary<string, object>
            {
                ["itemId"] = id.ToString(),
                ["sessionStatus"] = "Completed",
                ["computedStatus"] = computedStatus,
                ["browserReturned"] = browserReturned,
                ["launchId"] = existingRun.LaunchId?.ToString() ?? "none"
            });

            return Results.Ok(new MessageResponse
            {
                Message = $"Test item {id} finished successfully with status {computedStatus}"
            });
        }
        catch (NpgsqlException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Database);
            throw;
        }
        catch (RedisException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Redis);
            throw;
        }
        catch (TimeoutException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.Unexpected);
            throw;
        }
    }

    // Request/Response DTOs for test item operations
    /// <summary>
    ///     Resolves test item db_id to UUID (ReportPortal-style numeric URLs).
    ///     Returns test item details that can be used for further navigation.
    /// </summary>
    private static async Task<IResult> GetTestItemByNumber(
        [FromRoute] Guid launchId,
        [FromRoute] long number,
        [FromServices] IResultsStore store,
        [FromServices] NpgsqlDataSource db,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] IApiKeyAuthenticationService authService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetTestItemByNumber");

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["X-Project-Key"] = ["X-Project-Key header is required"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Authorize API key
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult;
        }

        // Query test_items by launch_id and db_id
        const string query = @"
            SELECT * FROM test_items
            WHERE launch_id = $1 AND db_id = $2
            LIMIT 1";

        await using var cmd = db.CreateCommand(query);
        cmd.Parameters.AddWithValue(launchId);
        cmd.Parameters.AddWithValue(number);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "launchId={LaunchId} number={Number}", launchId, number);

            return ProblemDetailsHelpers.NotFound(
                $"Test item with db_id {number} not found in launch {launchId}",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Map the test item from the reader (reuse existing mapping logic)
        var testItem = await store.GetTestItemsForLaunchAsync(launchId);
        var item = testItem.FirstOrDefault(ti => ti.DbId == number);

        if (item == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "launchId={LaunchId} number={Number}", launchId, number);

            return ProblemDetailsHelpers.NotFound(
                $"Test item with db_id {number} not found in launch {launchId}",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        return Results.Ok(item);
    }

    /// <summary>
    ///     Updates test item metadata (name, description, attributes).
    ///     Does not affect browser session or status fields.
    /// </summary>
    private static async Task<IResult> UpdateTestItem(
        [FromRoute] Guid id,
        [FromBody] UpdateTestItemRequest request,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] ChunkedLogger chunkedLogger,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var inputs = new Dictionary<string, object>
        {
            ["itemId"] = id.ToString(),
            ["hasName"] = !string.IsNullOrWhiteSpace(request.Name),
            ["hasDescription"] = request.Description != null,
            ["attributeCount"] = request.Attributes?.Count ?? 0
        };

        // Link to parent HTTP operation if one exists
        var parentOpId = OperationContext.Current?.OperationId;
        using var operation = (dynamic)chunkedLogger.BeginOperation("UpdateTestItem", inputs, parentOperationId: parentOpId);

        try
        {
            // Get the existing test item
            var existingRun = await store.GetRunAsync(id.ToString());
            if (existingRun == null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Test item {id} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Track what fields are being updated
            var nameUpdated = !string.IsNullOrWhiteSpace(request.Name) && request.Name != existingRun.RunName;
            var descriptionUpdated = request.Description != null && request.Description != existingRun.Description;
            var attributesUpdated = request.Attributes != null;
            var codeRefUpdated = request.CodeReference != null && request.CodeReference != existingRun.CodeRef;

            // Update only the editable fields
            await store.UpsertRunAsync(new ResultRunSummaryDto
            {
                RunId = existingRun.RunId,
                LaunchId = existingRun.LaunchId,
                ParentItemId = existingRun.ParentItemId,
                ItemType = existingRun.ItemType,
                RunName = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : existingRun.RunName,
                Description = request.Description ?? existingRun.Description,
                Attributes = request.Attributes?.Select(a => a.Key + (string.IsNullOrEmpty(a.Value) ? "" : ":" + a.Value)).ToArray() ?? existingRun.Attributes,
                CodeRef = request.CodeReference ?? existingRun.CodeRef,
                TestCaseId = existingRun.TestCaseId,
                TestCaseHash = existingRun.TestCaseHash,
                // Preserve existing session/browser fields
                SessionStatus = existingRun.SessionStatus,
                ComputedStatus = existingRun.ComputedStatus,
                Status = existingRun.Status,
                BrowserId = existingRun.BrowserId,
                WebSocketEndpoint = existingRun.WebSocketEndpoint,
                BrowserType = existingRun.BrowserType,
                WorkerNodeId = existingRun.WorkerNodeId,
                PlaywrightVersion = existingRun.PlaywrightVersion,
                BrowserVersion = existingRun.BrowserVersion,
                RegionOs = existingRun.RegionOs,
                StartedAtUtc = existingRun.StartedAtUtc,
                CompletedAtUtc = existingRun.CompletedAtUtc,
                // Preserve test result fields
                App = existingRun.App,
                Browser = existingRun.Browser,
                Env = existingRun.Env,
                Region = existingRun.Region,
                OS = existingRun.OS,
                TotalTests = existingRun.TotalTests,
                Passed = existingRun.Passed,
                Failed = existingRun.Failed,
                Skipped = existingRun.Skipped,
                TimedOut = existingRun.TimedOut
            });

            chunkedLogger.LogMilestone(EventCodes.TestItem.StatusUpdated,
                "Test item metadata updated",
                id);

            chunkedLogger.LogInformation(null, "Updated test item {ItemId} metadata", id);

            operation.SetOutputs(new Dictionary<string, object>
            {
                ["itemId"] = id.ToString(),
                ["nameUpdated"] = nameUpdated,
                ["descriptionUpdated"] = descriptionUpdated,
                ["attributesUpdated"] = attributesUpdated,
                ["codeRefUpdated"] = codeRefUpdated
            });

            return Results.Ok(new MessageResponse { Message = $"Test item {id} updated successfully" });
        }
        catch (NpgsqlException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Database);
            throw;
        }
        catch (RedisException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Redis);
            throw;
        }
        catch (TimeoutException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.Unexpected);
            throw;
        }
    }

    /// <summary>
    ///     Updates test item computed status only.
    ///     This is used by the dashboard to manually update the test status from the UI.
    /// </summary>
    private static async Task<IResult> UpdateTestItemStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateTestItemStatusRequest request,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] ChunkedLogger chunkedLogger,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var inputs = new Dictionary<string, object>
        {
            ["itemId"] = id.ToString(),
            ["newComputedStatus"] = request.ComputedStatus ?? "null"
        };

        // Link to parent HTTP operation if one exists
        var parentOpId = OperationContext.Current?.OperationId;
        using var operation = (dynamic)chunkedLogger.BeginOperation("UpdateTestItemStatus", inputs, parentOperationId: parentOpId);

        try
        {
            // Validate status
            if (string.IsNullOrWhiteSpace(request.ComputedStatus))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["computedStatus"] = ["ComputedStatus is required"]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Get the existing test item
            var existingRun = await store.GetRunAsync(id.ToString());
            if (existingRun == null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Test item {id} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Update only the computed status (allow updates even for completed sessions)
            await store.UpsertRunAsync(new ResultRunSummaryDto
            {
                RunId = existingRun.RunId,
                LaunchId = existingRun.LaunchId,
                ParentItemId = existingRun.ParentItemId,
                ItemType = existingRun.ItemType,
                RunName = existingRun.RunName,
                Description = existingRun.Description,
                Attributes = existingRun.Attributes,
                CodeRef = existingRun.CodeRef,
                TestCaseId = existingRun.TestCaseId,
                TestCaseHash = existingRun.TestCaseHash,
                // Update computed status
                ComputedStatus = request.ComputedStatus,
                // Preserve existing session/browser fields
                SessionStatus = existingRun.SessionStatus,
                Status = existingRun.Status,
                BrowserId = existingRun.BrowserId,
                WebSocketEndpoint = existingRun.WebSocketEndpoint,
                BrowserType = existingRun.BrowserType,
                WorkerNodeId = existingRun.WorkerNodeId,
                PlaywrightVersion = existingRun.PlaywrightVersion,
                BrowserVersion = existingRun.BrowserVersion,
                RegionOs = existingRun.RegionOs,
                StartedAtUtc = existingRun.StartedAtUtc,
                CompletedAtUtc = existingRun.CompletedAtUtc,
                // Preserve test result fields
                App = existingRun.App,
                Browser = existingRun.Browser,
                Env = existingRun.Env,
                Region = existingRun.Region,
                OS = existingRun.OS,
                TotalTests = existingRun.TotalTests,
                Passed = existingRun.Passed,
                Failed = existingRun.Failed,
                Skipped = existingRun.Skipped,
                TimedOut = existingRun.TimedOut
            });

            chunkedLogger.LogMilestone(EventCodes.TestItem.StatusUpdated,
                "Test item status updated",
                id, request.ComputedStatus);

            chunkedLogger.LogInformation(null, "Updated test item {ItemId} computed status to {Status}", id, request.ComputedStatus);

            var aggregationsRecalculated = false;
            // Refresh aggregations for suite and launch to reflect the status change
            if (existingRun.LaunchId.HasValue)
            {
                try
                {
                    await store.RecalculateLaunchAggregationsAsync(existingRun.LaunchId.Value);

                    chunkedLogger.LogMilestone(EventCodes.Launch.AggregationsUpdated,
                        "Launch aggregations recalculated",
                        existingRun.LaunchId.Value);

                    chunkedLogger.LogInformation(null, "Recalculated aggregations for launch {LaunchId} after status update",
                        existingRun.LaunchId);

                    aggregationsRecalculated = true;
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogWarning(ex, null, "Failed to recalculate aggregations for launch {LaunchId}",
                        existingRun.LaunchId);
                    // Don't fail the request if aggregation refresh fails
                }
            }

            operation.SetOutputs(new Dictionary<string, object>
            {
                ["itemId"] = id.ToString(),
                ["newStatus"] = request.ComputedStatus,
                ["aggregationsRecalculated"] = aggregationsRecalculated,
                ["launchId"] = existingRun.LaunchId?.ToString() ?? "none"
            });

            return Results.Ok(
                new MessageResponse { Message = $"Test item {id} status updated to {request.ComputedStatus}" });
        }
        catch (NpgsqlException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Database);
            throw;
        }
        catch (RedisException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.DependencyFailure, DependencyName.Redis);
            throw;
        }
        catch (TimeoutException ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            var context = OperationContext.Current;
            chunkedLogger.FailOperation(context, ex, ErrorType.Unexpected);
            throw;
        }
    }

    /// <summary>
    ///     Publishes a test item event or falls back to direct DB write.
    /// </summary>
    /// <param name="eventPublisher">The event publisher for RabbitMQ events</param>
    /// <param name="store">The results store for database writes</param>
    /// <param name="item">The test item to publish/write</param>
    /// <param name="config">Configuration for event publishing settings</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    private static async Task PublishOrWriteTestItemAsync(
        IEventPublisher eventPublisher,
        IResultsStore store,
        ResultRunSummaryDto item,
        IConfiguration config,
        ILogger logger)
    {
        // ALWAYS write to a database immediately to ensure data is available for subsequent operations
        await store.UpsertRunAsync(item);

        // Test items: Hub writes directly to DB (no events published)
        // This avoids duplicate writes and maintains immediate consistency for test item operations
    }

    private static async Task<(bool isTerminal, string? status)> IsLaunchInTerminalStateAsync(
        Guid launchId,
        NpgsqlDataSource db,
        IConnectionMultiplexer? redis = null)
    {
        // Try cache first if Redis available (5-second TTL)
        if (redis != null)
        {
            try
            {
                var cache = redis.GetDatabase();
                var cacheKey = $"launch:status:{launchId}";
                var cachedStatus = await cache.StringGetAsync(cacheKey);

                if (!cachedStatus.IsNullOrEmpty)
                {
                    var status = cachedStatus.ToString();
                    var terminal = new[] { "Finished", "Stopped", "Failed" };

                    // We only trust the cache for terminal states.
                    // If it says "InProgress", it might have been changed in the DB but not yet invalidated in Redis.
                    if (terminal.Contains(status))
                    {
                        return (true, status);
                    }
                }
            }
            catch
            {
                // Cache failure - fall through to a database
            }
        }

        // Database query
        const string query = "SELECT status FROM launches WHERE id = $1";
        await using var cmd = db.CreateCommand(query);
        cmd.Parameters.AddWithValue(launchId);

        var dbStatus = await cmd.ExecuteScalarAsync() as string;

        if (dbStatus == null)
        {
            return (false, null); // Launch doesn't exist
        }

        // Cache result for 5 seconds if Redis available
        if (redis != null)
        {
            try
            {
                var cache = redis.GetDatabase();
                var cacheKey = $"launch:status:{launchId}";
                await cache.StringSetAsync(cacheKey, dbStatus, TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Cache write failure - non-critical
            }
        }

        var terminalStates = new[] { "Finished", "Stopped", "Failed" };
        var isTerminal = terminalStates.Contains(dbStatus);
        return (isTerminal, dbStatus);
    }

    /// <summary>
    ///     Converts ItemAttribute[] to string[] format used by ResultRunSummaryDto.
    ///     Format: "key:value" for attributes with both key and value, or just "value" for empty keys.
    /// </summary>
    private static string[]? ConvertItemAttributesToStringArray(IList<ItemAttribute>? attributes)
    {
        if (attributes == null || attributes.Count == 0)
        {
            return null;
        }

        return attributes
            .Select(attr =>
            {
                if (string.IsNullOrWhiteSpace(attr.Key))
                {
                    return attr.Value; // Just value for empty keys
                }

                if (string.IsNullOrWhiteSpace(attr.Value))
                {
                    return attr.Key; // Just key for empty values (e.g., tags)
                }

                return $"{attr.Key}:{attr.Value}"; // key:value format
            })
            .ToArray();
    }

    /// <summary>
    ///     Get test execution history across launches
    /// </summary>
    private static async Task<IResult> GetTestItemHistory(
        [FromRoute] Guid id,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        [FromQuery] int limit = 20)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetTestItemHistory");

        // Validate limit parameter
        if (limit <= 0)
        {
            limit = 20;
        }

        if (limit > 100)
        {
            limit = 100;
        }

        // Get the test item to find its unique identifier
        var testItem = await store.GetTestItemAsync(id);
        if (testItem == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "testItemId={TestItemId}", id);

            return ProblemDetailsHelpers.NotFound(
                $"Test item {id} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Determine the identifier to use for history lookup
        var identifier = testItem.TestCaseId ?? testItem.Name;

        chunkedLogger.LogDebug(null,
            "Loading test history for item {ItemId}: identifier='{Identifier}', type='{Type}', limit={Limit}",
            id, identifier, testItem.ItemType, limit);

        // Find test history using test case ID or name+type combination
        try
        {
            var history = await store.GetTestItemHistoryAsync(
                identifier,
                testItem.ItemType,
                limit
            );

            chunkedLogger.LogDebug(null,
                "Loaded {Count} history items for test item {ItemId}",
                history.Count, id);

            return Results.Ok(history);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogError(ex, null,
                "Failed to load test history for item {ItemId} (identifier='{Identifier}', type='{Type}')",
                id, identifier, testItem.ItemType);

            return ProblemDetailsHelpers.InternalServerError(
                $"Failed to load test history: {ex.Message}",
                eventCode: EventCodes.TestItem.TestItemOperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    /// <summary>
    ///     Get log items for a test item
    /// </summary>
    private static async Task<IResult> GetTestItemLogs(
        [FromRoute] Guid id,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetTestItemLogs");

        try
        {
            var logs = await store.GetLogItemsForTestItemAsync(id);
            chunkedLogger.LogDebug(null, "Loaded {Count} log items for test item {ItemId}", logs.Count, id);
            return Results.Ok(logs);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogError(ex, null, "Failed to load log items for test item {ItemId}", id);
            return ProblemDetailsHelpers.InternalServerError(
                $"Failed to load log items: {ex.Message}",
                eventCode: EventCodes.LogItem.LogItemRetrievalFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> GetTestItemLogsWithSteps(
        [FromRoute] Guid id,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 1000)
    {
        var logger = loggerFactory.CreateLogger("TestItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "TestItemsEndpoints.GetTestItemLogsWithSteps");

        try
        {
            var hierarchicalLogs = await store.GetLogItemsWithStepsAsync(id, skip, take);
            chunkedLogger.LogDebug(null, "Loaded {Count} hierarchical log entries for test item {ItemId}",
                hierarchicalLogs.Count, id);
            return Results.Ok(hierarchicalLogs);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogWarning(ex, null, "Failed to load hierarchical logs for {ItemId}, falling back to flat logs", id);

            // Fallback: return flat logs without step headers
            try
            {
                var flatLogs = await store.GetLogItemsForTestItemAsync(id);
                var hierarchical = flatLogs.Select(l => new HierarchicalLogEntryDto
                {
                    Id = l.Id,
                    ParentId = null,
                    IsStepHeader = false,
                    IsNested = false,
                    NestLevel = 0,
                    Timestamp = l.Time,
                    Level = l.Level,
                    Source = "test",
                    Message = l.Message,
                    Name = "",
                    Description = "",
                    Status = "InProgress",
                    DurationMs = null,
                    AttachmentCount = 0,
                    HasAttachment = l.AttachmentId.HasValue,
                    AttachmentType = "",
                    AttachmentName = ""
                }).ToList();

                chunkedLogger.LogDebug(null, "Fallback successful: returned {Count} flat logs", hierarchical.Count);
                return Results.Ok(hierarchical);
            }
            catch (Exception fallbackEx)
            {
                chunkedLogger.LogError(fallbackEx, null, "Fallback also failed for test item {ItemId}", id);
                return ProblemDetailsHelpers.InternalServerError(
                    $"Failed to load hierarchical log items: {fallbackEx.Message}",
                    eventCode: EventCodes.LogItem.LogItemRetrievalFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        }
    }

    // Hub-specific DTOs (not in the Client library)
    private record UpdateTestItemStatusRequest
    {
        [JsonPropertyName("computedStatus")]
        public string ComputedStatus { get; init; } = "";
    }

    private record MessageResponse
    {
        public string Message { get; init; } = "";
    }
}
