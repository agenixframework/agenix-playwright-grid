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

namespace Agenix.PlaywrightGrid.Integration.Tests.Models;

/// <summary>
///     Shared test data models used across integration tests.
/// </summary>
/// <summary>
///     Represents a row in the history matrix (launch or suite history).
/// </summary>
public class HistoryRow
{
    /// <summary>
    ///     Gets or sets the item name (e.g., test name, suite name).
    /// </summary>
    public string ItemName { get; set; } = "";

    /// <summary>
    ///     Gets or sets the item type (e.g., Test, Suite, Scenario).
    /// </summary>
    public string ItemType { get; set; } = "";

    /// <summary>
    ///     Gets or sets the launches containing this item.
    /// </summary>
    public List<LaunchData> Launches { get; set; } = new();
}

/// <summary>
///     Represents launch data in the history matrix.
/// </summary>
public class LaunchData
{
    /// <summary>
    ///     Gets or sets the launch ID.
    /// </summary>
    [JsonPropertyName("launchId")]
    public Guid LaunchId { get; set; }

    /// <summary>
    ///     Gets or sets the launch number.
    /// </summary>
    [JsonPropertyName("launchNumber")]
    public int LaunchNumber { get; set; }

    /// <summary>
    ///     Gets or sets the start time of the launch.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    ///     Gets or sets the aggregated status for this item in this launch.
    ///     Values: Passed, Failed, InProgress, Skipped, Mixed, Empty.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>
    ///     Gets or sets the tooltip data with detailed status breakdown.
    /// </summary>
    [JsonPropertyName("tooltip")]
    public Dictionary<string, object?>? Tooltip { get; set; }
}

/// <summary>
///     Represents the result of a test item creation operation.
/// </summary>
public class TestItemCreateResult
{
    /// <summary>
    ///     Gets or sets the run ID (UUID) of the created test item.
    /// </summary>
    public Guid RunId { get; set; }

    /// <summary>
    ///     Gets or sets the database ID of the created test item.
    /// </summary>
    public long DbId { get; set; }
}

/// <summary>
///     Represents a test user with API key and membership.
/// </summary>
public class TestUserInfo
{
    /// <summary>
    ///     Gets or sets the user ID.
    /// </summary>
    public string UserId { get; set; } = "";

    /// <summary>
    ///     Gets or sets the username.
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    ///     Gets or sets the API key name.
    /// </summary>
    public string ApiKeyName { get; set; } = "";

    /// <summary>
    ///     Gets or sets the plain text API key value.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    ///     Gets or sets the SHA256 hash of the API key.
    /// </summary>
    public string ApiKeyHash { get; set; } = "";

    /// <summary>
    ///     Gets or sets the project key this user is a member of.
    /// </summary>
    public string ProjectKey { get; set; } = "";
}
