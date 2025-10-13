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
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
/// Provides endpoint mappings for operations related to test suites.
/// </summary>
/// <remarks>
/// A "suite" represents a test item with the ItemType set to 'Suite',
/// forming a logical grouping of test cases and related items.
/// </remarks>
public static class SuitesEndpoints
{
    /// <summary>
    /// Configures endpoints related to test suites for the given route group builder.
    /// This includes endpoints for retrieving suite details, associated runs, hierarchical test items,
    /// unique errors, child item execution history, and suite lookup by numeric identifiers.
    /// </summary>
    /// <param name="group">The route group builder to which suite-related endpoints should be mapped.</param>
    /// <returns>The route group builder with suite-related endpoints configured.</returns>
    public static RouteGroupBuilder MapSuitesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", GetSuiteById)
            .WithName("GetSuiteById")
            .WithTags("Suites")
            .WithSummary("Get a suite by ID (Suite is a test item with ItemType='Suite')")
            .Produces<TestItemDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{suiteId:guid}/runs", GetSuiteRuns)
            .WithName("GetSuiteRuns")
            .WithTags("Suites")
            .WithSummary("Get all test runs for a suite (test items with ParentItemId=suiteId)")
            .Produces<List<TestItemDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{suiteId:guid}/test-items", GetSuiteTestItems)
            .WithName("GetSuiteTestItems")
            .WithTags("Suites")
            .WithSummary("Get all test items for a suite (hierarchical tree)")
            .Produces<List<TestItemDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{suiteId:guid}/unique-errors", GetSuiteUniqueErrors)
            .WithName("GetSuiteUniqueErrors")
            .WithTags("Suites")
            .WithSummary("Get unique error patterns for a suite with grouped occurrences")
            .Produces<List<UniqueErrorDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{suiteDbId:long}/child-items-history", GetSuiteChildItemsHistory)
            .WithName("GetSuiteChildItemsHistory")
            .WithTags("Suites", "History")
            .WithSummary("Get test execution history for child items of a suite")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetSuiteById(
        [FromRoute] Guid id,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("SuitesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "SuitesEndpoints.GetSuiteById");

        var suite = await store.GetTestItemAsync(id);
        if (suite == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "suiteId={SuiteId}", id);

            return ProblemDetailsHelpers.NotFound(
                $"Suite {id} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Verify it's actually a suite
        if (suite.ItemType != "Suite")
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "error=NotASuite suiteId={SuiteId} itemType={ItemType}", id, suite.ItemType);

            return ProblemDetailsHelpers.NotFound(
                $"Test item {id} is not a suite",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        return Results.Ok(suite);
    }

    private static async Task<IResult> GetSuiteRuns(
        [FromRoute] Guid suiteId,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("SuitesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "SuitesEndpoints.GetSuiteRuns");

        // First, verify the suite exists
        var suite = await store.GetTestItemAsync(suiteId);
        if (suite is not { ItemType: "Suite" })
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "suiteId={SuiteId}", suiteId);

            return ProblemDetailsHelpers.NotFound(
                $"Suite {suiteId} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Get all test items (test runs) for this suite by parent_item_id
        var runs = await store.GetTestItemsForSuiteAsync(suiteId);
        return Results.Ok(runs);
    }

    private static async Task<IResult> GetSuiteTestItems(
        [FromRoute] Guid suiteId,
        [FromServices] IResultsStore store,
        [FromServices] IArtifactPrefetchService prefetchService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("SuitesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "SuitesEndpoints.GetSuiteTestItems");

        // First, verify the suite exists
        var suite = await store.GetTestItemAsync(suiteId);
        if (suite is not { ItemType: "Suite" })
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "suiteId={SuiteId}", suiteId);

            return ProblemDetailsHelpers.NotFound(
                $"Suite {suiteId} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Get the full tree of test items under this suite
        var tree = await store.GetTestItemWithChildrenAsync(suiteId);

        // Trigger batch artifact prefetch for all items in a tree (fire-and-forget)
        if (tree != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var itemIds = TestItemsEndpoints.CollectItemIds(tree);
                    await prefetchService.PrefetchArtifactsForItemsAsync(itemIds);
                }
                catch
                {
                    // Errors already logged in prefetch service
                }
            });
        }

        return Results.Ok(tree);
    }

    /// <summary>
    ///     Gets unique error patterns for a suite with grouped occurrences.
    ///     Uses error_fingerprint from log_tokens to group similar errors.
    ///     Similar to GetLaunchUniqueErrors but filtered to test items under this specific suite.
    /// </summary>
    private static async Task<IResult> GetSuiteUniqueErrors(
        [FromRoute] Guid suiteId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("SuitesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "SuitesEndpoints.GetSuiteUniqueErrors");

        // First, verify the suite exists
        var suite = await store.GetTestItemAsync(suiteId);
        if (suite is not { ItemType: "Suite" })
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "suiteId={SuiteId}", suiteId);

            return ProblemDetailsHelpers.NotFound(
                $"Suite {suiteId} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        await using var conn = await db.OpenConnectionAsync();

        // Query for unique errors in test items belonging to this suite
        // We need to find all test items where parent_item_id = suiteId (or nested deeper)
        // Use a CTE to recursively find all descendants of the suite
        const string sql = @"
            WITH RECURSIVE suite_items AS (
                -- Start with the suite itself
                SELECT run_id FROM test_items WHERE run_id = $1
                UNION ALL
                -- Recursively find all children
                SELECT ti.run_id
                FROM test_items ti
                JOIN suite_items si ON ti.parent_item_id = si.run_id
            )
            SELECT
                lt.error_fingerprint,
                COUNT(DISTINCT li.test_item_uuid) as failed_test_count,
                COUNT(*) as occurrence_count,
                MIN(li.time) as first_occurrence,
                MAX(li.time) as last_occurrence,
                lt.message as sample_stack_trace,
                ARRAY_AGG(DISTINCT li.test_item_uuid) as test_item_ids
            FROM log_items li
            JOIN log_tokens lt ON li.token_hash = lt.token_hash
            WHERE li.test_item_uuid IN (SELECT run_id FROM suite_items)
              AND li.level IN ('ERROR', 'FATAL')
              AND lt.error_fingerprint IS NOT NULL
            GROUP BY lt.error_fingerprint, lt.message
            ORDER BY failed_test_count DESC, occurrence_count DESC
            LIMIT 100";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(suiteId);

        var errors = new List<UniqueErrorDto>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            errors.Add(new UniqueErrorDto
            {
                Fingerprint = reader.GetString(0),
                FailedTestCount = reader.GetInt32(1),
                OccurrenceCount = reader.GetInt32(2),
                FirstOccurrence = reader.GetDateTime(3),
                LastOccurrence = reader.GetDateTime(4),
                SampleStackTrace = reader.GetString(5),
                TestItemIds = reader.GetFieldValue<Guid[]>(6).ToList()
            });
        }

        return Results.Ok(errors);
    }

    private static async Task<IResult> GetSuiteChildItemsHistory(
        [FromRoute] long suiteDbId,
        [FromQuery] int? depth,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("SuitesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "SuitesEndpoints.GetSuiteChildItemsHistory");

        try
        {
            var historyDepth = depth ?? 10;
            if (historyDepth is < 1 or > 50)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    "error=InvalidDepth depth={Depth}", historyDepth);

                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["depth"] = ["Depth must be between 1 and 50"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Call PostgresSQL function
            await using var cmd = db.CreateCommand(
                "SELECT item_name, item_type, launches FROM get_suite_child_items_history($1, $2)");
            cmd.Parameters.AddWithValue(suiteDbId);
            cmd.Parameters.AddWithValue(historyDepth);

            var rows = new List<object>();
            var columns = new List<object>();
            var columnsBuilt = false;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var itemName = reader.GetString(0);
                var itemType = reader.GetString(1);
                var launchesJson = reader.GetString(2);
                var launches = JsonSerializer.Deserialize<List<JsonElement>>(launchesJson) ?? new List<JsonElement>();

                if (!columnsBuilt && launches.Count > 0)
                {
                    foreach (var launch in launches)
                    {
                        columns.Add(new
                        {
                            LaunchId = launch.GetProperty("launchId").GetGuid(),
                            LaunchNumber = launch.GetProperty("launchNumber").GetInt64(),
                            StartTime = launch.GetProperty("startTime").GetDateTime()
                        });
                    }

                    columnsBuilt = true;
                }

                var cells = launches.Select(l => new
                {
                    LaunchId = l.GetProperty("launchId").GetGuid(),
                    Status = l.GetProperty("status").GetString(),
                    Tooltip = JsonSerializer.Deserialize<object>(l.GetProperty("tooltip").GetRawText())
                }).ToList();

                rows.Add(new { ItemName = itemName, ItemType = itemType, Cells = cells });
            }

            return Results.Ok(new { Columns = columns, Rows = rows });
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001") // Raised exception
        {
            chunkedLogger.LogMilestone(
                EventCodes.TestItem.TestItemNotFound,
                "error=SuiteNotFound suiteDbId={SuiteDbId} message={Message}", suiteDbId, ex.MessageText);

            return ProblemDetailsHelpers.NotFound(
                ex.MessageText,
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "error=FailedToGetHistory suiteDbId={SuiteDbId}", suiteDbId);

            return ProblemDetailsHelpers.InternalServerError(
                "Internal error loading history",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }
}
