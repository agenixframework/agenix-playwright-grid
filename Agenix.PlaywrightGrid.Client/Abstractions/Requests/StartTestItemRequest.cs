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
///     Defines a content of request for service to create new test item in progress state.
/// </summary>
public class StartTestItemRequest
{
    /// <summary>
    ///     Gets or sets the UUID of the launch.
    /// </summary>
    public string LaunchUuid { get; set; }

    /// <summary>
    ///     Gets or sets the label key for browser pool selection (e.g., "myapp:chromium:staging").
    ///     Required for Test-type items that need browser borrowing.
    ///     Format: "application:browser:environment" or "application:browser:environment:region".
    /// </summary>
    public string LabelKey { get; set; }

    /// <summary>
    ///     Gets or sets the UUID of the parent test item (for nested hierarchies).
    ///     Null for root items (e.g., Suite, top-level Test).
    ///     Example: Step's parent is a Test, Test's parent might be a Scenario.
    /// </summary>
    [JsonPropertyName("parentItemId")]
    public string ParentItemId { get; set; }

    /// <summary>
    ///     Gets or sets the name of the test item.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the description of the test item.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     Gets or sets the date and time when the test item execution is started.
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Gets or sets the type of the test item.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverterEx<TestItemType>))]
    public TestItemType Type { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether this is a retry.
    /// </summary>
    [JsonPropertyName("retry")]
    public bool IsRetry { get; set; }

    /// <summary>
    ///     Gets or sets the identifier of the test item being retried.
    /// </summary>
    public string RetryOf { get; set; }

    /// <summary>
    ///     Gets or sets the list of parameters for the test item.
    /// </summary>
    public IList<KeyValuePair<string, string>> Parameters { get; set; }

    /// <summary>
    ///     Gets or sets the unique ID of the test item.
    /// </summary>
    public string UniqueId { get; set; }

    /// <summary>
    ///     Gets or sets the test case ID.
    /// </summary>
    public string TestCaseId { get; set; }

    /// <summary>
    ///     Gets or sets the code reference.
    /// </summary>
    [JsonPropertyName("codeRef")]
    public string CodeReference { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the test item has statistics.
    /// </summary>
    public bool HasStats { get; set; } = true;

    /// <summary>
    ///     Gets or sets the list of attributes for the test item.
    /// </summary>
    public IList<ItemAttribute> Attributes { get; set; }
}
