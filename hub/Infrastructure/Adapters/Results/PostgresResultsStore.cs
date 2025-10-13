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
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using NpgsqlTypes;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using PlaywrightHub.Infrastructure.Services;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
///     PostgreSQL-backed durable implementation of IResultsStore.
///     Stores runs, tests and command logs with optional TTL-based retention.
///     TTL is implemented via expires_at columns and filtered on reads; a periodic cleanup is optional downstream.
/// </summary>
public sealed class PostgresResultsStore(
    IConfiguration config,
    IHubContext<LaunchesHub, ILaunchesClient> launchesHub) : IResultsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _connString = config["POSTGRES_CONNECTION_STRING"]
                                          ?? throw new InvalidOperationException(
                                              "POSTGRES_CONNECTION_STRING environment variable is required");

    private readonly TimeSpan? _logsTtl = ParseLogsTtl(config);

    // Separate TTLs for run/test results vs. verbose command logs
    private readonly TimeSpan? _runsTtl = ParseResultsTtl(config);

    private bool _initialized;

    public async Task UpsertRunAsync(ResultRunSummaryDto run)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Start transaction with READ COMMITTED isolation level (PostgresSQL default)
        // This ensures all operations are atomic and prevents lost updates
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            var json = JsonSerializer.Serialize(run, JsonOpts);
            cmd.CommandText = @"
INSERT INTO test_items (run_id, name, start_time, finish_time, run_json, app, browser, browser_id, browser_type, websocket_endpoint, worker_node_id, env, status, session_status, computed_status, started_at_utc, completed_at_utc, expires_at, description, attributes, parent_item_id, launch_id, item_type, playwright_version, browser_version, region_os, code_ref, test_case_id, test_case_hash, total_tests, passed_tests, failed_tests, skipped_tests, timedout_tests)
VALUES (@id, @name, @started, @completed, @json, @app, @browser, @browserId, @browserType, @websocketEndpoint, @workerNodeId, @env, @status, @sessionStatus, @computedStatus, @started, @completed, @expires, @description, @attributes, @parentItemId, @launchId, @itemType, @playwrightVersion, @browserVersion, @regionOs, @codeRef, @testCaseId, @testCaseHash, @totalTests, @passedTests, @failedTests, @skippedTests, @timedoutTests)
ON CONFLICT (run_id) DO UPDATE SET
    name = EXCLUDED.name,
    start_time = EXCLUDED.start_time,
    finish_time = EXCLUDED.finish_time,
    run_json = EXCLUDED.run_json,
    app = EXCLUDED.app,
    browser = EXCLUDED.browser,
    browser_id = EXCLUDED.browser_id,
    browser_type = EXCLUDED.browser_type,
    websocket_endpoint = EXCLUDED.websocket_endpoint,
    worker_node_id = EXCLUDED.worker_node_id,
    env = EXCLUDED.env,
    status = EXCLUDED.status,
    session_status = EXCLUDED.session_status,
    computed_status = EXCLUDED.computed_status,
    started_at_utc = EXCLUDED.started_at_utc,
    completed_at_utc = EXCLUDED.completed_at_utc,
    expires_at = EXCLUDED.expires_at,
    description = EXCLUDED.description,
    attributes = EXCLUDED.attributes,
    parent_item_id = EXCLUDED.parent_item_id,
    launch_id = EXCLUDED.launch_id,
    item_type = EXCLUDED.item_type,
    playwright_version = EXCLUDED.playwright_version,
    browser_version = EXCLUDED.browser_version,
    region_os = EXCLUDED.region_os,
    code_ref = EXCLUDED.code_ref,
    test_case_id = EXCLUDED.test_case_id,
    test_case_hash = EXCLUDED.test_case_hash,
    total_tests = CASE WHEN EXCLUDED.item_type = 'Suite' THEN test_items.total_tests ELSE EXCLUDED.total_tests END,
    passed_tests = CASE WHEN EXCLUDED.item_type = 'Suite' THEN test_items.passed_tests ELSE EXCLUDED.passed_tests END,
    failed_tests = CASE WHEN EXCLUDED.item_type = 'Suite' THEN test_items.failed_tests ELSE EXCLUDED.failed_tests END,
    skipped_tests = CASE WHEN EXCLUDED.item_type = 'Suite' THEN test_items.skipped_tests ELSE EXCLUDED.skipped_tests END,
    timedout_tests = CASE WHEN EXCLUDED.item_type = 'Suite' THEN test_items.timedout_tests ELSE EXCLUDED.timedout_tests END;";
            cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = Guid.Parse(run.RunId) });
            cmd.Parameters.AddWithValue("@name", run.RunName ?? run.RunId);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.Parameters.AddWithValue("@app", (object?)run.App ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@browser", (object?)run.Browser ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@browserId", (object?)run.BrowserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@browserType", (object?)run.BrowserType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@websocketEndpoint", (object?)run.WebSocketEndpoint ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@workerNodeId", (object?)run.WorkerNodeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@env", (object?)run.Env ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", (object?)run.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sessionStatus", (object?)run.SessionStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@computedStatus", (object?)run.ComputedStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@started", run.StartedAtUtc);

            // Auto-populate finish_time if not provided but item is in terminal status
            // Terminal statuses: Passed, Failed, Skipped, Timedout, Cancelled, Errored, Stopped, AutoStopped, Completed, Aborted
            var terminalStatuses = new[]
            {
                "Passed", "Failed", "Skipped", "Timedout", "Cancelled", "Errored", "Stopped", "AutoStopped",
                "Completed", "Aborted", "Finished"
            };
            var isTerminalStatus = (!string.IsNullOrWhiteSpace(run.ComputedStatus) &&
                                    terminalStatuses.Contains(run.ComputedStatus)) ||
                                   (!string.IsNullOrWhiteSpace(run.SessionStatus) &&
                                    terminalStatuses.Contains(run.SessionStatus)) ||
                                   (!string.IsNullOrWhiteSpace(run.Status) && terminalStatuses.Contains(run.Status));

            var completedAt = run.CompletedAtUtc ?? (isTerminalStatus ? DateTime.UtcNow : null);
            cmd.Parameters.AddWithValue("@completed", (object?)completedAt ?? DBNull.Value);
            var expires = _runsTtl is null ? (DateTime?)null : DateTime.UtcNow + _runsTtl.Value;
            cmd.Parameters.AddWithValue("@expires", (object?)expires ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@description", (object?)run.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@attributes", run.Attributes ?? Array.Empty<string>());
            cmd.Parameters.AddWithValue("@parentItemId", (object?)run.ParentItemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@launchId", (object?)run.LaunchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@itemType", run.ItemType);
            cmd.Parameters.AddWithValue("@playwrightVersion", (object?)run.PlaywrightVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@browserVersion", (object?)run.BrowserVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@regionOs", (object?)run.RegionOs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@codeRef", (object?)run.CodeRef ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@testCaseId", (object?)run.TestCaseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@testCaseHash", run.TestCaseHash);

            // Calculate test aggregations for all executable item types (Suite and other container types get aggregated by trigger)
            // For executable items (Test, Step, hooks): total_tests=1, and one of passed/failed/skipped/timedout=1 based on computed_status
            var totalTests = 0;
            var passedTests = 0;
            var failedTests = 0;
            var skippedTests = 0;
            var timedoutTests = 0;

            // All executable item types that should have aggregations
            var executableTypes = new[]
            {
                "Test", "Step", "BeforeMethod", "AfterMethod", "BeforeClass", "AfterClass", "BeforeTest",
                "AfterTest", "BeforeSuite", "AfterSuite"
            };

            if (executableTypes.Contains(run.ItemType) && !string.IsNullOrEmpty(run.ComputedStatus))
            {
                totalTests = 1;
                switch (run.ComputedStatus)
                {
                    case "Passed":
                        passedTests = 1;
                        break;
                    case "Failed":
                    case "Errored":
                        failedTests = 1;
                        break;
                    case "Skipped":
                        skippedTests = 1;
                        break;
                    case "Timedout":
                        timedoutTests = 1;
                        break;
                }
            }

            cmd.Parameters.AddWithValue("@totalTests", totalTests);
            cmd.Parameters.AddWithValue("@passedTests", passedTests);
            cmd.Parameters.AddWithValue("@failedTests", failedTests);
            cmd.Parameters.AddWithValue("@skippedTests", skippedTests);
            cmd.Parameters.AddWithValue("@timedoutTests", timedoutTests);

            await cmd.ExecuteNonQueryAsync();

            // Commit the transaction immediately - don't block on non-critical operations
            await transaction.CommitAsync();

            // PERFORMANCE: All non-critical operations disabled for maximum speed
            // The database trigger handles aggregations automatically, so manual refresh is redundant
        }
        catch
        {
            // Rollback on any error to ensure atomicity
            await transaction.RollbackAsync();

            // Re-throw the original exception to preserve stack trace and message
            throw;
        }
    }

    public async Task<ResultRunSummaryDto?> GetRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"SELECT run_json, code_ref, test_case_id, test_case_hash, playwright_version, browser_version, region_os, launch_id, parent_item_id, item_type
              FROM test_items
              WHERE run_id=@id AND (expires_at IS NULL OR expires_at > NOW()) LIMIT 1";
        cmd.Parameters.AddWithValue("@id", Guid.Parse(runId));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var json = reader.GetString(0);

            try
            {
                var dto = JsonSerializer.Deserialize<ResultRunSummaryDto>(json, JsonOpts);
                if (dto != null)
                {
                    // Override with database column values (these take precedence over JSON)
                    var codeRef = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var testCaseId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var testCaseHash = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                    var playwrightVersion = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var browserVersion = reader.IsDBNull(5) ? null : reader.GetString(5);
                    var regionOs = reader.IsDBNull(6) ? null : reader.GetString(6);
                    var launchId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7);
                    var parentItemId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8);
                    var itemType = reader.IsDBNull(9) ? "Test" : reader.GetString(9);

                    return dto with
                    {
                        CodeRef = codeRef,
                        TestCaseId = testCaseId,
                        TestCaseHash = testCaseHash,
                        PlaywrightVersion = playwrightVersion,
                        BrowserVersion = browserVersion,
                        RegionOs = regionOs,
                        LaunchId = launchId,
                        ParentItemId = parentItemId,
                        ItemType = itemType
                    };
                }

                return null;
            }
            catch { return null; }
        }

        return null;
    }

    public async Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100,
        string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        await EnsureCreatedAsync();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);
        var where = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
        var pars = new List<NpgsqlParameter>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("LOWER(status)=LOWER(@status)");
            pars.Add(new NpgsqlParameter("@status", status));
        }

        if (!string.IsNullOrWhiteSpace(app))
        {
            where.Add("LOWER(app)=LOWER(@app)");
            pars.Add(new NpgsqlParameter("@app", app));
        }

        if (!string.IsNullOrWhiteSpace(browser))
        {
            where.Add("LOWER(browser)=LOWER(@browser)");
            pars.Add(new NpgsqlParameter("@browser", browser));
        }

        if (!string.IsNullOrWhiteSpace(env))
        {
            where.Add("LOWER(env)=LOWER(@env)");
            pars.Add(new NpgsqlParameter("@env", env));
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var sql =
            $"SELECT run_json, code_ref, test_case_id, test_case_hash FROM test_items {whereSql} ORDER BY started_at_utc DESC OFFSET @skip LIMIT @take";
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in pars)
        {
            cmd.Parameters.Add(p);
        }

        cmd.Parameters.AddWithValue("@skip", skip);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<ResultRunSummaryDto>(take);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Check if run_json is NULL (can happen with old/corrupted data)
            if (reader.IsDBNull(0))
            {
                continue; // Skip this row
            }

            var json = reader.GetString(0);
            var codeRef = reader.IsDBNull(1) ? null : reader.GetString(1);
            var testCaseId = reader.IsDBNull(2) ? null : reader.GetString(2);
            var testCaseHash = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);

            var dto = SafeDeserialize<ResultRunSummaryDto>(json);
            if (dto is not null)
            {
                // Override with database column values
                list.Add(dto with { CodeRef = codeRef, TestCaseId = testCaseId, TestCaseHash = testCaseHash });
            }
        }

        return list;
    }

    public async Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null,
        string? env = null)
    {
        await EnsureCreatedAsync();
        var where = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
        var pars = new List<NpgsqlParameter>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("LOWER(status)=LOWER(@status)");
            pars.Add(new NpgsqlParameter("@status", status));
        }

        if (!string.IsNullOrWhiteSpace(app))
        {
            where.Add("LOWER(app)=LOWER(@app)");
            pars.Add(new NpgsqlParameter("@app", app));
        }

        if (!string.IsNullOrWhiteSpace(browser))
        {
            where.Add("LOWER(browser)=LOWER(@browser)");
            pars.Add(new NpgsqlParameter("@browser", browser));
        }

        if (!string.IsNullOrWhiteSpace(env))
        {
            where.Add("LOWER(env)=LOWER(@env)");
            pars.Add(new NpgsqlParameter("@env", env));
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var sql = $"SELECT CAST(COUNT(*) AS INT) FROM test_items {whereSql}";
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in pars)
        {
            cmd.Parameters.Add(p);
        }

        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : Convert.ToInt32(result);
    }

    public async Task AppendCommandAsync(CommandLogEventDto ev)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var expires = _logsTtl is null ? (DateTime?)null : DateTime.UtcNow + _logsTtl.Value;
        cmd.CommandText = @"
INSERT INTO commands (run_id, timestamp_utc, kind, message, props_json, test_id, expires_at)
VALUES (@run, @ts, @kind, @msg, @props, @test, @expires);";
        cmd.Parameters.AddWithValue("@run", Guid.Parse(ev.RunId));
        cmd.Parameters.AddWithValue("@ts", ev.TimestampUtc);
        cmd.Parameters.AddWithValue("@kind", (object?)ev.Kind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@msg", (object?)ev.Message ?? DBNull.Value);
        var propsJson = ev.Props is null ? null : JsonSerializer.Serialize(ev.Props);
        cmd.Parameters.AddWithValue("@props", (object?)propsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@test", (object?)ev.TestId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@expires", (object?)expires ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200)
    {
        await EnsureCreatedAsync();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 2000);
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT run_id, timestamp_utc, kind, message, props_json, test_id
FROM commands
WHERE run_id=@run AND (expires_at IS NULL OR expires_at > NOW())
ORDER BY timestamp_utc ASC OFFSET @skip LIMIT @take";
        cmd.Parameters.AddWithValue("@run", Guid.Parse(runId));
        cmd.Parameters.AddWithValue("@skip", skip);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<CommandLogEventDto>(take);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var propsJson = reader.IsDBNull(4) ? null : reader.GetString(4);
            Dictionary<string, string>? props = null;
            if (!string.IsNullOrEmpty(propsJson))
            {
                try { props = JsonSerializer.Deserialize<Dictionary<string, string>>(propsJson!, JsonOpts); }
                catch { }
            }

            var dto = new CommandLogEventDto
            {
                RunId = reader.GetGuid(0).ToString(), // run_id is UUID in database
                TimestampUtc = reader.GetDateTime(1),
                Kind = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Message = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Props = props,
                TestId = reader.IsDBNull(5) ? null : reader.GetString(5) // test_id is TEXT in database
            };
            list.Add(dto);
        }

        return list;
    }

    public async Task<int> GetCommandCountAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"SELECT CAST(COUNT(*) AS INT) FROM commands WHERE run_id=@run AND (expires_at IS NULL OR expires_at > NOW())";
        cmd.Parameters.AddWithValue("@run", Guid.Parse(runId));
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : Convert.ToInt32(result);
    }

    public async Task UpsertTestAsync(ResultTestCaseDto test)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var json = JsonSerializer.Serialize(test, JsonOpts);
        var expires = _runsTtl is null ? (DateTime?)null : DateTime.UtcNow + _runsTtl.Value;
        cmd.CommandText = @"
INSERT INTO tests (run_id, test_id, test_json, status, title, expires_at)
VALUES (@run, @id, @json, @status, @title, @expires)
ON CONFLICT (run_id, test_id) DO UPDATE SET
    test_json = EXCLUDED.test_json,
    status = EXCLUDED.status,
    title = EXCLUDED.title,
    expires_at = EXCLUDED.expires_at;";
        cmd.Parameters.AddWithValue("@run", test.RunId);
        cmd.Parameters.AddWithValue("@id", test.TestId);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@status", (object?)test.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", (object?)test.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@expires", (object?)expires ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200,
        string? status = null)
    {
        await EnsureCreatedAsync();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var where = new List<string> { "run_id=@run", "(expires_at IS NULL OR expires_at > NOW())" };
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("LOWER(status)=LOWER(@status)");
        }

        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT test_json FROM tests WHERE {whereSql} ORDER BY title ASC OFFSET @skip LIMIT @take";
        cmd.Parameters.AddWithValue("@run", Guid.Parse(runId));
        if (!string.IsNullOrWhiteSpace(status))
        {
            cmd.Parameters.AddWithValue("@status", status!);
        }

        cmd.Parameters.AddWithValue("@skip", skip);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<ResultTestCaseDto>(take);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var dto = SafeDeserialize<ResultTestCaseDto>(json);
            if (dto is not null)
            {
                list.Add(dto);
            }
        }

        return list;
    }

    public async Task<bool> DeleteRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // Delete child rows first
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM tests WHERE run_id = @id";
                cmd.Parameters.AddWithValue("@id", Guid.Parse(runId));
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM commands WHERE run_id = @id";
                cmd.Parameters.AddWithValue("@id", Guid.Parse(runId));
                await cmd.ExecuteNonQueryAsync();
            }

            int affected;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM test_items WHERE run_id = @id";
                cmd.Parameters.AddWithValue("@id", Guid.Parse(runId));
                affected = await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return affected > 0;
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            catch { }

            return false;
        }
    }

    // ===== Test Case Management Implementation =====

    public async Task UpsertTestCaseAsync(TestCaseDetailDto testCase)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var json = JsonSerializer.Serialize(testCase, JsonOpts);

        const string sql = @"
            INSERT INTO test_items (
                run_id, test_id, test_json, status, test_title,
                start_time, end_time, duration_ms, expires_at, updated_at
            ) VALUES (
                @runId, @testId, @json::jsonb, @status, @title,
                @start, @end, @duration, @expires, NOW()
            )
            ON CONFLICT (run_id, test_id) DO UPDATE SET
                test_json = EXCLUDED.test_json,
                status = EXCLUDED.status,
                test_title = EXCLUDED.test_title,
                start_time = EXCLUDED.start_time,
                end_time = EXCLUDED.end_time,
                duration_ms = EXCLUDED.duration_ms,
                expires_at = EXCLUDED.expires_at,
                updated_at = NOW()";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@runId", testCase.RunId);
        cmd.Parameters.AddWithValue("@testId", testCase.TestId);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@status", testCase.Status ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@title", testCase.TestTitle ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@start", testCase.StartTime);
        cmd.Parameters.AddWithValue("@end", testCase.EndTime ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@duration", testCase.DurationMs);

        var expires = _runsTtl is null ? (DateTime?)null : DateTime.UtcNow + _runsTtl.Value;
        cmd.Parameters.AddWithValue("@expires", expires ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<TestCaseDetailDto?> GetTestCaseAsync(string runId, string testId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT test_json
            FROM test_items
            WHERE run_id = @runId AND test_id = @testId
            AND (expires_at IS NULL OR expires_at > NOW())";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@testId", testId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var json = reader.GetString(0);
        return JsonSerializer.Deserialize<TestCaseDetailDto>(json, JsonOpts);
    }

    public async Task<List<TestCaseDetailDto>> GetTestCasesForRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT test_json
            FROM test_items
            WHERE run_id = @runId
            AND (expires_at IS NULL OR expires_at > NOW())
            ORDER BY start_time ASC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@runId", runId);

        var results = new List<TestCaseDetailDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var testCase = JsonSerializer.Deserialize<TestCaseDetailDto>(json, JsonOpts);
            if (testCase != null)
            {
                results.Add(testCase);
            }
        }

        // Load artifacts for each test case
        foreach (var testCase in results)
        {
            var artifacts = await GetArtifactsForTestAsync(runId, testCase.TestId);
            testCase.Attachments.Clear();
            testCase.Attachments.AddRange(artifacts);
        }

        return results;
    }

    public async Task DeleteTestCasesForRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = "DELETE FROM test_items WHERE run_id = @runId";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@runId", runId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CountTestCasesForRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT COUNT(*)
            FROM test_items
            WHERE run_id = @runId
            AND (expires_at IS NULL OR expires_at > NOW())";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@runId", runId);

        var result = await cmd.ExecuteScalarAsync();
        return result is long count ? (int)count : 0;
    }

    // ===== Test Result Aggregation Implementation (Phase 2) =====

    public async Task UpdateTestRunAggregationsAsync(
        string runId,
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        string computedStatus)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            UPDATE test_items
            SET
                total_tests = @totalTests,
                passed_tests = @passedTests,
                failed_tests = @failedTests,
                skipped_tests = @skippedTests,
                timedout_tests = @timedoutTests
            WHERE run_id = @runId";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@totalTests", totalTests);
        cmd.Parameters.AddWithValue("@passedTests", passedTests);
        cmd.Parameters.AddWithValue("@failedTests", failedTests);
        cmd.Parameters.AddWithValue("@skippedTests", skippedTests);
        cmd.Parameters.AddWithValue("@timedoutTests", timedoutTests);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateSuiteAggregationsAsync(
        Guid suiteId,
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        string computedStatus)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            UPDATE test_items
            SET
                total_tests = @totalTests,
                passed_tests = @passedTests,
                failed_tests = @failedTests,
                skipped_tests = @skippedTests,
                timedout_tests = @timedoutTests,
                computed_status = @computedStatus
            WHERE run_id = @suiteId AND item_type = 'Suite'";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@suiteId", suiteId);
        cmd.Parameters.AddWithValue("@totalTests", totalTests);
        cmd.Parameters.AddWithValue("@passedTests", passedTests);
        cmd.Parameters.AddWithValue("@failedTests", failedTests);
        cmd.Parameters.AddWithValue("@skippedTests", skippedTests);
        cmd.Parameters.AddWithValue("@timedoutTests", timedoutTests);
        cmd.Parameters.AddWithValue("@computedStatus", computedStatus);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateLaunchAggregationsAsync(
        Guid launchId,
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        string computedStatus)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            UPDATE launches
            SET
                total_tests = @totalTests,
                passed_tests = @passedTests,
                failed_tests = @failedTests,
                skipped_tests = @skippedTests,
                timedout_tests = @timedoutTests,
                computed_status = @computedStatus
            WHERE id = @launchId";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@launchId", launchId);
        cmd.Parameters.AddWithValue("@totalTests", totalTests);
        cmd.Parameters.AddWithValue("@passedTests", passedTests);
        cmd.Parameters.AddWithValue("@failedTests", failedTests);
        cmd.Parameters.AddWithValue("@skippedTests", skippedTests);
        cmd.Parameters.AddWithValue("@timedoutTests", timedoutTests);
        cmd.Parameters.AddWithValue("@computedStatus", computedStatus);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ResultRunSummaryDto>> GetTestRunsForSuiteAsync(Guid suiteId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT * FROM test_items
            WHERE parent_item_id = @parentItemId
            ORDER BY start_time DESC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@parentItemId", suiteId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var runs = new List<ResultRunSummaryDto>();

        while (await reader.ReadAsync())
        {
            runs.Add(MapRunFromReader(reader));
        }

        return runs;
    }

    public async Task<List<SuiteDto>> GetSuitesForLaunchAsync(Guid launchId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT * FROM test_items
            WHERE launch_id = @launchId
              AND item_type = 'Suite'
            ORDER BY start_time DESC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@launchId", launchId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var suites = new List<SuiteDto>();

        while (await reader.ReadAsync())
        {
            suites.Add(MapSuiteFromReader(reader));
        }

        return suites;
    }

    // ===== Artifact Storage Implementation =====

    /// <summary>
    ///     DEPRECATED: Use SaveArtifactWithEventAsync for event-driven async uploads.
    ///     This method still exists for backward compatibility but performs synchronous blocking uploads.
    /// </summary>
    [Obsolete("Use SaveArtifactWithEventAsync for async event-driven uploads")]
    public async Task<string> SaveArtifactAsync(
        string runId,
        string testId,
        string fileName,
        byte[] content,
        string contentType)
    {
        await EnsureCreatedAsync();

        // Generate unique storage path: {testItemId}/{guid}_{filename}
        // Note: Base path (ARTIFACTS_STORAGE_PATH) already includes "artifacts" directory
        // V1 schema simplified: runId is actually testItemId, testId is ignored
        var uniqueId = Guid.NewGuid().ToString("N");
        var sanitizedFileName = SanitizeFileName(fileName);
        var storagePath = $"{runId}/{uniqueId}_{sanitizedFileName}";

        // Get base path from configuration (default to ./data/artifacts)
        var basePath = config["AGENIX_ARTIFACTS_STORAGE_PATH"] ?? "./data/artifacts";
        var fullPath = Path.Combine(basePath, storagePath);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write file to disk
        await File.WriteAllBytesAsync(fullPath, content);

        // Store metadata in database
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Note: V1 schema uses test_item_id instead of run_id/test_id
        // Legacy method signature: runId is actually the test_item_id, testId ignored
        const string sql = @"
            INSERT INTO test_artifacts (
                test_item_id, file_name, content_type, file_size,
                storage_path, uploaded_at, expires_at, status
            ) VALUES (
                @testItemId, @fileName, @contentType, @size,
                @path, NOW(), @expires, 'uploaded'
            ) RETURNING id";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@testItemId", Guid.Parse(runId));
        cmd.Parameters.AddWithValue("@fileName", fileName);
        cmd.Parameters.AddWithValue("@contentType", contentType);
        cmd.Parameters.AddWithValue("@size", content.Length);
        cmd.Parameters.AddWithValue("@path", storagePath);

        var expires = _runsTtl is null ? (DateTime?)null : DateTime.UtcNow + _runsTtl.Value;
        cmd.Parameters.AddWithValue("@expires", expires ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();

        return storagePath;
    }

    /// <summary>
    ///     Creates artifact metadata with "pending" status and returns artifact ID.
    ///     Actual file upload is handled asynchronously by ingestion service.
    ///     Call this method then publish ArtifactUploadEvent via IEventPublisher.
    /// </summary>
    public async Task<Guid> CreateArtifactMetadataAsync(
        Guid testItemId,
        string fileName,
        string contentType,
        long fileSize,
        string projectKey)
    {
        await EnsureCreatedAsync();

        var artifactId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO test_artifacts (
                id, test_item_id, file_name, content_type, file_size,
                storage_path, uploaded_at, expires_at, status
            ) VALUES (
                @id, @testItemId, @fileName, @contentType, @size,
                @storagePath, NOW(), @expires, 'pending'
            ) RETURNING id";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", artifactId);
        cmd.Parameters.AddWithValue("@testItemId", testItemId);
        cmd.Parameters.AddWithValue("@fileName", fileName);
        cmd.Parameters.AddWithValue("@contentType", contentType);
        cmd.Parameters.AddWithValue("@size", fileSize);
        // Use placeholder path to satisfy UNIQUE constraint before actual upload/update
        cmd.Parameters.AddWithValue("@storagePath", $"pending/{artifactId}");

        var expires = _runsTtl is null ? (DateTime?)null : DateTime.UtcNow + _runsTtl.Value;
        cmd.Parameters.AddWithValue("@expires", expires ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();

        return artifactId;
    }

    public async Task<byte[]?> GetArtifactAsync(string path)
    {
        var basePath = config["AGENIX_ARTIFACTS_STORAGE_PATH"] ?? "./data/artifacts";
        var fullPath = Path.Combine(basePath, path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(fullPath);
    }

    public async Task<List<TestAttachmentDto>> GetArtifactsForTestAsync(string runId, string testId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Note: V1 schema simplified test_artifacts to use single test_item_id FK
        // Legacy method signature kept for backward compatibility (testId parameter ignored)
        const string sql = @"
            SELECT id, file_name, content_type, storage_path, file_size, uploaded_at, description
            FROM test_artifacts
            WHERE test_item_id = @testItemId
            AND (expires_at IS NULL OR expires_at > NOW())
            ORDER BY uploaded_at ASC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@testItemId", Guid.Parse(runId));

        var results = new List<TestAttachmentDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TestAttachmentDto
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                ContentType = reader.GetString(2),
                Path = reader.GetString(3),
                Size = reader.GetInt64(4),
                UploadedAt = reader.GetDateTime(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return results;
    }

    public async Task DeleteArtifactAsync(string path)
    {
        // Delete from database
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = "DELETE FROM test_artifacts WHERE storage_path = @path";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync();

        // Delete file from disk
        var basePath = config["AGENIX_ARTIFACTS_STORAGE_PATH"] ?? "./artifacts";
        var fullPath = Path.Combine(basePath, path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public async Task<List<ResultRunSummaryDto>> GetRunsForLaunchAsync(Guid launchId)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = @"
            SELECT * FROM test_items
            WHERE launch_id = $1
            ORDER BY start_time DESC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(launchId);

        var runs = new List<ResultRunSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            runs.Add(MapRunFromReader(reader));
        }

        return runs;
    }

    public async Task UpdateLaunchActivityAsync(Guid launchId)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = "UPDATE launches SET last_activity = NOW() WHERE id = $1";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(launchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecalculateLaunchAggregationsAsync(Guid launchId)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Use transaction for atomicity even though this is a single UPDATE
        // This ensures consistent reads across all subqueries
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await RecalculateLaunchAggregationsAsync(launchId, conn, transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task RecalculateLaunchAggregationsAsync(Guid launchId, NpgsqlConnection conn,
        NpgsqlTransaction transaction)
    {
        // Optimized: Single query with conditional aggregation instead of 5 separate subqueries
        // This reduces query time from O(5n) to O(n) for n test items
        var sql = @"
            UPDATE launches l SET
                total_test_runs = COALESCE(agg.total, 0),
                finished_test_runs = COALESCE(agg.finished, 0),
                running_test_runs = COALESCE(agg.running, 0),
                stopped_test_runs = COALESCE(agg.stopped, 0),
                errored_test_runs = COALESCE(agg.errored, 0)
            FROM (
                SELECT
                    COUNT(*) as total,
                    COUNT(*) FILTER (WHERE session_status = 'Completed') as finished,
                    COUNT(*) FILTER (WHERE session_status IN ('Running', 'Queued')) as running,
                    COUNT(*) FILTER (WHERE session_status IN ('Stopped', 'AutoStopped')) as stopped,
                    COUNT(*) FILTER (WHERE computed_status IN ('Failed', 'Errored')) as errored
                FROM test_items
                WHERE launch_id = $1
            ) agg
            WHERE l.id = $1";

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(launchId);

        // Add command timeout to prevent long-running queries (30 seconds)
        cmd.CommandTimeout = 30;

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"[DEBUG] RecalculateLaunchAggregations: launchId={launchId}, rowsAffected={rowsAffected}");
    }

    public async Task<List<LaunchDto>> GetInProgressLaunchesForProjectAsync(string projectKey, int limit = 100)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = @"
            SELECT * FROM launches
            WHERE project_key = $1
              AND status = 'InProgress'
            ORDER BY last_activity ASC
            LIMIT $2";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(projectKey);
        cmd.Parameters.AddWithValue(limit);

        var launches = new List<LaunchDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            launches.Add(MapLaunchFromReader(reader));
        }

        return launches;
    }

    public async Task UpdateLaunchStatusAsync(Guid launchId, string status, DateTimeOffset? finishTime = null)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = finishTime.HasValue
            ? "UPDATE launches SET status = $1, finish_time = $2 WHERE id = $3"
            : "UPDATE launches SET status = $1 WHERE id = $2";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(status);
        if (finishTime.HasValue)
        {
            cmd.Parameters.AddWithValue(finishTime.Value);
            cmd.Parameters.AddWithValue(launchId);
        }
        else
        {
            cmd.Parameters.AddWithValue(launchId);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    // ===== Test Item Hierarchy Methods (ReportPortal Model) =====

    public async Task<TestItemDto?> GetTestItemAsync(Guid itemId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        const string sql = "SELECT * FROM test_items WHERE run_id = $1";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(itemId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapTestItemFromReader(reader);
    }

    public async Task<List<TestItemDto>> GetChildItemsAsync(Guid parentItemId, string? itemType = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Build SQL with optional item_type filter
        var sql = @"
            SELECT * FROM test_items
            WHERE parent_item_id = $1";

        if (!string.IsNullOrWhiteSpace(itemType))
        {
            sql += " AND item_type = $2";
        }

        sql += " ORDER BY start_time ASC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(parentItemId);

        if (!string.IsNullOrWhiteSpace(itemType))
        {
            cmd.Parameters.AddWithValue(itemType);
        }

        var items = new List<TestItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapTestItemFromReader(reader));
        }

        return items;
    }

    public async Task<TestItemDto?> GetTestItemWithChildrenAsync(Guid itemId, int maxDepth = 5)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Recursive CTE to load entire tree
        const string sql = @"
            WITH RECURSIVE item_tree AS (
                -- Anchor: root item
                SELECT *, 0 as depth FROM test_items WHERE run_id = $1
                UNION ALL
                -- Recursive: children
                SELECT ti.*, it.depth + 1
                FROM test_items ti
                JOIN item_tree it ON ti.parent_item_id = it.run_id
                WHERE it.depth < $2
            )
            SELECT * FROM item_tree ORDER BY depth, start_time";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(itemId);
        cmd.Parameters.AddWithValue(maxDepth);

        var allItems = new List<TestItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            allItems.Add(MapTestItemFromReader(reader));
        }

        if (allItems.Count == 0)
        {
            return null;
        }

        // Build tree structure
        return BuildItemTree(allItems);
    }

    public async Task<List<TestItemDto>> GetTestItemsForLaunchAsync(Guid launchId, string? itemType = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = "SELECT * FROM test_items WHERE launch_id = $1";
        if (!string.IsNullOrEmpty(itemType))
        {
            sql += " AND item_type = $2";
        }

        sql += " ORDER BY start_time ASC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(launchId);
        if (!string.IsNullOrEmpty(itemType))
        {
            cmd.Parameters.AddWithValue(itemType);
        }

        var items = new List<TestItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapTestItemFromReader(reader));
        }

        return items;
    }

    public async Task<List<TestItemDto>> GetTestItemsForSuiteAsync(Guid suiteId, string? itemType = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = "SELECT * FROM test_items WHERE parent_item_id = $1";
        if (!string.IsNullOrEmpty(itemType))
        {
            sql += " AND item_type = $2";
        }

        sql += " ORDER BY start_time ASC";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(suiteId);
        if (!string.IsNullOrEmpty(itemType))
        {
            cmd.Parameters.AddWithValue(itemType);
        }

        var items = new List<TestItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapTestItemFromReader(reader));
        }

        return items;
    }

    public async Task<List<TestItemDto>> GetActiveTestItemsAsync(
        int skip,
        int take,
        string[] sessionStatuses,
        string[]? itemTypes = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        // Default to Test and Scenario types (only these borrow browsers)
        var types = itemTypes ?? new[] { "Test", "Scenario" };

        var sql = @"
            SELECT * FROM test_items
            WHERE session_status = ANY($1)
              AND item_type = ANY($2)
              AND has_stats = true
              AND finish_time IS NULL
            ORDER BY start_time ASC
            LIMIT $3 OFFSET $4";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(sessionStatuses);
        cmd.Parameters.AddWithValue(types);
        cmd.Parameters.AddWithValue(take);
        cmd.Parameters.AddWithValue(skip);

        var items = new List<TestItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapTestItemFromReader(reader));
        }

        return items;
    }

    public async Task<List<TestHistoryItemDto>> GetTestItemHistoryAsync(
        string uniqueIdOrName,
        string itemType,
        int limit)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync();

            // Calculate xxHash32 for fast history lookups using the composite index (launch_id, test_case_hash)
            var hash = TestItemIdentityHelper.ComputeHash(uniqueIdOrName);

            // Query test items across launches using ONLY test_case_hash for performance
            // The hash represents the test_case_id, so we don't need to filter by test_case_id as well
            // This leverages the composite index (launch_id, test_case_hash) for fast lookups
            var sql = @"
                SELECT
                    ti.run_id as test_item_id,
                    ti.launch_id,
                    l.launch_number,
                    COALESCE(ti.computed_status, 'Unknown') as status,
                    ti.attributes,
                    ti.start_time,
                    ti.finish_time,
                    ti.error_message
                FROM test_items ti
                JOIN launches l ON ti.launch_id = l.id
                WHERE ti.test_case_hash = @hash
                  AND ti.item_type = @itemType
                  AND ti.has_stats = true
                ORDER BY l.launch_number DESC
                LIMIT @limit";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("hash", hash);
            cmd.Parameters.AddWithValue("itemType", itemType);
            cmd.Parameters.AddWithValue("limit", limit);

            var history = new List<TestHistoryItemDto>();

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var attributes = reader.IsDBNull(4)
                    ? null
                    : (string[])reader.GetValue(4);

                var startTime = reader.GetDateTime(5);
                var finishTime = reader.IsDBNull(6)
                    ? (DateTime?)null
                    : reader.GetDateTime(6);

                TimeSpan? duration = finishTime.HasValue
                    ? finishTime.Value - startTime
                    : null;

                var errorMessage = reader.IsDBNull(7)
                    ? null
                    : reader.GetString(7);

                var errorCount = string.IsNullOrEmpty(errorMessage) ? 0 : 1;

                history.Add(new TestHistoryItemDto
                {
                    TestItemId = reader.GetGuid(0),
                    LaunchId = reader.GetGuid(1),
                    LaunchNumber = reader.GetInt32(2),
                    Status = reader.GetString(3) ?? "Unknown",
                    Attributes = attributes?.ToList(),
                    Duration = duration,
                    ErrorCount = errorCount
                });
            }

            return history;
        }
        catch (PostgresException pgEx)
        {
            throw new InvalidOperationException(
                $"Database error loading test history for '{uniqueIdOrName}' (type={itemType}): {pgEx.MessageText}",
                pgEx);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error loading test history for '{uniqueIdOrName}' (type={itemType}): {ex.Message}",
                ex);
        }
    }

    // ========================================
    // Log Items Implementation
    // ========================================

    public async Task<Guid> CreateLogItemAsync(CreateLogItemDto dto)
    {
        // NOTE: This method is deprecated and only used as fallback when event publishing is disabled.
        // All log items should flow through ingestion service for token deduplication.
        var logItemId = Guid.NewGuid();
        Guid? attachmentId = null;
        var expiresAt = _logsTtl.HasValue
            ? DateTime.UtcNow.Add(_logsTtl.Value)
            : (DateTime?)null;

        await using var connection = new NpgsqlConnection(_connString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // If attachment data provided, create artifact first
            if (dto.AttachmentData != null && !string.IsNullOrWhiteSpace(dto.AttachmentName))
            {
                attachmentId = Guid.NewGuid();
                // V1 schema: Use simplified 2-level path (no artifacts/ prefix, no middle attachmentId/)
                var storagePath = $"{dto.TestItemUuid}/{attachmentId}_{dto.AttachmentName}";

                var insertArtifactSql = @"
                    INSERT INTO test_artifacts (id, test_item_id, file_name, content_type, file_size, storage_path, uploaded_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7)";

                await using var artifactCmd = new NpgsqlCommand(insertArtifactSql, connection, transaction)
                {
                    Parameters =
                    {
                        new NpgsqlParameter { Value = attachmentId },
                        new NpgsqlParameter { Value = dto.TestItemUuid },
                        new NpgsqlParameter { Value = dto.AttachmentName },
                        new NpgsqlParameter { Value = dto.AttachmentMimeType ?? "application/octet-stream" },
                        new NpgsqlParameter { Value = dto.AttachmentData.LongLength },
                        new NpgsqlParameter { Value = storagePath },
                        new NpgsqlParameter { Value = DateTime.UtcNow }
                    }
                };
                await artifactCmd.ExecuteNonQueryAsync();

                // NOTE: File storage is handled by the ingestion service via event-driven architecture.
                // Hub service only stores metadata in DB and publishes events to RabbitMQ.
                // The ingestion service processes LogItemEvent and writes files to disk (see PostgresBatchWriter.InsertArtifactAsync).
            }

            // Insert log item without token deduplication (simple storage)
            var insertLogSql = @"
                INSERT INTO log_items (id, test_item_uuid, launch_uuid, time, level, message, attachment_id, created_at, expires_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)";

            await using var logCmd = new NpgsqlCommand(insertLogSql, connection, transaction)
            {
                Parameters =
                {
                    new NpgsqlParameter { Value = logItemId },
                    new NpgsqlParameter { Value = dto.TestItemUuid },
                    new NpgsqlParameter { Value = (object?)dto.LaunchUuid ?? DBNull.Value },
                    new NpgsqlParameter { Value = dto.Time },
                    new NpgsqlParameter { Value = dto.Level },
                    new NpgsqlParameter { Value = dto.Message },
                    new NpgsqlParameter { Value = (object?)attachmentId ?? DBNull.Value },
                    new NpgsqlParameter { Value = DateTime.UtcNow },
                    new NpgsqlParameter { Value = (object?)expiresAt ?? DBNull.Value }
                }
            };
            await logCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            return logItemId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<Guid>> CreateLogItemBatchAsync(List<CreateLogItemDto> dtos)
    {
        // NOTE: This method is deprecated and only used as a fallback when event publishing is disabled.
        // All log items should flow through the ingestion service for token deduplication.
        if (dtos.Count == 0)
        {
            return [];
        }

        var expiresAt = _logsTtl.HasValue
            ? DateTime.UtcNow.Add(_logsTtl.Value)
            : (DateTime?)null;

        var ids = new List<Guid>(dtos.Count);
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Use multi-value INSERT for better performance
            var valuesClauses = new List<string>();
            var parameters = new List<NpgsqlParameter>();

            for (var i = 0; i < dtos.Count; i++)
            {
                var dto = dtos[i];
                var id = Guid.NewGuid();
                ids.Add(id);

                valuesClauses.Add($"($1_{i}, $2_{i}, $3_{i}, $4_{i}, $5_{i}, $6_{i}, $7_{i}, $8_{i})");

                parameters.Add(new NpgsqlParameter($"1_{i}", id));
                parameters.Add(new NpgsqlParameter($"2_{i}", dto.TestItemUuid));
                parameters.Add(new NpgsqlParameter($"3_{i}", (object?)dto.LaunchUuid ?? DBNull.Value));
                parameters.Add(new NpgsqlParameter($"4_{i}", dto.Time));
                parameters.Add(new NpgsqlParameter($"5_{i}", dto.Level));
                parameters.Add(new NpgsqlParameter($"6_{i}", dto.Message));
                parameters.Add(new NpgsqlParameter($"7_{i}", DateTime.UtcNow));
                parameters.Add(new NpgsqlParameter($"8_{i}", (object?)expiresAt ?? DBNull.Value));
            }

            var sql = $@"
                INSERT INTO log_items
                    (id, test_item_uuid, launch_uuid, time, level, message, created_at, expires_at)
                VALUES {string.Join(", ", valuesClauses)}";

            await using var cmd = new NpgsqlCommand(sql, conn, transaction);
            cmd.Parameters.AddRange(parameters.ToArray());
            await cmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            return ids;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<LogItemDto?> GetLogItemAsync(Guid id)
    {
        var sql = @"
            SELECT id, test_item_uuid, launch_uuid, time, level, message, attachment_id, created_at
            FROM log_items
            WHERE id = $1";

        await using var connection = new NpgsqlConnection(_connString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection)
        {
            Parameters = { new NpgsqlParameter { Value = id } }
        };

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new LogItemDto
        {
            Id = reader.GetGuid(0),
            TestItemUuid = reader.GetGuid(1),
            LaunchUuid = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Time = reader.GetDateTime(3),
            Level = reader.GetString(4),
            Message = reader.IsDBNull(5) ? "[no message]" : reader.GetString(5),
            AttachmentId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
            CreatedAt = reader.GetDateTime(7)
        };
    }

    public async Task<List<LogItemDto>> GetLogItemsForTestItemAsync(Guid testItemUuid, int skip = 0, int take = 100)
    {
        var sql = @"
            SELECT id, test_item_uuid, launch_uuid, time, level, message, attachment_id, created_at
            FROM log_items
            WHERE test_item_uuid = $1
            ORDER BY time DESC
            LIMIT $2 OFFSET $3";

        await using var connection = new NpgsqlConnection(_connString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection)
        {
            Parameters =
            {
                new NpgsqlParameter { Value = testItemUuid },
                new NpgsqlParameter { Value = take },
                new NpgsqlParameter { Value = skip }
            }
        };

        var results = new List<LogItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new LogItemDto
            {
                Id = reader.GetGuid(0),
                TestItemUuid = reader.GetGuid(1),
                LaunchUuid = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Time = reader.GetDateTime(3),
                Level = reader.GetString(4),
                Message = reader.IsDBNull(5) ? "[no message]" : reader.GetString(5),
                AttachmentId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                CreatedAt = reader.GetDateTime(7)
            });
        }

        return results;
    }

    public async Task<List<LogItemDto>> GetLogItemsForLaunchAsync(Guid launchUuid, int skip = 0, int take = 100)
    {
        var sql = @"
            SELECT id, test_item_uuid, launch_uuid, time, level, message, attachment_id, created_at
            FROM log_items
            WHERE launch_uuid = $1
            ORDER BY time DESC
            LIMIT $2 OFFSET $3";

        await using var connection = new NpgsqlConnection(_connString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection)
        {
            Parameters =
            {
                new NpgsqlParameter { Value = launchUuid },
                new NpgsqlParameter { Value = take },
                new NpgsqlParameter { Value = skip }
            }
        };

        var results = new List<LogItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new LogItemDto
            {
                Id = reader.GetGuid(0),
                TestItemUuid = reader.GetGuid(1),
                LaunchUuid = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Time = reader.GetDateTime(3),
                Level = reader.GetString(4),
                Message = reader.IsDBNull(5) ? "[no message]" : reader.GetString(5),
                AttachmentId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                CreatedAt = reader.GetDateTime(7)
            });
        }

        return results;
    }

    public async Task<ArtifactMetadata?> GetArtifactAsync(Guid artifactId)
    {
        var sql = @"
            SELECT id, test_item_id, file_name, content_type, file_size, storage_path, uploaded_at
            FROM test_artifacts
            WHERE id = $1";

        await using var connection = new NpgsqlConnection(_connString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection)
        {
            Parameters = { new NpgsqlParameter { Value = artifactId } }
        };

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ArtifactMetadata(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetDateTime(6)
        );
    }

    public async Task<List<LogItemDto>> GetLogItemsForTestItemAsync(Guid testItemId)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var sql = @"
            SELECT id, test_item_uuid, launch_uuid, time, level, message, attachment_id, created_at
            FROM log_items
            WHERE test_item_uuid = @testItemId
            ORDER BY time ASC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("testItemId", testItemId);

        var logs = new List<LogItemDto>();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new LogItemDto
            {
                Id = reader.GetGuid(0),
                TestItemUuid = reader.GetGuid(1),
                LaunchUuid = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Time = reader.GetDateTime(3),
                Level = reader.GetString(4),
                Message = reader.IsDBNull(5) ? "" : reader.GetString(5),
                AttachmentId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                CreatedAt = reader.GetDateTime(7)
            });
        }

        return logs;
    }

    public async Task<List<HierarchicalLogEntryDto>> GetLogItemsWithStepsAsync(Guid testItemId, int skip = 0,
        int take = 1000)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();

        var result = new List<HierarchicalLogEntryDto>();

        // Step 1: Recursively get all child test items (supports nested steps)
        var stepsSql = @"
            WITH RECURSIVE step_tree AS (
                -- Direct children
                SELECT run_id, item_type, name, description, computed_status, start_time, finish_time,
                       parent_item_id, 1 as depth
                FROM test_items
                WHERE parent_item_id = @testItemId
                UNION ALL
                -- Nested children
                SELECT ti.run_id, ti.item_type, ti.name, ti.description, ti.computed_status,
                       ti.start_time, ti.finish_time, ti.parent_item_id, st.depth + 1
                FROM test_items ti
                JOIN step_tree st ON ti.parent_item_id = st.run_id
                WHERE st.depth < 5
            )
            SELECT * FROM step_tree ORDER BY start_time ASC";

        await using var stepsCmd = new NpgsqlCommand(stepsSql, conn);
        stepsCmd.Parameters.AddWithValue("testItemId", testItemId);

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

        // Step 2: Get log items with pagination
        var logsSql = @"
            SELECT l.id, l.test_item_uuid, l.time, l.level, l.message, l.attachment_id,
                   a.file_name as attachment_name, a.mime_type as attachment_mime
            FROM log_items l
            LEFT JOIN test_artifacts a ON l.attachment_id = a.id
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
                var nestLevel = stepDepth.ContainsKey(logTestItemId) ? stepDepth[logTestItemId] : 0;

                var logEntry = new HierarchicalLogEntryDto
                {
                    Id = logsReader.GetGuid(0),
                    ParentId = logTestItemId == testItemId ? null : logTestItemId,
                    IsStepHeader = false,
                    IsNested = logTestItemId != testItemId,
                    NestLevel = nestLevel,
                    Timestamp = logsReader.GetDateTime(2),
                    Level = logsReader.GetString(3),
                    Source = "test",
                    Message = logsReader.IsDBNull(4) ? "" : logsReader.GetString(4),
                    Name = "",
                    Description = "",
                    Status = "InProgress",
                    DurationMs = null,
                    AttachmentCount = 0,
                    HasAttachment = !logsReader.IsDBNull(5),
                    AttachmentType = !logsReader.IsDBNull(7)
                        ? logsReader.GetString(7).StartsWith("image/") ? "image" :
                        logsReader.GetString(7).StartsWith("video/") ? "video" : "file"
                        : "",
                    AttachmentName = logsReader.IsDBNull(6) ? "" : logsReader.GetString(6)
                };

                if (logTestItemId == testItemId)
                {
                    rootLogs.Add(logEntry);
                }
                else
                {
                    if (!logsByStep.ContainsKey(logTestItemId))
                    {
                        logsByStep[logTestItemId] = new List<HierarchicalLogEntryDto>();
                    }

                    logsByStep[logTestItemId].Add(logEntry);
                }
            }
        }

        // Step 3: Update attachment counts
        foreach (var stepId in stepOrder)
        {
            if (logsByStep.ContainsKey(stepId))
            {
                var count = logsByStep[stepId].Count(l => l.HasAttachment);
                steps[stepId] = steps[stepId] with { AttachmentCount = count };
            }
        }

        // Step 4: Assemble hierarchical result
        foreach (var stepId in stepOrder)
        {
            result.Add(steps[stepId]);
            if (logsByStep.ContainsKey(stepId))
            {
                result.AddRange(logsByStep[stepId]);
            }
        }

        result.AddRange(rootLogs);
        return result;
    }

    private async Task UpdateLaunchCompletionAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, string runId)
    {
        // First, get the launch_id and project_key for this run
        await using var getLaunchCmd = conn.CreateCommand();
        getLaunchCmd.Transaction = transaction;
        getLaunchCmd.CommandText = @"
            SELECT r.launch_id, l.project_key
            FROM test_items r
            LEFT JOIN launches l ON r.launch_id = l.id
            WHERE r.run_id = @runId";
        getLaunchCmd.Parameters.AddWithValue("@runId", Guid.Parse(runId));

        await using var reader = await getLaunchCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            // Run not found
            return;
        }

        var launchIdObj = reader.GetValue(0);
        if (launchIdObj == DBNull.Value)
        {
            // This run is not part of a launch
            return;
        }

        var launchId = (Guid)launchIdObj;
        var projectKey = reader.GetString(1);
        await reader.CloseAsync();

        // Check if all runs for this launch are completed (no running browser sessions)
        // NOTE: We DO NOT automatically set launch to terminal state (Failed/Finished/Stopped)
        // because new test suites may still be added. Launch status should only become terminal
        // when explicitly finished via /finish-launch endpoint.
        //
        // We only auto-update finish_time when all active sessions complete, but keep status as InProgress
        // to allow additional test items to be added.
        await using var updateCmd = conn.CreateCommand();
        updateCmd.Transaction = transaction;
        updateCmd.CommandText = @"
            UPDATE launches
            SET
                finish_time = CASE
                    WHEN finish_time IS NULL
                         AND (SELECT COUNT(*) FROM test_items WHERE launch_id = @launchId AND session_status IN ('Running', 'Queued')) = 0
                         AND (SELECT COUNT(*) FROM test_items WHERE launch_id = @launchId) > 0
                    THEN NOW()
                    ELSE finish_time
                END
                -- NOTE: Status is NOT auto-updated here. Launch remains in InProgress state
                -- until explicitly finished via /finish-launch endpoint. This allows:
                -- 1. New test suites to be added while others are running/completed
                -- 2. Parallel test execution with staggered completion times
                -- 3. Flexible test orchestration patterns
            WHERE id = @launchId
            RETURNING (xmax = 0) as was_updated";

        updateCmd.Parameters.AddWithValue("@launchId", launchId);
        var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

        // Notify SignalR clients about the launch update
        if (rowsAffected > 0)
        {
            await NotifyLaunchUpdated(projectKey, launchId);
        }
    }

    private async Task NotifyLaunchUpdated(string projectKey, Guid launchId)
    {
        try
        {
            // Notify all clients subscribed to this project
            await launchesHub.Clients.Group($"project:{projectKey}").LaunchUpdated(projectKey, launchId);

            // Notify all clients subscribed to this specific launch
            await launchesHub.Clients.Group($"launch:{launchId}").LaunchUpdated(projectKey, launchId);
        }
        catch
        {
            // Silently ignore SignalR errors to not break the main flow
        }
    }

    private Task EnsureCreatedAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        // Migrations are now run once at application startup in HubServiceRunner.cs
        // No need to run migrations here

        _initialized = true;
        return Task.CompletedTask;
    }

    private static TimeSpan? ParseResultsTtl(IConfiguration cfg)
    {
        if (int.TryParse(cfg["AGENIX_HUB_RESULTS_TTL_SECONDS"], out var sec) && sec > 0)
        {
            return TimeSpan.FromSeconds(sec);
        }

        if (int.TryParse(cfg["AGENIX_HUB_RESULTS_TTL_DAYS"], out var days) && days > 0)
        {
            return TimeSpan.FromDays(days);
        }

        if (int.TryParse(cfg["AGENIX_HUB_RESULTS_RETENTION_DAYS"], out var legacyDays) && legacyDays > 0)
        {
            return TimeSpan.FromDays(legacyDays);
        }

        return null;
    }

    private static TimeSpan? ParseLogsTtl(IConfiguration cfg)
    {
        if (int.TryParse(cfg["AGENIX_HUB_LOGS_TTL_SECONDS"], out var sec) && sec > 0)
        {
            return TimeSpan.FromSeconds(sec);
        }

        if (int.TryParse(cfg["AGENIX_HUB_LOGS_TTL_DAYS"], out var days) && days > 0)
        {
            return TimeSpan.FromDays(days);
        }

        return ParseResultsTtl(cfg);
    }

    private static T? SafeDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return default; }
    }

    private ResultRunSummaryDto MapRunFromReader(NpgsqlDataReader reader)
    {
        var runId = reader.GetGuid(reader.GetOrdinal("run_id")).ToString();
        var app = reader.IsDBNull(reader.GetOrdinal("app")) ? string.Empty : reader.GetString(reader.GetOrdinal("app"));
        var browser = reader.IsDBNull(reader.GetOrdinal("browser"))
            ? string.Empty
            : reader.GetString(reader.GetOrdinal("browser"));
        var env = reader.IsDBNull(reader.GetOrdinal("env")) ? string.Empty : reader.GetString(reader.GetOrdinal("env"));
        var status = reader.IsDBNull(reader.GetOrdinal("status"))
            ? "Queued"
            : reader.GetString(reader.GetOrdinal("status"));

        // Fixed: Column names are start_time and finish_time, not started_at_utc/completed_at_utc
        var startedAtUtc = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("start_time")).UtcDateTime;
        DateTime? completedAtUtc = reader.IsDBNull(reader.GetOrdinal("finish_time"))
            ? null
            : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("finish_time")).UtcDateTime;
        var description = reader.IsDBNull(reader.GetOrdinal("description"))
            ? null
            : reader.GetString(reader.GetOrdinal("description"));

        var attributesOrdinal = reader.GetOrdinal("attributes");
        string[]? attributes = null;
        if (!reader.IsDBNull(attributesOrdinal))
        {
            var attributesValue = reader.GetValue(attributesOrdinal);
            if (attributesValue is string[] strArray)
            {
                attributes = strArray;
            }
        }

        var launchIdOrdinal = reader.GetOrdinal("launch_id");
        Guid? launchId = null;
        if (!reader.IsDBNull(launchIdOrdinal))
        {
            launchId = reader.GetGuid(launchIdOrdinal);
        }

        var parentItemIdOrdinal = reader.GetOrdinal("parent_item_id");
        Guid? parentItemId = null;
        if (!reader.IsDBNull(parentItemIdOrdinal))
        {
            parentItemId = reader.GetGuid(parentItemIdOrdinal);
        }

        // Read test aggregations with fallback
        var totalTests = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var timedOut = 0;
        try
        {
            totalTests = reader.GetInt32(reader.GetOrdinal("total_tests"));
            passed = reader.GetInt32(reader.GetOrdinal("passed_tests"));
            failed = reader.GetInt32(reader.GetOrdinal("failed_tests"));
            skipped = reader.GetInt32(reader.GetOrdinal("skipped_tests"));
            timedOut = reader.GetInt32(reader.GetOrdinal("timedout_tests"));
        }
        catch
        {
            // Columns don't exist (pre-V19 migration)
        }

        var codeRefOrdinal = reader.GetOrdinal("code_ref");
        var codeRef = reader.IsDBNull(codeRefOrdinal) ? null : reader.GetString(codeRefOrdinal);

        var testCaseIdOrdinal = reader.GetOrdinal("test_case_id");
        var testCaseId = reader.IsDBNull(testCaseIdOrdinal) ? null : reader.GetString(testCaseIdOrdinal);

        var testCaseHashOrdinal = reader.GetOrdinal("test_case_hash");
        var testCaseHash = reader.IsDBNull(testCaseHashOrdinal) ? 0 : reader.GetInt32(testCaseHashOrdinal);

        return new ResultRunSummaryDto
        {
            RunId = runId,
            App = app,
            Browser = browser,
            Env = env,
            Status = status,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Description = description,
            Attributes = attributes,
            ParentItemId = parentItemId,
            LaunchId = launchId,
            TotalTests = totalTests,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            TimedOut = timedOut,
            CodeRef = codeRef,
            TestCaseId = testCaseId,
            TestCaseHash = testCaseHash
        };
    }

    private SuiteDto MapSuiteFromReader(NpgsqlDataReader reader)
    {
        // Suites are now stored as test_items with item_type='Suite'
        var id = reader.GetGuid(reader.GetOrdinal("run_id")); // test_items uses run_id as PK
        var launchId = reader.GetGuid(reader.GetOrdinal("launch_id"));

        // parent_item_id is used for suite hierarchy now
        var parentSuiteIdOrdinal = reader.GetOrdinal("parent_item_id");
        Guid? parentSuiteId = reader.IsDBNull(parentSuiteIdOrdinal) ? null : reader.GetGuid(parentSuiteIdOrdinal);

        var name = reader.GetString(reader.GetOrdinal("name"));
        var descriptionOrdinal = reader.GetOrdinal("description");
        var description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal);
        var attributes = (string[])reader.GetValue(reader.GetOrdinal("attributes"));

        // Use computed_status for test outcomes, fallback to status for legacy
        var statusOrdinal = reader.GetOrdinal("computed_status");
        var status = reader.IsDBNull(statusOrdinal)
            ? reader.GetString(reader.GetOrdinal("status"))
            : reader.GetString(statusOrdinal);

        var startTime = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("start_time"));
        var finishTimeOrdinal = reader.GetOrdinal("finish_time");
        var finishTime = reader.IsDBNull(finishTimeOrdinal)
            ? null
            : reader.GetFieldValue<DateTimeOffset?>(finishTimeOrdinal);

        // Test run aggregations - these columns don't exist in test_items
        // We'll calculate these by querying child test items
        var totalTestRuns = 0;
        var passedTestRuns = 0;
        var failedTestRuns = 0;
        var stoppedTestRuns = 0;

        double? durationSeconds = null;
        if (finishTime.HasValue)
        {
            durationSeconds = (finishTime.Value - startTime).TotalSeconds;
        }

        // Read test result aggregations from test_items columns
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
            // Columns might be null or not exist
        }

        return new SuiteDto
        {
            Id = id,
            LaunchId = launchId,
            ParentSuiteId = parentSuiteId,
            Name = name,
            Description = description,
            Attributes = attributes,
            Status = status,
            StartTime = startTime,
            FinishTime = finishTime,
            TotalTestRuns = totalTestRuns,
            PassedTestRuns = passedTestRuns,
            FailedTestRuns = failedTestRuns,
            StoppedTestRuns = stoppedTestRuns,
            DurationSeconds = durationSeconds,
            TotalTests = totalTests,
            PassedTests = passedTests,
            FailedTests = failedTests,
            SkippedTests = skippedTests,
            TimedoutTests = timedoutTests
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        // Remove invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    private static LaunchDto MapLaunchFromReader(NpgsqlDataReader reader)
    {
        // Helper to safely get ordinal (returns -1 if column doesn't exist)
        int TryGetOrdinal(string name)
        {
            try { return reader.GetOrdinal(name); }
            catch { return -1; }
        }

        var finishTime = reader.IsDBNull(reader.GetOrdinal("finish_time"))
            ? null
            : reader.GetFieldValue<DateTimeOffset?>(reader.GetOrdinal("finish_time"));

        var startTime = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("start_time"));

        var lastActivityOrdinal = TryGetOrdinal("last_activity");
        var lastActivity = lastActivityOrdinal >= 0 && !reader.IsDBNull(lastActivityOrdinal)
            ? reader.GetFieldValue<DateTimeOffset?>(lastActivityOrdinal)
            : null;

        // Compute duration if finish_time exists
        double? durationSeconds = null;
        var durationOrdinal = TryGetOrdinal("duration_seconds");
        if (durationOrdinal >= 0 && !reader.IsDBNull(durationOrdinal))
        {
            durationSeconds = reader.GetDouble(durationOrdinal);
        }
        else if (finishTime.HasValue)
        {
            durationSeconds = (finishTime.Value - startTime).TotalSeconds;
        }

        var computedStatusOrdinal = TryGetOrdinal("computed_status");

        // Read test aggregation fields
        var totalTestsOrdinal = TryGetOrdinal("total_tests");
        var passedTestsOrdinal = TryGetOrdinal("passed_tests");
        var failedTestsOrdinal = TryGetOrdinal("failed_tests");
        var skippedTestsOrdinal = TryGetOrdinal("skipped_tests");
        var timedoutTestsOrdinal = TryGetOrdinal("timedout_tests");

        return new LaunchDto
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description =
                reader.IsDBNull(reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("description")),
            Attributes = reader.GetFieldValue<string[]>(reader.GetOrdinal("attributes")),
            OwnerApiKey = reader.GetString(reader.GetOrdinal("owner_api_key")),
            OwnerUsername =
                reader.IsDBNull(reader.GetOrdinal("owner_username"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("owner_username")),
            ProjectKey = reader.GetString(reader.GetOrdinal("project_key")),
            StartTime = startTime,
            FinishTime = finishTime,
            LastActivity = lastActivity,
            LaunchNumber = reader.GetInt32(reader.GetOrdinal("launch_number")),
            TotalTestRuns = reader.GetInt32(reader.GetOrdinal("total_test_runs")),
            FinishedTestRuns = reader.GetInt32(reader.GetOrdinal("finished_test_runs")),
            RunningTestRuns = reader.GetInt32(reader.GetOrdinal("running_test_runs")),
            StoppedTestRuns = reader.GetInt32(reader.GetOrdinal("stopped_test_runs")),
            ErroredTestRuns = reader.GetInt32(reader.GetOrdinal("errored_test_runs")),
            IsImportant =
                !reader.IsDBNull(reader.GetOrdinal("is_important")) &&
                reader.GetBoolean(reader.GetOrdinal("is_important")),
            RetentionOverrideDays =
                reader.IsDBNull(reader.GetOrdinal("retention_override_days"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("retention_override_days")),
            DurationSeconds = durationSeconds,
            IsRunning = reader.GetFieldValue<string>(reader.GetOrdinal("status")) == "InProgress",
            Status = reader.GetString(reader.GetOrdinal("status")),
            TotalTests =
                totalTestsOrdinal >= 0 && !reader.IsDBNull(totalTestsOrdinal)
                    ? reader.GetInt32(totalTestsOrdinal)
                    : 0,
            PassedTests =
                passedTestsOrdinal >= 0 && !reader.IsDBNull(passedTestsOrdinal)
                    ? reader.GetInt32(passedTestsOrdinal)
                    : 0,
            FailedTests =
                failedTestsOrdinal >= 0 && !reader.IsDBNull(failedTestsOrdinal)
                    ? reader.GetInt32(failedTestsOrdinal)
                    : 0,
            SkippedTests =
                skippedTestsOrdinal >= 0 && !reader.IsDBNull(skippedTestsOrdinal)
                    ? reader.GetInt32(skippedTestsOrdinal)
                    : 0,
            TimedoutTests =
                timedoutTestsOrdinal >= 0 && !reader.IsDBNull(timedoutTestsOrdinal)
                    ? reader.GetInt32(timedoutTestsOrdinal)
                    : 0,
            ComputedStatus = computedStatusOrdinal >= 0 && !reader.IsDBNull(computedStatusOrdinal)
                ? reader.GetString(computedStatusOrdinal)
                : null
        };
    }

    private static TestItemDto MapTestItemFromReader(NpgsqlDataReader reader)
    {
        var runIdOrdinal = reader.GetOrdinal("run_id");
        var launchIdOrdinal = reader.GetOrdinal("launch_id");
        var parentItemIdOrdinal = reader.GetOrdinal("parent_item_id");
        var itemTypeOrdinal = reader.GetOrdinal("item_type");
        var hasStatsOrdinal = reader.GetOrdinal("has_stats");
        var nameOrdinal = reader.GetOrdinal("name");
        var startTimeOrdinal = reader.GetOrdinal("start_time");

        var id = reader.GetGuid(runIdOrdinal);
        var launchId = reader.GetGuid(launchIdOrdinal);
        var parentItemId = reader.IsDBNull(parentItemIdOrdinal) ? null : (Guid?)reader.GetGuid(parentItemIdOrdinal);
        var itemType = reader.GetString(itemTypeOrdinal);
        var hasStats = reader.GetBoolean(hasStatsOrdinal);
        var name = reader.GetString(nameOrdinal);
        var startTime = reader.GetDateTime(startTimeOrdinal);

        DateTimeOffset? finishTime = null;
        var finishTimeOrdinal = reader.GetOrdinal("finish_time");
        if (!reader.IsDBNull(finishTimeOrdinal))
        {
            finishTime = reader.GetDateTime(finishTimeOrdinal);
        }

        double? durationMs = finishTime.HasValue ? (finishTime.Value - startTime).TotalMilliseconds : null;

        return new TestItemDto
        {
            Id = id,
            LaunchId = launchId,
            ParentItemId = parentItemId,
            DbId = TryGetLong(reader, "db_id"),
            SuiteNumber = TryGetInt(reader, "suite_number"),
            ItemType = itemType,
            HasStats = hasStats,
            Name = name,
            Description = TryGetString(reader, "description"),
            Attributes = TryGetStringArray(reader, "attributes"),
            StartTime = startTime,
            FinishTime = finishTime,
            DurationMs = durationMs,
            SessionStatus = TryGetString(reader, "session_status"),
            ComputedStatus = TryGetString(reader, "computed_status"),
            Status = TryGetString(reader, "status"),
            BrowserId = TryGetString(reader, "browser_id"),
            WebSocketEndpoint = TryGetString(reader, "websocket_endpoint"),
            BrowserType = TryGetString(reader, "browser_type"),
            WorkerNodeId = TryGetString(reader, "worker_node_id"),
            PlaywrightVersion = TryGetString(reader, "playwright_version"),
            BrowserVersion = TryGetString(reader, "browser_version"),
            RegionOs = TryGetString(reader, "region_os"),
            TestTitle = TryGetString(reader, "test_title"),
            TestFile = TryGetString(reader, "test_file"),
            LineNumber = TryGetInt(reader, "line_number"),
            ErrorMessage = TryGetString(reader, "error_message"),
            ErrorStack = TryGetString(reader, "error_stack"),
            RetryAttempt = TryGetInt(reader, "retry_attempt"),
            Tags = TryGetStringArray(reader, "tags"),
            CodeRef = TryGetString(reader, "code_ref"),
            TestCaseId = TryGetString(reader, "test_case_id"),
            TestCaseHash = TryGetInt(reader, "test_case_hash") ?? 0,
            TotalTests = TryGetInt(reader, "total_tests") ?? 0,
            PassedTests = TryGetInt(reader, "passed_tests") ?? 0,
            FailedTests = TryGetInt(reader, "failed_tests") ?? 0,
            SkippedTests = TryGetInt(reader, "skipped_tests") ?? 0,
            TimedoutTests = TryGetInt(reader, "timedout_tests") ?? 0
        };
    }

    private static TestItemDto BuildItemTree(List<TestItemDto> flatList)
    {
        var itemsById = flatList.ToDictionary(i => i.Id);
        var root = flatList.FirstOrDefault(i => i.ParentItemId == null);

        if (root == null)
        {
            return flatList.First(); // Fallback if no root found
        }

        foreach (var item in flatList.Where(i => i.ParentItemId.HasValue))
        {
            if (item.ParentItemId.HasValue && itemsById.TryGetValue(item.ParentItemId.Value, out var parent))
            {
                // Need to create new instance with Children populated (records are immutable)
                var childrenList = parent.Children?.ToList() ?? new List<TestItemDto>();
                childrenList.Add(item);

                // Update dictionary with new parent instance containing updated children
                itemsById[parent.Id] = parent with { Children = childrenList };
            }
        }

        return itemsById[root.Id];
    }

    private static string? TryGetString(NpgsqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetInt(NpgsqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }
        catch
        {
            return null;
        }
    }

    private static long? TryGetLong(NpgsqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
        }
        catch
        {
            return null;
        }
    }

    private static string[]? TryGetStringArray(NpgsqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<string[]>(ordinal);
        }
        catch
        {
            return null;
        }
    }
}
