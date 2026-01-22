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

/// <summary>
///     Represents the sort direction.
/// </summary>
public enum SortDirection
{
    /// <summary>
    ///     Represents the ascending sort direction.
    /// </summary>
    [JsonPropertyName("ASC")] Ascending,

    /// <summary>
    ///     Represents the descending sort direction.
    /// </summary>
    [JsonPropertyName("DESC")] Descending
}

/// <summary>
///     Represents the sorting criteria.
/// </summary>
public class Sorting
{
    /// <summary>
    ///     Initializes a new instance of the Sorting class with the specified fields and direction.
    /// </summary>
    /// <param name="byFields">The list of fields to sort by.</param>
    /// <param name="direction">The sort direction.</param>
    public Sorting(List<string> byFields, SortDirection direction = SortDirection.Ascending)
    {
        Fields = byFields;
        Direction = direction;
    }

    /// <summary>
    ///     Gets or sets the list of fields to sort by.
    /// </summary>
    public List<string> Fields { get; set; }

    /// <summary>
    ///     Gets or sets the sort direction.
    /// </summary>
    public SortDirection Direction { get; set; }
}
