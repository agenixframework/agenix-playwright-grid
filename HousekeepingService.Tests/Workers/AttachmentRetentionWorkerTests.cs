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

using System.Data;
using System.Data.Common;
using Agenix.PlaywrightGrid.Shared.Logging;
using HousekeepingService.Infrastructure;
using HousekeepingService.Shared;
using HousekeepingService.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Npgsql;
using NUnit.Framework;
using StackExchange.Redis;

namespace HousekeepingService.Tests.Workers;

[TestFixture]
public class AttachmentRetentionWorkerTests
{
    private Mock<IConfiguration> _configMock;
    private Mock<IHousekeepingDataSource> _dataSourceMock;
    private Mock<IProjectSettingsReader> _settingsReaderMock;
    private Mock<IDatabase> _dbMock;
    private Mock<IMinioStorageService> _minioServiceMock;
    private Mock<ILogger<AttachmentRetentionWorker>> _loggerMock;
    private ChunkedLogger<AttachmentRetentionWorker> _chunkedLogger;
    private AttachmentRetentionWorker _worker;

    [SetUp]
    public void SetUp()
    {
        _configMock = new Mock<IConfiguration>();
        _dataSourceMock = new Mock<IHousekeepingDataSource>();
        _settingsReaderMock = new Mock<IProjectSettingsReader>();
        _dbMock = new Mock<IDatabase>();
        _minioServiceMock = new Mock<IMinioStorageService>();
        _loggerMock = new Mock<ILogger<AttachmentRetentionWorker>>();
        _chunkedLogger = new ChunkedLogger<AttachmentRetentionWorker>(_loggerMock.Object);

        _configMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

        _worker = new AttachmentRetentionWorker(
            _configMock.Object,
            _dataSourceMock.Object,
            _settingsReaderMock.Object,
            _dbMock.Object,
            _minioServiceMock.Object,
            _loggerMock.Object,
            _chunkedLogger);
    }

    [Test]
    public async Task ExecuteAsync_ShouldProcessProjects()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _configMock.Setup(x => x["AGENIX_HOUSEKEEPING_ATTACHMENT_RETENTION_CHECK_INTERVAL_HOURS"]).Returns("1");
        _configMock.Setup(x => x["AGENIX_HOUSEKEEPING_LEADERSHIP"]).Returns("false");

        _settingsReaderMock.Setup(x => x.GetAllProjectKeysAsync()).ReturnsAsync(new List<string> { "p1" });
        _settingsReaderMock.Setup(x => x.GetRetentionSettingsAsync("p1"))
                           .ReturnsAsync(new RetentionSettings { ProjectKey = "p1", KeepLaunchesDays = 30, KeepLogsDays = 7, KeepAttachmentsDays = 14, KeepAuditDays = 90 });

        var connectionMock = new Mock<DbConnection>();
        var commandMock = new Mock<DbCommand>();
        var parameters = new TestDbParameterCollection();
        var readerMock = new Mock<DbDataReader>();

        _dataSourceMock.Setup(x => x.OpenConnectionAsync(It.IsAny<CancellationToken>()))
                       .ReturnsAsync(connectionMock.Object);

        connectionMock.Protected().Setup<DbCommand>("CreateDbCommand").Returns(commandMock.Object);
        commandMock.Protected().Setup<DbParameter>("CreateDbParameter").Returns(new TestDbParameter());
        commandMock.Protected().SetupGet<DbParameterCollection>("DbParameterCollection").Returns(parameters);

        commandMock.Protected()
            .Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync", ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(readerMock.Object);

        // Setup reader to return no artifacts
        readerMock.Setup(x => x.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var runTask = _worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await runTask;

        // Assert
        _settingsReaderMock.Verify(x => x.GetRetentionSettingsAsync("p1"), Times.AtLeastOnce);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting attachment retention check")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
