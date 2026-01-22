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
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Defines a request to finish a test item.
/// </summary>
public class FinishTestItemRequest
{
    /// <summary>
    ///     Gets or sets the description of the test item.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     Gets or sets the date and time when the test item execution is finished.
    /// </summary>
    public DateTime EndTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Gets or sets the status of the test item.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverterEx<Status>))]
    public Status Status { get; set; } = Status.Passed;

    /// <summary>
    ///     Gets or sets the issue of the test item.
    /// </summary>
    public Issue Issue { get; set; }

    /// <summary>
    ///     Gets or sets the list of attributes for the test item.
    /// </summary>
    public IList<ItemAttribute> Attributes { get; set; }

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
    ///     Gets or sets the launch UUID.
    /// </summary>
    public string LaunchUuid { get; set; }
}
