#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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
using Npgsql;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
///     PostgreSQL-backed durable implementation of IResultsStore.
///     Stores runs, tests and command logs with optional TTL-based retention.
///     TTL is implemented via expires_at columns and filtered on reads; a periodic cleanup is optional downstream.
/// </summary>
public sealed class PostgresResultsStore(IConfiguration config) : IResultsStore
{
    private readonly string _connString = config["HUB_RESULTS_POSTGRES"] ??
                                          "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Separate TTLs for run/test results vs. verbose command logs
    private readonly TimeSpan? _runsTtl = ParseResultsTtl(config);
    private readonly TimeSpan? _logsTtl = ParseLogsTtl(config);

    private bool _initialized;

    private async Task EnsureCreatedAsync()
    {
        if (_initialized) return;
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS runs (
    run_id TEXT PRIMARY KEY,
    run_json TEXT NOT NULL,
    app TEXT NULL,
    browser TEXT NULL,
    env TEXT NULL,
    status TEXT NULL,
    started_at_utc TIMESTAMPTZ NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    expires_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_runs_by_start ON runs(started_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_runs_expires ON runs(expires_at);

CREATE TABLE IF NOT EXISTS tests (
    run_id TEXT NOT NULL,
    test_id TEXT NOT NULL,
    test_json TEXT NOT NULL,
    status TEXT NULL,
    title TEXT NULL,
    expires_at TIMESTAMPTZ NULL,
    PRIMARY KEY (run_id, test_id)
);
CREATE INDEX IF NOT EXISTS ix_tests_by_run ON tests(run_id);
CREATE INDEX IF NOT EXISTS ix_tests_by_run_status ON tests(run_id, status);
CREATE INDEX IF NOT EXISTS ix_tests_expires ON tests(expires_at);

CREATE TABLE IF NOT EXISTS commands (
    run_id TEXT NOT NULL,
    timestamp_utc TIMESTAMPTZ NOT NULL,
    kind TEXT NULL,
    message TEXT NULL,
    props_json TEXT NULL,
    test_id TEXT NULL,
    expires_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_commands_by_run_time ON commands(run_id, timestamp_utc);
CREATE INDEX IF NOT EXISTS ix_commands_expires ON commands(expires_at);
";
        await cmd.ExecuteNonQueryAsync();
        _initialized = true;
    }

    public async Task UpsertRunAsync(ResultRunSummaryDto run)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var json = JsonSerializer.Serialize(run, JsonOpts);
        cmd.CommandText = @"
INSERT INTO runs (run_id, run_json, app, browser, env, status, started_at_utc, completed_at_utc, expires_at)
VALUES (@id, @json, @app, @browser, @env, @status, @started, @completed, @expires)
ON CONFLICT (run_id) DO UPDATE SET
    run_json = EXCLUDED.run_json,
    app = EXCLUDED.app,
    browser = EXCLUDED.browser,
    env = EXCLUDED.env,
    status = EXCLUDED.status,
    started_at_utc = EXCLUDED.started_at_utc,
    completed_at_utc = EXCLUDED.completed_at_utc,
    expires_at = EXCLUDED.expires_at;";
        cmd.Parameters.AddWithValue("@id", run.RunId);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@app", (object?)run.App ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@browser", (object?)run.Browser ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@env", (object?)run.Env ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)run.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@started", run.StartedAtUtc);
        cmd.Parameters.AddWithValue("@completed", (object?)run.CompletedAtUtc ?? DBNull.Value);
        var expires = _runsTtl is null ? (DateTime?)null : DateTime.UtcNow + _runsTtl.Value;
        cmd.Parameters.AddWithValue("@expires", (object?)expires ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ResultRunSummaryDto?> GetRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT run_json FROM runs WHERE run_id=@id AND (expires_at IS NULL OR expires_at > NOW()) LIMIT 1";
        cmd.Parameters.AddWithValue("@id", runId);
        var result = await cmd.ExecuteScalarAsync();
        if (result is string json)
        {
            try { return JsonSerializer.Deserialize<ResultRunSummaryDto>(json, JsonOpts); }
            catch { return null; }
        }
        return null;
    }

    public async Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100, string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        await EnsureCreatedAsync();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);
        var where = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
        var pars = new List<NpgsqlParameter>();
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("LOWER(status)=LOWER(@status)"); pars.Add(new NpgsqlParameter("@status", status)); }
        if (!string.IsNullOrWhiteSpace(app)) { where.Add("LOWER(app)=LOWER(@app)"); pars.Add(new NpgsqlParameter("@app", app)); }
        if (!string.IsNullOrWhiteSpace(browser)) { where.Add("LOWER(browser)=LOWER(@browser)"); pars.Add(new NpgsqlParameter("@browser", browser)); }
        if (!string.IsNullOrWhiteSpace(env)) { where.Add("LOWER(env)=LOWER(@env)"); pars.Add(new NpgsqlParameter("@env", env)); }
        var whereSql = where.Count > 0 ? ("WHERE " + string.Join(" AND ", where)) : string.Empty;
        var sql = $"SELECT run_json FROM runs {whereSql} ORDER BY started_at_utc DESC OFFSET @skip LIMIT @take";
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in pars) cmd.Parameters.Add(p);
        cmd.Parameters.AddWithValue("@skip", skip);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<ResultRunSummaryDto>(take);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var dto = SafeDeserialize<ResultRunSummaryDto>(json);
            if (dto is not null) list.Add(dto);
        }
        return list;
    }

    public async Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        await EnsureCreatedAsync();
        var where = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
        var pars = new List<NpgsqlParameter>();
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("LOWER(status)=LOWER(@status)"); pars.Add(new NpgsqlParameter("@status", status)); }
        if (!string.IsNullOrWhiteSpace(app)) { where.Add("LOWER(app)=LOWER(@app)"); pars.Add(new NpgsqlParameter("@app", app)); }
        if (!string.IsNullOrWhiteSpace(browser)) { where.Add("LOWER(browser)=LOWER(@browser)"); pars.Add(new NpgsqlParameter("@browser", browser)); }
        if (!string.IsNullOrWhiteSpace(env)) { where.Add("LOWER(env)=LOWER(@env)"); pars.Add(new NpgsqlParameter("@env", env)); }
        var whereSql = where.Count > 0 ? ("WHERE " + string.Join(" AND ", where)) : string.Empty;
        var sql = $"SELECT CAST(COUNT(*) AS INT) FROM runs {whereSql}";
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in pars) cmd.Parameters.Add(p);
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
        cmd.Parameters.AddWithValue("@run", ev.RunId);
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
        cmd.Parameters.AddWithValue("@run", runId);
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
                try { props = JsonSerializer.Deserialize<Dictionary<string, string>>(propsJson!, JsonOpts); } catch { }
            }
            var dto = new CommandLogEventDto
            {
                RunId = reader.GetString(0),
                TimestampUtc = reader.GetDateTime(1),
                Kind = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Message = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Props = props,
                TestId = reader.IsDBNull(5) ? null : reader.GetString(5)
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
        cmd.CommandText = @"SELECT CAST(COUNT(*) AS INT) FROM commands WHERE run_id=@run AND (expires_at IS NULL OR expires_at > NOW())";
        cmd.Parameters.AddWithValue("@run", runId);
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

    public async Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200, string? status = null)
    {
        await EnsureCreatedAsync();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var where = new List<string> { "run_id=@run", "(expires_at IS NULL OR expires_at > NOW())" };
        if (!string.IsNullOrWhiteSpace(status)) where.Add("LOWER(status)=LOWER(@status)");
        var whereSql = string.Join(" AND ", where);
        cmd.CommandText = $"SELECT test_json FROM tests WHERE {whereSql} ORDER BY title ASC OFFSET @skip LIMIT @take";
        cmd.Parameters.AddWithValue("@run", runId);
        if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@status", status!);
        cmd.Parameters.AddWithValue("@skip", skip);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<ResultTestCaseDto>(take);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var dto = SafeDeserialize<ResultTestCaseDto>(json);
            if (dto is not null) list.Add(dto);
        }
        return list;
    }

    private static TimeSpan? ParseResultsTtl(IConfiguration cfg)
    {
        if (int.TryParse(cfg["HUB_RESULTS_TTL_SECONDS"], out var sec) && sec > 0)
        {
            return TimeSpan.FromSeconds(sec);
        }
        if (int.TryParse(cfg["HUB_RESULTS_TTL_DAYS"], out var days) && days > 0)
        {
            return TimeSpan.FromDays(days);
        }
        if (int.TryParse(cfg["HUB_RESULTS_RETENTION_DAYS"], out var legacyDays) && legacyDays > 0)
        {
            return TimeSpan.FromDays(legacyDays);
        }
        return null;
    }

    private static TimeSpan? ParseLogsTtl(IConfiguration cfg)
    {
        if (int.TryParse(cfg["HUB_LOGS_TTL_SECONDS"], out var sec) && sec > 0)
        {
            return TimeSpan.FromSeconds(sec);
        }
        if (int.TryParse(cfg["HUB_LOGS_TTL_DAYS"], out var days) && days > 0)
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
                cmd.Parameters.AddWithValue("@id", runId);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM commands WHERE run_id = @id";
                cmd.Parameters.AddWithValue("@id", runId);
                await cmd.ExecuteNonQueryAsync();
            }

            int affected;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM runs WHERE run_id = @id";
                cmd.Parameters.AddWithValue("@id", runId);
                affected = await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return affected > 0;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { }
            return false;
        }
    }
}
