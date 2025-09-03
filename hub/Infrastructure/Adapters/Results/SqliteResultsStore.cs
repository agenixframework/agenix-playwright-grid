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
using Microsoft.Data.Sqlite;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
///     SQLite-backed durable implementation of IResultsStore. Keeps runs, tests and command logs
///     without TTL; use Redis adapter for expiring/cached storage. Intended for optional durability.
/// </summary>
public sealed class SqliteResultsStore(IConfiguration config) : IResultsStore
{
    private readonly string _connString = config["HUB_RESULTS_SQLITE"] ?? "Data Source=results.db";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private bool _initialized;

    private async Task EnsureCreatedAsync()
    {
        if (_initialized) return;
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Runs (
    RunId TEXT PRIMARY KEY,
    RunJson TEXT NOT NULL,
    App TEXT,
    Browser TEXT,
    Env TEXT,
    Status TEXT,
    StartedAtUtc TEXT,
    CompletedAtUtc TEXT
);
CREATE INDEX IF NOT EXISTS IX_Runs_ByStart ON Runs(StartedAtUtc DESC);

CREATE TABLE IF NOT EXISTS Tests (
    RunId TEXT NOT NULL,
    TestId TEXT NOT NULL,
    TestJson TEXT NOT NULL,
    Status TEXT,
    Title TEXT,
    PRIMARY KEY (RunId, TestId)
);
CREATE INDEX IF NOT EXISTS IX_Tests_ByRun ON Tests(RunId);
CREATE INDEX IF NOT EXISTS IX_Tests_ByRunStatus ON Tests(RunId, Status);

CREATE TABLE IF NOT EXISTS Commands (
    RunId TEXT NOT NULL,
    TimestampUtc TEXT NOT NULL,
    Kind TEXT,
    Message TEXT,
    PropsJson TEXT,
    TestId TEXT
);
CREATE INDEX IF NOT EXISTS IX_Commands_ByRunTime ON Commands(RunId, TimestampUtc);
";
        await cmd.ExecuteNonQueryAsync();
        _initialized = true;
    }

    public async Task UpsertRunAsync(ResultRunSummaryDto run)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var json = JsonSerializer.Serialize(run, JsonOpts);
        cmd.CommandText = @"
INSERT INTO Runs (RunId, RunJson, App, Browser, Env, Status, StartedAtUtc, CompletedAtUtc)
VALUES ($id, $json, $app, $browser, $env, $status, $started, $completed)
ON CONFLICT(RunId) DO UPDATE SET
    RunJson=excluded.RunJson,
    App=excluded.App,
    Browser=excluded.Browser,
    Env=excluded.Env,
    Status=excluded.Status,
    StartedAtUtc=excluded.StartedAtUtc,
    CompletedAtUtc=excluded.CompletedAtUtc;";
        cmd.Parameters.AddWithValue("$id", run.RunId);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$app", run.App);
        cmd.Parameters.AddWithValue("$browser", run.Browser);
        cmd.Parameters.AddWithValue("$env", run.Env);
        cmd.Parameters.AddWithValue("$status", run.Status);
        cmd.Parameters.AddWithValue("$started", run.StartedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$completed", run.CompletedAtUtc?.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ResultRunSummaryDto?> GetRunAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT RunJson FROM Runs WHERE RunId=$id";
        cmd.Parameters.AddWithValue("$id", runId);
        var res = await cmd.ExecuteScalarAsync();
        if (res is string s)
        {
            try { return JsonSerializer.Deserialize<ResultRunSummaryDto>(s, JsonOpts); }
            catch { return null; }
        }
        return null;
    }

    public async Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100, string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        var where = new List<string>();
        var parms = new List<(string, object?)>();
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("Status = $status"); parms.Add(("$status", status)); }
        if (!string.IsNullOrWhiteSpace(app)) { where.Add("App = $app"); parms.Add(("$app", app)); }
        if (!string.IsNullOrWhiteSpace(browser)) { where.Add("Browser = $browser"); parms.Add(("$browser", browser)); }
        if (!string.IsNullOrWhiteSpace(env)) { where.Add("Env = $env"); parms.Add(("$env", env)); }
        var whereSql = where.Count > 0 ? (" WHERE " + string.Join(" AND ", where)) : string.Empty;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT RunJson FROM Runs{whereSql} ORDER BY StartedAtUtc DESC LIMIT $take OFFSET $skip";
        cmd.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 500));
        cmd.Parameters.AddWithValue("$skip", Math.Max(0, skip));
        foreach (var (k,v) in parms) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var list = new List<ResultRunSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var s = reader.GetString(0);
            try { var r = JsonSerializer.Deserialize<ResultRunSummaryDto>(s, JsonOpts); if (r is not null) list.Add(r); }
            catch { }
        }
        return list;
    }

    public async Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        var where = new List<string>();
        var parms = new List<(string, object?)>();
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("Status = $status"); parms.Add(("$status", status)); }
        if (!string.IsNullOrWhiteSpace(app)) { where.Add("App = $app"); parms.Add(("$app", app)); }
        if (!string.IsNullOrWhiteSpace(browser)) { where.Add("Browser = $browser"); parms.Add(("$browser", browser)); }
        if (!string.IsNullOrWhiteSpace(env)) { where.Add("Env = $env"); parms.Add(("$env", env)); }
        var whereSql = where.Count > 0 ? (" WHERE " + string.Join(" AND ", where)) : string.Empty;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM Runs{whereSql}";
        foreach (var (k,v) in parms) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var res = await cmd.ExecuteScalarAsync();
        return res is long l ? (int)l : Convert.ToInt32(res ?? 0);
    }

    public async Task AppendCommandAsync(CommandLogEventDto ev)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var propsJson = ev.Props is null ? null : JsonSerializer.Serialize(ev.Props, JsonOpts);
        cmd.CommandText = @"
INSERT INTO Commands (RunId, TimestampUtc, Kind, Message, PropsJson, TestId)
VALUES ($runId, $ts, $kind, $msg, $props, $testId);";
        cmd.Parameters.AddWithValue("$runId", ev.RunId);
        cmd.Parameters.AddWithValue("$ts", ev.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$kind", ev.Kind ?? string.Empty);
        cmd.Parameters.AddWithValue("$msg", ev.Message ?? string.Empty);
        cmd.Parameters.AddWithValue("$props", (object?)propsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$testId", (object?)ev.TestId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT RunId, TimestampUtc, Kind, Message, PropsJson, TestId
FROM Commands WHERE RunId=$runId
ORDER BY TimestampUtc ASC
LIMIT $take OFFSET $skip";
        cmd.Parameters.AddWithValue("$runId", runId);
        cmd.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 1000));
        cmd.Parameters.AddWithValue("$skip", Math.Max(0, skip));
        var list = new List<CommandLogEventDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ts = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);
            Dictionary<string, string>? props = null;
            if (!reader.IsDBNull(4))
            {
                try { props = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(4), JsonOpts); }
                catch { props = null; }
            }
            list.Add(new CommandLogEventDto
            {
                RunId = reader.GetString(0),
                TimestampUtc = ts,
                Kind = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Message = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Props = props,
                TestId = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
        return list;
    }

    public async Task<int> GetCommandCountAsync(string runId)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Commands WHERE RunId=$runId";
        cmd.Parameters.AddWithValue("$runId", runId);
        var res = await cmd.ExecuteScalarAsync();
        return res is long l ? (int)l : Convert.ToInt32(res ?? 0);
    }

    public async Task UpsertTestAsync(ResultTestCaseDto test)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var json = JsonSerializer.Serialize(test, JsonOpts);
        cmd.CommandText = @"
INSERT INTO Tests (RunId, TestId, TestJson, Status, Title)
VALUES ($runId, $testId, $json, $status, $title)
ON CONFLICT(RunId, TestId) DO UPDATE SET
    TestJson=excluded.TestJson,
    Status=excluded.Status,
    Title=excluded.Title;";
        cmd.Parameters.AddWithValue("$runId", test.RunId);
        cmd.Parameters.AddWithValue("$testId", test.TestId);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$status", test.Status);
        cmd.Parameters.AddWithValue("$title", test.Title);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200, string? status = null)
    {
        await EnsureCreatedAsync();
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();
        var where = new List<string> { "RunId = $runId" };
        if (!string.IsNullOrWhiteSpace(status)) where.Add("Status = $status");
        var whereSql = string.Join(" AND ", where);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TestJson FROM Tests WHERE {whereSql} ORDER BY Title ASC LIMIT $take OFFSET $skip";
        cmd.Parameters.AddWithValue("$runId", runId);
        if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 1000));
        cmd.Parameters.AddWithValue("$skip", Math.Max(0, skip));
        var list = new List<ResultTestCaseDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var s = reader.GetString(0);
            try { var t = JsonSerializer.Deserialize<ResultTestCaseDto>(s, JsonOpts); if (t is not null) list.Add(t); }
            catch { }
        }
        return list;
    }
}
