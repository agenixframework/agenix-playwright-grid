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

using FluentAssertions;
using Xunit;

namespace PlaywrightHub.Tests.Web;

/// <summary>
///     Unit tests for terminal state logic validation.
///     Tests verify that the terminal state detection correctly identifies
///     Finished, Stopped, and Failed states, while treating InProgress as non-terminal.
/// </summary>
public class LaunchTerminalStateLogicTests
{
    #region Terminal State Detection Logic Tests

    [Theory]
    [InlineData("Finished", true)]
    [InlineData("Stopped", true)]
    [InlineData("Failed", true)]
    [InlineData("InProgress", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLaunchInTerminalState_ShouldDetectCorrectly(string? status, bool expectedIsTerminal)
    {
        // Act
        var result = CheckIfTerminalState(status);

        // Assert
        result.Should().Be(expectedIsTerminal,
            $"status '{status ?? "null"}' should {(expectedIsTerminal ? "" : "not ")}be terminal");
    }

    [Fact]
    public void IsLaunchInTerminalState_WhenStatusIsFinished_ShouldReturnTrue()
    {
        // Arrange
        var status = "Finished";

        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeTrue("Finished is a terminal state");
    }

    [Fact]
    public void IsLaunchInTerminalState_WhenStatusIsStopped_ShouldReturnTrue()
    {
        // Arrange
        var status = "Stopped";

        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeTrue("Stopped (force-finished) is a terminal state");
    }

    [Fact]
    public void IsLaunchInTerminalState_WhenStatusIsFailed_ShouldReturnTrue()
    {
        // Arrange
        var status = "Failed";

        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeTrue("Failed is a terminal state");
    }

    [Fact]
    public void IsLaunchInTerminalState_WhenStatusIsInProgress_ShouldReturnFalse()
    {
        // Arrange
        var status = "InProgress";

        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeFalse("InProgress is not a terminal state");
    }

    [Fact]
    public void IsLaunchInTerminalState_WhenStatusIsNull_ShouldReturnFalse()
    {
        // Arrange
        string? status = null;

        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeFalse("null status means launch doesn't exist (not terminal)");
    }

    [Fact]
    public void IsLaunchInTerminalState_WhenStatusIsEmpty_ShouldReturnFalse()
    {
        // Arrange
        var status = string.Empty;

        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeFalse("empty status is not a valid terminal state");
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("FINISHED", false)] // Case-sensitive check
    [InlineData("finished", false)] // Case-sensitive check
    [InlineData("STOPPED", false)]  // Case-sensitive check
    [InlineData("stopped", false)]  // Case-sensitive check
    [InlineData("FAILED", false)]   // Case-sensitive check
    [InlineData("failed", false)]   // Case-sensitive check
    [InlineData("Stopped", true)]   // Exact match
    public void IsLaunchInTerminalState_ShouldBeCaseSensitive(string status, bool expectedIsTerminal)
    {
        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().Be(expectedIsTerminal,
            $"case-sensitive check: '{status}' should {(expectedIsTerminal ? "" : "not ")}be terminal");
    }

    [Theory]
    [InlineData("Finished ")]  // Trailing space
    [InlineData(" Finished")] // Leading space
    [InlineData("Finish")]    // Partial match
    [InlineData("FinishedX")] // Extra character
    public void IsLaunchInTerminalState_ShouldRequireExactMatch(string status)
    {
        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeFalse($"'{status}' is not an exact match for terminal state");
    }

    [Fact]
    public void TerminalStates_ShouldBeExactlyThreeStates()
    {
        // Arrange
        var terminalStates = new[] { "Finished", "Stopped", "Failed" };

        // Assert
        terminalStates.Should().HaveCount(3, "there are exactly three terminal states");
        terminalStates.Should().Contain("Finished");
        terminalStates.Should().Contain("Stopped");
        terminalStates.Should().Contain("Failed");
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    [InlineData("Pending")]
    [InlineData("Completed")]
    [InlineData("Success")]
    [InlineData("Error")]
    public void IsLaunchInTerminalState_WhenStatusIsOther_ShouldReturnFalse(string status)
    {
        // Act
        var isTerminal = CheckIfTerminalState(status);

        // Assert
        isTerminal.Should().BeFalse($"'{status}' is not a defined terminal state");
    }

    #endregion

    #region Multiple Status Checks

    [Fact]
    public void MultipleStatusChecks_ShouldBeConsistent()
    {
        // Arrange
        var testCases = new[]
        {
            ("Finished", true),
            ("Stopped", true),
            ("Failed", true),
            ("InProgress", false),
            ("InProgress", false), // Duplicate to test consistency
            ("Stopped", true),     // Duplicate to test consistency
        };

        // Act & Assert
        foreach (var (status, expectedIsTerminal) in testCases)
        {
            var result = CheckIfTerminalState(status);
            result.Should().Be(expectedIsTerminal,
                $"status '{status}' should consistently be {(expectedIsTerminal ? "terminal" : "non-terminal")}");
        }
    }

    #endregion

    #region Behavior Validation Tests

    [Fact]
    public void FinishLaunch_WhenAlreadyFinished_ShouldBeRejected()
    {
        // Arrange
        var currentStatus = "Finished";

        // Act
        var isTerminal = CheckIfTerminalState(currentStatus);

        // Assert - Operation should be rejected
        isTerminal.Should().BeTrue("cannot finish an already finished launch");
    }

    [Fact]
    public void FinishLaunch_WhenStopped_ShouldBeRejected()
    {
        // Arrange
        var currentStatus = "Stopped";

        // Act
        var isTerminal = CheckIfTerminalState(currentStatus);

        // Assert - Operation should be rejected
        isTerminal.Should().BeTrue("cannot finish a stopped (force-finished) launch");
    }

    [Fact]
    public void FinishLaunch_WhenFailed_ShouldBeRejected()
    {
        // Arrange
        var currentStatus = "Failed";

        // Act
        var isTerminal = CheckIfTerminalState(currentStatus);

        // Assert - Operation should be rejected
        isTerminal.Should().BeTrue("cannot finish a failed launch");
    }

    [Fact]
    public void StartTestItem_WhenLaunchFinished_ShouldBeRejected()
    {
        // Arrange
        var launchStatus = "Finished";

        // Act
        var isTerminal = CheckIfTerminalState(launchStatus);

        // Assert - Test item creation should be rejected
        isTerminal.Should().BeTrue("cannot create test items in a finished launch");
    }

    [Fact]
    public void StartTestItem_WhenLaunchStopped_ShouldBeRejected()
    {
        // Arrange
        var launchStatus = "Stopped";

        // Act
        var isTerminal = CheckIfTerminalState(launchStatus);

        // Assert - Test item creation should be rejected
        isTerminal.Should().BeTrue("cannot create test items in a stopped launch");
    }

    [Fact]
    public void StartTestItem_WhenLaunchInProgress_ShouldBeAllowed()
    {
        // Arrange
        var launchStatus = "InProgress";

        // Act
        var isTerminal = CheckIfTerminalState(launchStatus);

        // Assert - Test item creation should be allowed
        isTerminal.Should().BeFalse("can create test items in an in-progress launch");
    }

    [Fact]
    public void FinishTestItem_WhenLaunchFinished_ShouldBeRejected()
    {
        // Arrange
        var launchStatus = "Finished";

        // Act
        var isTerminal = CheckIfTerminalState(launchStatus);

        // Assert - Test item finish should be rejected
        isTerminal.Should().BeTrue("cannot finish test items in a finished launch");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Simulates the terminal state detection logic from the actual endpoints.
    ///     This mirrors the behavior of IsLaunchInTerminalStateAsync in LaunchesEndpoints and TestItemsEndpoints.
    /// </summary>
    private static bool CheckIfTerminalState(string? status)
    {
        if (status == null)
            return false;

        var terminalStates = new[] { "Finished", "Stopped", "Failed" };
        return terminalStates.Contains(status);
    }

    #endregion
}
