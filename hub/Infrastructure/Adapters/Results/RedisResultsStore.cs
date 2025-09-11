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
using Agenix.PlaywrightGrid.Domain;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
///     Redis-backed implementation of IResultsStore. Persists runs, tests and command logs
///     with simple key schema and optional TTL-based retention.
/// </summary>
public sealed class RedisResultsStore(IDatabase db, IConfiguration config) : IResultsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Separate TTLs for run/test results vs. verbose command logs
    private readonly TimeSpan? _runsTtl = ParseResultsTtl(config);
    private readonly TimeSpan? _logsTtl = ParseLogsTtl(config);

    private static string RunsByStartKey => RedisKeys.ResultsRunsByStart();

    public async Task UpsertRunAsync(ResultRunSummaryDto run)
    {
        var runKey = RunKey(run.RunId);
        var json = JsonSerializer.Serialize(run, JsonOpts);
        // Store as JSON string for simplicity
        await db.StringSetAsync(runKey, json);
        TouchExpire(_runsTtl, runKey);

        // Persist optional RunName alongside RunId for quick lookups/display
        var rnKey = RunNameKey(run.RunId);
        if (!string.IsNullOrWhiteSpace(run.RunName))
        {
            await db.StringSetAsync(rnKey, run.RunName);
            TouchExpire(_runsTtl, rnKey);
        }
        else
        {
            try { await db.KeyDeleteAsync((RedisKey)rnKey); } catch { }
        }

        // Update primary index by start time (descending via ZREVRANGE later)
        var score = run.StartedAtUtc.Ticks;
        await db.SortedSetAddAsync(RunsByStartKey, run.RunId, score);
        TouchExpire(_runsTtl, RunsByStartKey); // optional; keeps index during retention window
    }

    public async Task<ResultRunSummaryDto?> GetRunAsync(string runId)
    {
        var val = await db.StringGetAsync(RunKey(runId));
        if (val.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ResultRunSummaryDto>(val!, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100,
        string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);

        // Fast path: no filters -> page directly from ZSET
        if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(app) && string.IsNullOrWhiteSpace(browser) &&
            string.IsNullOrWhiteSpace(env))
        {
            var end = skip + take - 1;
            var ids = await db.SortedSetRangeByRankAsync(RunsByStartKey, skip, end, Order.Descending);
            var tasks = ids.Select(id => db.StringGetAsync(RunKey(id!))).ToArray();
            await Task.WhenAll(tasks);
            var list = tasks
                .Select(t => t.Result)
                .Where(v => !v.IsNullOrEmpty)
                .Select(v => SafeDeserialize<ResultRunSummaryDto>(v!))
                .Where(r => r is not null)
                .Select(r => r!)
                .OrderByDescending(r => r.StartedAtUtc)
                .ToList();
            return list;
        }

        // Filtered path: iterate pages from ZSET, client-side filter
        var collected = new List<ResultRunSummaryDto>(take);
        var needed = skip + take;
        var pageSize = Math.Max(200, take);
        long offset = 0;
        while (collected.Count < needed)
        {
            var end = offset + pageSize - 1;
            var ids = await db.SortedSetRangeByRankAsync(RunsByStartKey, offset, end, Order.Descending);
            if (ids.Length == 0)
            {
                break;
            }

            var tasks = ids.Select(id => db.StringGetAsync(RunKey(id!))).ToArray();
            await Task.WhenAll(tasks);
            foreach (var val in tasks.Select(t => t.Result))
            {
                if (val.IsNullOrEmpty)
                {
                    continue;
                }

                var run = SafeDeserialize<ResultRunSummaryDto>(val!);
                if (run is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(run.Status, status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(app) && !string.Equals(run.App, app, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(browser) &&
                    !string.Equals(run.Browser, browser, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(env) && !string.Equals(run.Env, env, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                collected.Add(run);
                if (collected.Count >= needed)
                {
                    break;
                }
            }

            offset += pageSize;
        }

        var result = collected
            .OrderByDescending(r => r.StartedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList();
        return result;
    }

    public async Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null,
        string? env = null)
    {
        // Fast path when no filters: use ZCARD
        if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(app) && string.IsNullOrWhiteSpace(browser) &&
            string.IsNullOrWhiteSpace(env))
        {
            var len = await db.SortedSetLengthAsync(RunsByStartKey);
            return (int)len;
        }

        // Filtered count: iterate through the ZSET and count matches
        long count = 0;
        long offset = 0;
        const int pageSize = 500;
        while (true)
        {
            var end = offset + pageSize - 1;
            var ids = await db.SortedSetRangeByRankAsync(RunsByStartKey, offset, end, Order.Descending);
            if (ids.Length == 0)
            {
                break;
            }

            var tasks = ids.Select(id => db.StringGetAsync(RunKey(id!))).ToArray();
            await Task.WhenAll(tasks);
            foreach (var val in tasks.Select(t => t.Result))
            {
                if (val.IsNullOrEmpty)
                {
                    continue;
                }

                var run = SafeDeserialize<ResultRunSummaryDto>(val!);
                if (run is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(run.Status, status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(app) && !string.Equals(run.App, app, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(browser) &&
                    !string.Equals(run.Browser, browser, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(env) && !string.Equals(run.Env, env, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
            }

            offset += pageSize;
        }

        return (int)count;
    }

    public async Task AppendCommandAsync(CommandLogEventDto ev)
    {
        var key = CmdKey(ev.RunId);
        var json = JsonSerializer.Serialize(ev, JsonOpts);
        await db.ListRightPushAsync(key, json);
        // Keep a hard cap to avoid unbounded growth
        await db.ListTrimAsync(key, -5000, -1);
        TouchExpire(_logsTtl, key);

        var cntKey = CmdCountKey(ev.RunId);
        await db.StringIncrementAsync(cntKey);
        TouchExpire(_logsTtl, cntKey);
    }

    public async Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);
        var vals = await db.ListRangeAsync(CmdKey(runId), skip, skip + take - 1);
        var list = new List<CommandLogEventDto>(vals.Length);
        foreach (var v in vals)
        {
            if (v.IsNullOrEmpty)
            {
                continue;
            }

            var ev = SafeDeserialize<CommandLogEventDto>(v!);
            if (ev is not null)
            {
                list.Add(ev);
            }
        }

        return list.OrderBy(e => e.TimestampUtc).ToList();
    }

    public async Task<int> GetCommandCountAsync(string runId)
    {
        var cnt = await db.StringGetAsync(CmdCountKey(runId));
        if (!cnt.IsNullOrEmpty && int.TryParse(cnt!, out var parsed))
        {
            return parsed;
        }

        var llen = await db.ListLengthAsync(CmdKey(runId));
        return (int)llen;
    }

    public async Task UpsertTestAsync(ResultTestCaseDto test)
    {
        var key = TestsKey(test.RunId);
        var json = JsonSerializer.Serialize(test, JsonOpts);
        await db.HashSetAsync(key, test.TestId, json);
        TouchExpire(_runsTtl, key);
    }

    public async Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200,
        string? status = null)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);
        var vals = await db.HashValuesAsync(TestsKey(runId));
        var q = vals
            .Where(v => !v.IsNullOrEmpty)
            .Select(v => SafeDeserialize<ResultTestCaseDto>(v!))
            .Where(t => t is not null)
            .Select(t => t!)
            .AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.OrderBy(t => t.Title).Skip(skip).Take(take).ToList();
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
        // Legacy fallback for backward compatibility
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
        // Default to results TTL if logs TTL is unspecified
        return ParseResultsTtl(cfg);
    }

    private static string RunKey(string runId)
    {
        return RedisKeys.ResultsRun(runId);
    }

    private static string TestsKey(string runId)
    {
        return RedisKeys.ResultsTests(runId);
    }

    private static string CmdKey(string runId)
    {
        return RedisKeys.ResultsCmd(runId);
    }

    private static string CmdCountKey(string runId)
    {
        return RedisKeys.ResultsCmdCount(runId);
    }

    private static string RunNameKey(string runId)
    {
        return RedisKeys.ResultsRunName(runId);
    }

    private void TouchExpire(TimeSpan? ttl, params RedisKey[] keys)
    {
        if (ttl is null)
        {
            return;
        }

        foreach (var k in keys)
        {
            try { db.KeyExpire(k, ttl); }
            catch { }
        }
    }

    private static T? SafeDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return default; }
    }

    public async Task<bool> DeleteRunAsync(string runId)
    {
        var runKey = RunKey(runId);
        bool existed = false;
        try
        {
            existed = await db.KeyDeleteAsync(runKey);
        }
        catch { }

        try { await db.KeyDeleteAsync(TestsKey(runId)); } catch { }
        try { await db.KeyDeleteAsync(CmdKey(runId)); } catch { }
        try { await db.KeyDeleteAsync(CmdCountKey(runId)); } catch { }
        try { await db.KeyDeleteAsync((RedisKey)RunNameKey(runId)); } catch { }
        try { await db.SortedSetRemoveAsync(RunsByStartKey, runId); } catch { }
        return existed;
    }
}
