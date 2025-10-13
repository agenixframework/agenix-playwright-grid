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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Request to create a log item for a test item.
/// </summary>
public class CreateLogItemRequest
{
    /// <summary>
    ///     Gets or sets the UUID of the test item this log belongs to.
    /// </summary>
    [JsonPropertyName("itemUuid")]
    public string TestItemUuid { get; set; }

    /// <summary>
    ///     Gets or sets the UUID of the launch (optional, can be inferred from test item).
    /// </summary>
    [JsonPropertyName("launchUuid")]
    public string? LaunchUuid { get; set; }

    /// <summary>
    ///     Gets or sets the timestamp of the log item.
    /// </summary>
    [JsonPropertyName("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Gets or sets the log level (TRACE, DEBUG, INFO, WARN, ERROR, FATAL).
    /// </summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "INFO";

    /// <summary>
    ///     Gets or sets the log message text.
    /// </summary>
    [JsonPropertyName("message")]
    public string Text { get; set; }

    /// <summary>
    ///     Gets or sets the optional file attachment.
    /// </summary>
    [JsonPropertyName("file")]
    public LogItemAttach? Attach { get; set; }
}

/// <summary>
///     Represents a file attachment for a log item.
/// </summary>
public class LogItemAttach
{
    /// <summary>
    ///     Gets or sets the name of the attachment file.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    ///     Gets or sets the base64-encoded file data.
    /// </summary>
    [JsonPropertyName("data")]
    public string? DataBase64 { get; set; }

    /// <summary>
    ///     Gets the decoded byte array from DataBase64.
    /// </summary>
    [JsonIgnore]
    public byte[]? Data => string.IsNullOrWhiteSpace(DataBase64) ? null : Convert.FromBase64String(DataBase64);

    /// <summary>
    ///     Gets or sets the MIME type of the attachment.
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? MimeType { get; set; }
}
