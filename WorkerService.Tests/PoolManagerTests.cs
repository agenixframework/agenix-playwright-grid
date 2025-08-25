using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

public class PoolManagerTests
{
    private static WorkerOptions CreateOptions()
    {
        return new WorkerOptions
        {
            NodeId = "node-1",
            Labels = new(StringComparer.OrdinalIgnoreCase) { ["region"] = "eu" },
            PoolConfig = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AppA:Chromium:Staging"] = 0 // start from 0; tests will invoke WarmLabelAsync with counts we choose
            },
            PublicWsHost = "public.example",
            PublicWsPort = "8080",
            PublicWsScheme = "ws"
        };
    }

    private static Process CreateDummyProcess()
    {
        // A non-started process instance is enough for wiring events and storing into Slot
        return new Process();
    }

    [Test]
    public async Task WarmLabelAsync_PushesToRedis_AndUpdatesMetrics()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Strict);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Strict);

        // Sidecar returns a dummy process and internal ws
        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));

        // Redis pushes per created slot
        db.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        // After warm, ListLengthAsync is called once to update metric
        db.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        // Metrics expectations
        metrics.Setup(m => m.SetPoolCapacity(options.NodeId, "AppA:Chromium:Staging", It.Is<int>(c => c == 2)));
        metrics.Setup(m => m.SetPoolAvailable(options.NodeId, "AppA:Chromium:Staging", 2));

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object);

        await pool.WarmLabelAsync("AppA:Chromium:Staging", 2);

        // Verify pushes happened twice
        db.Verify(d => d.ListRightPushAsync(
            It.Is<RedisKey>(k => k.ToString() == "available:AppA:Chromium:Staging"),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Exactly(2));

        // Verify metrics updated
        metrics.VerifyAll();

        // And TryGetFirstSlot should now return a slot
        Assert.That(pool.TryGetFirstSlot("AppA:Chromium:Staging", out var browserId, out var slot), Is.True);
        Assert.That(string.IsNullOrWhiteSpace(browserId), Is.False);
        Assert.That(slot, Is.Not.Null);
        Assert.That(slot.BrowserType, Is.EqualTo("Chromium"));
        Assert.That(slot.PublicWs.StartsWith("ws://public.example:8080/ws/"), Is.True);

        // TryFindSlotById finds the same slot
        Assert.That(pool.TryFindSlotById(browserId, out var slot2), Is.True);
        Assert.That(slot2.PublicWs, Is.EqualTo(slot.PublicWs));
    }

    [Test]
    public async Task CleanupLabelListsAsync_RemovesStaleItems_ForNodeAndLocalhost()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);

        var listKeyAvail = "available:AppA:Chromium:Staging";
        var listKeyInuse = "inuse:AppA:Chromium:Staging";

        var staleThisNode = $"{{\"nodeId\":\"{options.NodeId}\",\"browserId\":\"b1\",\"webSocketEndpoint\":\"ws://public/ws/b1\"}}";
        var staleLocalhost = "{\"nodeId\":\"n2\",\"browserId\":\"b2\",\"webSocketEndpoint\":\"ws://localhost:1234/ws/b2\"}";
        var goodItem = "{\"nodeId\":\"n3\",\"browserId\":\"b3\",\"webSocketEndpoint\":\"ws://ok/ws/b3\"}";

        // ListRangeAsync called for both keys
        db.Setup(d => d.ListRangeAsync(It.Is<RedisKey>(k => k.ToString() == listKeyAvail), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { staleThisNode, goodItem });
        db.Setup(d => d.ListRangeAsync(It.Is<RedisKey>(k => k.ToString() == listKeyInuse), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { staleLocalhost });

        // Expect removals for two stale entries
        db.Setup(d => d.ListRemoveAsync(It.Is<RedisKey>(k => k.ToString() == listKeyAvail), It.Is<RedisValue>(v => v.ToString() == staleThisNode), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        db.Setup(d => d.ListRemoveAsync(It.Is<RedisKey>(k => k.ToString() == listKeyInuse), It.Is<RedisValue>(v => v.ToString() == staleLocalhost), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object);
        await pool.CleanupLabelListsAsync("AppA:Chromium:Staging");

        db.VerifyAll();
    }

    [Test]
    public async Task GetAvailableCountAsync_ComposesCorrectKey()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);

        db.Setup(d => d.ListLengthAsync(It.Is<RedisKey>(k => k.ToString() == "available:AppA:Chromium:Staging"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(42);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object);
        var n = await pool.GetAvailableCountAsync("AppA:Chromium:Staging");
        Assert.That(n, Is.EqualTo(42));
        db.VerifyAll();
    }

    [Test]
    public async Task CleanupAllAsync_RemovesNodeKeys_AndCleansLists()
    {
        var options = CreateOptions();
        // ensure PoolConfig has labels to iterate
        options.PoolConfig["LabelX:Chromium:Dev"] = 1;
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);

        // CleanupLabelListsAsync will call ListRangeAsync on both keys; return empty
        db.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // SetRemove and KeyDelete calls
        db.Setup(d => d.SetRemoveAsync(It.Is<RedisKey>(k => k.ToString() == "nodes"), It.Is<RedisValue>(v => v.ToString() == options.NodeId), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString() == $"node:{options.NodeId}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString() == $"node_alive:{options.NodeId}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object);
        await pool.CleanupAllAsync();

        db.Verify(d => d.SetRemoveAsync("nodes", options.NodeId, It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.KeyDeleteAsync($"node:{options.NodeId}", It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.KeyDeleteAsync($"node_alive:{options.NodeId}", It.IsAny<CommandFlags>()), Times.Once);
    }
}
