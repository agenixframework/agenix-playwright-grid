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

using System.Diagnostics.Metrics;

namespace PlaywrightHub.Infrastructure.Metrics;

/// <summary>
///     OpenTelemetry metrics for test run status tracking and session monitoring.
/// </summary>
public sealed class TestRunMetrics
{
    private readonly Counter<long> _browserAutoStoppedCounter;
    private readonly Meter _meter;
    private readonly Counter<long> _sessionStatusMismatchCounter;
    private readonly Counter<long> _testRunCompletedCounter;
    private readonly Histogram<double> _testRunDurationHistogram;

    public TestRunMetrics()
    {
        _meter = new Meter("PlaywrightGrid.TestRuns", "1.0.0");

        // Counter for session status vs computed status mismatches
        _sessionStatusMismatchCounter = _meter.CreateCounter<long>(
            "playwright_grid_session_status_mismatch_total",
            "mismatches",
            "Total count of test runs where SessionStatus != Completed but ComputedStatus = Passed"
        );

        // Counter for browser auto-stopped incidents
        _browserAutoStoppedCounter = _meter.CreateCounter<long>(
            "playwright_grid_browser_autostopped_total",
            "incidents",
            "Total count of browser sessions that were auto-stopped due to timeout/cleanup issues"
        );

        // Histogram for test run duration
        _testRunDurationHistogram = _meter.CreateHistogram<double>(
            "playwright_grid_test_run_duration_seconds",
            "seconds",
            "Duration of test runs from start to completion"
        );

        // Counter for completed test runs by status
        _testRunCompletedCounter = _meter.CreateCounter<long>(
            "playwright_grid_test_run_completed_total",
            "runs",
            "Total count of completed test runs by computed status"
        );
    }

    /// <summary>
    ///     Records a session status mismatch when tests pass but browser fails.
    /// </summary>
    /// <param name="sessionStatus">The browser session status (e.g., AutoStopped)</param>
    /// <param name="computedStatus">The test outcome status (e.g., Passed)</param>
    /// <param name="app">Application label</param>
    /// <param name="browser">Browser type</param>
    /// <param name="env">Environment label</param>
    public void RecordSessionMismatch(
        string sessionStatus,
        string computedStatus,
        string app,
        string browser,
        string env)
    {
        _sessionStatusMismatchCounter.Add(1, new KeyValuePair<string, object?>("session_status", sessionStatus),
            new KeyValuePair<string, object?>("computed_status", computedStatus),
            new KeyValuePair<string, object?>("app", app),
            new KeyValuePair<string, object?>("browser", browser),
            new KeyValuePair<string, object?>("env", env)
        );
    }

    /// <summary>
    ///     Records a browser auto-stopped incident.
    /// </summary>
    /// <param name="reason">Reason for auto-stop (timeout, inactivity, etc.)</param>
    /// <param name="app">Application label</param>
    /// <param name="browser">Browser type</param>
    /// <param name="env">Environment label</param>
    /// <param name="workerNodeId">Worker node that hosted the session</param>
    public void RecordBrowserAutoStopped(
        string reason,
        string app,
        string browser,
        string env,
        string? workerNodeId)
    {
        _browserAutoStoppedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("app", app),
            new KeyValuePair<string, object?>("browser", browser),
            new KeyValuePair<string, object?>("env", env),
            new KeyValuePair<string, object?>("worker_node_id", workerNodeId ?? "unknown")
        );
    }

    /// <summary>
    ///     Records test run duration.
    /// </summary>
    /// <param name="durationSeconds">Duration in seconds</param>
    /// <param name="computedStatus">Test outcome status</param>
    /// <param name="sessionStatus">Browser session status</param>
    /// <param name="app">Application label</param>
    /// <param name="browser">Browser type</param>
    /// <param name="env">Environment label</param>
    public void RecordTestRunDuration(
        double durationSeconds,
        string computedStatus,
        string? sessionStatus,
        string app,
        string browser,
        string env)
    {
        _testRunDurationHistogram.Record(durationSeconds,
            new KeyValuePair<string, object?>("computed_status", computedStatus),
            new KeyValuePair<string, object?>("session_status", sessionStatus ?? "unknown"),
            new KeyValuePair<string, object?>("app", app),
            new KeyValuePair<string, object?>("browser", browser),
            new KeyValuePair<string, object?>("env", env)
        );
    }

    /// <summary>
    ///     Records a completed test run.
    /// </summary>
    /// <param name="computedStatus">Test outcome status</param>
    /// <param name="sessionStatus">Browser session status</param>
    /// <param name="app">Application label</param>
    /// <param name="browser">Browser type</param>
    /// <param name="env">Environment label</param>
    public void RecordTestRunCompleted(
        string computedStatus,
        string? sessionStatus,
        string app,
        string browser,
        string env)
    {
        _testRunCompletedCounter.Add(1, new KeyValuePair<string, object?>("computed_status", computedStatus),
            new KeyValuePair<string, object?>("session_status", sessionStatus ?? "unknown"),
            new KeyValuePair<string, object?>("app", app),
            new KeyValuePair<string, object?>("browser", browser),
            new KeyValuePair<string, object?>("env", env)
        );
    }

    /// <summary>
    ///     Helper to determine if there's a status mismatch worth alerting on.
    /// </summary>
    /// <param name="sessionStatus">Browser session status</param>
    /// <param name="computedStatus">Test outcome status</param>
    /// <returns>True if tests passed but browser had issues</returns>
    public static bool IsStatusMismatch(string? sessionStatus, string? computedStatus)
    {
        var sessionLower = sessionStatus?.Trim().ToLowerInvariant();
        var computedLower = computedStatus?.Trim().ToLowerInvariant();

        // Mismatch: Tests passed but browser didn't complete cleanly
        return computedLower is "passed" &&
               sessionLower is "stopped" or "autostopped" or "aborted";
    }
}
