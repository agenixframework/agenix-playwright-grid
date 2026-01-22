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
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

[TestFixture]
public class WebServerHostTests
{
    private Mock<IMetricsPort> _metricsMock;
    private Mock<IDatabase> _dbMock;
    private Mock<ILogger<WebServerHost>> _innerLoggerMock;
    private ChunkedLogger<WebServerHost> _chunkedLogger;
    private WorkerOptions _options;
    private Mock<ISidecarLauncher> _launcherMock;
    private PoolManager _pool;

    [SetUp]
    public void SetUp()
    {
        _metricsMock = new Mock<IMetricsPort>();
        _dbMock = new Mock<IDatabase>();
        _innerLoggerMock = new Mock<ILogger<WebServerHost>>();
        _chunkedLogger = new ChunkedLogger<WebServerHost>(_innerLoggerMock.Object, new ChunkedLoggerOptions { Enabled = true });

        _options = new WorkerOptions
        {
            NodeId = "test-node",
            NodeSecret = "test-secret",
            NodeNodeSecret = "test-node-secret",
            RedisUrl = "localhost:6379",
            PoolConfig = new ConcurrentDictionary<string, int>(new Dictionary<string, int> { ["App:Chromium:Test"] = 1 })
        };

        _launcherMock = new Mock<ISidecarLauncher>();
        _pool = new PoolManager(
            _options,
            _dbMock.Object,
            _metricsMock.Object,
            _launcherMock.Object,
            new Mock<ILogger<PoolManager>>().Object,
            null,
            null);
    }

    [Test]
    public void Constructor_InitializesCorrectly()
    {
        var host = new WebServerHost(_options, _metricsMock.Object, _pool, _dbMock.Object, null, _chunkedLogger);
        Assert.That(host, Is.Not.Null);
    }

    [Test]
    public async Task RunAsync_LogsStartupMilestones()
    {
        // We use port 0 to avoid binding conflicts
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:0");

        var host = new WebServerHost(_options, _metricsMock.Object, _pool, _dbMock.Object, null, _chunkedLogger);

        using var cts = new CancellationTokenSource();

        // Start RunAsync in a background task
        var runTask = host.RunAsync(Array.Empty<string>(), cts);

        // Give it a moment to start and log
        await Task.Delay(1000);

        // Signal shutdown
        await cts.CancelAsync();

        // Wait for it to complete (it should because we cancelled)
        await Task.WhenAny(runTask, Task.Delay(5000));

        // Verify milestones were logged via inner logger
        // EventCodes are part of the message or state in ChunkedLogger
        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.ServerStarting)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.ConfigurationDumped)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.EndpointsRegistered)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.ServerStarted)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.ListeningAddresses)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task RunAsync_GracefulShutdown_LogsStoppingAndStopped()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:0");
        Environment.SetEnvironmentVariable("WORKER_DRAIN_TIMEOUT_SECONDS", "1");

        var host = new WebServerHost(_options, _metricsMock.Object, _pool, _dbMock.Object, null, _chunkedLogger);

        using var cts = new CancellationTokenSource();
        var runTask = host.RunAsync(Array.Empty<string>(), cts);

        await Task.Delay(1000);
        await cts.CancelAsync();

        // Wait for it to complete
        await Task.WhenAny(runTask, Task.Delay(5000));

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.ServerStopping)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.ServerStopped)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task HealthEndpoint_LogsRequest()
    {
        var port = 5556; // Use a different port to be safe
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://127.0.0.1:{port}");

        var host = new WebServerHost(_options, _metricsMock.Object, _pool, _dbMock.Object, null, _chunkedLogger);

        using var cts = new CancellationTokenSource();
        var runTask = host.RunAsync(Array.Empty<string>(), cts);

        // Wait for it to start
        await Task.Delay(1500);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/health");

        Assert.That(response.IsSuccessStatusCode, Is.True);

        await cts.CancelAsync();
        await Task.WhenAny(runTask, Task.Delay(5000));

        _innerLoggerMock.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(EventCodes.WebServer.RequestReceived) && v.ToString()!.Contains("Health check requested")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }
}
