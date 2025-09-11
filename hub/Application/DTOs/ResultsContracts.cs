#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

namespace PlaywrightHub.Application.DTOs;

/// <summary>
///     Summary information about a test run as stored and shown in the dashboard.
/// </summary>
public sealed record ResultRunSummaryDto
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
    public string Browser { get; init; } = string.Empty; // Chromium|Firefox|WebKit

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
    public string Status { get; set; } = "Queued"; // Queued|Running|Passed|Failed|Aborted|Stopped|AutoStopped

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
    ///     Reported browser version.
    /// </summary>
    public string? BrowserVersion { get; set; }
}

/// <summary>
///     A single log event representing an action or message related to a run.
/// </summary>
public sealed record CommandLogEventDto
{
    /// <summary>
    ///     The run identifier this log belongs to.
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    ///     UTC timestamp when the event occurred.
    /// </summary>
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    ///     Event kind: ServerLaunch|Borrow|Connect|Disconnect|Return|WSProxy|API|Trace|Custom.
    /// </summary>
    public string Kind { get; init; } = string.Empty; // ServerLaunch|Borrow|Connect|Disconnect|Return|WSProxy|API|Trace|Custom

    /// <summary>
    ///     Human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    ///     Optional structured properties.
    /// </summary>
    public Dictionary<string, string>? Props { get; init; }

    /// <summary>
    ///     Optional test identifier that produced the event.
    /// </summary>
    public string? TestId { get; init; }
}

/// <summary>
///     Details of an individual test case within a run.
/// </summary>
public sealed record ResultTestCaseDto
{
    /// <summary>
    ///     Run identifier this test belongs to.
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    ///     Unique test case identifier (runner-provided).
    /// </summary>
    public string TestId { get; init; } = string.Empty;

    /// <summary>
    ///     Human-readable test title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    ///     Test file path.
    /// </summary>
    public string File { get; init; } = string.Empty;

    /// <summary>
    ///     Optional Playwright project name.
    /// </summary>
    public string? Project { get; init; }

    /// <summary>
    ///     Test status: Queued|Running|Passed|Failed|Skipped.
    /// </summary>
    public string Status { get; set; } = "Queued"; // Queued|Running|Passed|Failed|Skipped

    /// <summary>
    ///     Duration in milliseconds.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    ///     Optional error message when failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    ///     Optional error stack when failed.
    /// </summary>
    public string? ErrorStack { get; set; }
}
