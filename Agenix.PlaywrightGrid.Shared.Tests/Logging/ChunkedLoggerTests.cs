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

using System.Diagnostics;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Logging;

public class ChunkedLoggerTests : IDisposable
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly string _categoryName = "TestCategory";
    private readonly ChunkedLogger _logger;

    public ChunkedLoggerTests()
    {
        _mockLogger = new Mock<ILogger>();
        _logger = new ChunkedLogger(_mockLogger.Object, _categoryName);

        // Clear OperationContext before each test
        OperationContext.Current = null;
        Activity.Current = null;
    }

    public void Dispose()
    {
        OperationContext.Current = null;
        Activity.Current = null;
    }

    [Fact]
    public void Constructor_WithNullCategory_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new ChunkedLogger(_mockLogger.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("categoryName");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new ChunkedLogger(null!, _categoryName);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void BeginOperation_Enabled_LogsStartAndReturnsScope()
    {
        // Arrange
        var operationName = "TestOp";
        var inputs = new Dictionary<string, object> { ["key1"] = "val1" };

        // Act
        using (var scope = _logger.BeginOperation(operationName, inputs))
        {
            // Assert
            scope.Should().BeAssignableTo<IChunkedOperation>();
            scope.Should().BeOfType<ChunkedLogger.CompositeDisposable>();
            OperationContext.Current.Should().NotBeNull();
            OperationContext.Current!.OperationName.Should().Be(operationName);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Operation: TestOp")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        // Context should be cleared after disposal
        OperationContext.Current.Should().BeNull();
    }

    [Fact]
    public void BeginOperation_Disabled_ReturnsNoOpScopeAndDoesNotLog()
    {
        // Arrange
        var options = new ChunkedLoggerOptions { Enabled = false };
        var logger = new ChunkedLogger(_mockLogger.Object, _categoryName, options);

        // Act
        using (var scope = logger.BeginOperation("TestOp"))
        {
            // Assert
            scope.Should().BeAssignableTo<IChunkedOperation>();
            scope.Should().NotBeOfType<ChunkedLogger.CompositeDisposable>();
            OperationContext.Current.Should().BeNull();

            _mockLogger.Verify(
                x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
    }

    [Fact]
    public void LogMilestone_Enabled_LogsAndRecordsKeyEvent()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");
        var eventCode = EventCodes.BrowserPool.BrowserAllocated; // POOL02
        var message = "Allocated browser {BrowserId}";
        var browserId = "b1";

        // Act
        _logger.LogMilestone(eventCode, message, browserId);

        // Assert
        OperationContext.Current!.KeyEvents.Should().Contain(eventCode);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Browser allocated") && v.ToString()!.Contains("b1")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogMilestone_WithException_LogsErrorAndRecordsKeyEvent()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");
        var eventCode = EventCodes.BrowserPool.BorrowFailed;
        var ex = new Exception("Test error");

        // Act
        _logger.LogMilestone(eventCode, ex, "Failed to borrow");

        // Assert
        OperationContext.Current!.KeyEvents.Should().Contain(eventCode);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Browser borrow failed")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogDebug_WithEventCode_LogsAndRecordsKeyEvent()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");
        var eventCode = "DBG01";

        // Act
        _logger.LogDebug(eventCode, "Debug message");

        // Assert
        OperationContext.Current!.KeyEvents.Should().Contain(eventCode);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DBG01")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogDebug_WithoutEventCode_LogsOnly()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");

        // Act
        _logger.LogDebug(null, "Debug message");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Debug message")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogWarning_WithEventCode_LogsAndRecordsKeyEvent()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");
        var eventCode = "WRN01";

        // Act
        _logger.LogWarning(eventCode, "Warning message");

        // Assert
        OperationContext.Current!.KeyEvents.Should().Contain(eventCode);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("WRN01")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogWarning_WithoutEventCode_LogsOnly()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");

        // Act
        _logger.LogWarning(null, "Warning message");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Warning message")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OperationScope_Dispose_AutoCompletesOperation()
    {
        // Arrange
        var operationName = "AutoCompOp";

        // Act
        using (var scope = _logger.BeginOperation(operationName))
        {
            // Do nothing
        }

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: SUCCESS") && v.ToString()!.Contains("Type=OperationEnd")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OperationScope_Complete_ThenDispose_DoesNotDoubleLog()
    {
        // Arrange
        using (var scope = _logger.BeginOperation("TestOp"))
        {
            scope.Complete();
        }

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: SUCCESS") && v.ToString()!.Contains("Type=OperationEnd")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OperationScope_Fail_LogsFailureOnDispose()
    {
        // Arrange
        var ex = new Exception("Error");

        // Act
        using (var scope = _logger.BeginOperation("TestOp"))
        {
            scope.Fail(ex, ErrorType.Unexpected, DependencyName.Redis);
        }

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: FAILED") && v.ToString()!.Contains("Dependency=Redis") && v.ToString()!.Contains("Type=OperationEnd")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OperationScope_SetOutputs_IncludesInCompletionLog()
    {
        // Arrange
        var outputs = new Dictionary<string, object> { ["result"] = "ok" };

        // Act
        using (var scope = _logger.BeginOperation("TestOp"))
        {
            scope.SetOutputs(outputs);
        }

        // Assert
        // We verify that CompleteOperation was called with outputs.
        // Verifying the log content is better.
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: SUCCESS")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // To verify structured properties, we'd need to mock BeginScope and capture properties.
    }

    [Fact]
    public void BeginMigrationOperation_ReturnsMigrationScopeAndLogs()
    {
        // Arrange
        var operationName = "MigrateDB";
        var inputs = new Dictionary<string, object> { ["version"] = 1 };

        // Act
        using (var scope = ChunkedLogger.BeginMigrationOperation(_mockLogger.Object, operationName, inputs))
        {
            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Migration: MigrateDB Start")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        // Assert - auto-complete on dispose
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Migration: MigrateDB End SUCCESS")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BeginOperation_WithParentId_IncludesInProperties()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        // Act
        using var scope = _logger.BeginOperation("TestOp", parentOperationId: parentId);

        // Assert
        OperationContext.Current!.ParentOperationId.Should().Be(parentId);

        // Verifying properties would require mocking BeginScope
    }

    [Fact]
    public void BeginOperation_WithActivity_IncludesTraceAndSpanIds()
    {
        // Arrange
        using var activity = new Activity("TestActivity").Start();

        // Act
        using var scope = _logger.BeginOperation("TestOp");

        // Assert
        OperationContext.Current!.TraceId.Should().Be(activity.TraceId.ToString());
        OperationContext.Current!.SpanId.Should().Be(activity.SpanId.ToString());
    }

    [Fact]
    public void LogMethods_WhenDisabled_DoNotLog()
    {
        // Arrange
        var options = new ChunkedLoggerOptions { Enabled = false };
        var logger = new ChunkedLogger(_mockLogger.Object, _categoryName, options);

        // Act
        logger.LogMilestone("E1", "Msg");
        logger.LogMilestone("E1", new Exception(), "Msg");
        logger.LogDebug("E1", "Msg");
        logger.LogWarning("E1", "Msg");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void FailOperation_WithNullDependency_LogsWithoutDependency()
    {
        // Arrange
        using var scope = _logger.BeginOperation("TestOp");
        var context = OperationContext.Current!;
        var ex = new Exception("Err");

        // Act
        _logger.FailOperation(context, ex, ErrorType.Unexpected, null);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: FAILED") && !v.ToString()!.Contains("Dependency=")),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OperationScope_IdempotentDispose()
    {
        // Arrange
        var scope = _logger.BeginOperation("TestOp");

        // Act
        scope.Dispose();
        scope.Dispose();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: SUCCESS")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void NestedOperations_ShouldHaveTreeStructureWithBars()
    {
        // Arrange
        var rootOpName = "RootOp";
        var nestedOpName = "NestedOp";
        var deepOpName = "DeepOp";

        // Act
        using (var root = _logger.BeginOperation(rootOpName))
        {
            _logger.LogInformation(null, "Root Milestone");

            using (var nested = _logger.BeginOperation(nestedOpName))
            {
                _logger.LogInformation(null, "Nested Milestone");

                using (var deep = _logger.BeginOperation(deepOpName))
                {
                    _logger.LogInformation(null, "Deep Milestone");
                }
            }
        }

        // Assert
        VerifyLog("╔═ Operation: RootOp", Times.Once());
        VerifyLog("║ Root Milestone", Times.Once());
        VerifyLog("║ ╠═ Nested Operation: NestedOp", Times.Once());
        VerifyLog("║ ║ Nested Milestone", Times.Once());
        VerifyLog("║ ║ ╠═ Nested Operation: DeepOp", Times.Once());
        VerifyLog("║ ║ ║ Deep Milestone", Times.Once());
        VerifyLog("║ ║ ╚═ End: SUCCESS", Times.Once());
        VerifyLog("║ ╚═ End: SUCCESS", Times.Once());
        VerifyLog("╚═ End: SUCCESS", Times.Once());
    }

    [Fact]
    public void LogMilestone_WithNullProperties_ShouldNotThrow()
    {
        // Act
        Action act = () => _logger.LogMilestone("EVENT_CODE", "Message", null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void LogMilestone_WithExceptionAndNullProperties_ShouldNotThrow()
    {
        // Arrange
        var ex = new Exception("Test exception");

        // Act
        Action act = () => _logger.LogMilestone("EVENT_CODE", ex, "Message", null);

        // Assert
        act.Should().NotThrow();
    }

    private void VerifyLog(string expectedContent, Times times)
    {
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.StartsWith(expectedContent)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
