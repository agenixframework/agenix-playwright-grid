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
public class LaunchRetentionWorkerTests
{
    private Mock<IConfiguration> _configMock;
    private Mock<IHousekeepingDataSource> _dataSourceMock;
    private Mock<IProjectSettingsReader> _settingsReaderMock;
    private Mock<IDatabase> _dbMock;
    private Mock<ILogger<LaunchRetentionWorker>> _loggerMock;
    private ChunkedLogger<LaunchRetentionWorker> _chunkedLogger;
    private LaunchRetentionWorker _worker;

    [SetUp]
    public void SetUp()
    {
        _configMock = new Mock<IConfiguration>();
        _dataSourceMock = new Mock<IHousekeepingDataSource>();
        _settingsReaderMock = new Mock<IProjectSettingsReader>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<LaunchRetentionWorker>>();
        _chunkedLogger = new ChunkedLogger<LaunchRetentionWorker>(_loggerMock.Object);

        _worker = new LaunchRetentionWorker(
            _configMock.Object,
            _dataSourceMock.Object,
            _settingsReaderMock.Object,
            _dbMock.Object,
            _loggerMock.Object,
            _chunkedLogger);
    }

    [Test]
    public async Task ExecuteAsync_ShouldProcessProjectsAndExcludeDisabledOnes()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _configMock.Setup(x => x["AGENIX_HOUSEKEEPING_LAUNCH_RETENTION_CHECK_INTERVAL_HOURS"]).Returns("1");
        _configMock.Setup(x => x["AGENIX_HOUSEKEEPING_LEADERSHIP"]).Returns("false");

        _settingsReaderMock.Setup(x => x.GetAllProjectKeysAsync()).ReturnsAsync(new List<string> { "p1", "p2" });
        _settingsReaderMock.Setup(x => x.GetRetentionSettingsAsync("p1"))
                           .ReturnsAsync(new RetentionSettings { ProjectKey = "p1", KeepLaunchesDays = 30, KeepLogsDays = 7, KeepAttachmentsDays = 7, KeepAuditDays = 7 });
        _settingsReaderMock.Setup(x => x.GetRetentionSettingsAsync("p2"))
                           .ReturnsAsync(new RetentionSettings { ProjectKey = "p2", KeepLaunchesDays = 0, KeepLogsDays = 7, KeepAttachmentsDays = 7, KeepAuditDays = 7 });

        var connectionMock = new Mock<DbConnection>();
        var commandMock = new Mock<DbCommand>();
        var parameters = new TestDbParameterCollection();

        _dataSourceMock.Setup(x => x.OpenConnectionAsync(It.IsAny<CancellationToken>()))
                       .ReturnsAsync(connectionMock.Object);

        connectionMock.Protected().Setup<DbCommand>("CreateDbCommand").Returns(commandMock.Object);
        commandMock.Protected().Setup<DbParameter>("CreateDbParameter").Returns(new TestDbParameter());
        commandMock.Protected().SetupGet<DbParameterCollection>("DbParameterCollection").Returns(parameters);

        // Act
        // Run ExecuteAsync in a task and cancel after a short delay to stop the loop
        var runTask = _worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await runTask;

        // Assert
        _settingsReaderMock.Verify(x => x.GetRetentionSettingsAsync("p1"), Times.AtLeastOnce);
        _settingsReaderMock.Verify(x => x.GetRetentionSettingsAsync("p2"), Times.AtLeastOnce);
        // p2 has 0 days, so it should be skipped
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipping project p2")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
