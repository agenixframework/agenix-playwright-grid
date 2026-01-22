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

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Request payload for borrowing a browser session from the hub.
///     Includes the desired label key and optional run correlation fields.
/// </summary>
public sealed record BorrowRequestDto
{
    /// <summary>
    ///     Label key describing the desired capacity (e.g., "App:Browser:Env[:Region]").
    /// </summary>
    public string LabelKey { get; init; } = string.Empty;

    /// <summary>
    ///     Optional run identifier (Correlation-Id). When provided, the hub attributes the session
    ///     to the existing run. If omitted, the hub will generate one.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    ///     Optional human-friendly name for the run. UIs should fall back to <see cref="RunId" /> when null or empty.
    ///     See <see cref="RunNameRules" /> for validation and normalization rules.
    /// </summary>
    public string? RunName { get; init; }
}

/// <summary>
///     Response payload returned by the hub after a successful borrow request.
/// </summary>
public sealed record BorrowResponseDto
{
    /// <summary>
    ///     Unique browser session identifier assigned by the hub.
    /// </summary>
    public string BrowserId { get; init; } = string.Empty;

    /// <summary>
    ///     WebSocket endpoint to connect a Playwright client to.
    ///     The hub may use either "webSocketEndpoint" or the legacy "wsEndpoint" field name at the transport layer.
    /// </summary>
    public string WebSocketEndpoint { get; init; } = string.Empty;

    /// <summary>
    ///     Echoed label key for the borrowed capacity.
    /// </summary>
    public string LabelKey { get; init; } = string.Empty;

    /// <summary>
    ///     Optional browser type reported by the worker (e.g., chromium|firefox|webkit).
    /// </summary>
    public string? BrowserType { get; init; }

    /// <summary>
    ///     Optional worker node identifier that is serving this session.
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    ///     Optional human-friendly name for the run. Provided for convenience/echoing when available.
    /// </summary>
    public string? RunName { get; init; }

    /// <summary>
    ///     Optional expiration timestamp (UTC) when the borrow is expected to auto-expire.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; init; }
}

/// <summary>
///     Minimal run descriptor containing identity fields common across events and results.
/// </summary>
public sealed record Run
{
    /// <summary>
    ///     Unique run identifier (Correlation-Id / runId).
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    ///     Optional human-friendly name for the run. When not provided, UIs should fall back to RunId.
    ///     See <see cref="RunNameRules" /> for validation and normalization rules.
    /// </summary>
    public string? RunName { get; init; }
}

/// <summary>
///     Summary information about a test run as surfaced to UIs and logs.
///     Mirrors the hub/dashboard projection and is safe for serialization.
/// </summary>
public sealed record RunSummary
{
    /// <summary>
    ///     Unique run identifier (Correlation-Id / runId).
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    ///     Optional human-friendly name for the run. When not provided, UIs should fall back to RunId.
    /// </summary>
    public string? RunName { get; init; }

    /// <summary>
    ///     Optional external id provided by the runner (build id, CI run id, etc.).
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    ///     Application label (first segment of the label key).
    /// </summary>
    public string App { get; init; } = string.Empty;

    /// <summary>
    ///     Browser type (Chromium|Firefox|WebKit).
    /// </summary>
    public string Browser { get; init; } = string.Empty;

    /// <summary>
    ///     Environment label (e.g., UAT, Staging, Prod).
    /// </summary>
    public string Env { get; init; } = string.Empty;

    /// <summary>
    ///     Optional region label.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    ///     Optional OS label.
    /// </summary>
    public string? OS { get; init; }

    /// <summary>
    ///     Run status: Queued|Running|Passed|Failed|Aborted|Stopped|AutoStopped.
    /// </summary>
    public string Status { get; set; } = "Queued";

    /// <summary>
    ///     Total number of tests in the run.
    /// </summary>
    public int TotalTests { get; set; }

    /// <summary>
    ///     Number of passed tests.
    /// </summary>
    public int Passed { get; set; }

    /// <summary>
    ///     Number of failed tests.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    ///     UTC timestamp when the run started.
    /// </summary>
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    ///     UTC timestamp when the run completed, if applicable.
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    ///     Optional failure or abort reason.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    ///     Worker node id that handled the run (optional).
    /// </summary>
    public string? WorkerNodeId { get; set; }

    /// <summary>
    ///     Reported Playwright version.
    /// </summary>
    public string? PlaywrightVersion { get; set; }

    /// <summary>
    ///     Browser version used during the run, when available.
    /// </summary>
    public string? BrowserVersion { get; set; }
}
