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
    ///     Parent test item identifier for hierarchical structures (e.g., Step's parent is a Test).
    /// </summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>
    ///     Launch identifier (denormalized from suite for performance).
    /// </summary>
    public Guid? LaunchId { get; init; }

    /// <summary>
    ///     Optional human-friendly name for the run. When not provided, UIs should fall back to RunId.
    /// </summary>
    public string? RunName { get; init; }

    /// <summary>
    ///     Optional description providing additional context about the test run.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Optional array of key-value attributes or tags for the run (e.g., ["platform:linux", "smoke"]).
    /// </summary>
    public string[]? Attributes { get; init; }

    /// <summary>
    ///     Type of test item: Test|Step|Suite|Scenario|Story|BeforeTest|AfterTest|etc.
    ///     Default is "Test" for backward compatibility with legacy test runs.
    /// </summary>
    public string ItemType { get; init; } = "Test";

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
    ///     Legacy status field combining both browser session and test outcomes.
    ///     DEPRECATED: Use SessionStatus for browser lifecycle or ComputedStatus for test outcomes.
    ///     Values: Queued|Running|Passed|Failed|Aborted|Stopped|AutoStopped.
    /// </summary>
    public string Status { get; set; } = "Queued"; // Queued|Running|Passed|Failed|Aborted|Stopped|AutoStopped

    /// <summary>
    ///     Browser session lifecycle status (infrastructure state, not test outcomes).
    ///     Values: Queued|Running|Completed|Stopped|AutoStopped|Aborted.
    ///     - Queued: Browser not borrowed yet
    ///     - Running: Browser active, tests executing
    ///     - Completed: Browser returned successfully
    ///     - Stopped: User manually stopped, browser force-returned
    ///     - AutoStopped: Timeout/inactivity, browser force-returned
    ///     - Aborted: Infrastructure error prevented execution
    /// </summary>
    public string? SessionStatus { get; set; }

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
    ///     Number of skipped tests.
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    ///     Number of timed out tests.
    /// </summary>
    public int TimedOut { get; set; }

    /// <summary>
    ///     Computed test outcome status based on test result aggregations
    ///     (InProgress|Passed|Failed|Skipped|Timedout|Cancelled|Errored).
    ///     This reflects actual test execution outcomes, independent of browser session state.
    ///     Example: Tests can all pass (ComputedStatus=Passed) even if browser didn't close cleanly
    ///     (SessionStatus=AutoStopped).
    /// </summary>
    public string? ComputedStatus { get; set; }

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

    /// <summary>
    ///     Region/OS information for the browser session (e.g., "us-east-1/macOS 10.15.7").
    /// </summary>
    public string? RegionOs { get; set; }

    // Browser session details (populated when run borrows a browser)

    /// <summary>
    ///     Browser session identifier from the pool.
    /// </summary>
    public string? BrowserId { get; set; }

    /// <summary>
    ///     WebSocket endpoint URL for connecting to the browser.
    /// </summary>
    public string? WebSocketEndpoint { get; set; }

    /// <summary>
    ///     Type of browser (chromium, firefox, webkit).
    /// </summary>
    public string? BrowserType { get; set; }

    /// <summary>
    ///     Code reference (e.g., "tests/auth/login.spec.ts:42" or "io.agenix.demodata.beforeMethod").
    /// </summary>
    public string? CodeRef { get; init; }

    /// <summary>
    ///     Canonical test case ID for test history tracking (computed from testCaseId > codeRef > path).
    /// </summary>
    public string? TestCaseId { get; init; }

    /// <summary>
    ///     32-bit hash of TestCaseId for fast history lookups.
    /// </summary>
    public int TestCaseHash { get; init; }
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
    public string Kind { get; init; } =
        string.Empty; // ServerLaunch|Borrow|Connect|Disconnect|Return|WSProxy|API|Trace|Custom

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

    /// <summary>
    ///     Optional error stack trace (alias for backward compatibility).
    /// </summary>
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    ///     UTC timestamp when test started.
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    ///     UTC timestamp when test ended.
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
///     Detailed information about a single test case execution, including steps, errors, and artifacts.
/// </summary>
public sealed record TestCaseDetailDto
{
    /// <summary>
    ///     The run identifier this test case belongs to.
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    ///     Unique test case identifier (fully qualified test name or path).
    /// </summary>
    public string TestId { get; init; } = string.Empty;

    /// <summary>
    ///     Human-readable test title/name.
    /// </summary>
    public string TestTitle { get; init; } = string.Empty;

    /// <summary>
    ///     Test file path (relative to project root).
    /// </summary>
    public string? TestFile { get; init; }

    /// <summary>
    ///     Line number where test is defined.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    ///     Test execution status: Queued, Running, Passed, Failed, Skipped, Timedout.
    /// </summary>
    public string Status { get; init; } = "Queued";

    /// <summary>
    ///     UTC timestamp when test started.
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    ///     UTC timestamp when test ended (null if still running).
    /// </summary>
    public DateTime? EndTime { get; init; }

    /// <summary>
    ///     Test duration in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    ///     Error message if test failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    ///     Error stack trace if test failed.
    /// </summary>
    public string? ErrorStack { get; init; }

    /// <summary>
    ///     Hierarchical list of test steps (actions, assertions, hooks).
    /// </summary>
    public List<TestStepDto> Steps { get; init; } = new();

    /// <summary>
    ///     Standard output lines captured during test execution.
    /// </summary>
    public List<string> StdOut { get; init; } = new();

    /// <summary>
    ///     Standard error lines captured during test execution.
    /// </summary>
    public List<string> StdErr { get; init; } = new();

    /// <summary>
    ///     List of artifacts (screenshots, traces, videos) attached to this test.
    /// </summary>
    public List<TestAttachmentDto> Attachments { get; init; } = new();

    /// <summary>
    ///     Optional retry attempt number (0 for first attempt, 1+ for retries).
    /// </summary>
    public int? RetryAttempt { get; init; }

    /// <summary>
    ///     Optional browser/project configuration name (e.g., "chromium-desktop").
    /// </summary>
    public string? ProjectName { get; init; }

    /// <summary>
    ///     Optional tags or annotations associated with this test.
    /// </summary>
    public List<string> Tags { get; init; } = new();
}

/// <summary>
///     Represents a single step within a test case (action, assertion, hook).
/// </summary>
public sealed record TestStepDto
{
    /// <summary>
    ///     Step title/description.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    ///     Step category: hook, test.step, expect, pw:api, fixture.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    ///     UTC timestamp when step started.
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    ///     Step duration in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    ///     Error message if step failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    ///     Line number in source file where step is defined.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    ///     Code snippet showing the step's source code.
    /// </summary>
    public string? CodeSnippet { get; init; }

    /// <summary>
    ///     Nested child steps (for hierarchical step structures).
    /// </summary>
    public List<TestStepDto> Steps { get; init; } = new();

    /// <summary>
    ///     Number of times this step was executed (for retry tracking).
    /// </summary>
    public int Count { get; init; } = 1;
}

/// <summary>
///     Represents a file artifact attached to a test case (screenshot, trace, video, log).
/// </summary>
public sealed record TestAttachmentDto
{
    /// <summary>
    ///     Unique artifact identifier (GUID).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    ///     Artifact filename.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     MIME content type (image/png, video/webm, application/zip, text/plain).
    /// </summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    ///     Storage path or blob URI for retrieving the artifact.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    ///     File size in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    ///     UTC timestamp when artifact was uploaded.
    /// </summary>
    public DateTime UploadedAt { get; init; }

    /// <summary>
    ///     Optional description of what the artifact contains.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
///     Request for batch uploading multiple test case results at once (Azure-style).
/// </summary>
public sealed record BatchTestResultsRequest
{
    /// <summary>
    ///     List of test cases to upload.
    /// </summary>
    public List<TestCaseDetailDto> Tests { get; init; } = new();
}

/// <summary>
///     Response after successful artifact upload.
/// </summary>
public sealed record ArtifactUploadResponse
{
    /// <summary>
    ///     Storage path where artifact was saved.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    ///     Direct URL for accessing the artifact.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    ///     File size in bytes.
    /// </summary>
    public long Size { get; init; }
}

/// <summary>
///     Unified test item DTO supporting hierarchy.
///     Can represent: Test, Step, Suite, Scenario, or Hooks (BeforeTest, AfterTest, etc.).
///     Merges fields from ResultRunSummaryDto and TestCaseDetailDto into single hierarchical structure.
/// </summary>
public sealed record TestItemDto
{
    // ===== Core Identity =====

    /// <summary>
    ///     Unique item identifier (maps to run_id column for backward compatibility).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    ///     Launch identifier this item belongs to.
    /// </summary>
    public Guid LaunchId { get; init; }

    /// <summary>
    ///     Optional project key this item belongs to.
    /// </summary>
    public string? ProjectKey { get; init; }

    /// <summary>
    ///     Optional label key used for borrowing browsers (App:Browser:Env).
    /// </summary>
    public string? LabelKey { get; init; }

    /// <summary>
    ///     Parent item identifier for nested hierarchy (null for root items).
    ///     Example: A Step's parent is its Test, a Test's parent might be a Scenario.
    /// </summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>
    ///     Globally unique sequential database ID for hierarchical URLs.
    ///     Enables numeric URLs like /admin_default/launches/4/suites/148/runs/149/tests/150
    /// </summary>
    public long? DbId { get; init; }

    /// <summary>
    ///     Auto-incrementing suite number within the launch (numeric URL).
    ///     Only populated for ItemType='Suite'. Null for non-suite items.
    /// </summary>
    public int? SuiteNumber { get; init; }

    // ===== Item Type & Metadata =====

    /// <summary>
    ///     Type of test item: Test|Step|Suite|Scenario|Story|BeforeTest|AfterTest|BeforeClass|AfterClass|etc.
    /// </summary>
    public string ItemType { get; init; } = "Test";

    /// <summary>
    ///     Whether this item contributes to test statistics.
    ///     Typically false for nested Steps and hooks, true for Tests and Scenarios.
    /// </summary>
    public bool HasStats { get; init; } = true;

    /// <summary>
    ///     Item name (test name, step description, suite name, etc.).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     Optional detailed description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Optional array of key-value attributes or tags (e.g., ["platform:linux", "smoke"]).
    /// </summary>
    public string[]? Attributes { get; init; }

    // ===== Timestamps =====

    /// <summary>
    ///     Start time of the item execution.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    ///     Finish time of the item execution (null if still running).
    /// </summary>
    public DateTimeOffset? FinishTime { get; init; }

    /// <summary>
    ///     Duration in milliseconds (calculated from start/finish or stored).
    /// </summary>
    public double? DurationMs { get; init; }

    // ===== Status (Separated) =====

    /// <summary>
    ///     Browser session lifecycle status (for Test/Scenario types only).
    ///     Values: Queued|Running|Completed|Stopped|AutoStopped|Aborted.
    /// </summary>
    public string? SessionStatus { get; init; }

    /// <summary>
    ///     Computed test outcome status based on test results.
    ///     Values: InProgress|Passed|Failed|Skipped|Timedout|Cancelled|Errored.
    /// </summary>
    public string? ComputedStatus { get; init; }

    /// <summary>
    ///     Legacy combined status field (DEPRECATED - use SessionStatus or ComputedStatus).
    /// </summary>
    public string? Status { get; init; }

    // ===== Browser Session Fields (for Test/Scenario types) =====

    /// <summary>
    ///     Browser identifier from the pool (only for Test/Scenario items).
    /// </summary>
    public string? BrowserId { get; init; }

    /// <summary>
    ///     WebSocket endpoint for the browser (only for Test/Scenario items).
    /// </summary>
    public string? WebSocketEndpoint { get; init; }

    /// <summary>
    ///     Browser type: Chromium|Firefox|WebKit (only for Test/Scenario items).
    /// </summary>
    public string? BrowserType { get; init; }

    /// <summary>
    ///     Worker node ID that hosted the browser (only for Test/Scenario items).
    /// </summary>
    public string? WorkerNodeId { get; init; }

    /// <summary>
    ///     Reported Playwright version (only for Test/Scenario items).
    /// </summary>
    public string? PlaywrightVersion { get; init; }

    /// <summary>
    ///     Reported browser version (only for Test/Scenario items).
    /// </summary>
    public string? BrowserVersion { get; init; }

    /// <summary>
    ///     Region/OS information for the browser session (e.g., "us-east-1/macOS 10.15.7").
    /// </summary>
    public string? RegionOs { get; init; }

    // ===== Test Details (merged from TestCaseDetailDto) =====

    /// <summary>
    ///     Human-readable test title.
    /// </summary>
    public string? TestTitle { get; init; }

    /// <summary>
    ///     Source file path for the test.
    /// </summary>
    public string? TestFile { get; init; }

    /// <summary>
    ///     Line number in the source file.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    ///     Error message if the test failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    ///     Full error stack trace.
    /// </summary>
    public string? ErrorStack { get; init; }

    /// <summary>
    ///     Retry attempt number (0 for first attempt, 1+ for retries).
    /// </summary>
    public int? RetryAttempt { get; init; }

    /// <summary>
    ///     Test tags for filtering/grouping.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    ///     Code reference (e.g., "tests/auth/login.spec.ts:42").
    /// </summary>
    public string? CodeRef { get; init; }

    /// <summary>
    ///     Parameters for parameterized tests (key-value pairs).
    /// </summary>
    public Dictionary<string, string>? Parameters { get; init; }

    /// <summary>
    ///     Canonical test case ID for test history tracking (computed from testCaseId > codeRef > path).
    /// </summary>
    public string? TestCaseId { get; init; }

    /// <summary>
    ///     32-bit hash of TestCaseId for fast history lookups.
    /// </summary>
    public int TestCaseHash { get; init; }

    // ===== Hierarchy & Content =====

    /// <summary>
    ///     Flat list of test steps (legacy - for backward compatibility).
    ///     Use Children for hierarchical nested steps.
    /// </summary>
    public List<TestStepDto>? Steps { get; init; }

    /// <summary>
    ///     Nested child items (Steps, sub-tests, etc.) - hierarchy.
    ///     Enables recursive tree structure.
    /// </summary>
    public List<TestItemDto>? Children { get; init; }

    /// <summary>
    ///     Attached artifacts (screenshots, videos, logs, etc.).
    /// </summary>
    public List<TestAttachmentDto>? Attachments { get; init; }

    // ===== Test Aggregations (for items with children) =====

    /// <summary>
    ///     Total number of child test items (only for parent items).
    /// </summary>
    public int TotalTests { get; init; }

    /// <summary>
    ///     Number of passed child tests.
    /// </summary>
    public int PassedTests { get; init; }

    /// <summary>
    ///     Number of failed child tests.
    /// </summary>
    public int FailedTests { get; init; }

    /// <summary>
    ///     Number of skipped child tests.
    /// </summary>
    public int SkippedTests { get; init; }

    /// <summary>
    ///     Number of timed out child tests.
    /// </summary>
    public int TimedoutTests { get; init; }
}

/// <summary>
///     Represents a test execution in the history timeline across multiple launches.
/// </summary>
public record TestHistoryItemDto
{
    /// <summary>The launch ID for this execution.</summary>
    public Guid LaunchId { get; init; }

    /// <summary>The launch number (incremental identifier).</summary>
    public int LaunchNumber { get; init; }

    /// <summary>Test execution status (Passed, Failed, Skipped, Stopped).</summary>
    public string Status { get; init; } = "Unknown";

    /// <summary>Launch attributes.</summary>
    public List<string>? Attributes { get; init; }

    /// <summary>Test execution duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Number of errors in this execution.</summary>
    public int ErrorCount { get; init; }

    /// <summary>The test item ID for this specific execution.</summary>
    public Guid TestItemId { get; init; }
}

/// <summary>
///     Represents a unique error pattern with grouped occurrences.
///     Used for error analysis and grouping similar failures across test runs.
/// </summary>
public sealed record UniqueErrorDto
{
    /// <summary>Normalized error message fingerprint (e.g., "socket timeout exception read timed out after {num}ms").</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Number of distinct test items that failed with this error.</summary>
    public required int FailedTestCount { get; init; }

    /// <summary>Total number of log entries with this error (may be > FailedTestCount if same test logs error multiple times).</summary>
    public required int OccurrenceCount { get; init; }

    /// <summary>Timestamp of first occurrence of this error pattern.</summary>
    public required DateTime FirstOccurrence { get; init; }

    /// <summary>Timestamp of most recent occurrence of this error pattern.</summary>
    public required DateTime LastOccurrence { get; init; }

    /// <summary>Sample stack trace showing the full error message (not normalized).</summary>
    public required string SampleStackTrace { get; init; }

    /// <summary>List of test item UUIDs that failed with this error pattern.</summary>
    public required List<Guid> TestItemIds { get; init; }
}
