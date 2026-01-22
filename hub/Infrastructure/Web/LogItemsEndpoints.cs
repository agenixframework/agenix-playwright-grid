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
using System.Text.Json.Serialization;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     REST API endpoints for log items.
///     Supports creating log items with optional file attachments and querying by test item or launch.
/// </summary>
public static class LogItemsEndpoints
{
    public static void MapLogItemsEndpoints(this IEndpointRouteBuilder routes)
    {
        // ReportPortal-style route: v1/{projectName}/log
        var group = routes.MapGroup("v1/{projectName}/log")
            .WithTags("Log Items")
            .WithOpenApi();

        // POST v1/{projectName}/log - Single log item
        group.MapPost("", CreateLogItem)
            .WithName("CreateLogItem")
            .WithSummary("Create a single log item")
            .Produces<LogItemCreatedResponse>(201)
            .Produces(400);

        // POST v1/{projectName}/log - Batch log items
        group.MapPost("/batch", CreateLogItemBatch)
            .WithName("CreateLogItemBatch")
            .WithSummary("Create multiple log items in a single request")
            .Produces<LogItemsCreatedResponse>(201)
            .Produces(400);

        // GET v1/{projectName}/log/{id}
        group.MapGet("/{id:guid}", GetLogItem)
            .WithName("GetLogItem")
            .WithSummary("Get a log item by ID")
            .Produces<LogItemResponse>()
            .Produces(404);

        // GET v1/{projectName}/log/test-item/{testItemUuid}
        group.MapGet("/test-item/{testItemUuid:guid}", GetLogItemsForTestItem)
            .WithName("GetLogItemsForTestItem")
            .WithSummary("Get log items for a specific test item")
            .Produces<List<LogItemResponse>>();

        // GET v1/{projectName}/log/launch/{launchUuid}
        group.MapGet("/launch/{launchUuid:guid}", GetLogItemsForLaunch)
            .WithName("GetLogItemsForLaunch")
            .WithSummary("Get log items for a specific launch")
            .Produces<List<LogItemResponse>>();
    }

    private static async Task<IResult> CreateLogItem(
        string projectName,
        [FromBody] CreateLogItemRequest request,
        [FromServices] IResultsStore store,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LogItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, nameof(LogItemsEndpoints));

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.LogItemCreated, // LOG01
            "project={Project} testItemUuid={TestItemUuid}",
            projectName, request.TestItemUuid);

        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.TestItemUuid) ||
                !Guid.TryParse(request.TestItemUuid, out var testItemGuid) || testItemGuid == Guid.Empty)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemCreationFailed, // LOG02
                    "error=InvalidTestItemUuid testItemUuid={TestItemUuid}",
                    request.TestItemUuid);

                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["testItemUuid"] = ["TestItemUuid is required and must be a valid non-empty GUID"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Verify test item exists (soft validation)
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.TestItemLookupStarted, // LOG13
                "testItemUuid={TestItemUuid}",
                testItemGuid);

            // Validate test item exists before creating a log item
            var testItem = await store.GetTestItemAsync(testItemGuid);
            if (testItem == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemCreationFailed,
                    "error=TestItemNotFound testItemUuid={TestItemUuid}",
                    testItemGuid);

                return ProblemDetailsHelpers.NotFound(
                    $"Test item {testItemGuid} not found. Create the test item before adding log items.",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            chunkedLogger.LogMilestone(
                EventCodes.LogItem.TestItemFound, // LOG14
                "testItemUuid={TestItemUuid} itemType={ItemType}",
                testItemGuid, testItem.ItemType);

            var dto = new CreateLogItemDto
            {
                TestItemUuid = testItemGuid,
                LaunchUuid = string.IsNullOrWhiteSpace(request.LaunchUuid) ? null : Guid.Parse(request.LaunchUuid),
                Time = request.Time,
                Level = request.Level.ToUpperInvariant(),
                Message = request.Text,
                AttachmentData = request.Attach?.Data,
                AttachmentName = request.Attach?.Name,
                AttachmentMimeType = request.Attach?.MimeType
            };

            // Log items: Always publish to RabbitMQ for async ingestion (high throughput)
            var id = Guid.NewGuid();

            // Build metadata JSON with common properties
            var metadataJson = BuildMetadataJson(dto.AttachmentData, dto.AttachmentName, dto.AttachmentMimeType,
                dto.Level);

            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemEventPublished, // LOG20
                "logItemId={LogItemId} testItemUuid={TestItemUuid} level={Level}",
                id, dto.TestItemUuid, dto.Level);

            var evt = new LogItemEvent
            {
                EventType = "LogItemAppended",
                ItemId = dto.TestItemUuid,
                LaunchId = dto.LaunchUuid ?? Guid.Empty,
                Level = dto.Level,
                Message = dto.Message,
                TimestampUtc = dto.Time,
                MetadataJson = metadataJson,
                CorrelationId = Guid.NewGuid().ToString()
            };

            try
            {
                await eventPublisher.PublishLogItemEventAsync(evt);
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemEventPublishConfirmed, // LOG23
                    "logItemId={LogItemId} correlationId={CorrelationId}",
                    id, evt.CorrelationId);
            }
            catch (Exception ex)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemEventPublishFailed, // LOG21
                    ex,
                    "error={Error} logItemId={LogItemId} correlationId={CorrelationId}",
                    ex.Message, id, evt.CorrelationId);

                // Continue - event will be retried or logged for investigation
            }

            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchStarted, // Reuse LCH02 for consistency
                "logItemId={LogItemId} testItemUuid={TestItemUuid}",
                id, dto.TestItemUuid);

            return Results.Created($"/v1/{projectName}/log/{id}", new LogItemCreatedResponse { Id = id.ToString() });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemCreationFailed,
                ex,
                "error={Error} testItemUuid={TestItemUuid}",
                ex.Message, request.TestItemUuid);

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to create log item.",
                eventCode: EventCodes.LogItem.LogItemCreationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> CreateLogItemBatch(
        string projectName,
        [FromBody] CreateLogItemRequest[] requests,
        [FromServices] IResultsStore store,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LogItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, nameof(LogItemsEndpoints));

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.LogItemBatchCreated, // LOG03
            "project={Project} batchSize={BatchSize}",
            projectName, requests.Length);

        try
        {
            // Batch validation
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemBatchValidationStarted, // LOG10
                "batchSize={BatchSize}",
                requests.Length);

            var validRequests = new List<(CreateLogItemRequest request, Guid testItemGuid)>();
            var invalidCount = 0;

            foreach (var request in requests)
            {
                // Validate TestItemUuid is not empty
                if (string.IsNullOrWhiteSpace(request.TestItemUuid) ||
                    !Guid.TryParse(request.TestItemUuid, out var testItemGuid) ||
                    testItemGuid == Guid.Empty)
                {
                    invalidCount++;
                    continue;
                }

                validRequests.Add((request, testItemGuid));
            }

            if (invalidCount > 0)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemBatchInvalidItemsSkipped, // LOG12
                    "invalidCount={InvalidCount} validCount={ValidCount}",
                    invalidCount, validRequests.Count);
            }

            // If all requests are invalid, return bad request
            if (validRequests.Count == 0)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.LogItem.LogItemBatchFailed, // LOG04
                    "error=NoValidItemsInBatch");

                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["requests"] = ["No valid log items in batch"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemBatchValidationComplete, // LOG11
                "validCount={ValidCount}",
                validRequests.Count);

            // Process batch
            var publishedCount = 0;
            var failedCount = 0;

            chunkedLogger.LogMilestone(
                EventCodes.LogItem.BatchProcessingStarted, // LOG24
                "validCount={ValidCount}",
                validRequests.Count);

            var responses = new List<LogItemCreatedResponse>();

            foreach (var (request, testItemGuid) in validRequests)
            {
                try
                {
                    var id = Guid.NewGuid();
                    var normalizedLevel = request.Level.ToUpperInvariant();

                    // Infer logger name from log level and message patterns
                    var loggerName = InferLoggerName(normalizedLevel, request.Text);

                    // Build metadata JSON with common properties
                    var metadataJson = BuildMetadataJson(request.Attach?.Data, request.Attach?.Name,
                        request.Attach?.MimeType, normalizedLevel);

                    var evt = new LogItemEvent
                    {
                        EventType = "LogItemAppended",
                        ItemId = testItemGuid,
                        LaunchId =
                            string.IsNullOrWhiteSpace(request.LaunchUuid)
                                ? Guid.Empty
                                : Guid.Parse(request.LaunchUuid),
                        Level = normalizedLevel,
                        Message = request.Text,
                        TimestampUtc = request.Time,
                        LoggerName = loggerName,
                        MetadataJson = metadataJson,
                        CorrelationId = Guid.NewGuid().ToString()
                    };
                    await eventPublisher.PublishLogItemEventAsync(evt);
                    publishedCount++;
                    responses.Add(new LogItemCreatedResponse { Id = id.ToString() });
                }
                catch (Exception ex)
                {
                    failedCount++;
                    chunkedLogger.LogWarning(
                        EventCodes.LogItem.LogItemEventPublishFailed,
                        "batchError={Error} itemIndex={Index} testItemUuid={TestItemUuid}",
                        ex.Message, publishedCount + failedCount, testItemGuid);
                }
            }

            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemBatchEventsPublished, // LOG22
                "publishedCount={PublishedCount} failedCount={FailedCount} total={Total}",
                publishedCount, failedCount, validRequests.Count);

            return Results.Created($"/v1/{projectName}/log/batch",
                new LogItemsCreatedResponse { Responses = responses });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemBatchFailed,
                ex,
                "error={Error}",
                ex.Message);

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to create log item batch.",
                eventCode: EventCodes.LogItem.LogItemBatchFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> GetLogItem(
        string projectName,
        [FromRoute] Guid id,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LogItemsEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, nameof(LogItemsEndpoints));

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.LogItemRetrieved, // LOG05
            "project={Project} logItemId={LogItemId}",
            projectName, id);

        var logItem = await store.GetLogItemAsync(id);

        if (logItem == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.LogItem.LogItemRetrievalFailed, // LOG06
                "error=NotFound logItemId={LogItemId}",
                id);
            return ProblemDetailsHelpers.NotFound(
                $"Log item {id} not found",
                eventCode: EventCodes.LogItem.LogItemRetrievalFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        return Results.Ok(new LogItemResponse
        {
            Id = logItem.Id.ToString(),
            ItemUuid = logItem.TestItemUuid.ToString(),
            LaunchUuid = logItem.LaunchUuid?.ToString(),
            Time = logItem.Time,
            Level = logItem.Level,
            Message = logItem.Message
        });
    }

    private static async Task<IResult> GetLogItemsForTestItem(
        string projectName,
        [FromRoute] Guid testItemUuid,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var logger = loggerFactory.CreateLogger(nameof(LogItemsEndpoints));
        var chunkedLogger = new ChunkedLogger(logger, nameof(LogItemsEndpoints));

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.LogItemsForTestItemRetrieved, // LOG07
            "project={Project} testItemUuid={TestItemUuid}",
            projectName, testItemUuid);

        var logItems = await store.GetLogItemsForTestItemAsync(testItemUuid, skip, take);

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.QueryCompleted, // LOG40
            "testItemUuid={TestItemUuid} count={Count}",
            testItemUuid, logItems.Count);

        var responses = logItems.Select(item => new LogItemResponse
        {
            Id = item.Id.ToString(),
            ItemUuid = item.TestItemUuid.ToString(),
            LaunchUuid = item.LaunchUuid?.ToString(),
            Time = item.Time,
            Level = item.Level,
            Message = item.Message
        }).ToList();

        return Results.Ok(responses);
    }

    private static async Task<IResult> GetLogItemsForLaunch(
        string projectName,
        [FromRoute] Guid launchUuid,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var logger = loggerFactory.CreateLogger(nameof(LogItemsEndpoints));
        var chunkedLogger = new ChunkedLogger(logger, nameof(LogItemsEndpoints));

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.LogItemsForLaunchRetrieved, // LOG08
            "project={Project} launchUuid={LaunchUuid}",
            projectName, launchUuid);

        var logItems = await store.GetLogItemsForLaunchAsync(launchUuid, skip, take);

        chunkedLogger.LogMilestone(
            EventCodes.LogItem.QueryForLaunchCompleted, // LOG41
            "launchUuid={LaunchUuid} count={Count}",
            launchUuid, logItems.Count);

        var responses = logItems.Select(item => new LogItemResponse
        {
            Id = item.Id.ToString(),
            ItemUuid = item.TestItemUuid.ToString(),
            LaunchUuid = item.LaunchUuid?.ToString(),
            Time = item.Time,
            Level = item.Level,
            Message = item.Message
        }).ToList();

        return Results.Ok(responses);
    }

    /// <summary>
    ///     Infers a logger name from the log level and message content patterns
    /// </summary>
    private static string? InferLoggerName(string level, string message)
    {
        // Playwright-specific patterns
        if (message.Contains("browser.newContext") || message.Contains("browser.newPage"))
        {
            return "Playwright.Browser";
        }

        if (message.Contains("page.goto") || message.Contains("page.click") || message.Contains("page.fill"))
        {
            return "Playwright.Page";
        }

        if (message.Contains("expect(") || message.Contains("assertion"))
        {
            return "Playwright.Assertions";
        }

        if (message.Contains("beforeEach") || message.Contains("afterEach") || message.Contains("test("))
        {
            return "Playwright.Test";
        }

        // HTTP/Network patterns
        if (message.Contains("HTTP") || message.Contains("GET ") || message.Contains("POST ") ||
            message.Contains("PUT ") || message.Contains("DELETE ") || message.Contains("status code"))
        {
            return "HTTP.Request";
        }

        if (message.Contains("timeout") || message.Contains("connection refused") || message.Contains("ECONNREFUSED"))
        {
            return "Network.Connection";
        }

        // Database patterns
        if (message.Contains("SELECT ") || message.Contains("INSERT ") || message.Contains("UPDATE ") ||
            message.Contains("DELETE FROM") || message.Contains("SQL"))
        {
            return "Database.Query";
        }

        if (message.Contains("deadlock") || message.Contains("constraint violation") ||
            message.Contains("duplicate key"))
        {
            return "Database.Error";
        }

        // Test execution patterns
        if (message.Contains("screenshot") || message.Contains("trace") || message.Contains("video"))
        {
            return "Test.Artifacts";
        }

        if (message.Contains("retry") || message.Contains("attempt"))
        {
            return "Test.Retry";
        }

        if (message.Contains("passed") || message.Contains("failed") || message.Contains("skipped"))
        {
            return "Test.Result";
        }

        // Error patterns
        if (level == "ERROR" || level == "FATAL")
        {
            if (message.Contains("exception") || message.Contains("stack trace"))
            {
                return "Error.Exception";
            }

            if (message.Contains("validation") || message.Contains("invalid"))
            {
                return "Error.Validation";
            }

            return "Error.General";
        }

        // Warning patterns
        if (level == "WARN" || level == "WARNING")
        {
            if (message.Contains("deprecated"))
            {
                return "Warning.Deprecation";
            }

            if (message.Contains("performance") || message.Contains("slow"))
            {
                return "Warning.Performance";
            }

            return "Warning.General";
        }

        // Default logger names by level
        return level switch
        {
            "TRACE" => "Trace.General",
            "DEBUG" => "Debug.General",
            "INFO" => "Info.General",
            _ => null // No logger name for unrecognized patterns
        };
    }

    /// <summary>
    ///     Builds metadata JSON with common properties extracted from log context
    /// </summary>
    private static string? BuildMetadataJson(byte[]? attachmentData, string? attachmentName, string? attachmentMimeType,
        string level)
    {
        var metadata = new Dictionary<string, object>
        {
            // Always include level in metadata for analytics
            ["level"] = level
        };

        // Include attachment info if present
        if (attachmentData != null)
        {
            metadata["attachmentName"] = attachmentName ?? "unknown";
            metadata["attachmentMimeType"] = attachmentMimeType ?? "application/octet-stream";
            metadata["attachmentDataBase64"] = Convert.ToBase64String(attachmentData);
            metadata["attachmentSize"] = attachmentData.Length;
        }

        // Include timestamp for correlation
        metadata["capturedAt"] = DateTime.UtcNow;

        return metadata.Count > 1
            ? JsonSerializer.Serialize(metadata)
            : null; // Only return if we have more than just level
    }
}

// ========================================
// Request/Response Models
// ========================================

public record CreateLogItemRequest
{
    [JsonPropertyName("itemUuid")] public required string TestItemUuid { get; init; }

    [JsonPropertyName("launchUuid")] public string? LaunchUuid { get; init; }

    [JsonPropertyName("time")] public DateTime Time { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("level")] public string Level { get; init; } = "INFO";

    [JsonPropertyName("message")] public required string Text { get; init; }

    [JsonPropertyName("file")] public LogItemAttach? Attach { get; init; }
}

public record LogItemAttach
{
    [JsonPropertyName("name")] public string Name { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("data")] public string? DataBase64 { get; init; }

    [JsonIgnore]
    public byte[]? Data => string.IsNullOrWhiteSpace(DataBase64) ? null : Convert.FromBase64String(DataBase64);

    [JsonPropertyName("contentType")] public string? MimeType { get; init; }
}

public record LogItemCreatedResponse
{
    public required string Id { get; init; }
}

public record LogItemsCreatedResponse
{
    public required List<LogItemCreatedResponse> Responses { get; init; }
}

public record LogItemResponse
{
    public required string Id { get; init; }
    public required string ItemUuid { get; init; }
    public string? LaunchUuid { get; init; }
    public DateTime Time { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
}
