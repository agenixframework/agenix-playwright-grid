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
using HousekeepingService.Shared;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace HousekeepingService.Tests.Shared;

[TestFixture]
public class HousekeepingWorkerBaseTests
{
    private Mock<ILogger> _loggerMock;
    private ChunkedLogger _chunkedLogger;
    private TestWorker _worker;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger>();
        _chunkedLogger = new ChunkedLogger(_loggerMock.Object, "TestWorker");
        _worker = new TestWorker(_chunkedLogger);
    }

    [Test]
    public void BeginWorkerOperation_ShouldAddWorkerNameAndOperationToInputs()
    {
        // Act
        using var op = _worker.CallBeginWorkerOperation("TestOp", new Dictionary<string, object> { ["custom"] = "val" });

        // Assert
        Assert.That(OperationContext.Current, Is.Not.Null);
        Assert.That(OperationContext.Current!.OperationName, Is.EqualTo("TestWorker.TestOp"));
        Assert.That(OperationContext.Current.Properties["workerName"], Is.EqualTo("TestWorker"));
        Assert.That(OperationContext.Current.Properties["operation"], Is.EqualTo("TestOp"));
        Assert.That(OperationContext.Current.Properties["custom"], Is.EqualTo("val"));
    }

    [Test]
    public void LogWorkerMilestone_ShouldPrependWorkerName()
    {
        // Act
        _worker.CallLogWorkerMilestone("E123", "Message {Arg}", "val");

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[TestWorker] Message val")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private class TestWorker(ChunkedLogger chunkedLogger) : HousekeepingWorkerBase(chunkedLogger, "TestWorker")
    {
        public IChunkedOperation CallBeginWorkerOperation(string operationName, Dictionary<string, object>? inputs = null)
        {
            return BeginWorkerOperation(operationName, inputs);
        }

        public void CallLogWorkerMilestone(string eventCode, string messageTemplate, params object[] args)
        {
            LogWorkerMilestone(eventCode, messageTemplate, args);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
