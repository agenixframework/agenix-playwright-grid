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
///     Represents a test reporter that is responsible for reporting test results.
/// </summary>
public interface ITestReporter : IReporter
{
    /// <summary>
    ///     Gets the information about the test reporter.
    /// </summary>
    ITestReporterInfo Info { get; }

    /// <summary>
    ///     Gets the parent test reporter.
    /// </summary>
    ITestReporter ParentTestReporter { get; }

    /// <summary>
    ///     Gets the launch reporter.
    /// </summary>
    ILaunchReporter LaunchReporter { get; }

    /// <summary>
    ///     Gets the list of child test reporters.
    /// </summary>
    IList<ITestReporter> ChildTestReporters { get; }

    /// <summary>
    ///     Starts the test item.
    /// </summary>
    /// <param name="startTestItemRequest">The request to start the test item.</param>
    void Start(StartTestItemRequest startTestItemRequest);

    /// <summary>
    ///     Finishes the test item.
    /// </summary>
    /// <param name="finishTestItemRequest">The request to finish the test item.</param>
    void Finish(FinishTestItemRequest finishTestItemRequest);

    /// <summary>
    ///     Starts a child test reporter.
    /// </summary>
    /// <param name="startTestItemRequest">The request to start the child test item.</param>
    /// <returns>The child test reporter.</returns>
    ITestReporter StartChildTestReporter(StartTestItemRequest startTestItemRequest);

    /// <summary>
    ///     Logs a log item.
    /// </summary>
    /// <param name="createLogItemRequest">The request to create the log item.</param>
    void Log(CreateLogItemRequest createLogItemRequest);
}
