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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses;

/// <summary>
///     Represents a response object for querying a log item.
/// </summary>
public class LogItemResponse
{
    /// <summary>
    ///     Gets or sets the UUID of the log item.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    ///     Gets or sets the UUID of the test item this log belongs to.
    /// </summary>
    [JsonPropertyName("itemUuid")]
    public string ItemUuid { get; set; }

    /// <summary>
    ///     Gets or sets the UUID of the launch (optional).
    /// </summary>
    [JsonPropertyName("launchUuid")]
    public string? LaunchUuid { get; set; }

    /// <summary>
    ///     Gets or sets the timestamp of the log item.
    /// </summary>
    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    /// <summary>
    ///     Gets or sets the log level (TRACE, DEBUG, INFO, WARN, ERROR, FATAL).
    /// </summary>
    [JsonPropertyName("level")]
    public string Level { get; set; }

    /// <summary>
    ///     Gets or sets the log message text.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; }
}
