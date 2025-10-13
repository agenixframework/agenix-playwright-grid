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

using Agenix.PlaywrightGrid.Client.Abstractions.Models;

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Calculates unified test result status based on test outcome aggregations.
///     Prioritizes test outcomes over browser session lifecycle semantics.
/// </summary>
public static class TestResultStatusCalculator
{
    /// <summary>
    ///     Calculates the overall status for a Launch, Suite, or TestRun based on test result aggregations.
    /// </summary>
    /// <param name="totalTests">Total number of tests</param>
    /// <param name="passedTests">Number of passed tests</param>
    /// <param name="failedTests">Number of failed tests</param>
    /// <param name="skippedTests">Number of skipped tests</param>
    /// <param name="timedoutTests">Number of timed out tests</param>
    /// <param name="isInProgress">Whether execution is still in progress</param>
    /// <param name="wasCancelled">Whether execution was manually cancelled</param>
    /// <param name="hadInfrastructureError">Whether there was an infrastructure/system error</param>
    /// <returns>The calculated test result status</returns>
    public static Status CalculateStatus(
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        bool isInProgress,
        bool wasCancelled = false,
        bool hadInfrastructureError = false)
    {
        // Infrastructure errors take highest priority
        if (hadInfrastructureError)
        {
            return Status.Stopped; // Map Errored -> Stopped (closest match in Status enum)
        }

        // If cancelled, return cancelled status
        if (wasCancelled)
        {
            return Status.Cancelled;
        }

        // If still in progress, return InProgress (regardless of test count)
        if (isInProgress)
        {
            return Status.InProgress;
        }

        // If launch is finished but no tests were run, return Skipped
        if (totalTests == 0)
        {
            return Status.Skipped;
        }

        // Calculate completed tests
        var completedTests = passedTests + failedTests + skippedTests + timedoutTests;

        // If not all tests have completed, still in progress
        if (completedTests < totalTests)
        {
            return Status.InProgress;
        }

        // Priority order for status determination:
        // 1. Timeout - if any tests timed out (map to Interrupted)
        // 2. Failed - if any tests failed
        // 3. Skipped - if ALL tests were skipped
        // 4. Passed - if all tests passed

        if (timedoutTests > 0)
        {
            return Status.Interrupted; // Map Timeout -> Interrupted
        }

        if (failedTests > 0)
        {
            return Status.Failed;
        }

        if (skippedTests == totalTests && totalTests > 0)
        {
            return Status.Skipped;
        }

        if (passedTests > 0)
        {
            return Status.Passed;
        }

        // Default to InProgress if we can't determine
        return Status.InProgress;
    }

    /// <summary>
    ///     Calculates status from database columns typically found in results/suites/launches tables.
    /// </summary>
    /// <param name="totalTests">Value from total_tests column</param>
    /// <param name="passedTests">Value from passed_tests column</param>
    /// <param name="failedTests">Value from failed_tests column</param>
    /// <param name="skippedTests">Value from skipped_tests column</param>
    /// <param name="timedoutTests">Value from timedout_tests column</param>
    /// <param name="finishTime">Finish timestamp (null if still running)</param>
    /// <param name="legacyStatus">Legacy status field (for determining cancelled/errored states)</param>
    /// <returns>The calculated test result status as a string</returns>
    public static string CalculateStatusFromDbColumns(
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        DateTime? finishTime,
        string? legacyStatus = null)
    {
        var isInProgress = !finishTime.HasValue;

        // Check legacy status for cancelled/stopped/error states
        var wasCancelled = legacyStatus?.Equals("Stopped", StringComparison.OrdinalIgnoreCase) == true ||
                           legacyStatus?.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) == true ||
                           legacyStatus?.Equals("Aborted", StringComparison.OrdinalIgnoreCase) == true ||
                           legacyStatus?.Equals("AutoStopped", StringComparison.OrdinalIgnoreCase) == true;

        var hadInfrastructureError = legacyStatus?.Equals("Error", StringComparison.OrdinalIgnoreCase) == true ||
                                     legacyStatus?.Equals("Errored", StringComparison.OrdinalIgnoreCase) == true;

        var status = CalculateStatus(
            totalTests,
            passedTests,
            failedTests,
            skippedTests,
            timedoutTests,
            isInProgress,
            wasCancelled,
            hadInfrastructureError);

        return status.ToString();
    }

    /// <summary>
    /// Checks if a status represents a completed state (not InProgress)
    /// </summary>
    public static bool IsCompleted(Status status)
    {
        return status != Status.InProgress;
    }

    /// <summary>
    /// Checks if a status represents a successful outcome (Passed or Skipped)
    /// </summary>
    public static bool IsSuccessful(Status status)
    {
        return status == Status.Passed || status == Status.Skipped;
    }

    /// <summary>
    /// Checks if a status represents a failure (Failed, Interrupted, or Stopped)
    /// </summary>
    public static bool IsFailure(Status status)
    {
        return status == Status.Failed || status == Status.Interrupted || status == Status.Stopped;
    }
}
