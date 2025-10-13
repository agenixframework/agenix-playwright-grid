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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Filtering;

public enum FilterOperation
{
    [JsonPropertyName("eq")] Equals,

    [JsonPropertyName("ne")] NotEquals,

    [JsonPropertyName("cnt")] Contains,

    [JsonPropertyName("!cnt")] NotContains,

    [JsonPropertyName("ex")] Exists,

    [JsonPropertyName("in")] In,

    [JsonPropertyName("!in")] NotIn,

    [JsonPropertyName("gt")] GreaterThan,

    [JsonPropertyName("gte")] GreaterThanOrEquals,

    [JsonPropertyName("lt")] LowerThan,

    [JsonPropertyName("lte")] LowerThanOrEquals,

    [JsonPropertyName("btw")] Between,

    [JsonPropertyName("size")] Size,

    [JsonPropertyName("has")] Has,

    [JsonPropertyName("!has")] NotHas
}

public class Filter
{
    public Filter(FilterOperation operation, string field, object value, params object[] values)
    {
        Operation = operation;
        Field = field;
        Values = new List<object> { value };
        Values.AddRange(values);
    }

    public FilterOperation Operation { get; set; }

    public string Field { get; set; }

    public List<object> Values { get; set; }
}
