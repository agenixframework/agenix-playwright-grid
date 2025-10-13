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
using NUnit.Framework;
using StackExchange.Redis;

namespace HousekeepingService.Tests.Workers;

[TestFixture]
public class LaunchAutoStopWorkerTests
{
    private Mock<IConfiguration> _configMock;
    private Mock<IDatabase> _redisMock;
    private Mock<IHousekeepingDataSource> _dataSourceMock;
    private Mock<IProjectSettingsReader> _settingsReaderMock;
    private Mock<ILogger<LaunchAutoStopWorker>> _loggerMock;
    private ChunkedLogger<LaunchAutoStopWorker> _chunkedLogger;
    private LaunchAutoStopWorker _worker;

    [SetUp]
    public void SetUp()
    {
        _configMock = new Mock<IConfiguration>();
        _redisMock = new Mock<IDatabase>();
        _dataSourceMock = new Mock<IHousekeepingDataSource>();
        _settingsReaderMock = new Mock<IProjectSettingsReader>();
        _loggerMock = new Mock<ILogger<LaunchAutoStopWorker>>();
        _chunkedLogger = new ChunkedLogger<LaunchAutoStopWorker>(_loggerMock.Object);

        _worker = new LaunchAutoStopWorker(
            _configMock.Object,
            _redisMock.Object,
            _dataSourceMock.Object,
            _settingsReaderMock.Object,
            _loggerMock.Object,
            _chunkedLogger);
    }

    [Test]
    public async Task ExecuteAsync_ShouldProcessProjectsAndStopInactiveLaunches()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _configMock.Setup(x => x["AGENIX_HOUSEKEEPING_LAUNCH_AUTO_STOP_INTERVAL_MINUTES"]).Returns("1");
        _configMock.Setup(x => x["AGENIX_HOUSEKEEPING_LEADERSHIP"]).Returns("false");

        _settingsReaderMock.Setup(x => x.GetAllProjectKeysAsync()).ReturnsAsync(new List<string> { "p1" });
        _settingsReaderMock.Setup(x => x.GetRetentionSettingsAsync("p1"))
                           .ReturnsAsync(new RetentionSettings
                           {
                               ProjectKey = "p1",
                               KeepLaunchesDays = 30,
                               KeepLogsDays = 7,
                               KeepAttachmentsDays = 7,
                               KeepAuditDays = 7
                           });

        var connectionMock = new Mock<DbConnection>();
        var commandMock = new Mock<DbCommand>();
        commandMock.SetupAllProperties();
        var readerMock = new Mock<DbDataReader>();
        var parameters = new TestDbParameterCollection();

        _dataSourceMock.Setup(x => x.OpenConnectionAsync(It.IsAny<CancellationToken>()))
                       .ReturnsAsync(connectionMock.Object);

        connectionMock.Protected().Setup<DbCommand>("CreateDbCommand").Returns(commandMock.Object);
        commandMock.Protected().Setup<DbParameter>("CreateDbParameter").Returns(new TestDbParameter());
        commandMock.Protected().SetupGet<DbParameterCollection>("DbParameterCollection").Returns(parameters);

        // Mock FindSql
        commandMock.Protected().Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync", ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
                   .ReturnsAsync(readerMock.Object);

        var readSequence = new List<bool> { true, false };
        var readIndex = 0;
        readerMock.Setup(x => x.ReadAsync(It.IsAny<CancellationToken>()))
                  .Returns(() => Task.FromResult(readSequence[readIndex++]));
        readerMock.Setup(x => x.GetGuid(0)).Returns(Guid.NewGuid());
        readerMock.Setup(x => x.GetString(1)).Returns("Test Launch");
        readerMock.Setup(x => x.GetDateTime(2)).Returns(DateTime.UtcNow.AddHours(-1));

        // Mock Transaction for StopLaunchAsync
        var transactionMock = new Mock<DbTransaction>();
        connectionMock.Protected().Setup<ValueTask<DbTransaction>>("BeginDbTransactionAsync", ItExpr.IsAny<System.Data.IsolationLevel>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(transactionMock.Object);

        // Act
        var runTask = _worker.StartAsync(cts.Token);
        await Task.Delay(200, cts.Token);
        await cts.CancelAsync();
        await runTask;

        // Assert
        // Verify that the SQL used 'id' and not 'launch_id' for launches table
        Assert.That(commandMock.Object.CommandText, Is.Not.Null);
        Assert.That(commandMock.Object.CommandText, Does.Not.Contain("launch_id"));
        Assert.That(commandMock.Object.CommandText, Does.Not.Contain("created_at"));
        Assert.That(commandMock.Object.CommandText, Does.Not.Contain("end_time"));

        // It should contain the correct columns
        Assert.That(commandMock.Object.CommandText, Does.Contain("id").Or.Contain("start_time").Or.Contain("finish_time"));
    }
}
