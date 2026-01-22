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

namespace Agenix.PlaywrightGrid.Client.Abstractions.Responses;

/// <summary>
///     Represents the response for creating a test item.
///     Includes browser session details if a browser was borrowed.
/// </summary>
public class TestItemCreatedResponse
{
    /// <summary>
    ///     Gets or sets the UUID of the created test item.
    /// </summary>
    [JsonPropertyName("id")]
    public string Uuid { get; set; }

    /// <summary>
    ///     Gets or sets the session status (browser lifecycle state).
    ///     Values: "Queued", "Running", "Completed", "Stopped", "AutoStopped", "Aborted"
    /// </summary>
    public string SessionStatus { get; set; }

    /// <summary>
    ///     Gets or sets the browser ID (if the browser was borrowed for this test item).
    ///     Null for non-test items (Step, BeforeTest, etc.) that use parent's browser.
    /// </summary>
    public string BrowserId { get; set; }

    /// <summary>
    ///     Gets or sets the WebSocket endpoint for connecting to the browser.
    ///     Example: "ws://worker-node-1:3000/browser/abc123"
    /// </summary>
    public string WebSocketEndpoint { get; set; }

    /// <summary>
    ///     Gets or sets the browser type.
    ///     Values: "chromium", "firefox", "webkit"
    /// </summary>
    public string BrowserType { get; set; }

    /// <summary>
    ///     Gets or sets the worker node ID where the browser is running.
    /// </summary>
    public string WorkerNodeId { get; set; }

    /// <summary>
    ///     Gets or sets the Playwright version used by the worker.
    /// </summary>
    public string PlaywrightVersion { get; set; }

    /// <summary>
    ///     Gets or sets the browser version.
    /// </summary>
    public string BrowserVersion { get; set; }

    /// <summary>
    ///     Gets or sets the code reference (e.g., test file path and line number).
    /// </summary>
    public string CodeRef { get; set; }

    /// <summary>
    ///     Gets or sets the canonical test case ID for history tracking.
    /// </summary>
    public string TestCaseId { get; set; }
}
