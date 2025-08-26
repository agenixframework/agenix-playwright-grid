using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.SignalR;

public interface IResultsClient
{
    Task RunsIndex(ResultRunSummaryDto[] page);
    Task RunUpdate(ResultRunSummaryDto run);
    Task TestUpdate(ResultTestCaseDto test);
    Task CommandLogChunk(CommandLogEventDto[] items);
}

public sealed class ResultsHub(IResultsStore store) : Hub<IResultsClient>
{
    public override async Task OnConnectedAsync()
    {
        // Send initial runs page to the caller for the index view
        var runs = await store.GetRunsAsync(0, 100);
        await Clients.Caller.RunsIndex(runs.ToArray());
        await base.OnConnectedAsync();
    }

    public Task JoinRun(string runId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, $"run:{runId}");
    }

    public Task LeaveRun(string runId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run:{runId}");
    }
}
