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

using System.Collections.Concurrent;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

public class HeartbeatServiceTests
{
    private static WorkerOptions CreateOptions()
    {
        return new WorkerOptions
        {
            NodeId = "node-123",
            Labels =
                new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["region"] = "eu" },
            PoolConfig = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppA:Chromium:Staging"] = 2,
                ["AppB:Firefox:Prod"] = 1
            }
        };
    }

    [Test]
    public async Task HeartbeatOnceAsync_WritesExpectedKeysAndSetsTTL()
    {
        // Arrange
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);

        // Default setups for called methods
        db.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger<HeartbeatService>>(MockBehavior.Loose);
        var svc = new HeartbeatService(options, db.Object);

        // Act
        await svc.HeartbeatOnceAsync();

        // Assert
        var nodeKey = $"node:{options.NodeId}";
        var expectedLabelsJson = JsonSerializer.Serialize(options.Labels.ToDictionary(k => k.Key, v => v.Value));
        var expectedCapacity = options.PoolConfig.Values.Sum().ToString();

        db.Verify(d => d.HashSetAsync(
            It.Is<RedisKey>(k => k.ToString() == nodeKey),
            It.Is<RedisValue>(f => f.ToString() == "LastSeen"),
            It.Is<RedisValue>(v => !string.IsNullOrWhiteSpace(v.ToString())),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);

        db.Verify(d => d.HashSetAsync(
            It.Is<RedisKey>(k => k.ToString() == nodeKey),
            It.Is<RedisValue>(f => f.ToString() == "Labels"),
            It.Is<RedisValue>(v => v.ToString() == expectedLabelsJson),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);

        db.Verify(d => d.HashSetAsync(
            It.Is<RedisKey>(k => k.ToString() == nodeKey),
            It.Is<RedisValue>(f => f.ToString() == "Capacity"),
            It.Is<RedisValue>(v => v.ToString() == expectedCapacity),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);

        db.Verify(d => d.SetAddAsync(
            It.Is<RedisKey>(k => k.ToString() == "nodes"),
            It.Is<RedisValue>(v => v.ToString() == options.NodeId),
            It.IsAny<CommandFlags>()), Times.Once);

        db.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString() == $"node_alive:{options.NodeId}"),
            It.Is<RedisValue>(v => v.ToString() == "1"),
            It.Is<TimeSpan?>(t => t.HasValue && Math.Abs((t.Value - TimeSpan.FromSeconds(90)).TotalSeconds) < 0.001),
            It.Is<bool>(keep => !keep),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task HeartbeatOnceAsync_WhenDbThrows_DoesNotPropagate()
    {
        // Arrange
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        db.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("boom"));

        var logger = new Mock<ILogger<HeartbeatService>>(MockBehavior.Loose);
        var svc = new HeartbeatService(options, db.Object);

        // Act + Assert: should not throw
        await svc.HeartbeatOnceAsync();
    }

    [Test]
    public async Task HeartbeatOnceAsync_WithChunkedLogger_LogsToChunked()
    {
        // Arrange
        var options = CreateOptions();
        var db = new Mock<IDatabase>();
        db.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger<HeartbeatService>>();
        var chunkedLogger = new ChunkedLogger<HeartbeatService>(logger.Object);
        var svc = new HeartbeatService(options, db.Object, chunkedLogger);

        // Act
        await svc.HeartbeatOnceAsync();

        // Assert - Verify that milestone was logged via underlying logger
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.Worker.HeartbeatTick)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task HeartbeatLoopAsync_GapDetection_InvokesCallbackAndLogs()
    {
        // Arrange
        var options = new WorkerOptions
        {
            NodeId = "node-gap",
            HeartbeatIntervalSeconds = 1
        };
        var db = new Mock<IDatabase>();
        db.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger<HeartbeatService>>();
        var chunkedLogger = new ChunkedLogger<HeartbeatService>(logger.Object);
        var svc = new HeartbeatService(options, db.Object, chunkedLogger);

        var callbackInvoked = false;
        svc.SetGapDetectedCallback(() =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        });

        // Set last heartbeat to past to trigger gap
        var field = typeof(HeartbeatService).GetField("_lastHeartbeatTime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(svc, DateTimeOffset.UtcNow.AddSeconds(-10));

        using var cts = new CancellationTokenSource();

        // Act
        var loopTask = svc.HeartbeatLoopAsync(cts.Token);

        // Wait for loop to run at least once
        for (var i = 0; i < 10 && !callbackInvoked; i++)
        {
            await Task.Delay(100);
        }

        cts.Cancel();
        try
        {
            await loopTask;
        }
        catch (OperationCanceledException)
        {
        }

        // Assert
        Assert.That(callbackInvoked, Is.True, "Callback was not invoked");
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.Worker.HeartbeatGapDetected)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }
}
