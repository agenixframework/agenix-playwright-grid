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

using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

[TestFixture]
public class WorkerRegistrationVerifierTests
{
    private Mock<WorkerServiceRunner> _runnerMock;
    private Mock<IHubClient> _hubClientMock;
    private Mock<ILogger<WorkerRegistrationVerifier>> _loggerMock;
    private WorkerOptions _options;

    [SetUp]
    public void SetUp()
    {
        _runnerMock = new Mock<WorkerServiceRunner>((IServiceProvider)null);
        _hubClientMock = new Mock<IHubClient>();
        _loggerMock = new Mock<ILogger<WorkerRegistrationVerifier>>();
        _options = new WorkerOptions
        {
            NodeId = "test-node",
            HubUrl = "http://hub:5000",
            RegistrationVerificationIntervalSeconds = 1
        };
    }

    [Test]
    public async Task VerifyRegistrationAsync_WorkerFound_DoesNotTriggerReRegistration()
    {
        // Arrange
        var diagnostics = new HubDiagnosticsDto
        {
            Workers = new List<WorkerDto> { new WorkerDto { Id = "test-node" } }
        };
        _hubClientMock.Setup(h => h.GetDiagnosticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(diagnostics);

        var verifier = new WorkerRegistrationVerifier(
            _runnerMock.Object,
            _options,
            _hubClientMock.Object);

        // Act
        var method = typeof(WorkerRegistrationVerifier).GetMethod("VerifyRegistrationAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method.Invoke(verifier, new object[] { CancellationToken.None });

        // Assert
        _runnerMock.Verify(r => r.EnsureRegisteredAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyRegistrationAsync_WorkerMissing_TriggersReRegistration()
    {
        // Arrange
        var diagnostics = new HubDiagnosticsDto
        {
            Workers = new List<WorkerDto> { new WorkerDto { Id = "other-node" } }
        };
        _hubClientMock.Setup(h => h.GetDiagnosticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(diagnostics);

        _runnerMock.Setup(r => r.EnsureRegisteredAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var verifier = new WorkerRegistrationVerifier(
            _runnerMock.Object,
            _options,
            _hubClientMock.Object);

        // Act
        var method = typeof(WorkerRegistrationVerifier).GetMethod("VerifyRegistrationAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method.Invoke(verifier, new object[] { CancellationToken.None });

        // Assert
        _runnerMock.Verify(r => r.EnsureRegisteredAsync("periodic_verification"), Times.Once);
    }

    [Test]
    public async Task VerifyRegistrationAsync_HubUnreachable_DoesNotTriggerReRegistration()
    {
        // Arrange
        _hubClientMock.Setup(h => h.GetDiagnosticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HubDiagnosticsDto)null);

        var verifier = new WorkerRegistrationVerifier(
            _runnerMock.Object,
            _options,
            _hubClientMock.Object);

        // Act
        var method = typeof(WorkerRegistrationVerifier).GetMethod("VerifyRegistrationAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method.Invoke(verifier, new object[] { CancellationToken.None });

        // Assert
        _runnerMock.Verify(r => r.EnsureRegisteredAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyRegistrationAsync_HubThrows_DoesNotPropagate()
    {
        // Arrange
        _hubClientMock.Setup(h => h.GetDiagnosticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("network error"));

        var verifier = new WorkerRegistrationVerifier(
            _runnerMock.Object,
            _options,
            _hubClientMock.Object);

        // Act
        var method = typeof(WorkerRegistrationVerifier).GetMethod("VerifyRegistrationAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Should not throw as it's caught inside VerifyRegistrationAsync
        await (Task)method.Invoke(verifier, new object[] { CancellationToken.None });

        // Assert
        _runnerMock.Verify(r => r.EnsureRegisteredAsync(It.IsAny<string>()), Times.Never);
    }
}
