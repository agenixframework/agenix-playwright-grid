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

using Agenix.PlaywrightGrid.Shared.Logging;
using FluentAssertions;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Logging;

public class ChunkedSinkTests
{
    private readonly Mock<ILogEventSink> _mockInternalSink;
    private readonly TestChunkedSink _sink;

    public ChunkedSinkTests()
    {
        _mockInternalSink = new Mock<ILogEventSink>();
        _sink = new TestChunkedSink(_mockInternalSink.Object);
    }

    [Fact]
    public void Emit_NonOperationEvent_WritesImmediately()
    {
        // Arrange
        var logEvent = CreateLogEvent();

        // Act
        _sink.Emit(logEvent);

        // Assert
        _mockInternalSink.Verify(x => x.Emit(logEvent), Times.Once);
    }

    [Fact]
    public void Emit_OperationEvent_BuffersUntilEnd()
    {
        // Arrange
        var operationId = Guid.NewGuid();
        var event1 = CreateOperationLogEvent(operationId, "OperationStart");
        var event2 = CreateOperationLogEvent(operationId, "Milestone");
        var event3 = CreateOperationLogEvent(operationId, "OperationEnd");

        // Act & Assert
        _sink.Emit(event1);
        _mockInternalSink.Verify(x => x.Emit(It.IsAny<LogEvent>()), Times.Never);

        _sink.Emit(event2);
        _mockInternalSink.Verify(x => x.Emit(It.IsAny<LogEvent>()), Times.Never);

        _sink.Emit(event3);
        _mockInternalSink.Verify(x => x.Emit(event1), Times.Once);
        _mockInternalSink.Verify(x => x.Emit(event2), Times.Once);
        _mockInternalSink.Verify(x => x.Emit(event3), Times.Once);
    }

    [Fact]
    public void Emit_BufferOverflow_FlushesAutomatically()
    {
        // Arrange
        var sink = new TestChunkedSink(_mockInternalSink.Object, maxEventsPerChunk: 2);
        var operationId = Guid.NewGuid();
        var event1 = CreateOperationLogEvent(operationId, "OperationStart");
        var event2 = CreateOperationLogEvent(operationId, "Milestone");

        // Act
        sink.Emit(event1);
        sink.Emit(event2); // Should flush because maxEventsPerChunk = 2

        // Assert
        _mockInternalSink.Verify(x => x.Emit(event1), Times.Once);
        _mockInternalSink.Verify(x => x.Emit(event2), Times.Once);
    }

    [Fact]
    public void Dispose_FlushesRemainingBuffers()
    {
        // Arrange
        var operationId = Guid.NewGuid();
        var event1 = CreateOperationLogEvent(operationId, "OperationStart");
        _sink.Emit(event1);

        // Act
        _sink.Dispose();

        // Assert
        _mockInternalSink.Verify(x => x.Emit(event1), Times.Once);
    }

    [Fact]
    public void Emit_NestedOperation_BuffersUntilRootEnd()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var nestedId = Guid.NewGuid();

        var rootStart = CreateOperationLogEvent(rootId, rootId, true, "OperationStart");
        var nestedStart = CreateOperationLogEvent(nestedId, rootId, false, "OperationStart");
        var nestedEnd = CreateOperationLogEvent(nestedId, rootId, false, "OperationEnd");
        var rootEnd = CreateOperationLogEvent(rootId, rootId, true, "OperationEnd");

        // Act & Assert
        _sink.Emit(rootStart);
        _sink.Emit(nestedStart);
        _sink.Emit(nestedEnd);
        _mockInternalSink.Verify(x => x.Emit(It.IsAny<LogEvent>()), Times.Never); // Should NOT flush yet

        _sink.Emit(rootEnd); // Should flush everything
        _mockInternalSink.Verify(x => x.Emit(rootStart), Times.Once);
        _mockInternalSink.Verify(x => x.Emit(nestedStart), Times.Once);
        _mockInternalSink.Verify(x => x.Emit(nestedEnd), Times.Once);
        _mockInternalSink.Verify(x => x.Emit(rootEnd), Times.Once);
    }

    private static LogEvent CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.Now,
            LogEventLevel.Information,
            null,
            new MessageTemplate("Test", []),
            []);
    }

    private static LogEvent CreateOperationLogEvent(Guid operationId, string eventType)
    {
        return CreateOperationLogEvent(operationId, operationId, true, eventType);
    }

    private static LogEvent CreateOperationLogEvent(Guid operationId, Guid rootId, bool isRoot, string eventType)
    {
        return new LogEvent(
            DateTimeOffset.Now,
            LogEventLevel.Information,
            null,
            new MessageTemplate("Test", []),
            [
                new LogEventProperty("OperationId", new ScalarValue(operationId)),
                new LogEventProperty("RootOperationId", new ScalarValue(rootId)),
                new LogEventProperty("IsRootOperation", new ScalarValue(isRoot)),
                new LogEventProperty("EventType", new ScalarValue(eventType))
            ]);
    }

    private class TestChunkedSink(ILogEventSink internalSink, int maxEventsPerChunk = 1000, int maxAgeSeconds = 60)
        : ChunkedSinkBase(maxEventsPerChunk, maxAgeSeconds)
    {
        protected override void WriteLogEvent(LogEvent logEvent)
        {
            internalSink.Emit(logEvent);
        }
    }
}
