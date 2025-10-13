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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure.Adapters;

namespace WorkerService.Tests;

[TestFixture]
public class HubHttpClientTests
{
    private Mock<ILogger<HubHttpClient>> _loggerMock;
    private ChunkedLogger<HubHttpClient> _chunkedLogger;
    private Mock<IHttpClientFactory> _httpClientFactoryMock;
    private Mock<HttpMessageHandler> _handlerMock;
    private HubHttpClient _client;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<HubHttpClient>>();
        _chunkedLogger = new ChunkedLogger<HubHttpClient>(_loggerMock.Object);
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        var httpClient = new HttpClient(_handlerMock.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient("HubClient")).Returns(httpClient);

        _client = new HubHttpClient(_chunkedLogger, _httpClientFactoryMock.Object);
    }

    [Test]
    public async Task RegisterAsync_SuccessfulRequest_ReturnsTrue()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("OK")
            });

        // Act
        var result = await _client.RegisterAsync(
            "http://hub", "secret", "node1", "http://node",
            new[] { "app1" }, 1, new Dictionary<string, string>());

        // Assert
        Assert.That(result, Is.True);
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri.ToString() == "http://hub/node/register" &&
                req.Headers.Contains("x-hub-secret")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task RegisterAsync_FailureThenSuccess_RetriesAndReturnsTrue()
    {
        // Arrange
        _handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError })
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("OK") });

        // Act
        var result = await _client.RegisterAsync(
            "http://hub", "secret", "node1", "http://node",
            new[] { "app1" }, 1, new Dictionary<string, string>());

        // Assert
        Assert.That(result, Is.True);
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task RegisterAsync_AllAttemptsFail_ReturnsFalse()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError });

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately to avoid long retry loop

        // Act
        var result = await _client.RegisterAsync(
            "http://hub", "secret", "node1", "http://node",
            new[] { "app1" }, 1, new Dictionary<string, string>(), ct: cts.Token);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RegisterAsync_CancellationDuringBackoff_ReturnsFalse()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var result = await _client.RegisterAsync(
            "http://hub", "secret", "node1", "http://node",
            new[] { "app1" }, 1, new Dictionary<string, string>(), ct: cts.Token);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RegisterAsync_DoesNotLogSecret()
    {
        // Arrange
        var secret = "super-secret-token";
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("OK") });

        // Act
        await _client.RegisterAsync(
            "http://hub", secret, "node1", "http://node",
            new[] { "app1" }, 1, new Dictionary<string, string>());

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(secret)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never,
            "The secret should never be logged.");
    }

    [Test]
    public async Task GetDiagnosticsAsync_SerializationError_ReturnsNull()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("invalid-json")
            });

        // Act
        var result = await _client.GetDiagnosticsAsync("http://hub");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetDiagnosticsAsync_SuccessfulRequest_ReturnsDto()
    {
        // Arrange
        var diagnostics = new HubDiagnosticsDto
        {
            Workers = new List<WorkerDto> { new WorkerDto { Id = "node1" } }
        };
        var json = JsonSerializer.Serialize(diagnostics);

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        // Act
        var result = await _client.GetDiagnosticsAsync("http://hub");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Workers, Has.Count.EqualTo(1));
        Assert.That(result.Workers[0].Id, Is.EqualTo("node1"));
    }

    [Test]
    public async Task GetDiagnosticsAsync_Non2xxResponse_ReturnsNull()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        // Act
        var result = await _client.GetDiagnosticsAsync("http://hub");

        // Assert
        Assert.That(result, Is.Null);
    }
}
