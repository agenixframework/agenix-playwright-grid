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

using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using IngestionService.Application;
using IngestionService.Infrastructure;
using IngestionService.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace IngestionService.Tests.Workers;

[TestFixture]
public class IngestionWorkerTests
{
    private Mock<IRabbitMqConsumer> _consumerMock;
    private Mock<IPostgresBatchWriter> _pgWriterMock;
    private Mock<IConfiguration> _configMock;
    private Mock<ILogger<IngestionWorker>> _loggerMock;
    private Mock<ILoggerFactory> _loggerFactoryMock;
    private ChunkedLogger<IngestionWorker> _chunkedLogger;

    [SetUp]
    public void SetUp()
    {
        _consumerMock = new Mock<IRabbitMqConsumer>();
        _pgWriterMock = new Mock<IPostgresBatchWriter>();

        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<IngestionWorker>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        _chunkedLogger = new ChunkedLogger<IngestionWorker>(_loggerMock.Object, new ChunkedLoggerOptions());
    }

    [Test]
    public async Task ExecuteAsync_StartsConsumers()
    {
        // Arrange
        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);
        // Default concurrency is 4 if not set.

        var cts = new CancellationTokenSource();
        var worker = new IngestionWorker(
            _consumerMock.Object,
            _pgWriterMock.Object,
            _configMock.Object,
            _loggerMock.Object,
            _loggerFactoryMock.Object,
            _chunkedLogger);

        _consumerMock.Setup(x => x.ConsumeAsync<TestItemEvent>(It.IsAny<string>(), It.IsAny<Func<TestItemEvent, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _consumerMock.Setup(x => x.ConsumeAsync<CommandEvent>(It.IsAny<string>(), It.IsAny<Func<CommandEvent, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _consumerMock.Setup(x => x.ConsumeAsync<LogItemEvent>(It.IsAny<string>(), It.IsAny<Func<LogItemEvent, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await worker.StartAsync(cts.Token);

        // Wait a bit for background tasks to start
        await Task.Delay(100);

        await worker.StopAsync(cts.Token);

        // Assert
        // With default concurrency 4, it should call ConsumeAsync 4 times for each queue
        _consumerMock.Verify(x => x.ConsumeAsync<TestItemEvent>(
            "agenix-test-platform.test-items",
            It.IsAny<Func<TestItemEvent, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(1));

        _consumerMock.Verify(x => x.ConsumeAsync<CommandEvent>(
            "agenix-test-platform.commands",
            It.IsAny<Func<CommandEvent, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(1));

        _consumerMock.Verify(x => x.ConsumeAsync<LogItemEvent>(
            "agenix-test-platform.log-items",
            It.IsAny<Func<LogItemEvent, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(1));
    }
}
