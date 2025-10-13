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
using FluentAssertions;
using PlaywrightHub.Infrastructure.Services;
using Xunit;

namespace PlaywrightHub.Tests.Services;

public class TestResultStatusCalculatorTests
{
    #region Infrastructure Error Tests

    [Fact]
    public void CalculateStatus_WhenInfrastructureError_ShouldReturnErrored()
    {
        // Arrange
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: true);

        // Assert
        result.Should().Be(Status.Stopped); // Errored maps to Stopped
    }

    [Fact]
    public void CalculateStatus_WhenInfrastructureErrorAndCancelled_ShouldReturnErrored()
    {
        // Infrastructure error takes precedence over cancelled
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 0,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: true,
            hadInfrastructureError: true);

        result.Should().Be(Status.Stopped);
    }

    #endregion

    #region Cancelled Tests

    [Fact]
    public void CalculateStatus_WhenCancelled_ShouldReturnCancelled()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 3,
            failedTests: 2,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: true,
            hadInfrastructureError: false);

        result.Should().Be(Status.Cancelled);
    }

    #endregion

    #region InProgress Tests

    [Fact]
    public void CalculateStatus_WhenNoTests_ShouldReturnSkipped()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 0,
            passedTests: 0,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Skipped);
    }

    [Fact]
    public void CalculateStatus_WhenInProgress_ShouldReturnInProgress()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: true,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.InProgress);
    }

    [Fact]
    public void CalculateStatus_WhenNotAllTestsCompleted_ShouldReturnInProgress()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 5,
            failedTests: 2,
            skippedTests: 1,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        // Only 8 out of 10 tests completed
        result.Should().Be(Status.InProgress);
    }

    #endregion

    #region Timedout Tests

    [Fact]
    public void CalculateStatus_WhenAnyTestTimedOut_ShouldReturnTimedout()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 8,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 2,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Interrupted);
    }

    [Fact]
    public void CalculateStatus_WhenTimedOutAndFailed_ShouldReturnTimedout()
    {
        // Timedout takes precedence over Failed
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 6,
            failedTests: 2,
            skippedTests: 0,
            timedoutTests: 2,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Interrupted);
    }

    #endregion

    #region Failed Tests

    [Fact]
    public void CalculateStatus_WhenAnyTestFailed_ShouldReturnFailed()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 7,
            failedTests: 3,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Failed);
    }

    [Fact]
    public void CalculateStatus_WhenOnlyOneFailed_ShouldReturnFailed()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 9,
            failedTests: 1,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Failed);
    }

    #endregion

    #region Skipped Tests

    [Fact]
    public void CalculateStatus_WhenAllTestsSkipped_ShouldReturnSkipped()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 0,
            failedTests: 0,
            skippedTests: 10,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Skipped);
    }

    [Fact]
    public void CalculateStatus_WhenSomeSkippedSomePassed_ShouldReturnPassed()
    {
        // Passed takes precedence when not all are skipped
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 5,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Passed);
    }

    #endregion

    #region Passed Tests

    [Fact]
    public void CalculateStatus_WhenAllTestsPassed_ShouldReturnPassed()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: 10,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Passed);
    }

    [Fact]
    public void CalculateStatus_WhenOnlyPassedTests_ShouldReturnPassed()
    {
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 5,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        result.Should().Be(Status.Passed);
    }

    #endregion

    #region Priority Order Tests

    [Fact]
    public void CalculateStatus_ShouldFollowPriorityOrder()
    {
        // Priority: Errored > Cancelled > InProgress > Timedout > Failed > Skipped > Passed

        // Test 1: Errored has highest priority
        var result1 = TestResultStatusCalculator.CalculateStatus(
            10, 5, 1, 0, 1, false, true, true);
        result1.Should().Be(Status.Stopped);

        // Test 2: Cancelled when no infrastructure error
        var result2 = TestResultStatusCalculator.CalculateStatus(
            10, 5, 1, 0, 1, false, true, false);
        result2.Should().Be(Status.Cancelled);

        // Test 3: Timedout over Failed
        var result3 = TestResultStatusCalculator.CalculateStatus(
            10, 5, 2, 0, 3, false, false, false);
        result3.Should().Be(Status.Interrupted);

        // Test 4: Failed over Passed
        var result4 = TestResultStatusCalculator.CalculateStatus(
            10, 9, 1, 0, 0, false, false, false);
        result4.Should().Be(Status.Failed);

        // Test 5: Passed when no failures
        var result5 = TestResultStatusCalculator.CalculateStatus(
            10, 10, 0, 0, 0, false, false, false);
        result5.Should().Be(Status.Passed);
    }

    #endregion

    #region CalculateStatusFromDbColumns Tests

    [Fact]
    public void CalculateStatusFromDbColumns_WhenFinishTimeNull_ShouldReturnInProgress()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: null,
            legacyStatus: "Running");

        result.Should().Be("InProgress");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenFinishTimeSet_AndAllPassed_ShouldReturnPassed()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 10,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: "Finished");

        result.Should().Be("Passed");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusStopped_ShouldReturnCancelled()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: "Stopped");

        result.Should().Be("Cancelled");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusAborted_ShouldReturnCancelled()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: "Aborted");

        result.Should().Be("Cancelled");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusAutoStopped_ShouldReturnCancelled()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: "AutoStopped");

        result.Should().Be("Cancelled");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusError_ShouldReturnStopped()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: "Error");

        result.Should().Be("Stopped");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusErrored_ShouldReturnStopped()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: "Errored");

        result.Should().Be("Stopped");
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("STOPPED")]
    [InlineData("Stopped")]
    public void CalculateStatusFromDbColumns_ShouldBeCaseInsensitive(string legacyStatus)
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 5,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: legacyStatus);

        result.Should().Be("Cancelled");
    }

    #endregion

    #region Helper Method Tests

    [Theory]
    [InlineData(Status.Passed, true)]
    [InlineData(Status.Failed, true)]
    [InlineData(Status.Skipped, true)]
    [InlineData(Status.Interrupted, true)]
    [InlineData(Status.Cancelled, true)]
    [InlineData(Status.Stopped, true)]
    [InlineData(Status.InProgress, false)]
    public void IsCompleted_ShouldReturnCorrectValue(Status status, bool expected)
    {
        TestResultStatusCalculator.IsCompleted(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(Status.Passed, true)]
    [InlineData(Status.Skipped, true)]
    [InlineData(Status.Failed, false)]
    [InlineData(Status.Interrupted, false)]
    [InlineData(Status.Cancelled, false)]
    [InlineData(Status.Stopped, false)]
    [InlineData(Status.InProgress, false)]
    public void IsSuccessful_ShouldReturnCorrectValue(Status status, bool expected)
    {
        TestResultStatusCalculator.IsSuccessful(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(Status.Failed, true)]
    [InlineData(Status.Interrupted, true)]
    [InlineData(Status.Stopped, true)]
    [InlineData(Status.Passed, false)]
    [InlineData(Status.Skipped, false)]
    [InlineData(Status.Cancelled, false)]
    [InlineData(Status.InProgress, false)]
    public void IsFailure_ShouldReturnCorrectValue(Status status, bool expected)
    {
        TestResultStatusCalculator.IsFailure(status).Should().Be(expected);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CalculateStatus_WhenNegativeValues_ShouldHandleGracefully()
    {
        // Should not crash with negative values (though this shouldn't happen in practice)
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 10,
            passedTests: -1,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        // With negative passed, completed < total, so should be InProgress
        result.Should().Be(Status.InProgress);
    }

    [Fact]
    public void CalculateStatus_WhenCountsExceedTotal_ShouldStillCalculate()
    {
        // Edge case: counts exceed total (data inconsistency)
        var result = TestResultStatusCalculator.CalculateStatus(
            totalTests: 5,
            passedTests: 10,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            isInProgress: false,
            wasCancelled: false,
            hadInfrastructureError: false);

        // 10 passed > 5 total, all completed, should be Passed
        result.Should().Be(Status.Passed);
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusNull_ShouldNotCrash()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 10,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: null);

        result.Should().Be("Passed");
    }

    [Fact]
    public void CalculateStatusFromDbColumns_WhenLegacyStatusEmpty_ShouldNotCrash()
    {
        var result = TestResultStatusCalculator.CalculateStatusFromDbColumns(
            totalTests: 10,
            passedTests: 10,
            failedTests: 0,
            skippedTests: 0,
            timedoutTests: 0,
            finishTime: DateTime.UtcNow,
            legacyStatus: string.Empty);

        result.Should().Be("Passed");
    }

    #endregion
}
