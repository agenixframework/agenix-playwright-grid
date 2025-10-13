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
///     Defines a request to analyze a launch.
/// </summary>
public class AnalyzeLaunchRequest
{
    /// <summary>
    ///     Gets or sets the ID of the launch to be analyzed.
    /// </summary>
    public long LaunchId { get; set; }

    /// <summary>
    ///     Gets or sets the mode of the analyzer.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverterEx<AnalyzerMode>))]
    public AnalyzerMode AnalyzerMode { get; set; }

    /// <summary>
    ///     Gets or sets the name of the analyzer type.
    /// </summary>
    public string AnalyzerTypeName { get; set; }

    /// <summary>
    ///     Gets or sets the mode of the analyzer items.
    /// </summary>
    [JsonPropertyName("analyzeItemsMode")]
    public List<string> AnalyzerItemsMode { get; set; }
}
