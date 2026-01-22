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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Models;

/// <summary>
///     Describes levels for log items.
/// </summary>
public enum LogLevel
{
    /// <summary>
    ///     Represents the TRACE log level.
    /// </summary>
    [JsonPropertyName("TRACE")] Trace,

    /// <summary>
    ///     Represents the DEBUG log level.
    /// </summary>
    [JsonPropertyName("DEBUG")] Debug,

    /// <summary>
    ///     Represents the INFO log level.
    /// </summary>
    [JsonPropertyName("INFO")] Info,

    /// <summary>
    ///     Represents the WARNING log level.
    /// </summary>
    [JsonPropertyName("WARN")] Warning,

    /// <summary>
    ///     Represents the ERROR log level.
    /// </summary>
    [JsonPropertyName("ERROR")] Error,

    /// <summary>
    ///     Represents the FATAL log level.
    /// </summary>
    [JsonPropertyName("FATAL")] Fatal
}
