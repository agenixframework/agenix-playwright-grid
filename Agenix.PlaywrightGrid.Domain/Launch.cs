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

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Represents a test launch that groups multiple test runs together.
///     A project can contain zero, one, or many launches.
///     Each launch contains one or more test runs.
/// </summary>
public sealed class Launch
{
    /// <summary>
    ///     Unique identifier for the launch
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Name of the launch (e.g., "Demo Api Tests")
    ///     Multiple launches can share the same name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Description of the launch (e.g., "Demonstration launch.")
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Key-value attributes for the launch (e.g., ["tag1", "tag2", "platform:x64", "build:3.4.7.47.10"])
    /// </summary>
    public required string[] Attributes { get; init; }

    /// <summary>
    ///     The API key of the user who owns/created this launch
    /// </summary>
    public required string OwnerApiKey { get; init; }

    /// <summary>
    ///     The username of the owner (resolved from admin_users by API key)
    /// </summary>
    public string? OwnerUsername { get; init; }

    /// <summary>
    ///     The project key this launch belongs to
    /// </summary>
    public required string ProjectKey { get; init; }

    /// <summary>
    ///     When the launch started
    /// </summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>
    ///     When the launch finished (null if still running)
    /// </summary>
    public DateTimeOffset? FinishTime { get; init; }

    /// <summary>
    ///     Launch number for same-named launches (e.g., #5 is the fifth launch with the same name)
    /// </summary>
    public int LaunchNumber { get; init; }

    /// <summary>
    ///     Total count of test runs in this launch
    /// </summary>
    public int TotalTestRuns { get; init; }

    /// <summary>
    ///     Count of finished test runs
    /// </summary>
    public int FinishedTestRuns { get; init; }

    /// <summary>
    ///     Count of running test runs
    /// </summary>
    public int RunningTestRuns { get; init; }

    /// <summary>
    ///     Count of stopped test runs
    /// </summary>
    public int StoppedTestRuns { get; init; }

    /// <summary>
    ///     Count of errored test runs
    /// </summary>
    public int ErroredTestRuns { get; init; }

    /// <summary>
    ///     Whether this launch is marked as important (extends retention period)
    /// </summary>
    public bool IsImportant { get; init; }

    /// <summary>
    ///     Custom retention period in days (null means use default retention policy)
    /// </summary>
    public int? RetentionOverrideDays { get; init; }

    /// <summary>
    ///     Calculated duration in seconds (FinishTime - StartTime)
    /// </summary>
    public double? DurationSeconds
    {
        get
        {
            if (FinishTime.HasValue)
            {
                return (FinishTime.Value - StartTime).TotalSeconds;
            }

            return null;
        }
    }

    /// <summary>
    ///     Whether this launch is still running
    /// </summary>
    public bool IsRunning => !FinishTime.HasValue;
}
