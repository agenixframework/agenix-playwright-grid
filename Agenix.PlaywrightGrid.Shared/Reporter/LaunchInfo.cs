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

namespace Agenix.PlaywrightGrid.Shared.Reporter;

/// <summary>
///     Represents the information about a launch reporter.
/// </summary>
public class LaunchInfo : ILaunchReporterInfo
{
    /// <summary>
    ///     Gets or sets the UUID of the launch.
    /// </summary>
    public string Uuid { get; set; }

    /// <summary>
    ///     Gets or sets the name of the launch.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the start time of the launch.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    ///     Gets or sets the finish time of the launch.
    /// </summary>
    public DateTime? FinishTime { get; set; }

    /// <summary>
    ///     Gets or sets the URL of the launch.
    /// </summary>
    public string Url { get; set; }
}
