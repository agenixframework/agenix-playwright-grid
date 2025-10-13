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

using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class PidRedisManagerTests
{
    private Mock<IDatabase> _mockRedis = null!;
    private Mock<ILogger<PidRedisManager>> _mockLogger = null!;
    private const string TestWorkerId = "worker-test-123";
    private PidRedisManager _pidManager = null!;

    [SetUp]
    public void SetUp()
    {
        _mockRedis = new Mock<IDatabase>();
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.Configuration).Returns("localhost:6379");
        _mockRedis.Setup(r => r.Multiplexer).Returns(mockMultiplexer.Object);
        _mockRedis.Setup(r => r.Database).Returns(0);

        _mockLogger = new Mock<ILogger<PidRedisManager>>();
        var chunkedLogger = new ChunkedLogger<PidRedisManager>(_mockLogger.Object, new ChunkedLoggerOptions { Enabled = true });
        _pidManager = new PidRedisManager(_mockRedis.Object, TestWorkerId, chunkedLogger);
    }

    #region A) InitializeAsync Tests

    [Test]
    public async Task InitializeAsync_NoTrackedPids_ReturnsEmptyList()
    {
        // Arrange
        _mockRedis
            .Setup(r => r.SetMembersAsync(
                It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:pids"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var result = await _pidManager.InitializeAsync();

        // Assert
        Assert.That(result, Is.Empty);
        _mockRedis.Verify(r => r.SetMembersAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task InitializeAsync_TrackedPidsFound_ReturnsList()
    {
        // Arrange
        var trackedPids = new RedisValue[] { 123, 456, 789 };
        _mockRedis
            .Setup(r => r.SetMembersAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(trackedPids);

        // Act
        var result = await _pidManager.InitializeAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result, Does.Contain(123));
        Assert.That(result, Does.Contain(456));
        Assert.That(result, Does.Contain(789));
    }

    [Test]
    public async Task InitializeAsync_RedisException_ReturnsEmptyListAndLogsError()
    {
        // Arrange
        _mockRedis
            .Setup(r => r.SetMembersAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        // Act
        var result = await _pidManager.InitializeAsync();

        // Assert
        Assert.That(result, Is.Empty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to initialize")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region B) TrackPidAsync Tests

    [Test]
    public async Task TrackPidAsync_ValidPid_TracksSuccessfully()
    {
        // Arrange
        var pid = 12345;
        var browserType = "Chromium";
        var labelKey = "AppA:Chromium:Staging";

        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.TrackPidAsync(pid, browserType, labelKey);

        // Assert
        mockTransaction.Verify(t => t.SetAddAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:pids"),
            pid,
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString() == $"pid:{pid}:metadata"),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task TrackPidAsync_TransactionFails_LogsWarning()
    {
        // Arrange
        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        await _pidManager.TrackPidAsync(12345, "Firefox", "AppB:Firefox:UAT");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to track PID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task TrackPidAsync_RedisException_LogsError()
    {
        // Arrange
        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        // Act
        await _pidManager.TrackPidAsync(12345, "Webkit", "AppC:Webkit:PROD");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to track PID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task TrackPidAsync_LargePID_TracksSuccessfully()
    {
        // Arrange
        var largePid = int.MaxValue - 1;
        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.TrackPidAsync(largePid, "Chromium", "App:Chromium:PROD");

        // Assert
        mockTransaction.Verify(t => t.SetAddAsync(
            It.IsAny<RedisKey>(),
            largePid,
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task TrackPidAsync_UnicodeInMetadata_SerializesCorrectly()
    {
        // Arrange
        var pid = 99999;
        var labelKey = "App:Chromium:PROD-日本語";
        var mockTransaction = new Mock<ITransaction>();

        string? capturedMetadata = null;
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>(
                (k, v, ts, keepTtl, w, f) => capturedMetadata = v.ToString())
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.TrackPidAsync(pid, "Chromium", labelKey);

        // Assert
        Assert.That(capturedMetadata, Is.Not.Null);
        // JSON serialization escapes Unicode characters, so check for either raw or escaped form
        Assert.That(capturedMetadata, Does.Contain("日本語").Or.Contain("\\u65E5\\u672C\\u8A9E"));
    }

    #endregion

    #region C) UntrackPidAsync Tests

    [Test]
    public async Task UntrackPidAsync_ValidPid_UntracksSuccessfully()
    {
        // Arrange
        var pid = 12345;
        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetRemoveAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.KeyDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.UntrackPidAsync(pid);

        // Assert
        mockTransaction.Verify(t => t.SetRemoveAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:pids"),
            pid,
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"pid:{pid}:metadata"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task UntrackPidAsync_Exception_LogsError()
    {
        // Arrange
        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        // Act
        await _pidManager.UntrackPidAsync(12345);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to untrack PID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region D) CleanupAsync Tests

    [Test]
    public async Task CleanupAsync_Success_CleansAllKeys()
    {
        // Arrange
        var trackedPids = new RedisValue[] { 1001, 1002, 1003 };

        _mockRedis
            .Setup(r => r.SetMembersAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(trackedPids);

        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.KeyDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.CleanupAsync();

        // Assert
        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == "pid:1001:metadata"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == "pid:1002:metadata"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == "pid:1003:metadata"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:pids"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:heartbeat"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:metadata"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task CleanupAsync_NoTrackedPids_CleansWorkerKeysOnly()
    {
        // Arrange
        _mockRedis
            .Setup(r => r.SetMembersAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.KeyDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.CleanupAsync();

        // Assert
        // Should only delete worker keys, not pid metadata (since no pids)
        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:pids"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:heartbeat"),
            It.IsAny<CommandFlags>()), Times.Once);

        mockTransaction.Verify(t => t.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:metadata"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task CleanupAsync_Exception_LogsError()
    {
        // Arrange
        _mockRedis
            .Setup(r => r.SetMembersAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        // Act
        await _pidManager.CleanupAsync();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to cleanup")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region E) Concurrent Operations Test

    [Test]
    public async Task ConcurrentOperations_HandleRaceConditionsCorrectly()
    {
        // Arrange
        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var tasks = new List<Task>();

        // Act - 10 parallel TrackPidAsync calls
        for (int i = 0; i < 10; i++)
        {
            var pid = 1000 + i;
            tasks.Add(_pidManager.TrackPidAsync(pid, "Chromium", "App:Chromium:PROD"));
        }

        await Task.WhenAll(tasks);

        // Assert - all 10 PIDs should be tracked
        mockTransaction.Verify(t => t.SetAddAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:pids"),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Exactly(10));
    }

    #endregion

    #region F) SendHeartbeatAsync Tests

    [Test]
    public async Task SendHeartbeatAsync_Success_SetsRedisKey()
    {
        // Arrange
        _mockRedis
            .Setup(r => r.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:heartbeat"),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _pidManager.SendHeartbeatAsync();

        // Assert
        _mockRedis.Verify(r => r.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString() == $"worker:{TestWorkerId}:heartbeat"),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(5)),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.AtLeastOnce); // At least once because it also fires on CTOR
    }

    [Test]
    public async Task SendHeartbeatAsync_Exception_LogsError()
    {
        // Arrange
        _mockRedis
            .Setup(r => r.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection failed"));

        // Act
        await _pidManager.SendHeartbeatAsync();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to send heartbeat")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region G) DetectAndKillOrphansAsync Tests

    [Test]
    public async Task DetectAndKillOrphansAsync_PidsNotRunning_CleansUpRedis()
    {
        // Arrange
        var nonExistentPids = new List<int> { -1, -2 };

        var mockTransaction = new Mock<ITransaction>();
        _mockRedis
            .Setup(r => r.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var killedCount = await _pidManager.DetectAndKillOrphansAsync(nonExistentPids);

        // Assert
        Assert.That(killedCount, Is.EqualTo(0)); // They were just cleaned up from Redis, not killed (already dead)

        // Should untrack each PID
        mockTransaction.Verify(t => t.SetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Exactly(2));
    }

    #endregion
}
