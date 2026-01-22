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
        var runs = await store.GetRunsAsync();
        await Clients.Caller.RunsIndex([.. runs]);
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
