
using System.Collections.Concurrent;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
/// Default in-memory implementation of IResultsStore used by the Hub to keep
/// transient run summaries, command logs, and test cases. This store is suitable
/// for development and local testing: data lives only in the Hub process memory,
/// is capped (e.g., last 5000 commands per run), and is lost on hub restart or
/// scale-out. A durable adapter (e.g., Redis/DB) can replace this via DI.
/// </summary>
public sealed class InMemoryResultsStore : IResultsStore
{
    private readonly ConcurrentDictionary<string, ResultRunSummaryDto> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CommandLogEventDto>> _cmd = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ResultTestCaseDto>> _tests = new(StringComparer.OrdinalIgnoreCase);

    public Task UpsertRunAsync(ResultRunSummaryDto run)
    {
        _runs.AddOrUpdate(run.RunId, run, (_, __) => run);
        return Task.CompletedTask;
    }

    public Task<ResultRunSummaryDto?> GetRunAsync(string runId)
    {
        _runs.TryGetValue(runId, out var run);
        return Task.FromResult(run);
    }

    public Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100, string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        IEnumerable<ResultRunSummaryDto> q = _runs.Values;
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(app)) q = q.Where(r => string.Equals(r.App, app, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(browser)) q = q.Where(r => string.Equals(r.Browser, browser, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(env)) q = q.Where(r => string.Equals(r.Env, env, StringComparison.OrdinalIgnoreCase));
        var list = q.OrderByDescending(r => r.StartedAtUtc).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500)).ToList();
        return Task.FromResult((IReadOnlyList<ResultRunSummaryDto>)list);
    }

    public Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null, string? env = null)
    {
        IEnumerable<ResultRunSummaryDto> q = _runs.Values;
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(app)) q = q.Where(r => string.Equals(r.App, app, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(browser)) q = q.Where(r => string.Equals(r.Browser, browser, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(env)) q = q.Where(r => string.Equals(r.Env, env, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(q.Count());
    }

    public Task AppendCommandAsync(CommandLogEventDto ev)
    {
        var queue = _cmd.GetOrAdd(ev.RunId, _ => new ConcurrentQueue<CommandLogEventDto>());
        queue.Enqueue(ev);

        // Cap to a reasonable memory footprint (e.g., keep last 5000)
        while (queue.Count > 5000 && queue.TryDequeue(out _)) { }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200)
    {
        if (!_cmd.TryGetValue(runId, out var queue))
            return Task.FromResult((IReadOnlyList<CommandLogEventDto>)Array.Empty<CommandLogEventDto>());

        var arr = queue.ToArray();
        var page = arr.OrderBy(c => c.TimestampUtc).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 1000)).ToList();
        return Task.FromResult((IReadOnlyList<CommandLogEventDto>)page);
    }

    public Task<int> GetCommandCountAsync(string runId)
    {
        if (!_cmd.TryGetValue(runId, out var queue))
            return Task.FromResult(0);
        return Task.FromResult(queue.Count);
    }

    public Task UpsertTestAsync(ResultTestCaseDto test)
    {
        var dict = _tests.GetOrAdd(test.RunId, _ => new ConcurrentDictionary<string, ResultTestCaseDto>(StringComparer.OrdinalIgnoreCase));
        dict.AddOrUpdate(test.TestId, test, (_, __) => test);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200, string? status = null)
    {
        if (!_tests.TryGetValue(runId, out var dict))
            return Task.FromResult((IReadOnlyList<ResultTestCaseDto>)Array.Empty<ResultTestCaseDto>());
        IEnumerable<ResultTestCaseDto> q = dict.Values;
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));
        var list = q.OrderBy(t => t.Title).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 1000)).ToList();
        return Task.FromResult((IReadOnlyList<ResultTestCaseDto>)list);
    }
}
