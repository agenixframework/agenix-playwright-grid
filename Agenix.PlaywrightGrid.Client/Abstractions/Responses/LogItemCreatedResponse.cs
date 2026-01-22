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
///     Represents the response for creating a single log item.
/// </summary>
public class LogItemCreatedResponse
{
    /// <summary>
    ///     Gets or sets the UUID of the created log item.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }
}

/// <summary>
///     Represents the response for creating multiple log items (batch operation).
/// </summary>
public class LogItemsCreatedResponse
{
    /// <summary>
    ///     Gets or sets the list of created log item responses.
    /// </summary>
    [JsonPropertyName("responses")]
    public List<LogItemCreatedResponse> Responses { get; set; }
}
