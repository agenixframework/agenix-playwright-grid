using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
/// Redis-backed implementation of IResultsStore. Persists runs, tests and command logs
/// with simple key schema and optional TTL-based retention.
/// </summary>
public sealed class RedisResultsStore(IDatabase db, IConfiguration config) : IResultsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly TimeSpan? _ttl = ParseTtl(config);

    private static TimeSpan? ParseTtl(IConfiguration cfg)
    {
        if (int.TryParse(cfg["HUB_RESULTS_RETENTION_DAYS"], out var days) && days > 0)
        {
            return TimeSpan.FromDays(days);
        }

        return null;
    }

    private static string RunKey(string runId)
    {
        return $"results:run:{runId}";
    }

    private static string RunsByStartKey => "results:runs:byStart";

    private static string TestsKey(string runId)
    {
        return $"results:tests:{runId}";
    }

    private static string CmdKey(string runId)
    {
        return $"results:cmd:{runId}";
    }

    private static string CmdCountKey(string runId)
    {
        return $"results:cmdcount:{runId}";
    }

    private void TouchExpire(params RedisKey[] keys)
    {
        if (_ttl is null)
        {
            return;
        }

        foreach (var k in keys)
        {
            try { db.KeyExpire(k, _ttl); }
            catch { }
        }
    }

    public async Task UpsertRunAsync(ResultRunSummaryDto run)
    {
        var runKey = RunKey(run.RunId);
        var json = JsonSerializer.Serialize(run, JsonOpts);
        // Store as JSON string for simplicity
        await db.StringSetAsync(runKey, json);
        TouchExpire(runKey);

        // Update primary index by start time (descending via ZREVRANGE later)
        var score = run.StartedAtUtc.Ticks;
        await db.SortedSetAddAsync(RunsByStartKey, run.RunId, score);
        TouchExpire(RunsByStartKey); // optional; keeps index during retention window
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
        TouchExpire(key);

        var cntKey = CmdCountKey(ev.RunId);
        await db.StringIncrementAsync(cntKey);
        TouchExpire(cntKey);
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
        TouchExpire(key);
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

    private static T? SafeDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return default; }
    }
}
