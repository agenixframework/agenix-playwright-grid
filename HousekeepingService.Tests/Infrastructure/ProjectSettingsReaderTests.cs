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

using System.Net;
using System.Text.Json;
using HousekeepingService.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;

namespace HousekeepingService.Tests.Infrastructure;

[TestFixture]
public class ProjectSettingsReaderTests
{
    private Mock<IDatabase> _dbMock;
    private Mock<IConnectionMultiplexer> _muxMock;
    private Mock<ILogger<ProjectSettingsReader>> _loggerMock;
    private ProjectSettingsReader _reader;

    [SetUp]
    public void SetUp()
    {
        _dbMock = new Mock<IDatabase>();
        _muxMock = new Mock<IConnectionMultiplexer>();
        _loggerMock = new Mock<ILogger<ProjectSettingsReader>>();
        _reader = new ProjectSettingsReader(_dbMock.Object, _muxMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task GetRetentionSettingsAsync_WhenKeyDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _reader.GetRetentionSettingsAsync("test-project");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetRetentionSettingsAsync_WhenJsonIsInvalid_ShouldReturnNullAndLogWarning()
    {
        // Arrange
        _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync("invalid-json");

        // Act
        var result = await _reader.GetRetentionSettingsAsync("test-project");

        // Assert
        Assert.That(result, Is.Null);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to get retention settings")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task GetRetentionSettingsAsync_WhenSettingsExist_ShouldParseCorrectly()
    {
        // Arrange
        var settingsJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["keepLaunches"] = 45,
            ["keepLogs"] = "14d",
            ["keepAttachments"] = 10,
            ["keepAudit"] = "60",
            ["launchInactivityTimeout"] = "12h"
        });

        _dbMock.Setup(x => x.StringGetAsync((RedisKey)"project:test-project:settings", It.IsAny<CommandFlags>()))
               .ReturnsAsync(settingsJson);

        // Act
        var result = await _reader.GetRetentionSettingsAsync("test-project");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ProjectKey, Is.EqualTo("test-project"));
        Assert.That(result.KeepLaunchesDays, Is.EqualTo(45));
        Assert.That(result.KeepLogsDays, Is.EqualTo(14));
        Assert.That(result.KeepAttachmentsDays, Is.EqualTo(10));
        Assert.That(result.KeepAuditDays, Is.EqualTo(60));
        Assert.That(result.LaunchInactivityTimeout, Is.EqualTo("12h"));
    }

    [Test]
    public async Task GetRetentionSettingsAsync_WhenSomeSettingsMissing_ShouldUseDefaultValues()
    {
        // Arrange
        var settingsJson = "{}";

        _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(settingsJson);

        // Act
        var result = await _reader.GetRetentionSettingsAsync("test-project");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.KeepLaunchesDays, Is.EqualTo(30));
        Assert.That(result.KeepLogsDays, Is.EqualTo(7));
        Assert.That(result.KeepAttachmentsDays, Is.EqualTo(7));
        Assert.That(result.KeepAuditDays, Is.EqualTo(90));
        Assert.That(result.LaunchInactivityTimeout, Is.Null);
    }

    [Test]
    public async Task GetAllProjectKeysAsync_ShouldReturnProjectKeysFromRedis()
    {
        // Arrange
        var serverMock = new Mock<IServer>();
        var endPoint = new IPEndPoint(IPAddress.Loopback, 6379);
        _muxMock.Setup(x => x.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { endPoint });
        _muxMock.Setup(x => x.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>())).Returns(serverMock.Object);

        var redisKeys = new RedisKey[] { "project:p1:settings", "project:p2:settings" };

        serverMock.Setup(x => x.KeysAsync(It.IsAny<int>(), (RedisValue)"project:*:settings", It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
                  .Returns(redisKeys.ToAsyncEnumerable());

        // Act
        var result = await _reader.GetAllProjectKeysAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("p1"));
        Assert.That(result, Contains.Item("p2"));
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> enumerable)
    {
        foreach (var item in enumerable)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
