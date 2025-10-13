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
///     Represents an attribute of an item.
/// </summary>
public class ItemAttribute
{
    /// <summary>
    ///     Gets or sets the key of the attribute.
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    ///     Gets or sets the value of the attribute.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the attribute is a system attribute.
    /// </summary>
    [JsonPropertyName("system")]
    public bool IsSystem { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.IsNullOrEmpty(Key) ? Value : $"{Key}:{Value}";
    }
}
