using System.Collections.Generic;
using System.Threading.Tasks;
using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Application.Ports;

/// <summary>
/// Abstraction for persisting and querying run summaries, test cases, and command logs
/// used by the Hub Results HTTP endpoints and the SignalR ResultsHub consumed by the Dashboard.
/// Implementations may be in-memory and ephemeral (development/testing) or durable (e.g., Redis/DB).
/// </summary>
public interface IResultsStore
{
    Task UpsertRunAsync(ResultRunSummaryDto run);
    Task<ResultRunSummaryDto?> GetRunAsync(string runId);
    Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100,
        string? status = null, string? app = null, string? browser = null, string? env = null);
    Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null, string? env = null);

    Task AppendCommandAsync(CommandLogEventDto ev);
    Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200);
    Task<int> GetCommandCountAsync(string runId);

    Task UpsertTestAsync(ResultTestCaseDto test);
    Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200, string? status = null);
}
