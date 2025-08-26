using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

        var svc = new HeartbeatService(options, db.Object);

        // Act + Assert: should not throw
        await svc.HeartbeatOnceAsync();
    }
}
