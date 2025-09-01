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

using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Abstraction for persisting and querying run summaries, test cases, and command logs
///     used by the Hub Results HTTP endpoints and the SignalR ResultsHub consumed by the Dashboard.
///     Implementations may be in-memory and ephemeral (development/testing) or durable (e.g., Redis/DB).
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

    Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200,
        string? status = null);
}
