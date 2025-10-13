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

using System.Text.Json.Serialization;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Defines a request to merge launches.
/// </summary>
public class MergeLaunchesRequest
{
    /// <summary>
    ///     Gets or sets the short name of the launch.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the launch description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     Gets or sets the launch execution mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverterEx<LaunchMode>))]
    public LaunchMode Mode { get; set; }

    /// <summary>
    ///     Gets or sets the launch start datetime.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    ///     Gets or sets the launch end datetime.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    ///     Gets or sets the list of launch IDs to merge.
    /// </summary>
    public List<long> Launches { get; set; }

    /// <summary>
    ///     Gets or sets the type of launch merge.
    /// </summary>
    public string MergeType { get; set; }
}
