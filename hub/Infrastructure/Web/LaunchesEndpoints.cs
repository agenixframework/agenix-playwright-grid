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
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using PlaywrightHub.Infrastructure.Services;
using StackExchange.Redis;
using UpdateLaunchRequest = PlaywrightHub.Application.DTOs.UpdateLaunchRequest;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     Endpoints for launches management (listing, creating §data).
/// </summary>
public static class LaunchesEndpoints
{
    public static void MapLaunchesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/launches");

        // GET /api/launches?projectKey={projectKey}&filter={latest|all}
        group.MapGet("/", GetLaunches);

        // POST /api/launches
        group.MapPost("/", CreateLaunch);

        // GET /api/launches/{id}
        group.MapGet("/{id:guid}", GetLaunchById);

        // GET /api/launches/by-number/{projectKey}/{number} - numeric URL resolution
        group.MapGet("/by-number/{projectKey}/{number:int}", GetLaunchByNumber);

        // GET /api/launches/by-db-id/{projectKey}/{dbId} - Lookup by globally unique db_id
        group.MapGet("/by-db-id/{projectKey}/{dbId:long}", GetLaunchByDbId);

        // GET /api/launches/{id}/runs
        group.MapGet("/{id:guid}/runs", GetLaunchRuns);

        // GET /api/launches/{id}/suites
        group.MapGet("/{id:guid}/suites", GetLaunchSuites);

        // GET /api/launches/{id}/test-items - test item hierarchy
        group.MapGet("/{id:guid}/test-items", GetLaunchTestItems);

        // GET /api/launches/{id}/unique-errors - grouped error patterns
        group.MapGet("/{id:guid}/unique-errors", GetLaunchUniqueErrors);

        // PUT /api/launches/{id}
        group.MapPut("/{id:guid}", UpdateLaunch);

        // PUT /api/launches/{id}/finish
        group.MapPut("/{id:guid}/finish", FinishLaunch);

        // DELETE /api/launches/{id}
        group.MapDelete("/{id:guid}", DeleteLaunch);

        // POST /api/launches/{id}/force-finish
        group.MapPost("/{id:guid}/force-finish", ForceFinishLaunch);

        // POST /api/launches/bulk-update
        group.MapPost("/bulk-update", BulkUpdateLaunches);

        // POST /api/launches/compare
        group.MapPost("/compare", CompareLaunches);

        // GET /api/launches/{id}/parent-items-history - History matrix (launch-level)
        group.MapGet("/{id:guid}/parent-items-history", GetLaunchParentItemsHistory);
    }


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static async Task<IResult> GetLaunches(
        [FromQuery] string? projectKey,
        [FromHeader(Name = "X-Project-Key")] string? projectKeyHeader,
        [FromQuery] string? filter,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunches");

        // Accept a project key from either query param or header (header takes precedence)
        var effectiveProjectKey = projectKeyHeader ?? projectKey;

        if (string.IsNullOrWhiteSpace(effectiveProjectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["projectKey"] = ["projectKey is required (either as query parameter or X-Project-Key header)"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(effectiveProjectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        var query = filter?.ToUpperInvariant() == "LATEST"
            ? @"
                WITH ranked_launches AS (
                    SELECT *,
                           ROW_NUMBER() OVER (PARTITION BY name ORDER BY start_time DESC) as rn
                    FROM launches
                    WHERE project_key = $1
                )
                SELECT * FROM ranked_launches WHERE rn = 1
                ORDER BY start_time DESC"
            : @"
                SELECT *
                FROM launches
                WHERE project_key = $1
                ORDER BY start_time DESC";

        await using var cmd = db.CreateCommand(query);
        cmd.Parameters.AddWithValue(effectiveProjectKey);

        var launches = new List<LaunchDto>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var dto = MapLaunchFromReader(reader);
            launches.Add(dto);
        }

        return Results.Ok(launches);
    }

    private static async Task<IResult> GetLaunchById(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchById");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        const string query = @"
            SELECT *
            FROM launches
            WHERE id = $1";

        await using var cmd = db.CreateCommand(query);
        cmd.Parameters.AddWithValue(id);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchNotFound,
                "launchId={LaunchId}", id);

            return ProblemDetailsHelpers.NotFound(
                $"Launch {id} not found",
                eventCode: EventCodes.Launch.LaunchNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var dto = MapLaunchFromReader(reader);
        // Return LaunchDto with all fields including Status, ComputedStatus, test aggregations
        // Dashboard needs these fields to calculate display status correctly
        return Results.Ok(dto);
    }

    /// <summary>
    ///     Resolves launch number to UUID (numeric URLs).
    ///     Returns launch details with UUID that can be used for later navigation.
    /// </summary>
    private static async Task<IResult> GetLaunchByNumber(
        [FromRoute] string projectKey,
        [FromRoute] int number,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchByNumber");

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["projectKey"] = ["projectKey is required"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // Lookup launch by project_key and launch_number (sequential ID shown in UI)
        // Note: launch_number is per launch name, not globally unique
        const string query = @"
            SELECT *
            FROM launches
            WHERE project_key = $1 AND launch_number = $2";

        await using var cmd = db.CreateCommand(query);
        cmd.Parameters.AddWithValue(projectKey);
        cmd.Parameters.AddWithValue(number);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchNotFound,
                "projectKey={ProjectKey} launchNumber={LaunchNumber}", projectKey, number);

            return ProblemDetailsHelpers.NotFound(
                $"Launch #{number} not found in project {projectKey}",
                eventCode: EventCodes.Launch.LaunchNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var dto = MapLaunchFromReader(reader);
        // Return LaunchDto with all fields
        return Results.Ok(dto);
    }

    /// <summary>
    ///     Resolves db_id to launch UUID.
    ///     db_id is a globally unique sequential ID per project for unambiguous URLs.
    /// </summary>
    private static async Task<IResult> GetLaunchByDbId(
        [FromRoute] string projectKey,
        [FromRoute] long dbId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchByDbId");

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["projectKey"] = ["projectKey is required"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // Lookup launch by project_key and db_id (globally unique sequential ID)
        const string query = @"
            SELECT *
            FROM launches
            WHERE project_key = $1 AND db_id = $2";

        await using var cmd = db.CreateCommand(query);
        cmd.Parameters.AddWithValue(projectKey);
        cmd.Parameters.AddWithValue(dbId);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchNotFound,
                "projectKey={ProjectKey} dbId={DbId}", projectKey, dbId);

            return ProblemDetailsHelpers.NotFound(
                $"Launch with dbId {dbId} not found in project {projectKey}",
                eventCode: EventCodes.Launch.LaunchNotFound,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var dto = MapLaunchFromReader(reader);
        // Return LaunchDto with all fields
        return Results.Ok(dto);
    }

    private static async Task<IResult> GetLaunchRuns(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchRuns");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // First, verify the launch exists
        const string launchQuery = "SELECT id FROM launches WHERE id = $1";
        await using (var launchCmd = db.CreateCommand(launchQuery))
        {
            launchCmd.Parameters.AddWithValue(id);
            var launchExists = await launchCmd.ExecuteScalarAsync();
            if (launchExists == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Launch.LaunchNotFound,
                    "launchId={LaunchId}", id);

                return ProblemDetailsHelpers.NotFound(
                    $"Launch {id} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        }

        // Get all runs for this launch from the runs table
        const string runsQuery = @"
            SELECT
                run_id, run_json
            FROM test_items
            WHERE launch_id = $1
            ORDER BY start_time DESC";

        var runs = new List<ResultRunSummaryDto>();

        await using var cmd = db.CreateCommand(runsQuery);
        cmd.Parameters.AddWithValue(id);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var runJson = reader.GetString(1);

            // Parse the run JSON to extract the data
            var runData = JsonSerializer.Deserialize<ResultRunSummaryDto>(runJson, JsonOptions);
            if (runData != null)
            {
                runs.Add(runData);
            }
        }

        return Results.Ok(runs);
    }

    private static async Task<IResult> GetLaunchSuites(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchSuites");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // First verify the launch exists
        const string launchQuery = "SELECT id FROM launches WHERE id = $1";
        await using (var launchCmd = db.CreateCommand(launchQuery))
        {
            launchCmd.Parameters.AddWithValue(id);
            var launchExists = await launchCmd.ExecuteScalarAsync();
            if (launchExists == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Launch.LaunchNotFound,
                    "launchId={LaunchId}", id);

                return ProblemDetailsHelpers.NotFound(
                    $"Launch {id} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        }

        // Get all test items of type Suite for this launch
        const string suitesQuery = @"
            SELECT
                run_id as id,
                name,
                start_time as started_at_utc,
                finish_time as completed_at_utc,
                session_status,
                computed_status,
                total_tests,
                passed_tests,
                failed_tests,
                skipped_tests,
                timedout_tests
            FROM test_items
            WHERE launch_id = $1
              AND item_type = 'Suite'
            ORDER BY start_time DESC";

        var suites = new List<object>();

        await using var cmd = db.CreateCommand(suitesQuery);
        cmd.Parameters.AddWithValue(id);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var suite = new
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                StartedAtUtc = reader.GetDateTime(2),
                CompletedAtUtc = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                SessionStatus = reader.IsDBNull(4) ? null : reader.GetString(4),
                ComputedStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
                TotalTests = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                PassedTests = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                FailedTests = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                SkippedTests = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                TimedoutTests = reader.IsDBNull(10) ? 0 : reader.GetInt32(10)
            };
            suites.Add(suite);
        }

        return Results.Ok(suites);
    }

    /// <summary>
    ///     Gets all test items for a launch using the hierarchical model.
    ///     Returns test items from the test_items table with their db_id for numeric URLs.
    /// </summary>
    private static async Task<IResult> GetLaunchTestItems(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] IResultsStore store,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchTestItems");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // Use IResultsStore to get test items for launch
        var testItems = await store.GetTestItemsForLaunchAsync(id);

        return Results.Ok(testItems);
    }

    /// <summary>
    ///     Gets unique error patterns for a launch with grouped occurrences.
    ///     Uses error_fingerprint from log_tokens to group similar errors.
    /// </summary>
    private static async Task<IResult> GetLaunchUniqueErrors(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchUniqueErrors");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        await using var conn = await db.OpenConnectionAsync();

        const string sql = @"
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
            WHERE li.launch_uuid = $1
              AND li.level IN ('ERROR', 'FATAL')
              AND lt.error_fingerprint IS NOT NULL
            GROUP BY lt.error_fingerprint, lt.message
            ORDER BY failed_test_count DESC, occurrence_count DESC
            LIMIT 100";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(id);

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

    private static async Task<IResult> CreateLaunch(
        HttpRequest req,
        [FromBody] StartLaunchRequest request,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] IApiKeyAuthenticationService authService,
        [FromServices] IHubContext<LaunchesHub, ILaunchesClient> hubContext,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(nameof(LaunchesEndpoints));
        var chunkedLogger = new ChunkedLogger(logger, nameof(LaunchesEndpoints));

        // Log milestone at operation start
        chunkedLogger.LogMilestone(
            EventCodes.Launch.LaunchCreated, // LCH01
            "name={Name} projectKey={ProjectKey}",
            request.Name ?? string.Empty, projectKey ?? string.Empty);

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "validationError=ProjectKeyRequired");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["X-Project-Key"] = ["X-Project-Key header is required"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: req.Path,
                traceId: req.HttpContext.TraceIdentifier);
        }

        // Authorize API key - all project members allowed
        var authResult = await req.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchFailed,
                "error=AuthorizationFailed");
            return authResult; // 401/403/404
        }

        var userId = req.HttpContext.Items["AuthUserId"] as string;
        var username = req.HttpContext.Items["AuthUsername"] as string;

        // Calculate the launch number for this name
        chunkedLogger.LogMilestone(
            "LCH06", // LaunchNumberCalculationStarted
            "name={Name}",
            request.Name ?? string.Empty);

        const string countQuery = @"
            SELECT COALESCE(MAX(launch_number), 0) + 1
            FROM launches
            WHERE project_key = $1 AND name = $2";

        int launchNumber;
        await using (var countCmd = db.CreateCommand(countQuery))
        {
            countCmd.Parameters.AddWithValue(projectKey ?? string.Empty);
            countCmd.Parameters.AddWithValue(request.Name ?? string.Empty);
            launchNumber = (int)(await countCmd.ExecuteScalarAsync() ?? 1);
        }

        chunkedLogger.LogMilestone(
            "LCH07", // LaunchNumberCalculated
            "launchNumber={LaunchNumber}",
            launchNumber);

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Store in database
        chunkedLogger.LogMilestone(
            "LCH08", // LaunchPersistStarted
            "launchId={LaunchId}",
            id);

        const string insertQuery = @"
            INSERT INTO launches (
                id, name, description, attributes, owner_api_key, owner_username, project_key,
                start_time, finish_time, launch_number, total_test_runs,
                finished_test_runs, running_test_runs, stopped_test_runs, errored_test_runs, status
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16
            )";

        await using var cmd = db.CreateCommand(insertQuery);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(request.Name ?? string.Empty);
        cmd.Parameters.AddWithValue(request.Description ?? (object)DBNull.Value);
        // Convert ItemAttribute[] to string[] (key:value format)
        var attributeStrings = request.Attributes?.Select(a =>
            string.IsNullOrEmpty(a.Key) ? a.Value : $"{a.Key}:{a.Value}").ToArray() ?? Array.Empty<string>();
        cmd.Parameters.AddWithValue(attributeStrings);
        cmd.Parameters.AddWithValue(userId ?? (object)DBNull.Value); // owner_api_key now stores userId
        cmd.Parameters.AddWithValue(username ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(projectKey ?? string.Empty);
        cmd.Parameters.AddWithValue(now);
        cmd.Parameters.AddWithValue(DBNull.Value); // finish_time
        cmd.Parameters.AddWithValue(launchNumber);
        cmd.Parameters.AddWithValue(0); // total_test_runs
        cmd.Parameters.AddWithValue(0); // finished_test_runs
        cmd.Parameters.AddWithValue(0); // running_test_runs
        cmd.Parameters.AddWithValue(0); // stopped_test_runs
        cmd.Parameters.AddWithValue(0); // errored_test_runs
        cmd.Parameters.AddWithValue("InProgress"); // status

        await cmd.ExecuteNonQueryAsync();

        chunkedLogger.LogMilestone(
            EventCodes.Launch.LaunchStarted, // LCH02
            "launchId={LaunchId} launchNumber={LaunchNumber}",
            id, launchNumber);

        // Notify connected clients about the new launch
        try
        {
            await hubContext.Clients.Group($"project:{projectKey ?? string.Empty}").LaunchUpdated(projectKey ?? string.Empty, id);
            chunkedLogger.LogMilestone(
                "LCH09", // LaunchNotificationSent
                "channel=project:{ProjectKey}",
                projectKey ?? string.Empty);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogWarning(
                "LCH10", // LaunchNotificationFailed
                "error={Error} (non-critical)",
                ex.Message);
        }

        var response = new LaunchCreatedResponse { Uuid = id.ToString(), Number = launchNumber };

        return Results.Created($"/api/launches/{id}", response);
    }

    private static async Task<IResult> UpdateLaunch(
        [FromRoute] Guid id,
        [FromBody] UpdateLaunchRequest request,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        [FromServices] ICacheInvalidationOutbox outbox,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.UpdateLaunch");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // Use transaction for atomic update
        await using var conn = await db.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Check if launch exists with row-level lock
            const string checkQuery = "SELECT status FROM launches WHERE id = $1 FOR UPDATE";
            string? currentStatus;
            await using (var checkCmd = new NpgsqlCommand(checkQuery, conn, transaction))
            {
                checkCmd.Parameters.AddWithValue(id);
                currentStatus = await checkCmd.ExecuteScalarAsync() as string;
            }

            if (currentStatus == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Launch.LaunchNotFound,
                    "launchId={LaunchId}", id);

                return ProblemDetailsHelpers.NotFound(
                    $"Launch {id} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var terminalStates = new[] { "Finished", "Stopped", "Failed" };
            var isTerminal = terminalStates.Contains(currentStatus);

            // Build update query dynamically based on what fields are provided
            var updates = new List<string>();
            var paramIndex = 2;

            // Metadata/documentation fields - allowed even for terminal launches
            if (request.Name != null)
            {
                updates.Add($"name = ${paramIndex++}");
            }

            if (request.Description != null)
            {
                updates.Add($"description = ${paramIndex++}");
            }

            if (request.Attributes != null)
            {
                updates.Add($"attributes = ${paramIndex++}");
            }

            if (request.IsImportant.HasValue)
            {
                updates.Add($"is_important = ${paramIndex++}");
            }

            if (request.RetentionOverrideDays.HasValue)
            {
                updates.Add($"retention_override_days = ${paramIndex++}");
            }

            // Status changes - NOT allowed for terminal launches (would change execution state)
            if (request.Status != null)
            {
                if (isTerminal)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Launch.LaunchAlreadyFinished,
                        "error=AlreadyTerminal currentStatus={CurrentStatus} launchId={LaunchId}", currentStatus, id);

                    return ProblemDetailsHelpers.Conflict(
                        $"Cannot change status - launch is already in terminal state '{currentStatus}'.",
                        eventCode: EventCodes.Launch.LaunchAlreadyFinished,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }
                updates.Add($"status = ${paramIndex++}");
            }

            if (updates.Count == 0)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    "error=NoFieldsToUpdate");

                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["At least one field must be provided for update"]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var updateQuery = $"UPDATE launches SET {string.Join(", ", updates)} WHERE id = $1";

            await using var cmd = new NpgsqlCommand(updateQuery, conn, transaction);
            cmd.Parameters.AddWithValue(id);

            if (request.Name != null)
            {
                cmd.Parameters.AddWithValue(request.Name);
            }

            if (request.Description != null)
            {
                cmd.Parameters.AddWithValue(request.Description);
            }

            if (request.Attributes != null)
            {
                cmd.Parameters.AddWithValue(request.Attributes);
            }

            if (request.IsImportant.HasValue)
            {
                cmd.Parameters.AddWithValue(request.IsImportant.Value);
            }

            if (request.RetentionOverrideDays.HasValue)
            {
                cmd.Parameters.AddWithValue(request.RetentionOverrideDays.Value);
            }

            if (request.Status != null)
            {
                cmd.Parameters.AddWithValue(request.Status);
            }

            await cmd.ExecuteNonQueryAsync();

            // Invalidate cache if status was updated to prevent race conditions via outbox (atomic with transaction)
            if (request.Status != null)
            {
                await outbox.AddAsync($"launch:status:{id}", conn, transaction);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var response = new MessageResponse { Info = "Launch updated successfully" };

        return Results.Ok(response);
    }

    /// <summary>
    ///     Bulk update multiple launches in a single operation.
    ///     Much more efficient than updating launches one by one.
    /// </summary>
    private static async Task<IResult> BulkUpdateLaunches(
        [FromBody] BulkUpdateLaunchesRequest request,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        [FromServices] ICacheInvalidationOutbox outbox,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.BulkUpdateLaunches");

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

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            return authResult; // 401/403/404
        }

        // Validate request
        if (request.LaunchIds == null || request.LaunchIds.Length == 0)
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=NoLaunchIds");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["LaunchIds"] = ["LaunchIds array is required and cannot be empty"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (request.LaunchIds.Length > 10000)
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=TooManyLaunchIds count={Count}", request.LaunchIds.Length);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["LaunchIds"] = ["Cannot update more than 10,000 launches at once"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Build update query dynamically based on what fields are provided
        var updates = new List<string>();
        var paramIndex = 2; // $1 is for launch IDs array

        if (request.Updates.Name != null)
        {
            updates.Add($"name = ${paramIndex++}");
        }

        if (request.Updates.Description != null)
        {
            updates.Add($"description = ${paramIndex++}");
        }

        if (request.Updates.Attributes != null)
        {
            updates.Add($"attributes = ${paramIndex++}");
        }

        if (request.Updates.IsImportant.HasValue)
        {
            updates.Add($"is_important = ${paramIndex++}");
        }

        if (request.Updates.RetentionOverrideDays.HasValue)
        {
            updates.Add($"retention_override_days = ${paramIndex++}");
        }

        if (request.Updates.Status != null)
        {
            updates.Add($"status = ${paramIndex++}");
        }

        if (updates.Count == 0)
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=NoFieldsToUpdate");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["updates"] = ["At least one field must be provided for update"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var updateQuery = $@"
            UPDATE launches
            SET {string.Join(", ", updates)}
            WHERE id = ANY($1::uuid[])
              AND project_key = ${paramIndex}
            RETURNING id";

        // Use transaction for atomic bulk update operation
        await using var conn = await db.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using var cmd = new NpgsqlCommand(updateQuery, conn, transaction);
            cmd.Parameters.AddWithValue(request.LaunchIds);

            // Add parameters in the same order as the updates list
            if (request.Updates.Name != null)
            {
                cmd.Parameters.AddWithValue(request.Updates.Name);
            }

            if (request.Updates.Description != null)
            {
                cmd.Parameters.AddWithValue(request.Updates.Description);
            }

            if (request.Updates.Attributes != null)
            {
                cmd.Parameters.AddWithValue(request.Updates.Attributes);
            }

            if (request.Updates.IsImportant.HasValue)
            {
                cmd.Parameters.AddWithValue(request.Updates.IsImportant.Value);
            }

            if (request.Updates.RetentionOverrideDays.HasValue)
            {
                cmd.Parameters.AddWithValue(request.Updates.RetentionOverrideDays.Value);
            }

            if (request.Updates.Status != null)
            {
                cmd.Parameters.AddWithValue(request.Updates.Status);
            }

            cmd.Parameters.AddWithValue(projectKey);

            // Execute and count updated rows
            var updatedIds = new List<Guid>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    updatedIds.Add(reader.GetGuid(0));
                }
            }

            // Invalidate cache for all updated launches if status changed (atomic with transaction)
            if (request.Updates.Status != null)
            {
                foreach (var updatedId in updatedIds)
                {
                    await outbox.AddAsync($"launch:status:{updatedId}", conn, transaction);
                }
            }

            await transaction.CommitAsync();

            var successCount = updatedIds.Count;
            var failureCount = request.LaunchIds.Length - successCount;

            logger.LogInformation(
                "Bulk updated {SuccessCount}/{TotalCount} launches in project {ProjectKey}",
                successCount, request.LaunchIds.Length, projectKey);

            var response = new BulkUpdateLaunchesResponse
            {
                SuccessCount = successCount,
                FailureCount = failureCount,
                TotalRequested = request.LaunchIds.Length,
                Errors = failureCount > 0
                    ? [$"{failureCount} launches not found or not accessible"]
                    : null
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error bulk updating launches in project {ProjectKey}", projectKey);
            return ProblemDetailsHelpers.InternalServerError(
                "An error occurred while updating launches",
                eventCode: EventCodes.Launch.LaunchOperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> FinishLaunch(
        [FromRoute] Guid id,
        [FromBody] FinishLaunchRequest request,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IApiKeyAuthenticationService authService,
        [FromServices] ICacheInvalidationOutbox outbox,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.FinishLaunch");

        chunkedLogger.LogMilestone(
            EventCodes.Launch.FinishLaunchStarted, // LCH13
            "launchId={LaunchId}",
            id);

        // Validation checks
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=ProjectKeyRequired launchId={LaunchId}",
                id);
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["X-Project-Key"] = ["X-Project-Key header is required"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        chunkedLogger.LogMilestone(
            EventCodes.Launch.AuthorizationStarted, // LCH14
            "launchId={LaunchId}",
            id);

        // Authorize API key - all project members allowed
        var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
        if (authResult != null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchFailed,
                "error=AuthorizationFailed launchId={LaunchId}",
                id);
            return authResult; // 401/403/404
        }

        // Use transaction for atomic finish operation
        await using var conn = await db.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Check if launch exists and validate it's not already in the terminal state
            chunkedLogger.LogMilestone(
                EventCodes.Launch.TerminalStateCheckStarted, // LCH15
                "launchId={LaunchId}",
                id);

            const string checkQuery = "SELECT status FROM launches WHERE id = $1 FOR UPDATE";
            string? currentStatus;
            await using (var checkCmd = new NpgsqlCommand(checkQuery, conn, transaction))
            {
                checkCmd.Parameters.AddWithValue(id);
                currentStatus = await checkCmd.ExecuteScalarAsync() as string;
            }

            if (currentStatus == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Launch.LaunchNotFound,
                    "error=LaunchNotFound launchId={LaunchId}",
                    id);
                return ProblemDetailsHelpers.NotFound(
                    $"Launch {id} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var terminalStates = new[] { "Finished", "Stopped", "Failed" };
            var isTerminal = terminalStates.Contains(currentStatus);

            if (isTerminal)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Launch.LaunchAlreadyFinished,
                    "error=AlreadyInTerminalState launchId={LaunchId} status={Status}",
                    id, currentStatus);

                return ProblemDetailsHelpers.Conflict(
                    $"Launch is already in terminal state '{currentStatus}'",
                    eventCode: EventCodes.Launch.LaunchAlreadyFinished,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var finishTime = request.EndTime;

            // Auto-calculate status from test aggregations
            chunkedLogger.LogMilestone(
                EventCodes.Launch.StatusCalculationStarted, // LCH16
                "launchId={LaunchId} finishTime={FinishTime}",
                id, finishTime);

            string status;

            // Read test aggregations to calculate status
            const string aggregationQuery = @"
                SELECT total_tests, passed_tests, failed_tests, skipped_tests, timedout_tests
                FROM launches
                WHERE id = $1";

            await using (var aggCmd = new NpgsqlCommand(aggregationQuery, conn, transaction))
            {
                aggCmd.Parameters.AddWithValue(id);
                await using var reader = await aggCmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var totalTests = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    var passedTests = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    var failedTests = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    var skippedTests = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                    var timeoutTests = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

                    // Use calculator to determine status
                    var computedStatus = TestResultStatusCalculator.CalculateStatusFromDbColumns(
                        totalTests,
                        passedTests,
                        failedTests,
                        skippedTests,
                        timeoutTests,
                        finishTime); // no legacy status override

                    // Map computed status to allowed launch status values (InProgress, Finished, Stopped, Failed)
                    status = MapToLaunchStatus(computedStatus ?? "Finished");

                    chunkedLogger.LogMilestone(
                        EventCodes.Launch.StatusCalculated, // LCH10
                        "launchId={LaunchId} status={Status} totalTests={TotalTests} failedTests={FailedTests}",
                        id, status, totalTests, failedTests);
                }
                else
                {
                    status = "Finished"; // fallback
                    chunkedLogger.LogWarning(
                        EventCodes.Launch.StatusCalculationFallback, // LCH17
                        "launchId={LaunchId} - no data found, using Finished",
                        id);
                }
            }

            // Update launch
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchUpdateStarted, // LCH18
                "launchId={LaunchId} status={Status}",
                id, status);

            const string updateQuery = @"
                UPDATE launches
                SET finish_time = $2, status = $3
                WHERE id = $1";

            await using var cmd = new NpgsqlCommand(updateQuery, conn, transaction);
            cmd.Parameters.AddWithValue(id);
            cmd.Parameters.AddWithValue(finishTime);
            cmd.Parameters.AddWithValue(status);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();

            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchUpdated, // LCH19
                "launchId={LaunchId} rowsAffected={RowsAffected}",
                id, rowsAffected);

            // Invalidate cache after status update via outbox (atomic with transaction)
            await outbox.AddAsync($"launch:status:{id}", conn, transaction);

            // Commit the transaction
            await transaction.CommitAsync();

            // Operation complete
            chunkedLogger.LogMilestone(
                EventCodes.Launch.LaunchFinished, // LCH03
                "launchId={LaunchId} status={Status}",
                id, status);

            var response = new MessageResponse { Info = $"Launch finished successfully with status: {status}" };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error finishing launch {LaunchId}", id);
            return ProblemDetailsHelpers.InternalServerError(
                $"An error occurred while finishing launch {id}",
                eventCode: EventCodes.Launch.LaunchOperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> DeleteLaunch(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Project-Key")] string? projectKey,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IApiKeyAuthenticationService authService,
        [FromServices] ICacheInvalidationOutbox outbox,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.DeleteLaunch");
        using var operation = chunkedLogger.BeginOperation(
            "DeleteLaunch",
            new Dictionary<string, object>
            {
                ["LaunchId"] = id,
                ["ProjectKey"] = projectKey ?? "null"
            });

        try
        {
            chunkedLogger.LogMilestone(EventCodes.Launch.DeleteLaunchStarted,
                "Starting delete launch operation for LaunchId={LaunchId}, ProjectKey={ProjectKey}",
                id, projectKey ?? "null");

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

            chunkedLogger.LogMilestone(EventCodes.Launch.DeleteAuthorizationStarted,
                "Authorizing API key for delete operation");

            // Authorize API key - all project members allowed
            var authResult = await httpContext.Request.AuthorizeApiKeyAsync(projectKey, authService);
            if (authResult != null)
            {
                return authResult; // 401/403/404
            }

            chunkedLogger.LogMilestone(EventCodes.Launch.DeleteTransactionStarted,
                "Starting database transaction for atomic delete");

            // Use transaction for atomic delete operation
            await using var conn = await db.OpenConnectionAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // Check if launch exists
                const string checkQuery = "SELECT COUNT(*) FROM launches WHERE id = $1";
                await using var checkCmd = new NpgsqlCommand(checkQuery, conn, transaction);
                checkCmd.Parameters.AddWithValue(id);
                var exists = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L) > 0;

                if (!exists)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Launch.LaunchNotFound,
                        "launchId={LaunchId}", id);

                    return ProblemDetailsHelpers.NotFound(
                        $"Launch {id} not found",
                        eventCode: EventCodes.Launch.LaunchNotFound,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                // Delete the launch (regardless of status)
                const string deleteQuery = "DELETE FROM launches WHERE id = $1";
                await using (var deleteCmd = new NpgsqlCommand(deleteQuery, conn, transaction))
                {
                    deleteCmd.Parameters.AddWithValue(id);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // Invalidate cache via outbox (atomic with transaction)
                await outbox.AddAsync($"launch:status:{id}", conn, transaction);

                chunkedLogger.LogMilestone(EventCodes.Launch.LaunchDeleted,
                    "Launch deleted from database: LaunchId={LaunchId}",
                    id);

                await transaction.CommitAsync();

                chunkedLogger.LogMilestone(EventCodes.Launch.DeleteLaunchCompleted,
                    "Delete launch operation completed successfully");

                return Results.NoContent();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            chunkedLogger.FailOperation(null, ex, ErrorType.DependencyFailure, DependencyName.Database);
            throw;
        }
    }

    private static async Task<IResult> ForceFinishLaunch(
        [FromRoute] Guid id,
        [FromBody] ForceFinishLaunchRequest? request,
        [FromServices] IResultsStore store,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IHubContext<LaunchesHub, ILaunchesClient> hubContext,
        [FromServices] IBrowserPoolService browserPool,
        [FromServices] ICacheInvalidationOutbox outbox,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var projectKey = httpContext.Request.Headers["X-Project-Key"].ToString();
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.ForceFinishLaunch");

        if (string.IsNullOrEmpty(projectKey))
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

        var reason = request?.Reason;

        using var operation = chunkedLogger.BeginOperation(
            "ForceFinishLaunch",
            new Dictionary<string, object>
            {
                ["LaunchId"] = id,
                ["ProjectKey"] = projectKey,
                ["Reason"] = reason ?? "null"
            });

        try
        {
            chunkedLogger.LogMilestone(EventCodes.Launch.ForceFinishStarted,
                "Starting force finish launch operation for LaunchId={LaunchId}, ProjectKey={ProjectKey}, Reason={Reason}",
                id, projectKey, reason ?? "null");

            var now = DateTimeOffset.UtcNow;
            var stoppedCount = 0;
            var browsersReleased = 0;
            var activeTestItems =
                new List<(Guid runId, string sessionStatus, string? browserId, string? workerNodeId)>();

            // Use transaction for atomic force finish operation
            await using var conn = await db.OpenConnectionAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // 1. Get launch and verify it's in progress
                string? launchStatus = null;
                await using (var checkCmd = new NpgsqlCommand(
                                 "SELECT status FROM launches WHERE id = $1 FOR UPDATE", conn, transaction))
                {
                    checkCmd.Parameters.AddWithValue(id);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result == null)
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.Launch.LaunchNotFound,
                            "launchId={LaunchId}", id);

                        return ProblemDetailsHelpers.NotFound(
                            $"Launch {id} not found",
                            eventCode: EventCodes.Launch.LaunchNotFound,
                            instance: httpContext.Request.Path,
                            traceId: httpContext.TraceIdentifier);
                    }

                    launchStatus = result.ToString();
                }

                chunkedLogger.LogMilestone(EventCodes.Launch.ForceFinishLaunchStatusChecked,
                    "Launch status checked: LaunchId={LaunchId}, Status={Status}",
                    id, launchStatus ?? "null");

                if (launchStatus != "InProgress")
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Launch.LaunchAlreadyFinished,
                        "error=NotActive launchId={LaunchId} status={Status}", id, launchStatus);

                    return ProblemDetailsHelpers.Conflict(
                        $"Launch is not active (current status: {launchStatus})",
                        eventCode: EventCodes.Launch.LaunchAlreadyFinished,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                // 2. Find all active test items (those with Running or Queued session status)
                await using var cmd = new NpgsqlCommand(
                    @"SELECT run_id, session_status, browser_id, worker_node_id
                      FROM test_items
                      WHERE launch_id = $1
                        AND session_status IN ('Queued', 'Running')",
                    conn, transaction);
                cmd.Parameters.AddWithValue(id);

                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        activeTestItems.Add((
                            reader.GetGuid(0),
                            reader.GetString(1),
                            reader.IsDBNull(2) ? null : reader.GetString(2),
                            reader.IsDBNull(3) ? null : reader.GetString(3)
                        ));
                    }
                }

                chunkedLogger.LogMilestone(EventCodes.Launch.ActiveTestItemsFound,
                    "Found active test items: Count={Count}",
                    activeTestItems.Count);

                // 3. Force finish each active test item
                foreach (var (runId, sessionStatus, browserId, workerNodeId) in activeTestItems)
                {
                    // Update test item: SessionStatus = "Stopped", ComputedStatus = "Cancelled"
                    await using var updateCmd = new NpgsqlCommand(
                        @"UPDATE test_items
                          SET session_status = 'Stopped',
                              computed_status = 'Cancelled',
                              finish_time = $2,
                              browser_id = NULL,
                              websocket_endpoint = NULL,
                              worker_node_id = NULL
                          WHERE run_id = $1",
                        conn, transaction);
                    updateCmd.Parameters.AddWithValue(runId);
                    updateCmd.Parameters.AddWithValue(now);
                    await updateCmd.ExecuteNonQueryAsync();

                    stoppedCount++;

                    chunkedLogger.LogMilestone(EventCodes.Launch.TestItemStopped,
                        "Test item stopped: RunId={RunId}, SessionStatus={SessionStatus}",
                        runId, sessionStatus);
                }

                // 4. Update launch status to "Stopped"
                await using (var launchCmd = new NpgsqlCommand(
                                 @"UPDATE launches
                      SET status = 'Stopped',
                          finish_time = COALESCE(finish_time, $2)
                      WHERE id = $1",
                                 conn, transaction))
                {
                    launchCmd.Parameters.AddWithValue(id);
                    launchCmd.Parameters.AddWithValue(now);
                    await launchCmd.ExecuteNonQueryAsync();
                }

                chunkedLogger.LogMilestone(EventCodes.Launch.ForceFinishLaunchStatusUpdated,
                    "Launch status updated to Stopped: LaunchId={LaunchId}",
                    id);

                // 5. Recalculate launch aggregations (counts may have changed)
                // Use existing connection and transaction to avoid nested transaction deadlock
                await store.RecalculateLaunchAggregationsAsync(id, conn, transaction);

                chunkedLogger.LogMilestone(EventCodes.Launch.ForceFinishAggregationsRecalculated,
                    "Launch aggregations recalculated: LaunchId={LaunchId}",
                    id);

                // 6. Record audit log
                try
                {
                    // Count browsers we intend to release (they are already cleared in DB)
                    var browsersToRelease = activeTestItems.Count(x =>
                        !string.IsNullOrEmpty(x.browserId) && !string.IsNullOrEmpty(x.workerNodeId));

                    var details = new
                    {
                        launchId = id,
                        testItemsStopped = stoppedCount,
                        browsersReleased = browsersToRelease,
                        previousStatus = launchStatus,
                        newStatus = "Stopped",
                        reason
                    };

                    await using var auditCmd = new NpgsqlCommand(
                        @"INSERT INTO audit_entries (timestamp, category, action, actor, remote_ip, severity, details)
                          VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb)",
                        conn, transaction);
                    auditCmd.Parameters.AddWithValue(now);
                    auditCmd.Parameters.AddWithValue("Launch");
                    auditCmd.Parameters.AddWithValue("ForceFinish");
                    auditCmd.Parameters.AddWithValue((object?)httpContext.User.Identity?.Name ?? "System");
                    auditCmd.Parameters.AddWithValue((object?)httpContext.Connection.RemoteIpAddress?.ToString() ??
                                                     DBNull.Value);
                    auditCmd.Parameters.AddWithValue("Info");
                    auditCmd.Parameters.AddWithValue(JsonSerializer.Serialize(details));
                    await auditCmd.ExecuteNonQueryAsync();

                    chunkedLogger.LogMilestone(EventCodes.Launch.ForceFinishAuditLogged,
                        "Audit log recorded: LaunchId={LaunchId}, TestItemsStopped={StoppedCount}, BrowsersReleased={BrowsersReleased}",
                        id, stoppedCount, browsersToRelease);
                }
                catch (Exception ex)
                {
                    // Audit logging failure shouldn't block the operation
                    chunkedLogger.LogWarning(EventCodes.Launch.ForceFinishAuditLogged,
                        "Failed to record audit log: {Message}",
                        ex.Message);
                }

                // Invalidate cache via outbox (atomic with transaction)
                await outbox.AddAsync($"launch:status:{id}", conn, transaction);

                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }

            // 7. External operations after successful commit
            // We perform these after commit to ensure DB consistency even if external calls fail
            // or if we need to roll back the DB transaction.
            foreach (var (runId, sessionStatus, browserId, workerNodeId) in activeTestItems)
            {
                // Release browser if borrowed
                if (!string.IsNullOrEmpty(browserId) && !string.IsNullOrEmpty(workerNodeId))
                {
                    try
                    {
                        // Use BrowserPoolService to properly return browser (no HTTP calls)
                        await browserPool.ReturnBrowserAsync(
                            browserId,
                            workerNodeId,
                            "Cancelled");
                        browsersReleased++;

                        chunkedLogger.LogMilestone(EventCodes.Launch.BrowserReleased,
                            "Browser released: BrowserId={BrowserId}, WorkerNodeId={WorkerNodeId}",
                            browserId, workerNodeId);
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogWarning(EventCodes.Launch.BrowserReleased,
                            "Error releasing browser {BrowserId}: {Message}",
                            browserId, ex.Message);
                        // Continue anyway - browser sweeper will clean up
                    }
                }

                // Notify via SignalR (optional)
                try
                {
                    await hubContext.Clients.Group($"test-item:{runId}")
                        .TestItemStatusChanged(runId, "Stopped", "Cancelled");
                }
                catch
                {
                    /* SignalR notification failure shouldn't block operation */
                }
            }

            // 9. Notify launch update via SignalR (after commit)
            try
            {
                await hubContext.Clients.Group($"launch:{id}")
                    .LaunchUpdated(projectKey, id);
            }
            catch
            {
                /* SignalR notification failure shouldn't block operation */
            }

            chunkedLogger.LogMilestone(EventCodes.Launch.ForceFinishCompleted,
                "Force finish launch operation completed: LaunchId={LaunchId}, TestItemsStopped={StoppedCount}, BrowsersReleased={BrowsersReleased}",
                id, stoppedCount, browsersReleased);

            return Results.Ok(new
            {
                message = "Launch force finished successfully",
                launchId = id,
                testItemsStopped = stoppedCount,
                browsersReleased,
                newLaunchStatus = "Stopped",
                reason
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.FailOperation(null, ex, ErrorType.DependencyFailure, DependencyName.Database);
            return ProblemDetailsHelpers.InternalServerError(
                $"An error occurred while force finishing launch {id}",
                eventCode: EventCodes.Launch.LaunchOperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static LaunchDto MapLaunchFromReader(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var name = reader.GetString(reader.GetOrdinal("name"));
        var descriptionOrdinal = reader.GetOrdinal("description");
        var description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal);
        var attributes = (string[])reader.GetValue(reader.GetOrdinal("attributes"));
        var ownerApiKey = reader.GetString(reader.GetOrdinal("owner_api_key"));
        var ownerUsernameOrdinal = reader.GetOrdinal("owner_username");
        var ownerUsername = reader.IsDBNull(ownerUsernameOrdinal) ? null : reader.GetString(ownerUsernameOrdinal);
        var projectKey = reader.GetString(reader.GetOrdinal("project_key"));
        var startTime = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("start_time"));
        var finishTimeOrdinal = reader.GetOrdinal("finish_time");
        var finishTime = reader.IsDBNull(finishTimeOrdinal)
            ? null
            : reader.GetFieldValue<DateTimeOffset?>(finishTimeOrdinal);
        var launchNumber = reader.GetInt32(reader.GetOrdinal("launch_number"));

        // Read db_id with a safe fallback for compatibility (column added in V3 migration)
        long dbId = 0;
        try
        {
            var dbIdOrdinal = reader.GetOrdinal("db_id");
            if (!reader.IsDBNull(dbIdOrdinal))
            {
                dbId = reader.GetInt64(dbIdOrdinal);
            }
        }
        catch
        {
            // Column doesn't exist (pre-V3 migration), use default
        }

        var totalTestRuns = reader.GetInt32(reader.GetOrdinal("total_test_runs"));
        var finishedTestRuns = reader.GetInt32(reader.GetOrdinal("finished_test_runs"));
        var runningTestRuns = reader.GetInt32(reader.GetOrdinal("running_test_runs"));
        var stoppedTestRuns = reader.GetInt32(reader.GetOrdinal("stopped_test_runs"));
        var erroredTestRuns = reader.GetInt32(reader.GetOrdinal("errored_test_runs"));

        // Read new fields with safe fallback for compatibility
        var isImportant = false;
        int? retentionOverrideDays = null;
        try
        {
            var isImportantOrdinal = reader.GetOrdinal("is_important");
            isImportant = reader.GetBoolean(isImportantOrdinal);

            var retentionOrdinal = reader.GetOrdinal("retention_override_days");
            if (!reader.IsDBNull(retentionOrdinal))
            {
                retentionOverrideDays = reader.GetInt32(retentionOrdinal);
            }
        }
        catch
        {
            // Column doesn't exist (pre-migration), use defaults
        }

        // Read status with safe fallback for compatibility
        var status = "InProgress";
        try
        {
            var statusOrdinal = reader.GetOrdinal("status");
            if (!reader.IsDBNull(statusOrdinal))
            {
                status = reader.GetString(statusOrdinal);
            }
        }
        catch
        {
            // Column doesn't exist (pre-migration), determine from finish_time
            if (finishTime.HasValue)
            {
                if (erroredTestRuns > 0)
                {
                    status = "Failed";
                }
                else if (stoppedTestRuns > 0)
                {
                    status = "Stopped";
                }
                else
                {
                    status = "Finished";
                }
            }
        }

        // Calculate duration: finish_time is the source of truth
        double? durationSeconds = null;
        if (finishTime.HasValue)
        {
            durationSeconds = (finishTime.Value - startTime).TotalSeconds;
        }

        // Read test result aggregations with safe fallback for compatibility
        var totalTests = 0;
        var passedTests = 0;
        var failedTests = 0;
        var skippedTests = 0;
        var timedoutTests = 0;
        try
        {
            totalTests = reader.GetInt32(reader.GetOrdinal("total_tests"));
            passedTests = reader.GetInt32(reader.GetOrdinal("passed_tests"));
            failedTests = reader.GetInt32(reader.GetOrdinal("failed_tests"));
            skippedTests = reader.GetInt32(reader.GetOrdinal("skipped_tests"));
            timedoutTests = reader.GetInt32(reader.GetOrdinal("timedout_tests"));
        }
        catch
        {
            // Columns don't exist (pre-V19 migration), use defaults (zeros)
        }

        // Calculate computed status based on test results
        var computedStatus = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests,
            passedTests,
            failedTests,
            skippedTests,
            timedoutTests,
            finishTime?.DateTime,
            status);

        return new LaunchDto
        {
            Id = id,
            Name = name,
            Description = description,
            Attributes = attributes,
            OwnerApiKey = ownerApiKey,
            OwnerUsername = ownerUsername,
            ProjectKey = projectKey,
            StartTime = startTime,
            FinishTime = finishTime,
            LaunchNumber = launchNumber,
            DbId = dbId,
            TotalTestRuns = totalTestRuns,
            FinishedTestRuns = finishedTestRuns,
            RunningTestRuns = runningTestRuns,
            StoppedTestRuns = stoppedTestRuns,
            ErroredTestRuns = erroredTestRuns,
            IsImportant = isImportant,
            RetentionOverrideDays = retentionOverrideDays,
            DurationSeconds = durationSeconds,
            IsRunning = !finishTime.HasValue,
            Status = status,
            TotalTests = totalTests,
            PassedTests = passedTests,
            FailedTests = failedTests,
            SkippedTests = skippedTests,
            TimedoutTests = timedoutTests,
            ComputedStatus = computedStatus
        };
    }

    private static async Task<IResult> CompareLaunches(
        [FromBody] CompareLaunchesRequest request,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.CompareLaunches");

        if (request.LaunchIds == null || request.LaunchIds.Count == 0)
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=NoLaunchIds");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["LaunchIds"] = ["LaunchIds are required"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (request.LaunchIds.Count < 2)
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=InsufficientLaunchIds count={Count}", request.LaunchIds.Count);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["LaunchIds"] = ["At least 2 launches are required for comparison"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (request.LaunchIds.Count > 5)
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=TooManyLaunchIds count={Count}", request.LaunchIds.Count);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["LaunchIds"] = ["Maximum 5 launches can be compared at once"]
                },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var comparisons = new List<LaunchComparisonDto>();

        foreach (var launchId in request.LaunchIds)
        {
            // Get launch metadata
            const string launchQuery = @"
                SELECT *
                FROM launches
                WHERE id = $1";
            LaunchDto? launch;

            await using (var launchCmd = db.CreateCommand(launchQuery))
            {
                launchCmd.Parameters.AddWithValue(launchId);
                await using var reader = await launchCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    continue; // Skip if launch not found
                }

                launch = MapLaunchFromReader(reader);
            }

            // Get test statistics for this launch (test outcomes, not run statuses)
            const string statsQuery = @"
                SELECT
                    COUNT(*) as total,
                    COUNT(*) FILTER (WHERE computed_status = 'Passed') as passed,
                    COUNT(*) FILTER (WHERE computed_status = 'Failed') as failed,
                    COUNT(*) FILTER (WHERE computed_status = 'Skipped') as skipped
                FROM test_items
                WHERE launch_id = $1";

            int totalTests = 0, passedTests = 0, failedTests = 0, skippedTests = 0;

            await using (var statsCmd = db.CreateCommand(statsQuery))
            {
                statsCmd.Parameters.AddWithValue(launchId);
                await using var reader = await statsCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    totalTests = Convert.ToInt32(reader.GetInt64(0));
                    passedTests = Convert.ToInt32(reader.GetInt64(1));
                    failedTests = Convert.ToInt32(reader.GetInt64(2));
                    skippedTests = Convert.ToInt32(reader.GetInt64(3));
                }
            }

            // Calculate percentages
            var passedPct = totalTests > 0 ? passedTests / (double)totalTests * 100 : 0;
            var failedPct = totalTests > 0 ? failedTests / (double)totalTests * 100 : 0;
            var skippedPct = totalTests > 0 ? skippedTests / (double)totalTests * 100 : 0;

            comparisons.Add(new LaunchComparisonDto
            {
                LaunchId = launch.Id,
                LaunchName = launch.Name,
                LaunchNumber = launch.LaunchNumber,
                OwnerUsername = launch.OwnerUsername,
                StartTime = launch.StartTime,
                DurationSeconds = launch.DurationSeconds,
                TotalTests = totalTests,
                PassedTests = passedTests,
                FailedTests = failedTests,
                SkippedTests = skippedTests,
                PassedPercentage = passedPct,
                FailedPercentage = failedPct,
                SkippedPercentage = skippedPct
            });
        }

        // Sort by start time
        comparisons = comparisons.OrderBy(c => c.StartTime).ToList();

        return Results.Ok(comparisons);
    }


    /// <summary>
    ///     Maps internal LaunchDto to public LaunchResponse for Client library compatibility.
    /// </summary>
    private static LaunchResponse MapToLaunchResponse(LaunchDto dto)
    {
        // Convert attributes from string[] to ItemAttribute[]
        var attributes = dto.Attributes?.Select(attr =>
        {
            var parts = attr.Split(':', 2);
            return new ItemAttribute
            {
                Key = parts.Length > 1 ? parts[0] : "",
                Value = parts.Length > 1 ? parts[1] : parts[0],
                IsSystem = false
            };
        }).ToList() ?? [];

        return new LaunchResponse
        {
            Id = Convert.ToInt64(dto.LaunchNumber), // Use launch number as numeric ID
            Uuid = dto.Id.ToString(),
            DbId = dto.DbId, // Globally unique sequential ID per project
            Number = dto.LaunchNumber,
            Name = dto.Name,
            Description = dto.Description ?? "",
            Mode = LaunchMode.Default, // Default mode
            StartTime = dto.StartTime.UtcDateTime,
            EndTime = dto.FinishTime?.UtcDateTime,
            HasRetries = false, // Not tracked in current schema
            Attributes = attributes,
            Statistics = new Statistic
            {
                Executions =
                    new Executions
                    {
                        Total = dto.TotalTests,
                        Passed = dto.PassedTests,
                        Failed = dto.FailedTests,
                        Skipped = dto.SkippedTests
                    },
                Defects = new Defects
                {
                    ProductBugs = new Defect { Total = 0 },
                    AutomationBugs = new Defect { Total = 0 },
                    SystemIssues = new Defect { Total = 0 },
                    ToInvestigate = new Defect { Total = dto.FailedTests }, // Map all failures to investigate
                    NoDefect = new Defect { Total = 0 }
                }
            }
        };
    }

    private static async Task<IResult> GetLaunchParentItemsHistory(
        Guid id,
        [FromQuery] int? depth,
        [FromServices] NpgsqlDataSource db,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchesEndpoints");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchesEndpoints.GetLaunchParentItemsHistory");

        try
        {
            var historyDepth = depth ?? 10;
            if (historyDepth is < 1 or > 50)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    "error=InvalidDepth depth={Depth}", historyDepth);

                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["depth"] = ["Depth must be between 1 and 50"]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Get project key from launch
            await using var launchCmd = db.CreateCommand("SELECT project_key FROM launches WHERE id = $1");
            launchCmd.Parameters.AddWithValue(id);
            var projectKey = await launchCmd.ExecuteScalarAsync() as string;

            if (projectKey == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Launch.LaunchNotFound,
                    "launchId={LaunchId}", id);

                return ProblemDetailsHelpers.NotFound(
                    $"Launch {id} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Call PostgresSQL function
            await using var cmd = db.CreateCommand(
                "SELECT item_name, item_type, launches FROM get_launch_parent_items_history($1, $2)");
            cmd.Parameters.AddWithValue(projectKey);
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
                    Tooltip = l.GetProperty("tooltip").Deserialize<object>()
                }).ToList();

                rows.Add(new { ItemName = itemName, ItemType = itemType, Cells = cells });
            }

            return Results.Ok(new { Columns = columns, Rows = rows });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get launch parent items history");
            return ProblemDetailsHelpers.InternalServerError(
                "Internal error loading history",
                eventCode: EventCodes.Launch.LaunchOperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    /// <summary>
    /// Maps computed status from TestResultStatusCalculator to allowed launch status values.
    /// Database constraint allows: InProgress, Finished, Stopped, Failed
    /// </summary>
    private static string MapToLaunchStatus(string computedStatus)
    {
        return computedStatus switch
        {
            "Passed" => "Finished",      // Test passed -> Launch finished successfully
            "Skipped" => "Finished",     // All tests skipped -> Launch finished
            "Cancelled" => "Stopped",    // Cancelled -> Stopped
            "Interrupted" => "Stopped",  // Interrupted (timeout) -> Stopped
            "Stopped" => "Stopped",      // Already correct
            "Failed" => "Failed",        // Already correct
            "InProgress" => "InProgress", // Already correct
            "Errored" => "Failed",       // Infrastructure error -> Failed
            _ => "Finished"              // Default fallback
        };
    }
}

public sealed record CompareLaunchesRequest
{
    public List<Guid> LaunchIds { get; init; } = [];
}

public sealed record ForceFinishLaunchRequest
{
    public string? Reason { get; init; }
}

public sealed record LaunchComparisonDto
{
    public Guid LaunchId { get; init; }
    public string LaunchName { get; init; } = string.Empty;
    public int LaunchNumber { get; init; }
    public string? OwnerUsername { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public double? DurationSeconds { get; init; }

    public int TotalTests { get; init; }
    public int PassedTests { get; init; }
    public int FailedTests { get; init; }
    public int SkippedTests { get; init; }

    public double PassedPercentage { get; init; }
    public double FailedPercentage { get; init; }
    public double SkippedPercentage { get; init; }
}
