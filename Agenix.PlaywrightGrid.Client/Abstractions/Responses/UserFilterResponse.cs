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
using Agenix.PlaywrightGrid.Client.Abstractions.Filtering;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses;

public class UserFilterResponse
{
    public long Id { get; set; }
    public string Description { get; set; }
    public IList<Condition> Conditions { get; set; }
    public string Name { get; set; }
    public IList<FilterOrder> Orders { get; set; }

    [JsonPropertyName("share")] public bool IsShared { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverterEx<UserFilterType>))]
    public UserFilterType UserFilterType { get; set; }

    public string Owner { get; set; }
}

public class Condition
{
    [JsonPropertyName("condition")]
    [JsonConverter(typeof(JsonStringEnumConverterEx<FilterOperation>))]
    public FilterOperation UserFilterCondition { get; set; }

    public string FilteringField { get; set; }
    public string Value { get; set; }
}

public class FilterOrder
{
    [JsonPropertyName("isAsc")] public bool Asc { get; set; }

    public string SortingColumn { get; set; }
}
