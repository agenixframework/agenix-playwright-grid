using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

public class HeartbeatServiceLoopTests
{
    private static WorkerOptions CreateOptions()
    {
        return new WorkerOptions
        {
            NodeId = "node-loop",
            Labels =
                new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["region"] = "eu" },
            PoolConfig =
                new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AppA:Chromium:Staging"] = 1
                }
        };
    }

    [Test]
    public async Task HeartbeatLoopAsync_CancelsAndCompletes_WithoutThrowing()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);

        // Setup all called operations to succeed
        db.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var svc = new HeartbeatService(options, db.Object);
        using var cts = new CancellationTokenSource();

        var loopTask = svc.HeartbeatLoopAsync(cts.Token);

        // Cancel quickly to prevent long waits
        cts.CancelAfter(50);

        var completed = await Task.WhenAny(loopTask, Task.Delay(2000));
        Assert.That(completed == loopTask, Is.True, "Heartbeat loop did not complete upon cancellation in time");
    }
}
