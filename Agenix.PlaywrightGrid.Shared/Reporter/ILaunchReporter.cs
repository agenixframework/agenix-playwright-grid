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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;

namespace Agenix.PlaywrightGrid.Shared.Reporter;

/// <summary>
///     Represents a reporter for a launch in the ReportPortal system.
/// </summary>
public interface ILaunchReporter : IReporter
{
    /// <summary>
    ///     Gets the information about the launch reporter.
    /// </summary>
    ILaunchReporterInfo Info { get; }

    /// <summary>
    ///     Gets the list of child test reporters.
    /// </summary>
    IList<ITestReporter> ChildTestReporters { get; }

    /// <summary>
    ///     Starts a new launch with the specified start launch request.
    /// </summary>
    /// <param name="startLaunchRequest">The start launch request.</param>
    void Start(StartLaunchRequest startLaunchRequest);

    /// <summary>
    ///     Finishes the current launch with the specified finish launch request.
    /// </summary>
    /// <param name="finishLaunchRequest">The finish launch request.</param>
    void Finish(FinishLaunchRequest finishLaunchRequest);

    /// <summary>
    ///     Starts a child test reporter with the specified start test item request.
    /// </summary>
    /// <param name="startTestItemRequest">The start test item request.</param>
    /// <returns>The child test reporter.</returns>
    ITestReporter StartChildTestReporter(StartTestItemRequest startTestItemRequest);

    /// <summary>
    ///     Logs a new log item with the specified create log item request.
    /// </summary>
    /// <param name="createLogItemRequest">The create log item request.</param>
    void Log(CreateLogItemRequest createLogItemRequest);
}
