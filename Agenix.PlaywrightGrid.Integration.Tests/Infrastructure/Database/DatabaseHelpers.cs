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

using Npgsql;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Database;

/// <summary>
///     Common database helper methods for integration tests.
///     Provides reusable operations for test data setup, cleanup, and queries.
/// </summary>
public static class DatabaseHelpers
{
    /// <summary>
    ///     Creates a launch in the database with the specified parameters.
    /// </summary>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="launchId">The unique identifier for the launch.</param>
    /// <param name="projectKey">The project key the launch belongs to.</param>
    /// <param name="launchNumber">The sequential launch number.</param>
    /// <param name="status">The launch status (e.g., InProgress, Finished, Failed).</param>
    /// <param name="ownerApiKey">The API key of the user who created the launch. Defaults to "test-api-key".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CreateLaunchAsync(
        NpgsqlDataSource dataSource,
        Guid launchId,
        string projectKey,
        int launchNumber,
        string status,
        string ownerApiKey = "test-api-key",
        CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(@"
            INSERT INTO launches (id, project_key, launch_number, name, status, start_time, owner_api_key)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (id) DO NOTHING");

        cmd.Parameters.AddWithValue(launchId);
        cmd.Parameters.AddWithValue(projectKey);
        cmd.Parameters.AddWithValue(launchNumber);
        cmd.Parameters.AddWithValue($"Launch #{launchNumber}");
        cmd.Parameters.AddWithValue(status);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue(ownerApiKey);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    ///     Creates a test item in the database with the specified parameters.
    /// </summary>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="runId">The unique identifier for the test item.</param>
    /// <param name="launchId">The launch this test item belongs to.</param>
    /// <param name="parentItemId">The parent test item ID (null for root items).</param>
    /// <param name="itemType">The item type (e.g., Test, Suite, Step, Scenario).</param>
    /// <param name="name">The name of the test item.</param>
    /// <param name="sessionStatus">The browser session status.</param>
    /// <param name="computedStatus">The test execution status (can be null).</param>
    /// <param name="startTime">The start time (defaults to now).</param>
    /// <param name="finishTime">The finish time (null if not finished).</param>
    /// <param name="hasStats">Whether this item contributes to statistics. Defaults to true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The database ID (db_id) of the created test item.</returns>
    public static async Task<long> CreateTestItemAsync(
        NpgsqlDataSource dataSource,
        Guid runId,
        Guid launchId,
        Guid? parentItemId,
        string itemType,
        string name,
        string sessionStatus,
        string? computedStatus,
        DateTimeOffset? startTime = null,
        DateTimeOffset? finishTime = null,
        bool hasStats = true,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(@"
            INSERT INTO test_items (
                run_id, launch_id, parent_item_id, item_type, name,
                session_status, computed_status, start_time, finish_time, has_stats
            )
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            RETURNING db_id");

        cmd.Parameters.AddWithValue(runId);
        cmd.Parameters.AddWithValue(launchId);
        cmd.Parameters.AddWithValue(parentItemId.HasValue ? parentItemId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(itemType);
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(sessionStatus);
        cmd.Parameters.AddWithValue(computedStatus ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(startTime ?? DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue(finishTime ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(hasStats);

        var dbId = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(dbId);
    }

    /// <summary>
    ///     Cleans up all test data for the specified project key.
    ///     Deletes test items and launches in the correct order to respect foreign key constraints.
    /// </summary>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="projectKey">The project key to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CleanupProjectDataAsync(
        NpgsqlDataSource dataSource,
        string projectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete test items first (foreign key constraint to launches)
            await using var cmd1 = dataSource.CreateCommand(
                "DELETE FROM test_items WHERE launch_id IN (SELECT id FROM launches WHERE project_key = $1)");
            cmd1.Parameters.AddWithValue(projectKey);
            await cmd1.ExecuteNonQueryAsync(cancellationToken);

            // Delete launches
            await using var cmd2 = dataSource.CreateCommand("DELETE FROM launches WHERE project_key = $1");
            cmd2.Parameters.AddWithValue(projectKey);
            await cmd2.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await TestContext.Progress.WriteLineAsync(
                $"[DatabaseHelpers] Cleanup warning for {projectKey}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Executes a query and returns the first result as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to return.</typeparam>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first result, or default(T) if no results.</returns>
    public static async Task<T?> ExecuteScalarAsync<T>(
        NpgsqlDataSource dataSource,
        string sql,
        object[]? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(sql);

        if (parameters != null)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                cmd.Parameters.AddWithValue(parameters[i]);
            }
        }

        var result = await cmd.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    ///     Executes a non-query command and returns the number of affected rows.
    /// </summary>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="sql">The SQL command to execute.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    public static async Task<int> ExecuteNonQueryAsync(
        NpgsqlDataSource dataSource,
        string sql,
        object[]? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(sql);

        if (parameters != null)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                cmd.Parameters.AddWithValue(parameters[i]);
            }
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
