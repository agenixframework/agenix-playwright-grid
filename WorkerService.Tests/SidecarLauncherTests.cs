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

using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

[TestFixture]
public class SidecarLauncherTests
{
    private WorkerOptions _options;
    private Mock<ILogger<SidecarLauncher>> _loggerMock;
    private ChunkedLogger<SidecarLauncher> _chunkedLogger;

    [SetUp]
    public void SetUp()
    {
        _options = new WorkerOptions
        {
            NodeExe = "bash",
            SidecarScript = "-c",
            SidecarReadyTimeoutSeconds = 5
        };
        _loggerMock = new Mock<ILogger<SidecarLauncher>>();
        _chunkedLogger = new ChunkedLogger<SidecarLauncher>(_loggerMock.Object);
    }

    [Test]
    public async Task StartAsync_HappyPath_ReturnsResult()
    {
        // Arrange
        // bash -c "echo 'JSON' #" chromium
        var options = new WorkerOptions
        {
            NodeExe = "bash",
            SidecarScript = "-c \"echo '{\\\"wsEndpoint\\\":\\\"ws://localhost:1234\\\", \\\"playwrightVersion\\\":\\\"1.40\\\", \\\"browserVersion\\\":\\\"119\\\"}' #\"",
            SidecarReadyTimeoutSeconds = 5
        };
        var launcher = new SidecarLauncher(options, _chunkedLogger);

        // Act
        var result = await launcher.StartAsync("chromium");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ws, Is.EqualTo("ws://localhost:1234"));
            Assert.That(result.playwrightVersion, Is.EqualTo("1.40"));
            Assert.That(result.browserVersion, Is.EqualTo("119"));
            Assert.That(result.browser, Is.EqualTo("chromium"));
            Assert.That(result.proc, Is.Not.Null);
        });

        if (!result.proc.HasExited)
        {
            result.proc.Kill(true);
        }
    }

    [Test]
    public void StartAsync_Timeout_ThrowsTimeoutException()
    {
        // Arrange
        // bash -c "sleep 10 #" chromium
        var options = new WorkerOptions
        {
            NodeExe = "bash",
            SidecarScript = "-c \"sleep 10 #\"",
            SidecarReadyTimeoutSeconds = 1
        };
        var launcher = new SidecarLauncher(options, _chunkedLogger);

        // Act & Assert
        Assert.ThrowsAsync<TimeoutException>(async () => await launcher.StartAsync("chromium"));
    }

    [Test]
    public void StartAsync_InvalidJson_ThrowsTimeoutExceptionAfterStdoutCloses()
    {
        // Arrange
        // bash -c "echo 'not json' #" chromium
        var options = new WorkerOptions
        {
            NodeExe = "bash",
            SidecarScript = "-c \"echo 'not json' #\"",
            SidecarReadyTimeoutSeconds = 5
        };
        var launcher = new SidecarLauncher(options, _chunkedLogger);

        // Act & Assert
        var ex = Assert.ThrowsAsync<TimeoutException>(async () => await launcher.StartAsync("chromium"));
        Assert.That(ex!.Message, Does.Contain("Sidecar exited before providing wsEndpoint"));
    }

    [Test]
    public void StartAsync_ProcessFailsToStart_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new WorkerOptions
        {
            NodeExe = "non-existent-executable-12345",
            SidecarScript = "-c",
            SidecarReadyTimeoutSeconds = 5
        };
        var launcher = new SidecarLauncher(options, _chunkedLogger);

        // Act & Assert
        Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await launcher.StartAsync("chromium"));
    }

    [Test]
    public async Task StartAsync_CapturesStderr()
    {
        // Arrange
        // bash -c "echo '{\"wsEndpoint\":\"ws://...\"}'; echo 'error line' >&2; sleep 0.1 #"
        var options = new WorkerOptions
        {
            NodeExe = "bash",
            SidecarScript = "-c \"echo '{\\\"wsEndpoint\\\":\\\"ws://localhost:1234\\\"}'; echo 'error line' >&2; sleep 0.1 #\"",
            SidecarReadyTimeoutSeconds = 5
        };
        var launcher = new SidecarLauncher(options, _chunkedLogger);

        // Act
        var result = await launcher.StartAsync("chromium");

        // Assert
        Assert.That(result.ws, Is.EqualTo("ws://localhost:1234"));

        // Wait a bit for stderr to be processed
        await Task.Delay(200);

        _loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("error line")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        if (!result.proc.HasExited)
        {
            result.proc.Kill(true);
        }
    }
}
