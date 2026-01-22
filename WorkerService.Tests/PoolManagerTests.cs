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
using System.Diagnostics;
using System.Runtime.InteropServices;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
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
            Labels = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["region"] = "eu" },
            PoolConfig = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
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
        // Start a harmless process to get a valid PID.
        // We use a long-lived process (sleep/timeout) instead of 'dotnet --version'
        // to avoid race conditions where the process exits before test assertions.
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd" : "sleep",
                Arguments = isWindows ? "/c timeout /t 60" : "60",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        proc.Start();
        return proc;
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
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));

        // Redis pushes per created slot
        db.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        // After warm, ListLengthAsync is called once to update metric (and also by debug logs)
        db.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);
        // Debug logs call KeyExistsAsync
        db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Metrics expectations
        metrics.Setup(m => m.SetPoolCapacity(options.NodeId, "AppA:Chromium:Staging", It.Is<int>(c => c == 2)));
        metrics.Setup(m => m.SetPoolAvailable(options.NodeId, "AppA:Chromium:Staging", 2));

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);

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

        // Cleanup: Kill the dummy processes created during the test
        pool.KillAll();
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

        var staleThisNode =
            $"{{\"nodeId\":\"{options.NodeId}\",\"browserId\":\"b1\",\"webSocketEndpoint\":\"ws://public/ws/b1\"}}";
        var staleLocalhost =
            "{\"nodeId\":\"n2\",\"browserId\":\"b2\",\"webSocketEndpoint\":\"ws://localhost:1234/ws/b2\"}";
        var goodItem = "{\"nodeId\":\"n3\",\"browserId\":\"b3\",\"webSocketEndpoint\":\"ws://ok/ws/b3\"}";

        // ListRangeAsync called for both keys
        db.Setup(d => d.ListRangeAsync(It.Is<RedisKey>(k => k.ToString() == listKeyAvail), It.IsAny<long>(),
                It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { staleThisNode, goodItem });
        db.Setup(d => d.ListRangeAsync(It.Is<RedisKey>(k => k.ToString() == listKeyInuse), It.IsAny<long>(),
                It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { staleLocalhost });

        // Expect removals for two stale entries
        db.Setup(d => d.ListRemoveAsync(It.Is<RedisKey>(k => k.ToString() == listKeyAvail),
                It.Is<RedisValue>(v => v.ToString() == staleThisNode), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        db.Setup(d => d.ListRemoveAsync(It.Is<RedisKey>(k => k.ToString() == listKeyInuse),
                It.Is<RedisValue>(v => v.ToString() == staleLocalhost), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);
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

        db.Setup(d => d.ListLengthAsync(It.Is<RedisKey>(k => k.ToString() == "available:AppA:Chromium:Staging"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(42);

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);
        var n = await pool.GetAvailableCountAsync("AppA:Chromium:Staging");
        Assert.That(n, Is.EqualTo(42));
        db.VerifyAll();
    }

    [Test]
    public async Task InitializeAsync_StartsOperation_AndLogsMilestones()
    {
        var options = CreateOptions();
        options.PoolConfig.Clear();
        options.PoolConfig["AppA:Chromium:Staging"] = 1;
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);

        var chunkedLogger = new ChunkedLogger<PoolManager>(logger.Object);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object,
            chunkedLogger: chunkedLogger);

        // Setup sidecar to return a dummy process
        using var dummyProc = CreateDummyProcess();
        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SidecarStartResult(dummyProc, "ws://localhost:1234", "1.0.0", "120.0", "chromium"));

        await pool.InitializeAsync();

        // Verify that BeginScope was called (BeginOperation uses BeginScope internally)
        logger.Verify(
            l => l.BeginScope(It.Is<Dictionary<string, object?>>(d =>
                d.ContainsKey("operation") && d["operation"]!.ToString() == "PoolInitialize")), Times.AtLeastOnce);

        // Verify milestones were logged (WRK07 - warming started, WRK08 - completed)
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK07]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK08]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        pool.KillAll();
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
        db.Setup(d =>
                d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync([]);

        // SetRemove and KeyDelete calls
        db.Setup(d => d.SetRemoveAsync(It.Is<RedisKey>(k => k.ToString() == "nodes"),
                It.Is<RedisValue>(v => v.ToString() == options.NodeId), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d =>
                d.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString() == $"node:{options.NodeId}"),
                    It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString() == $"node_alive:{options.NodeId}"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);
        await pool.CleanupAllAsync();

        db.Verify(d => d.SetRemoveAsync("nodes", options.NodeId, It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.KeyDeleteAsync($"node:{options.NodeId}", It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.KeyDeleteAsync($"node_alive:{options.NodeId}", It.IsAny<CommandFlags>()), Times.Once);
    }

    [Test]
    public async Task WarmLabelAsync_PrunesExtraBrowsers_WhenTargetCountDecreased()
    {
        // Arrange
        var options = CreateOptions();
        options.PoolConfig["AppA:Chromium:Staging"] = 2;
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);

        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);

        // Warm up to 5 browsers first
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 5);
        Assert.That(pool.Pools["AppA:Chromium:Staging"].Count, Is.EqualTo(5));

        // Act: Warm up to 2 browsers
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 2);

        // Assert
        Assert.That(pool.Pools["AppA:Chromium:Staging"].Count, Is.EqualTo(2), "Should have pruned to 2 browsers");

        pool.KillAll();
    }

    [Test]
    public async Task OnSidecarExited_DoesNotReplace_IfTargetCapacityReached()
    {
        // Arrange
        var options = CreateOptions();
        options.PoolConfig["AppA:Chromium:Staging"] = 1;
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);

        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);

        // Start with 2 browsers (exceeding target 1)
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 2);
        Assert.That(pool.Pools["AppA:Chromium:Staging"].Count, Is.EqualTo(2));

        var browserIds = pool.Pools["AppA:Chromium:Staging"].Keys.ToList();
        var idToExit = browserIds[0];

        // Act: Simulate sidecar exit
        await pool.OnSidecarExited("AppA:Chromium:Staging", idToExit, "chromium");

        // Assert: Should NOT have replaced because we still have 1 browser left, which is our target
        Assert.That(pool.Pools["AppA:Chromium:Staging"].Count, Is.EqualTo(1));

        pool.KillAll();
    }

    [Test]
    public async Task WarmLabelAsync_RepopulatesRedisCorrectly_WithInUseBrowsers()
    {
        // Arrange
        var options = CreateOptions();
        options.PoolConfig["AppA:Chromium:Staging"] = 2;
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);

        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));

        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object, pidRedisManager: null);

        // Warm up to 2 browsers
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 2);
        var browserIds = pool.Pools["AppA:Chromium:Staging"].Keys.ToList();

        // Mark one as InUse
        pool.MarkConnectionStart(browserIds[0]);

        // Act: Re-warm (simulating re-registration)
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 2);

        // Assert: One should be pushed to available:*, another to inuse:*
        db.Verify(d => d.ListRightPushAsync(
            It.Is<RedisKey>(k => k.ToString() == "available:AppA:Chromium:Staging"),
            It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Exactly(3)); // 2 from first warm + 1 from second warm

        db.Verify(d => d.ListRightPushAsync(
            It.Is<RedisKey>(k => k.ToString() == "inuse:AppA:Chromium:Staging"),
            It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Exactly(1)); // 1 from second warm

        pool.KillAll();
    }

    [Test]
    public void MarkConnectionStart_LogsMilestone_AndUpdatesCount()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<PoolManager>(logger.Object);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object,
            chunkedLogger: chunkedLogger);

        pool.MarkConnectionStart("browser-1");

        Assert.That(pool.HasActiveConnection("browser-1"), Is.True);

        // Verify WRK16 milestone
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK16]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Test]
    public void MarkConnectionEnd_LogsMilestone_AndRemovesActiveConnection()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<PoolManager>(logger.Object);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object,
            chunkedLogger: chunkedLogger);

        pool.MarkConnectionStart("browser-1");
        pool.MarkConnectionEnd("browser-1");

        Assert.That(pool.HasActiveConnection("browser-1"), Is.False);

        // Verify WRK17 milestone
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK17]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Test]
    public async Task ReconcileLoopAsync_PrunesExtraBrowsers_AndLogsMilestones()
    {
        var options = CreateOptions();
        options.PoolConfig["AppA:Chromium:Staging"] = 1;
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<PoolManager>(logger.Object);

        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object,
            chunkedLogger: chunkedLogger);

        // Warm to 3 browsers first
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 3);
        Assert.That(pool.Pools["AppA:Chromium:Staging"].Count, Is.EqualTo(3));

        // Start reconcile loop in background
        using var cts = new CancellationTokenSource();
        var loopTask = pool.ReconcileLoopAsync(cts.Token);

        // Poll for expected state (max 5 seconds)
        var maxWait = TimeSpan.FromSeconds(5);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < maxWait && pool.Pools["AppA:Chromium:Staging"].Count != 1)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await loopTask; } catch (OperationCanceledException) { }

        // Should have pruned to 1 browser
        Assert.That(pool.Pools["AppA:Chromium:Staging"].Count, Is.EqualTo(1),
            $"Expected pool to be pruned to 1 browser, but took {sw.ElapsedMilliseconds}ms");

        // Verify WRK18 (PoolResizeStarted), WRK19 (PoolResizeCompleted), and WRK28 (BrowserPruned)
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK18]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK19]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[WRK28]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeast(2));

        pool.KillAll();
    }

    [Test]
    public async Task OnSidecarExited_FailsOperation_WhenExceptionOccurs()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<PoolManager>(logger.Object);

        // Make db.ListRangeAsync throw to trigger catch block
        db.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("Redis failure"));

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object,
            chunkedLogger: chunkedLogger);

        // Need at least one slot to trigger the exit logic
        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 1);

        var browserId = pool.Pools["AppA:Chromium:Staging"].Keys.First();

        await pool.OnSidecarExited("AppA:Chromium:Staging", browserId, "chromium");

        // Verify that operation was failed
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: FAILED") && v.ToString()!.Contains("Unexpected")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        pool.KillAll();
    }

    [Test]
    public async Task ReconcileLoopAsync_FailsOperation_WhenExceptionOccursDuringReplace()
    {
        var options = CreateOptions();
        options.PoolConfig["AppA:Chromium:Staging"] = 1;
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<PoolManager>(logger.Object);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object,
            chunkedLogger: chunkedLogger);

        // Warm to 1 browser
        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                new SidecarStartResult(CreateDummyProcess(), "ws://internal:1234/abc", null, null, "chromium"));
        await pool.WarmLabelAsync("AppA:Chromium:Staging", 1);

        var browserId = pool.Pools["AppA:Chromium:Staging"].Keys.First();

        // Trigger via recycle key instead of process exit to avoid race with exit handler
        db.Setup(d => d.KeyExistsAsync(It.Is<RedisKey>(k => k.ToString().Contains("recycle:")), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Make StartPwServerAsync throw during reconciliation
        sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Start failure"));

        // Start reconcile loop in background
        using var cts = new CancellationTokenSource();
        var loopTask = pool.ReconcileLoopAsync(cts.Token);

        // Give it some time to process
        await Task.Delay(1000);
        cts.Cancel();
        try { await loopTask; } catch (OperationCanceledException) { }

        // Verify that ReconcileReplace operation was failed
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("End: FAILED") && v.ToString()!.Contains("Unexpected")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        pool.KillAll();
    }

    [Test]
    public async Task MarkConnectionStart_HandlesConcurrentCalls()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var metrics = new Mock<IMetricsPort>(MockBehavior.Loose);
        var sidecar = new Mock<ISidecarLauncher>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PoolManager>>(MockBehavior.Loose);

        var pool = new PoolManager(options, db.Object, metrics.Object, sidecar.Object, logger.Object);

        var browserId = "browser-1";
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => pool.MarkConnectionStart(browserId)));
        }

        await Task.WhenAll(tasks);

        Assert.That(pool.HasActiveConnection(browserId), Is.True);

        // Internal state check (if I could access it, but HasActiveConnection is enough)
        // I'll check MarkConnectionEnd too
        tasks.Clear();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => pool.MarkConnectionEnd(browserId)));
        }
        await Task.WhenAll(tasks);
        Assert.That(pool.HasActiveConnection(browserId), Is.False);
    }
}
