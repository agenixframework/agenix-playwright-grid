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

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     Endpoints for launch filters management (CRUD operations for saved filters and user preferences).
/// </summary>
public static class LaunchFiltersEndpoints
{
    public static void MapLaunchFiltersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/launch-filters");

        // GET /api/launch-filters?projectKey={projectKey}&userId={userId}
        group.MapGet("/", GetFilters);

        // POST /api/launch-filters
        group.MapPost("/", CreateFilter);

        // PUT /api/launch-filters/{id}
        group.MapPut("/{id:guid}", UpdateFilter);

        // DELETE /api/launch-filters/{id}
        group.MapDelete("/{id:guid}", DeleteFilter);

        // GET /api/launch-filters/preference?projectKey={projectKey}&userId={userId}
        group.MapGet("/preference", GetFilterPreference);

        // PUT /api/launch-filters/preference
        group.MapPut("/preference", UpdateFilterPreference);

        // PUT /api/launch-filters/{id}/display
        group.MapPut("/{id:guid}/display", ToggleFilterDisplay);
    }


    private static async Task<IResult> GetFilters(
        [FromQuery] string projectKey,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.GetFilters");

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["projectKey"] = ["projectKey is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId projectKey={ProjectKey}", projectKey);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            // Join with user_filter_display_preferences to get per-user display setting
            // If user has a preference, use it; otherwise fall back to filter's default display_on_launches
            var query = @"
                SELECT
                    lf.id,
                    lf.name,
                    lf.description,
                    lf.project_key,
                    lf.user_id,
                    lf.criteria_json,
                    lf.sort_by,
                    lf.is_shared,
                    COALESCE(ufdp.display_on_launches, lf.display_on_launches) as display_on_launches,
                    lf.created_at,
                    lf.updated_at
                FROM launch_filters lf
                LEFT JOIN user_filter_display_preferences ufdp
                    ON lf.id = ufdp.filter_id AND ufdp.user_id = $2
                WHERE lf.project_key = $1 AND (lf.user_id = $2 OR lf.is_shared = TRUE)
                ORDER BY lf.created_at DESC";

            await using var cmd = db.CreateCommand(query);
            cmd.Parameters.AddWithValue(projectKey);
            cmd.Parameters.AddWithValue(userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            var filters = new List<LaunchFilterDto>();

            while (await reader.ReadAsync())
            {
                var criteriaJson = reader.GetString(5);
                var criteria = JsonSerializer.Deserialize<List<FilterCriterionDto>>(criteriaJson) ??
                               [];

                filters.Add(new LaunchFilterDto
                {
                    Id = reader.GetGuid(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ProjectKey = reader.GetString(3),
                    UserId = reader.GetString(4),
                    Criteria = criteria,
                    SortBy = reader.GetString(6),
                    IsShared = reader.GetBoolean(7),
                    DisplayOnLaunches = reader.GetBoolean(8), // This now includes per-user preference
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(9),
                    UpdatedAt = reader.GetFieldValue<DateTimeOffset>(10)
                });
            }

            return Results.Ok(filters);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "projectKey={ProjectKey} userId={UserId}", projectKey, userId);

            return ProblemDetailsHelpers.InternalServerError(
                "Error retrieving filters.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> CreateFilter(
        [FromBody] SaveLaunchFilterRequest request,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.CreateFilter");

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingName userId={UserId}", userId);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = ["Filter name is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.ProjectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey userId={UserId}", userId);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["projectKey"] = ["Project key is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var criteriaJson = JsonSerializer.Serialize(request.Criteria);

            // Use transaction for atomic insert operation
            await using var conn = await db.OpenConnectionAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                var query = @"
                    INSERT INTO launch_filters (id, name, description, project_key, user_id, criteria_json, sort_by, is_shared, display_on_launches, created_at, updated_at)
                    VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8, $9, $10, $11)
                    RETURNING id, name, description, project_key, user_id, criteria_json, sort_by, is_shared, display_on_launches, created_at, updated_at";

                await using var cmd = new NpgsqlCommand(query, conn, transaction);
                cmd.Parameters.AddWithValue(id);
                cmd.Parameters.AddWithValue(request.Name);
                cmd.Parameters.AddWithValue(request.Description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue(request.ProjectKey);
                cmd.Parameters.AddWithValue(userId);
                cmd.Parameters.AddWithValue(criteriaJson);
                cmd.Parameters.AddWithValue(request.SortBy);
                cmd.Parameters.AddWithValue(request.IsShared);
                cmd.Parameters.AddWithValue(request.DisplayOnLaunches);
                cmd.Parameters.AddWithValue(now);
                cmd.Parameters.AddWithValue(now);

                LaunchFilterDto? filter = null;
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var resultCriteriaJson = reader.GetString(5);
                        var criteria = JsonSerializer.Deserialize<List<FilterCriterionDto>>(resultCriteriaJson) ??
                                       [];

                        filter = new LaunchFilterDto
                        {
                            Id = reader.GetGuid(0),
                            Name = reader.GetString(1),
                            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                            ProjectKey = reader.GetString(3),
                            UserId = reader.GetString(4),
                            Criteria = criteria,
                            SortBy = reader.GetString(6),
                            IsShared = reader.GetBoolean(7),
                            DisplayOnLaunches = reader.GetBoolean(8),
                            CreatedAt = reader.GetFieldValue<DateTimeOffset>(9),
                            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(10)
                        };
                    }
                } // Reader is disposed here

                if (filter != null)
                {
                    await transaction.CommitAsync();
                    return Results.Created($"/api/launch-filters/{filter.Id}", filter);
                }

                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed,
                    "error=InsertReturnedNoResult userId={UserId}", userId);

                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to create filter.",
                    eventCode: EventCodes.Database.OperationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "error=TransactionFailed userId={UserId}", userId);

                throw;
            }
        }
        catch (Exception ex)
        {
            // Already logged if it was a transaction failure
            if (ex is not NpgsqlException)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "userId={UserId}", userId);
            }

            return ProblemDetailsHelpers.InternalServerError(
                "Error creating filter.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> UpdateFilter(
        [FromRoute] Guid id,
        [FromBody] SaveLaunchFilterRequest request,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.UpdateFilter");

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId filterId={Id}", id);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingName filterId={Id} userId={UserId}", id, userId);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = ["Filter name is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var criteriaJson = JsonSerializer.Serialize(request.Criteria);

            // Use transaction for atomic update operation
            await using var conn = await db.OpenConnectionAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // Only allow updating filters that belong to the user (or shared filters if user is admin)
                var query = @"
                    UPDATE launch_filters
                    SET name = $2, description = $3, criteria_json = $4::jsonb, sort_by = $5, is_shared = $6, display_on_launches = $7, updated_at = $8
                    WHERE id = $1 AND user_id = $9
                    RETURNING id, name, description, project_key, user_id, criteria_json, sort_by, is_shared, display_on_launches, created_at, updated_at";

                await using var cmd = new NpgsqlCommand(query, conn, transaction);
                cmd.Parameters.AddWithValue(id);
                cmd.Parameters.AddWithValue(request.Name);
                cmd.Parameters.AddWithValue(request.Description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue(criteriaJson);
                cmd.Parameters.AddWithValue(request.SortBy);
                cmd.Parameters.AddWithValue(request.IsShared);
                cmd.Parameters.AddWithValue(request.DisplayOnLaunches);
                cmd.Parameters.AddWithValue(now);
                cmd.Parameters.AddWithValue(userId);

                LaunchFilterDto? filter = null;
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var resultCriteriaJson = reader.GetString(5);
                        var criteria = JsonSerializer.Deserialize<List<FilterCriterionDto>>(resultCriteriaJson) ??
                                       [];

                        filter = new LaunchFilterDto
                        {
                            Id = reader.GetGuid(0),
                            Name = reader.GetString(1),
                            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                            ProjectKey = reader.GetString(3),
                            UserId = reader.GetString(4),
                            Criteria = criteria,
                            SortBy = reader.GetString(6),
                            IsShared = reader.GetBoolean(7),
                            DisplayOnLaunches = reader.GetBoolean(8),
                            CreatedAt = reader.GetFieldValue<DateTimeOffset>(9),
                            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(10)
                        };
                    }
                } // Reader is disposed here

                if (filter != null)
                {
                    await transaction.CommitAsync();
                    return Results.Ok(filter);
                }

                chunkedLogger.LogMilestone(
                    EventCodes.TestItem.TestItemNotFound,
                    "error=FilterNotFoundOrNoPermission filterId={Id} userId={UserId}", id, userId);

                return ProblemDetailsHelpers.NotFound(
                    "Filter not found or you don't have permission to update it",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "error=TransactionFailed filterId={Id} userId={UserId}", id, userId);

                throw;
            }
        }
        catch (Exception ex)
        {
            // Already logged if it was a transaction failure
            if (ex is not NpgsqlException)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "filterId={Id} userId={UserId}", id, userId);
            }

            return ProblemDetailsHelpers.InternalServerError(
                "Error updating filter.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> DeleteFilter(
        [FromRoute] Guid id,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.DeleteFilter");

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId filterId={Id}", id);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            // Use transaction for atomic delete operation
            await using var conn = await db.OpenConnectionAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // Only allow deleting filters that belong to the user
                var query = @"
                    DELETE FROM launch_filters
                    WHERE id = $1 AND user_id = $2";

                await using var cmd = new NpgsqlCommand(query, conn, transaction);
                cmd.Parameters.AddWithValue(id);
                cmd.Parameters.AddWithValue(userId);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.TestItem.TestItemNotFound,
                        "error=FilterNotFoundOrNoPermission filterId={Id} userId={UserId}", id, userId);

                    return ProblemDetailsHelpers.NotFound(
                        "Filter not found or you don't have permission to delete it",
                        eventCode: EventCodes.TestItem.TestItemNotFound,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                await transaction.CommitAsync();
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "error=TransactionFailed filterId={Id} userId={UserId}", id, userId);

                throw;
            }
        }
        catch (Exception ex)
        {
            // Already logged if it was a transaction failure
            if (ex is not NpgsqlException)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "filterId={Id} userId={UserId}", id, userId);
            }

            return ProblemDetailsHelpers.InternalServerError(
                "Error deleting filter.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> GetFilterPreference(
        [FromQuery] string projectKey,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.GetFilterPreference");

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["projectKey"] = ["projectKey is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId projectKey={ProjectKey}", projectKey);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            var query = @"
                SELECT user_id, project_key, selected_filter_id, updated_at
                FROM user_filter_preferences
                WHERE user_id = $1 AND project_key = $2";

            await using var cmd = db.CreateCommand(query);
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(projectKey);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var preference = new UserFilterPreferenceDto
                {
                    UserId = reader.GetString(0),
                    ProjectKey = reader.GetString(1),
                    SelectedFilterId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3)
                };

                return Results.Ok(preference);
            }

            // Return a default preference if none exists
            return Results.Ok(new UserFilterPreferenceDto
            {
                UserId = userId,
                ProjectKey = projectKey,
                SelectedFilterId = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "projectKey={ProjectKey} userId={UserId}", projectKey, userId);

            return ProblemDetailsHelpers.InternalServerError(
                "Error retrieving filter preference.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> UpdateFilterPreference(
        [FromBody] UpdateFilterPreferenceRequest request,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.UpdateFilterPreference");

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.ProjectKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingProjectKey userId={UserId}", userId);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["projectKey"] = ["Project key is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            var query = @"
                INSERT INTO user_filter_preferences (user_id, project_key, selected_filter_id, updated_at)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (user_id, project_key)
                DO UPDATE SET selected_filter_id = EXCLUDED.selected_filter_id, updated_at = EXCLUDED.updated_at
                RETURNING user_id, project_key, selected_filter_id, updated_at";

            await using var cmd = db.CreateCommand(query);
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(request.ProjectKey);
            cmd.Parameters.AddWithValue(request.SelectedFilterId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(now);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var preference = new UserFilterPreferenceDto
                {
                    UserId = reader.GetString(0),
                    ProjectKey = reader.GetString(1),
                    SelectedFilterId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3)
                };

                return Results.Ok(preference);
            }

            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed,
                "error=UpsertReturnedNoResult userId={UserId} projectKey={ProjectKey}", userId, request.ProjectKey);

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to update filter preference.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "userId={UserId} projectKey={ProjectKey}", userId, request.ProjectKey);

            return ProblemDetailsHelpers.InternalServerError(
                "Error updating filter preference.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> ToggleFilterDisplay(
        [FromRoute] Guid id,
        [FromBody] ToggleFilterDisplayRequest request,
        [FromQuery] string userId,
        [FromServices] NpgsqlDataSource db,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("LaunchFilters");
        var chunkedLogger = new ChunkedLogger(logger, "LaunchFilters.ToggleFilterDisplay");

        if (string.IsNullOrWhiteSpace(userId))
        {
            chunkedLogger.LogMilestone(
                EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                "error=MissingUserId filterId={Id}", id);

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            // Use transaction for atomic multi-operation update (filter + user preference)
            await using var conn = await db.OpenConnectionAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // First, check if user has access to this filter (owner or shared with project)
                var checkQuery = @"
                    SELECT user_id, is_shared, display_on_launches
                    FROM launch_filters
                    WHERE id = $1";

                await using var checkCmd = new NpgsqlCommand(checkQuery, conn, transaction);
                checkCmd.Parameters.AddWithValue(id);

                string? filterOwner = null;
                var isShared = false;
                var currentFilterDisplay = false;

                await using (var checkReader = await checkCmd.ExecuteReaderAsync())
                {
                    if (await checkReader.ReadAsync())
                    {
                        filterOwner = checkReader.GetString(0);
                        isShared = checkReader.GetBoolean(1);
                        currentFilterDisplay = checkReader.GetBoolean(2);
                    }
                    else
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.TestItem.TestItemNotFound,
                            "error=FilterNotFound filterId={Id} userId={UserId}", id, userId);

                        return ProblemDetailsHelpers.NotFound(
                            "Filter not found",
                            eventCode: EventCodes.TestItem.TestItemNotFound,
                            instance: httpContext.Request.Path,
                            traceId: httpContext.TraceIdentifier);
                    }
                }

                var isOwner = string.Equals(filterOwner, userId, StringComparison.OrdinalIgnoreCase);

                // User must be owner OR filter must be shared
                if (!isOwner && !isShared)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        "error=Forbidden filterId={Id} userId={UserId}", id, userId);

                    return ProblemDetailsHelpers.Forbidden(
                        "You don't have permission to toggle this filter display",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                var now = DateTimeOffset.UtcNow;

                // If user is owner, update both the filter's default display_on_launches AND their preference
                if (isOwner)
                {
                    // Update launch_filters table
                    var updateFilterQuery = @"
                        UPDATE launch_filters
                        SET display_on_launches = $2, updated_at = $3
                        WHERE id = $1";

                    await using var updateFilterCmd = new NpgsqlCommand(updateFilterQuery, conn, transaction);
                    updateFilterCmd.Parameters.AddWithValue(id);
                    updateFilterCmd.Parameters.AddWithValue(request.DisplayOnLaunches);
                    updateFilterCmd.Parameters.AddWithValue(now);

                    await updateFilterCmd.ExecuteNonQueryAsync();
                }

                // Insert or update user's personal preference
                var upsertPreferenceQuery = @"
                    INSERT INTO user_filter_display_preferences (user_id, filter_id, display_on_launches, updated_at)
                    VALUES ($1, $2, $3, $4)
                    ON CONFLICT (user_id, filter_id)
                    DO UPDATE SET display_on_launches = EXCLUDED.display_on_launches, updated_at = EXCLUDED.updated_at";

                await using var upsertCmd = new NpgsqlCommand(upsertPreferenceQuery, conn, transaction);
                upsertCmd.Parameters.AddWithValue(userId);
                upsertCmd.Parameters.AddWithValue(id);
                upsertCmd.Parameters.AddWithValue(request.DisplayOnLaunches);
                upsertCmd.Parameters.AddWithValue(now);

                await upsertCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
                return Results.Ok(new { displayOnLaunches = request.DisplayOnLaunches });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "error=TransactionFailed filterId={Id} userId={UserId}", id, userId);

                throw;
            }
        }
        catch (Exception ex)
        {
            // Already logged if it was a transaction failure
            if (ex is not NpgsqlException)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Database.OperationFailed, ex,
                    "filterId={Id} userId={UserId}", id, userId);
            }

            return ProblemDetailsHelpers.InternalServerError(
                "Error toggling filter display.",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }
}
