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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Defines a request to update test item metadata (name, description, attributes, code reference).
///     Does not affect browser session or status fields.
/// </summary>
public class UpdateTestItemRequest
{
    /// <summary>
    ///     Gets or sets the name of the test item.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the description of the test item.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     Gets or sets the attributes for the test item.
    /// </summary>
    public List<ItemAttribute> Attributes { get; set; }

    /// <summary>
    ///     Gets or sets the code reference (e.g., test file path and line number).
    /// </summary>
    [JsonPropertyName("codeRef")]
    public string CodeReference { get; set; }

    /// <summary>
    ///     Gets or sets the unique identifier for the test item.
    /// </summary>
    public string UniqueId { get; set; }
}
