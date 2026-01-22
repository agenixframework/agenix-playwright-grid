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

using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Defines a request for creating user filters.
/// </summary>
public class CreateUserFilterRequest
{
    /// <summary>
    ///     Gets or sets the name of the user filter.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the description of the user filter.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     Gets or sets the list of conditions to filter data.
    /// </summary>
    public IEnumerable<Condition> Conditions { get; set; }

    /// <summary>
    ///     Gets or sets the list of parameters of selection.
    /// </summary>
    public IEnumerable<FilterOrder> Orders { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the filter is shared.
    /// </summary>
    [JsonPropertyName("share")]
    public bool IsShared { get; set; }

    /// <summary>
    ///     Gets or sets the owner of the filter.
    /// </summary>
    [DataMember(Name = "owner")]
    public string Owner { get; set; }

    /// <summary>
    ///     Gets or sets the user filter type enum.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverterEx<UserFilterType>))]
    public UserFilterType UserFilterType { get; set; }
}
