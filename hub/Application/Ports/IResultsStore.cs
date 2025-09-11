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
    /// <summary>
    ///     Inserts or updates a run summary.
    /// </summary>
    /// <param name="run">Run summary to upsert.</param>
    Task UpsertRunAsync(ResultRunSummaryDto run);

    /// <summary>
    ///     Retrieves a run summary by its id.
    /// </summary>
    /// <param name="runId">The unique run identifier.</param>
    /// <returns>The run summary or null if not found.</returns>
    Task<ResultRunSummaryDto?> GetRunAsync(string runId);

    /// <summary>
    ///     Pages run summaries optionally filtered by status/app/browser/env.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take (page size).</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="app">Optional app filter.</param>
    /// <param name="browser">Optional browser filter.</param>
    /// <param name="env">Optional environment filter.</param>
    Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100,
        string? status = null, string? app = null, string? browser = null, string? env = null);

    /// <summary>
    ///     Returns the total number of runs for the given optional filters.
    /// </summary>
    Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null, string? env = null);

    /// <summary>
    ///     Appends a command/log event for a run.
    /// </summary>
    /// <param name="ev">The event to store.</param>
    Task AppendCommandAsync(CommandLogEventDto ev);

    /// <summary>
    ///     Pages command/log events for a specific run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200);

    /// <summary>
    ///     Returns the total number of command/log events for a run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    Task<int> GetCommandCountAsync(string runId);

    /// <summary>
    ///     Inserts or updates a test case record.
    /// </summary>
    /// <param name="test">Test case to upsert.</param>
    Task UpsertTestAsync(ResultTestCaseDto test);

    /// <summary>
    ///     Pages test cases for a specific run with optional status filter.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="status">Optional test status filter.</param>
    Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200,
        string? status = null);

    /// <summary>
    ///     Deletes a run and all associated tests and command logs.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>True if a run existed and was deleted; otherwise false.</returns>
    Task<bool> DeleteRunAsync(string runId);
}
