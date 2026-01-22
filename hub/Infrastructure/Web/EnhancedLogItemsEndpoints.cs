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

using System.Text;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Caching;
using PlaywrightHub.Infrastructure.Helpers;

namespace PlaywrightHub.Infrastructure.Web;

public static class EnhancedLogItemsEndpoints
{
    public static void MapEnhancedLogItemsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test-items/{itemId:guid}/logs");

        // GET /api/test-items/{itemId}/logs/hierarchical - Hierarchical view with pagination
        group.MapGet("/hierarchical", GetHierarchicalLogs)
            .WithName("GetHierarchicalLogs")
            .WithTags("LogItems")
            .WithSummary("Get hierarchical logs with nested steps and pagination")
            .WithDescription(
                "Returns test item logs organized hierarchically by steps with support for deep nesting, caching, and retry logic")
            .Produces<HierarchicalLogsResponse>()
            .Produces(404);

        // GET /api/test-items/{itemId}/logs/flat - Flat view with search/filtering
        group.MapGet("/flat", GetFlatLogs)
            .WithName("GetFlatLogs")
            .WithTags("LogItems")
            .WithSummary("Get flat logs with search and filtering")
            .WithDescription(
                "Returns test item logs as flat list with search, filtering by level/status, and pagination")
            .Produces<FlatLogsResponse>()
            .Produces(404);

        // GET /api/test-items/{itemId}/logs/export - Export to JSON/CSV
        group.MapGet("/export", ExportLogs)
            .WithName("ExportLogs")
            .WithTags("LogItems")
            .WithSummary("Export logs to JSON or CSV format")
            .WithDescription("Exports all logs for test item in JSON or CSV format with optional filtering")
            .Produces<string>(200, "application/json", "text/csv");

        // GET /api/test-items/{itemId}/logs/search - Search within steps
        group.MapGet("/search", SearchLogs)
            .WithName("SearchLogs")
            .WithTags("LogItems")
            .WithSummary("Search logs within steps using text query")
            .WithDescription("Full-text search across log messages, step names, and descriptions")
            .Produces<SearchLogsResponse>()
            .Produces(404);

        // GET /api/test-items/{itemId}/logs/stats - Log statistics
        group.MapGet("/stats", GetLogStats)
            .WithName("GetLogStats")
            .WithTags("LogItems")
            .WithSummary("Get log statistics and telemetry")
            .WithDescription("Returns cache hit rates, log counts by level, and performance metrics")
            .Produces<LogStatsResponse>();
    }

    private static async Task<IResult> GetHierarchicalLogs(
        Guid itemId,
        [FromServices] IResultsStore store,
        [FromServices] TestItemCache? cache,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] NpgsqlDataSource dataSource,
        HttpContext httpContext,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 1000,
        [FromQuery] int maxDepth = 5,
        [FromQuery] bool useCache = true)
    {
        var logger = loggerFactory.CreateLogger("EnhancedLogItems");
        var chunkedLogger = new ChunkedLogger(logger, "EnhancedLogItems.GetHierarchicalLogs");
        var cacheKey = $"hierarchical_logs:{itemId}:{skip}:{take}:{maxDepth}";

        try
        {
            // Try cache first
            if (useCache && cache != null)
            {
                var cached = cache.Get<HierarchicalLogsResponse>(cacheKey);
                if (cached != null)
                {
                    logger.LogDebug("Cache hit for hierarchical logs: {ItemId}", itemId);
                    return Results.Ok(cached);
                }
            }

            // Apply retry policy for transient errors
            var retryPolicy = DatabaseRetryPolicy.CreateRetryPolicy(logger);
            var logs = await retryPolicy.ExecuteAsync(async () =>
            {
                // Use optimized query with proper indexing
                await using var conn = await dataSource.OpenConnectionAsync();
                return await GetHierarchicalLogsOptimized(conn, itemId, skip, take, maxDepth);
            });

            var response = new HierarchicalLogsResponse
            {
                ItemId = itemId,
                Logs = logs,
                Skip = skip,
                Take = take,
                TotalCount = logs.Count,
                MaxDepth = maxDepth,
                CacheHit = false
            };

            // Cache the result
            if (useCache && cache != null)
            {
                cache.Set(cacheKey, response, TimeSpan.FromMinutes(5));
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemQueryFailed, ex,
                "error=GetHierarchicalLogsFailed itemId={ItemId} skip={Skip} take={Take}", itemId, skip, take);

            // Fallback to flat logs
            logger.LogWarning("Falling back to flat log view for item {ItemId}", itemId);
            try
            {
                var fallbackLogs = await store.GetLogItemsForTestItemAsync(itemId, skip, take);
                var fallbackResponse = new HierarchicalLogsResponse
                {
                    ItemId = itemId,
                    Logs = fallbackLogs.Select(l => new HierarchicalLogEntryDto
                    {
                        Id = l.Id,
                        ParentId = null,
                        IsStepHeader = false,
                        IsNested = false,
                        NestLevel = 0,
                        Timestamp = l.Time,
                        Level = l.Level,
                        Source = "fallback",
                        Message = l.Message,
                        Name = "",
                        Description = "",
                        Status = "InProgress",
                        DurationMs = null,
                        AttachmentCount = 0,
                        HasAttachment = l.AttachmentId.HasValue,
                        AttachmentType = "",
                        AttachmentName = ""
                    }).ToList(),
                    Skip = skip,
                    Take = take,
                    TotalCount = fallbackLogs.Count,
                    MaxDepth = 0,
                    CacheHit = false,
                    FallbackMode = true
                };
                return Results.Ok(fallbackResponse);
            }
            catch (Exception fallbackEx)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemQueryFailed, fallbackEx,
                    "error=FallbackFailed itemId={ItemId}", itemId);

                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to retrieve logs",
                    eventCode: EventCodes.LogItem.LogItemQueryFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        }
    }

    private static async Task<List<HierarchicalLogEntryDto>> GetHierarchicalLogsOptimized(
        NpgsqlConnection conn,
        Guid testItemId,
        int skip,
        int take,
        int maxDepth)
    {
        var result = new List<HierarchicalLogEntryDto>();

        // Step 1: Recursively get all child test items with item_type = 'Step' and depth tracking
        // Use path array to maintain tree structure - children appear immediately after their parents
        var stepsSql = @"
            WITH RECURSIVE step_tree AS (
                SELECT run_id, item_type, name, description, computed_status,
                       start_time, finish_time, parent_item_id, 1 as depth,
                       ARRAY[start_time] as path
                FROM test_items
                WHERE parent_item_id = @testItemId
                  AND item_type = 'Step'
                UNION ALL
                SELECT ti.run_id, ti.item_type, ti.name, ti.description, ti.computed_status,
                       ti.start_time, ti.finish_time, ti.parent_item_id, st.depth + 1,
                       st.path || ti.start_time
                FROM test_items ti
                INNER JOIN step_tree st ON ti.parent_item_id = st.run_id
                WHERE st.depth < @maxDepth
                  AND ti.item_type = 'Step'
            )
            SELECT run_id, item_type, name, description, computed_status,
                   start_time, finish_time, parent_item_id, depth
            FROM step_tree
            ORDER BY path ASC";

        await using var stepsCmd = new NpgsqlCommand(stepsSql, conn);
        stepsCmd.Parameters.AddWithValue("testItemId", testItemId);
        stepsCmd.Parameters.AddWithValue("maxDepth", maxDepth);

        var steps = new Dictionary<Guid, HierarchicalLogEntryDto>();
        var stepOrder = new List<Guid>();
        var stepDepth = new Dictionary<Guid, int>();

        await using (var stepsReader = await stepsCmd.ExecuteReaderAsync())
        {
            while (await stepsReader.ReadAsync())
            {
                var stepId = stepsReader.GetGuid(0);
                var depth = stepsReader.GetInt32(8);

                stepDepth[stepId] = depth;

                var stepHeader = new HierarchicalLogEntryDto
                {
                    Id = stepId,
                    ParentId = stepsReader.IsDBNull(7) ? null : stepsReader.GetGuid(7),
                    IsStepHeader = true,
                    IsNested = depth > 1,
                    NestLevel = depth - 1,
                    Timestamp = stepsReader.GetFieldValue<DateTimeOffset>(5).DateTime,
                    Level = "INFO",
                    Source = stepsReader.GetString(1),
                    Message = "",
                    Name = stepsReader.GetString(2),
                    Description = stepsReader.IsDBNull(3) ? "" : stepsReader.GetString(3),
                    Status = stepsReader.IsDBNull(4) ? "InProgress" : stepsReader.GetString(4),
                    DurationMs = stepsReader.IsDBNull(6)
                        ? null
                        : (long)(stepsReader.GetFieldValue<DateTimeOffset>(6) -
                                 stepsReader.GetFieldValue<DateTimeOffset>(5)).TotalMilliseconds,
                    AttachmentCount = 0,
                    HasAttachment = false,
                    AttachmentType = "",
                    AttachmentName = ""
                };

                steps[stepId] = stepHeader;
                stepOrder.Add(stepId);
            }
        }

        // Step 2: Get log items with optimized join to log_tokens for deduplicated messages
        var logsSql = @"
            SELECT l.id, l.test_item_uuid, l.time, l.level,
                   COALESCE(l.message, lt.message) as message,
                   l.attachment_id, l.logger_name, l.exception_message, l.stack_trace,
                   ta.file_name, ta.content_type
            FROM log_items l
            LEFT JOIN log_tokens lt ON l.token_hash = lt.token_hash
            LEFT JOIN test_artifacts ta ON l.attachment_id = ta.id
            WHERE l.test_item_uuid = @testItemId
               OR l.test_item_uuid = ANY(@stepIds)
            ORDER BY l.time ASC
            LIMIT @take OFFSET @skip";

        await using var logsCmd = new NpgsqlCommand(logsSql, conn);
        logsCmd.Parameters.AddWithValue("testItemId", testItemId);
        logsCmd.Parameters.AddWithValue("stepIds", stepOrder.ToArray());
        logsCmd.Parameters.AddWithValue("skip", skip);
        logsCmd.Parameters.AddWithValue("take", take);

        var logsByStep = new Dictionary<Guid, List<HierarchicalLogEntryDto>>();
        var rootLogs = new List<HierarchicalLogEntryDto>();

        await using (var logsReader = await logsCmd.ExecuteReaderAsync())
        {
            while (await logsReader.ReadAsync())
            {
                var logTestItemId = logsReader.GetGuid(1);
                var nestLevel = stepDepth.TryGetValue(logTestItemId, out var value) ? value : 0;

                // COALESCE in SQL handles null message by falling back to log_tokens.message
                var message = logsReader.IsDBNull(4) ? "" : logsReader.GetString(4);
                if (!logsReader.IsDBNull(7)) // Has exception
                {
                    var exceptionMsg = logsReader.GetString(7);
                    message = string.IsNullOrEmpty(message)
                        ? $"Exception: {exceptionMsg}"
                        : $"{message}\nException: {exceptionMsg}";
                }

                var logEntry = new HierarchicalLogEntryDto
                {
                    Id = logsReader.GetGuid(0),
                    ParentId = logTestItemId == testItemId ? null : logTestItemId,
                    IsStepHeader = false,
                    IsNested = logTestItemId != testItemId,
                    NestLevel = nestLevel,
                    Timestamp = logsReader.GetDateTime(2),
                    Level = logsReader.IsDBNull(3) ? "INFO" : logsReader.GetString(3),
                    Source = logsReader.IsDBNull(6) ? "test" : logsReader.GetString(6),
                    Message = message,
                    Name = "",
                    Description = "",
                    Status = "InProgress",
                    DurationMs = null,
                    AttachmentCount = 0,
                    HasAttachment = !logsReader.IsDBNull(5),
                    AttachmentType =
                        logsReader.IsDBNull(10) ? "" : logsReader.GetString(10), // content_type from test_artifacts
                    AttachmentName =
                        logsReader.IsDBNull(9) ? "" : logsReader.GetString(9), // file_name from test_artifacts
                    ArtifactId = logsReader.IsDBNull(5) ? null : logsReader.GetGuid(5)
                };

                if (logTestItemId == testItemId)
                {
                    rootLogs.Add(logEntry);
                }
                else
                {
                    if (!logsByStep.ContainsKey(logTestItemId))
                    {
                        logsByStep[logTestItemId] = [];
                    }

                    logsByStep[logTestItemId].Add(logEntry);
                }
            }
        }

        // Step 3: Update attachment counts
        foreach (var stepId in stepOrder)
        {
            if (logsByStep.TryGetValue(stepId, out var value))
            {
                var count = value.Count(l => l.HasAttachment);
                steps[stepId] = steps[stepId] with { AttachmentCount = count };
            }
        }

        // Step 4: Assemble hierarchical result
        // Add root logs first (logs attached directly to test item, before any steps)
        result.AddRange(rootLogs);

        // Then add steps and their logs in hierarchical order
        foreach (var stepId in stepOrder)
        {
            result.Add(steps[stepId]);
            if (logsByStep.TryGetValue(stepId, out var value))
            {
                result.AddRange(value);
            }
        }

        return result;
    }

    private static async Task<IResult> GetFlatLogs(
        Guid itemId,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null)
    {
        var logger = loggerFactory.CreateLogger("EnhancedLogItems");
        var chunkedLogger = new ChunkedLogger(logger, "EnhancedLogItems.GetFlatLogs");

        try
        {
            var retryPolicy = DatabaseRetryPolicy.CreateRetryPolicy(logger);
            var logs = await retryPolicy.ExecuteAsync(() =>
                store.GetLogItemsForTestItemAsync(itemId, skip, take));

            // Apply filters
            var filtered = logs.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(level))
            {
                filtered = filtered.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                filtered = filtered.Where(l =>
                    l.Message.Contains(searchLower, StringComparison.CurrentCultureIgnoreCase) ||
                    (l.Level?.ToLower().Contains(searchLower, StringComparison.CurrentCultureIgnoreCase) ?? false));
            }

            var results = filtered.ToList();

            return Results.Ok(new FlatLogsResponse
            {
                ItemId = itemId,
                Logs = results,
                Skip = skip,
                Take = take,
                TotalCount = results.Count,
                Filtered = level != null || search != null
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemQueryFailed, ex,
                "error=GetFlatLogsFailed itemId={ItemId} skip={Skip} take={Take}", itemId, skip, take);

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to retrieve logs",
                eventCode: EventCodes.LogItem.LogItemQueryFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> SearchLogs(
        Guid itemId,
        [FromServices] NpgsqlDataSource dataSource,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        [FromQuery] string query = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var logger = loggerFactory.CreateLogger("EnhancedLogItems");
        var chunkedLogger = new ChunkedLogger(logger, "EnhancedLogItems.SearchLogs");

        if (string.IsNullOrWhiteSpace(query))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=EmptyQuery itemId={ItemId}", itemId);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["query"] = ["Query parameter is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync();

            // Full-text search across messages, step names, and descriptions
            const string sql = @"
                WITH step_names AS (
                    SELECT run_id, name, description
                    FROM test_items
                    WHERE parent_item_id = @itemId
                      AND (name ILIKE @query OR description ILIKE @query)
                )
                SELECT l.id, l.test_item_uuid, l.time, l.level,
                       COALESCE(l.message, lt.message) as message,
                       l.attachment_id, l.logger_name,
                       COALESCE(s.name, 'Root') as step_name
                FROM log_items l
                LEFT JOIN log_tokens lt ON l.token_hash = lt.token_hash
                LEFT JOIN step_names s ON l.test_item_uuid = s.run_id
                WHERE (l.test_item_uuid = @itemId OR l.test_item_uuid IN (SELECT run_id FROM step_names))
                  AND (l.message ILIKE @query OR lt.message ILIKE @query)
                ORDER BY l.time DESC
                LIMIT @take OFFSET @skip";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("itemId", itemId);
            cmd.Parameters.AddWithValue("query", $"%{query}%");
            cmd.Parameters.AddWithValue("skip", skip);
            cmd.Parameters.AddWithValue("take", take);

            var results = new List<SearchResultDto>();

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new SearchResultDto
                {
                    Id = reader.GetGuid(0),
                    TestItemId = reader.GetGuid(1),
                    Timestamp = reader.GetDateTime(2),
                    Level = reader.GetString(3),
                    Message = reader.GetString(4),
                    HasAttachment = !reader.IsDBNull(5),
                    Source = reader.IsDBNull(6) ? "test" : reader.GetString(6),
                    StepName = reader.GetString(7)
                });
            }

            return Results.Ok(new SearchLogsResponse
            {
                ItemId = itemId,
                Query = query,
                Results = results,
                Skip = skip,
                Take = take,
                TotalCount = results.Count
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemQueryFailed, ex,
                "error=SearchLogsFailed itemId={ItemId} query={Query}", itemId, query);

            return ProblemDetailsHelpers.InternalServerError(
                "Search failed",
                eventCode: EventCodes.LogItem.LogItemQueryFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> ExportLogs(
        Guid itemId,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        [FromQuery] string format = "json",
        [FromQuery] string? level = null)
    {
        var logger = loggerFactory.CreateLogger("EnhancedLogItems");
        var chunkedLogger = new ChunkedLogger(logger, "EnhancedLogItems.ExportLogs");

        try
        {
            var logs = await store.GetLogItemsForTestItemAsync(itemId, 0, 10000); // Max 10k logs

            // Apply level filter
            if (!string.IsNullOrWhiteSpace(level))
            {
                logs = logs.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (format.Equals("csv", StringComparison.CurrentCultureIgnoreCase))
            {
                var csv = new StringBuilder();
                csv.AppendLine("Timestamp,Level,Source,Message,HasAttachment");

                foreach (var log in logs)
                {
                    csv.AppendLine(
                        $"{log.Time:yyyy-MM-dd HH:mm:ss.fff},{log.Level},\"{log.Level}\",\"{log.Message.Replace("\"", "\"\"")}\",{log.AttachmentId.HasValue}");
                }

                return Results.Content(csv.ToString(), "text/csv", Encoding.UTF8);
            }

            var json = JsonSerializer.Serialize(logs,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return Results.Content(json, "application/json", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemQueryFailed, ex,
                "error=ExportLogsFailed itemId={ItemId} format={Format}", itemId, format);

            return ProblemDetailsHelpers.InternalServerError(
                "Export failed",
                eventCode: EventCodes.LogItem.LogItemQueryFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static IResult GetLogStats(
        Guid itemId,
        [FromServices] TestItemCache? cache)
    {
        var stats = cache?.GetStatistics() ?? (0, 0, 0.0);

        return Results.Ok(new LogStatsResponse
        {
            ItemId = itemId,
            CacheHits = stats.Hits,
            CacheMisses = stats.Misses,
            CacheHitRate = stats.HitRate,
            Timestamp = DateTime.UtcNow
        });
    }
}

// Response DTOs
public record HierarchicalLogsResponse
{
    public required Guid ItemId { get; init; }
    public required List<HierarchicalLogEntryDto> Logs { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public int TotalCount { get; init; }
    public int MaxDepth { get; init; }
    public bool CacheHit { get; init; }
    public bool FallbackMode { get; init; }
}

public record FlatLogsResponse
{
    public required Guid ItemId { get; init; }
    public required List<LogItemDto> Logs { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public int TotalCount { get; init; }
    public bool Filtered { get; init; }
}

public record SearchLogsResponse
{
    public required Guid ItemId { get; init; }
    public required string Query { get; init; }
    public required List<SearchResultDto> Results { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public int TotalCount { get; init; }
}

public record SearchResultDto
{
    public required Guid Id { get; init; }
    public required Guid TestItemId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public bool HasAttachment { get; init; }
    public required string Source { get; init; }
    public required string StepName { get; init; }
}

public record LogStatsResponse
{
    public required Guid ItemId { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public double CacheHitRate { get; init; }
    public DateTime Timestamp { get; init; }
}
