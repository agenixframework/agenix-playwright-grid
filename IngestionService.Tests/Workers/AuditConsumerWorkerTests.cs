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
using IngestionService.Infrastructure;
using IngestionService.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace IngestionService.Tests.Workers;

[TestFixture]
public class AuditConsumerWorkerTests
{
    private Mock<IRabbitMqConsumer> _consumerMock;
    private Mock<IPostgresBatchWriter> _pgWriterMock;
    private Mock<IConfiguration> _configMock;
    private Mock<ILogger<AuditConsumerWorker>> _loggerMock;
    private Mock<ILoggerFactory> _loggerFactoryMock;
    private ChunkedLogger<AuditConsumerWorker> _chunkedLogger;

    [SetUp]
    public void SetUp()
    {
        _consumerMock = new Mock<IRabbitMqConsumer>();
        _pgWriterMock = new Mock<IPostgresBatchWriter>();
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<AuditConsumerWorker>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        _chunkedLogger = new ChunkedLogger<AuditConsumerWorker>(_loggerMock.Object, new ChunkedLoggerOptions());
    }

    [Test]
    public async Task ExecuteAsync_StartsConsumer()
    {
        // Arrange
        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

        var cts = new CancellationTokenSource();
        var worker = new AuditConsumerWorker(
            _configMock.Object,
            _loggerMock.Object,
            _loggerFactoryMock.Object,
            _chunkedLogger,
            _consumerMock.Object,
            _pgWriterMock.Object);

        _consumerMock.Setup(x => x.ConsumeAsync<AuditEvent>(It.IsAny<string>(), It.IsAny<Func<AuditEvent, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(cts.Token);

        // Assert
        _consumerMock.Verify(x => x.ConsumeAsync<AuditEvent>(
            "agenix-test-platform.audit",
            It.IsAny<Func<AuditEvent, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
