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
using Npgsql;
using NUnit.Framework;
using RabbitMQ.Client;

namespace IngestionService.Tests.Workers;

[TestFixture]
public class ArtifactUploadWorkerTests
{
    private Mock<IRabbitMqConsumer> _consumerMock;
    private Mock<IConfiguration> _configMock;
    private Mock<ILogger<ArtifactUploadWorker>> _loggerMock;
    private ChunkedLogger<ArtifactUploadWorker> _chunkedLogger;
    private Mock<IConnection> _connectionMock;
    private Mock<IModel> _channelMock;
    private Mock<NpgsqlDataSource> _dataSourceMock;

    [SetUp]
    public void SetUp()
    {
        _consumerMock = new Mock<IRabbitMqConsumer>();
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<ArtifactUploadWorker>>();
        _chunkedLogger = new ChunkedLogger<ArtifactUploadWorker>(_loggerMock.Object, new ChunkedLoggerOptions());

        _connectionMock = new Mock<IConnection>();
        _channelMock = new Mock<IModel>();

        _consumerMock.Setup(x => x.GetConnection()).Returns(_connectionMock.Object);
        _connectionMock.Setup(x => x.CreateModel()).Returns(_channelMock.Object);
    }

    [Test]
    public async Task ExecuteAsync_DeclaresQueue_AndStartsConsuming()
    {
        // Arrange
        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);
        _configMock.Setup(x => x["AGENIX_ARTIFACTS_STORAGE_BACKEND"]).Returns("local");
        _configMock.Setup(x => x["AGENIX_ARTIFACTS_STORAGE_PATH"]).Returns("./test_artifacts");

        var cts = new CancellationTokenSource();
        var worker = new ArtifactUploadWorker(
            _configMock.Object,
            _consumerMock.Object,
            _loggerMock.Object,
            _chunkedLogger,
            null!); // dataSource

        // Act
        // Use a task to run it as it blocks on Task.Delay(Timeout.Infinite)
        var runTask = worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(cts.Token);

        // Assert
        _channelMock.Verify(x => x.QueueDeclare(
            "agenix-test-platform.artifacts",
            true, false, false,
            It.IsAny<IDictionary<string, object>>()), Times.Once);

        _channelMock.Verify(x => x.BasicConsume(
            "agenix-test-platform.artifacts",
            false,
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IBasicConsumer>()), Times.Once);
    }

    [Test]
    public void SanitizeFileName_WithNull_ReturnsDefaultName()
    {
        var result = ArtifactUploadWorker.SanitizeFileName(null);
        Assert.That(result, Is.EqualTo("artifact"));
    }

    [Test]
    public void SanitizeFileName_WithEmptyString_ReturnsDefaultName()
    {
        var result = ArtifactUploadWorker.SanitizeFileName("");
        Assert.That(result, Is.EqualTo("artifact"));
    }

    [Test]
    public void SanitizeFileName_WithInvalidChars_SanitizesCorrectly()
    {
        // '/' is universally invalid as a file name character across platforms
        var result = ArtifactUploadWorker.SanitizeFileName("test/filename.txt");
        Assert.That(result, Is.EqualTo("test_filename.txt"));
    }

    [Test]
    public async Task ProcessArtifactUploadAsync_WithEmptyIds_ReturnsWithoutThrowing()
    {
        // Arrange
        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);
        _configMock.Setup(x => x["AGENIX_ARTIFACTS_STORAGE_BACKEND"]).Returns("local");
        _configMock.Setup(x => x["AGENIX_ARTIFACTS_STORAGE_PATH"]).Returns("./test_artifacts");

        var worker = new ArtifactUploadWorker(
            _configMock.Object,
            _consumerMock.Object,
            _loggerMock.Object,
            _chunkedLogger,
            null!);

        var evt = new ArtifactUploadEvent(
            Guid.Empty,
            Guid.Empty,
            "test.txt",
            "text/plain",
            0,
            [],
            DateTime.UtcNow,
            "project"
        );

        // Act & Assert
        // Should not throw and should not call any storage/database methods
        Assert.DoesNotThrowAsync(async () => await worker.ProcessArtifactUploadAsync(evt, CancellationToken.None));
    }

    [Test]
    public async Task ProcessArtifactUploadAsync_WithNullContent_ReturnsWithoutThrowing()
    {
        // Arrange
        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);
        _configMock.Setup(x => x["AGENIX_ARTIFACTS_STORAGE_BACKEND"]).Returns("local");
        _configMock.Setup(x => x["AGENIX_ARTIFACTS_STORAGE_PATH"]).Returns("./test_artifacts");

        var worker = new ArtifactUploadWorker(
            _configMock.Object,
            _consumerMock.Object,
            _loggerMock.Object,
            _chunkedLogger,
            null!);

        var evt = new ArtifactUploadEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test.txt",
            "text/plain",
            0,
            null!, // Content is null
            DateTime.UtcNow,
            "project"
        );

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await worker.ProcessArtifactUploadAsync(evt, CancellationToken.None));
    }
}
