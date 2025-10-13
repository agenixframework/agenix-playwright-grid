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
using WorkerService.Infrastructure.Adapters;
using WorkerService.Services;

namespace WorkerService.Tests;

[TestFixture]
public class BrowserHealthCheckerTests
{
    private Mock<IDatabase> _db;
    private Mock<IMetricsPort> _metrics;
    private Mock<ISidecarLauncher> _sidecar;
    private Mock<IPlaywrightProtocolClientFactory> _clientFactory;
    private Mock<IPlaywrightProtocolClient> _client;
    private Mock<ILogger<BrowserHealthChecker>> _logger;
    private Mock<ILogger<PoolManager>> _poolLogger;
    private ChunkedLogger<BrowserHealthChecker> _chunkedLogger;
    private WorkerOptions _options;
    private PoolManager _pool;
    private BrowserHealthChecker _checker;

    [SetUp]
    public void SetUp()
    {
        _db = new Mock<IDatabase>();
        _metrics = new Mock<IMetricsPort>();
        _sidecar = new Mock<ISidecarLauncher>();
        _clientFactory = new Mock<IPlaywrightProtocolClientFactory>();
        _client = new Mock<IPlaywrightProtocolClient>();
        _clientFactory.Setup(f => f.CreateClient()).Returns(_client.Object);
        _logger = new Mock<ILogger<BrowserHealthChecker>>();
        _poolLogger = new Mock<ILogger<PoolManager>>();
        _chunkedLogger = new ChunkedLogger<BrowserHealthChecker>(_logger.Object);

        _options = new WorkerOptions
        {
            NodeId = "test-node",
            Labels = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            PoolConfig = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["App:Chromium:Test"] = 0
            }
        };

        _pool = new PoolManager(_options, _db.Object, _metrics.Object, _sidecar.Object, _poolLogger.Object, null, null);
        _checker = new BrowserHealthChecker(_pool, _db.Object, _options, _metrics.Object, _chunkedLogger, _clientFactory.Object);

        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_ENABLED", "true");
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS", "10");
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS", "1");
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD", "2");
    }

    [TearDown]
    public void TearDown()
    {
        _pool.KillAll();
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_ENABLED", null);
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS", null);
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS", null);
        Environment.SetEnvironmentVariable("AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD", null);
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
    public async Task CheckAllBrowsersAsync_Success_ResetsFailureCount()
    {
        // Arrange
        using var proc = CreateDummyProcess();
        _sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SidecarStartResult(proc, "ws://internal", "1.0", "120", "chromium"));

        // Redis setup for WarmLabelAsync
        _db.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        _db.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        _db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await _pool.WarmLabelAsync("App:Chromium:Test", 1);
        Assert.That(_pool.TryGetFirstSlot("App:Chromium:Test", out var browserId, out var slot), Is.True);

        _client.Setup(c => c.SendCommandAsync("Browser.version", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"version\":\"1.0\"}");

        // Act
        var method = typeof(BrowserHealthChecker).GetMethod("CheckAllBrowsersAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method.Invoke(_checker, new object[] { TimeSpan.FromSeconds(1), 2, CancellationToken.None });

        // Assert
        _client.Verify(c => c.ConnectAsync("ws://internal", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        _metrics.Verify(m => m.RecordBrowserHealthCheck(_options.NodeId, "App:Chromium:Test", It.Is<string>(s => s.Equals("chromium", StringComparison.OrdinalIgnoreCase)), true), Times.Once);

        // Success should log BHC11
        _logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("[BHC11]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Test]
    public async Task CheckAllBrowsersAsync_Failure_IncrementsFailureCountAndTriggersRecycle()
    {
        // Arrange
        using var proc = CreateDummyProcess();
        _sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SidecarStartResult(proc, "ws://internal", "1.0", "120", "chromium"));

        // Redis setup for WarmLabelAsync
        _db.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        _db.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        _db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await _pool.WarmLabelAsync("App:Chromium:Test", 1);
        Assert.That(_pool.TryGetFirstSlot("App:Chromium:Test", out var browserId, out var slot), Is.True);

        _client.Setup(c => c.SendCommandAsync("Browser.version", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null); // Failure

        _db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var method = typeof(BrowserHealthChecker).GetMethod("CheckAllBrowsersAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act - 1st failure
        await (Task)method.Invoke(_checker, new object[] { TimeSpan.FromSeconds(1), 2, CancellationToken.None });

        // Assert 1st failure
        _metrics.Verify(m => m.RecordBrowserHealthCheck(_options.NodeId, "App:Chromium:Test", It.Is<string>(s => s.Equals("chromium", StringComparison.OrdinalIgnoreCase)), false), Times.Once);
        _db.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);

        // Act - 2nd failure (triggers recycle)
        await (Task)method.Invoke(_checker, new object[] { TimeSpan.FromSeconds(1), 2, CancellationToken.None });

        // Assert 2nd failure
        _metrics.Verify(m => m.RecordBrowserHealthCheck(_options.NodeId, "App:Chromium:Test", It.Is<string>(s => s.Equals("chromium", StringComparison.OrdinalIgnoreCase)), false), Times.Exactly(2));
        _db.Verify(d => d.StringSetAsync(It.Is<RedisKey>(k => k.ToString() == $"recycle:{browserId}"), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);

        // Recycle should log BHC20
        _logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("[BHC20]")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Test]
    public async Task CheckAllBrowsersAsync_SkipsActiveConnections()
    {
        // Arrange
        using var proc = CreateDummyProcess();
        _sidecar.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SidecarStartResult(proc, "ws://internal", "1.0", "120", "chromium"));

        // Redis setup for WarmLabelAsync
        _db.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        _db.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);
        _db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await _pool.WarmLabelAsync("App:Chromium:Test", 1);
        Assert.That(_pool.TryGetFirstSlot("App:Chromium:Test", out var browserId, out var slot), Is.True);

        _pool.MarkConnectionStart(browserId); // Active connection

        var method = typeof(BrowserHealthChecker).GetMethod("CheckAllBrowsersAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        await (Task)method.Invoke(_checker, new object[] { TimeSpan.FromSeconds(1), 2, CancellationToken.None });

        // Assert
        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
