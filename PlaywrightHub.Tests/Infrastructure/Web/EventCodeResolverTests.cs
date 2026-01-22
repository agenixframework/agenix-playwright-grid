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
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using PlaywrightHub.Infrastructure.Web;
using StackExchange.Redis;

namespace PlaywrightHub.Tests.Infrastructure.Web;

public class EventCodeResolverTests
{
    private readonly Mock<ILogger<EventCodeResolver>> _loggerMock;
    private readonly EventCodeResolver _resolver;
    private readonly Mock<HttpContext> _httpContextMock;
    private readonly Mock<HttpRequest> _httpRequestMock;

    public EventCodeResolverTests()
    {
        _loggerMock = new Mock<ILogger<EventCodeResolver>>();
        _resolver = new EventCodeResolver(_loggerMock.Object);
        _httpContextMock = new Mock<HttpContext>();
        _httpRequestMock = new Mock<HttpRequest>();
        _httpContextMock.Setup(c => c.Request).Returns(_httpRequestMock.Object);
    }

    [Theory]
    [InlineData("23505", EventCodes.Database.TransactionFailed)]
    [InlineData("23503", EventCodes.Database.TransactionFailed)]
    [InlineData("40001", EventCodes.Database.TransactionFailed)]
    [InlineData("XXXXX", EventCodes.Database.OperationFailed)]
    public void ResolveEventCode_NpgsqlException_ReturnsCorrectEventCode(string sqlState, string expectedEventCode)
    {
        // Arrange
        // NpgsqlException is abstract in recent versions, but we can mock it or use PostgresException
        var exception = new PostgresException("Test message", "ERROR", "ERROR", sqlState);

        // Act
        var result = _resolver.ResolveEventCode(exception, _httpContextMock.Object);

        // Assert
        result.Should().Be(expectedEventCode);
        VerifyDebugLogWasCalled($"Resolved PostgresException to event code {expectedEventCode}");
    }

    [Fact]
    public void ResolveEventCode_RedisException_ReturnsRedisOperationFailed()
    {
        // Arrange
        var exception = new RedisException("Test redis error");

        // Act
        var result = _resolver.ResolveEventCode(exception, _httpContextMock.Object);

        // Assert
        result.Should().Be(EventCodes.Redis.OperationFailed);
        VerifyDebugLogWasCalled($"Resolved RedisException to event code {EventCodes.Redis.OperationFailed}");
    }

    [Theory]
    [InlineData("/api/launches", EventCodes.Launch.LaunchOperationFailed)]
    [InlineData("/api/LAUNCHES/123", EventCodes.Launch.LaunchOperationFailed)]
    [InlineData("/api/test-items", EventCodes.TestItem.TestItemOperationFailed)]
    [InlineData("/api/TEST-ITEMS/456", EventCodes.TestItem.TestItemOperationFailed)]
    [InlineData("/api/other", EventCodes.WebServer.RequestFailed)]
    public void ResolveEventCode_TimeoutException_ReturnsContextAwareEventCode(string path, string expectedEventCode)
    {
        // Arrange
        var exception = new TimeoutException("Test timeout");
        _httpRequestMock.Setup(r => r.Path).Returns(new PathString(path));

        // Act
        var result = _resolver.ResolveEventCode(exception, _httpContextMock.Object);

        // Assert
        result.Should().Be(expectedEventCode);
        VerifyDebugLogWasCalled($"Resolved TimeoutException to event code {expectedEventCode}");
    }

    [Fact]
    public void ResolveEventCode_InvalidOperationException_ReturnsRequestFailed()
    {
        // Arrange
        var exception = new InvalidOperationException("Test invalid operation");

        // Act
        var result = _resolver.ResolveEventCode(exception, _httpContextMock.Object);

        // Assert
        result.Should().Be(EventCodes.WebServer.RequestFailed);
        VerifyDebugLogWasCalled($"Resolved InvalidOperationException to event code {EventCodes.WebServer.RequestFailed}");
    }

    [Fact]
    public void ResolveEventCode_GenericException_ReturnsRequestFailed()
    {
        // Arrange
        var exception = new Exception("Generic error");

        // Act
        var result = _resolver.ResolveEventCode(exception, _httpContextMock.Object);

        // Assert
        result.Should().Be(EventCodes.WebServer.RequestFailed);
        VerifyDebugLogWasCalled($"Resolved Exception to event code {EventCodes.WebServer.RequestFailed}");
    }

    [Theory]
    [InlineData(400, "ADM91")] // EventCodes.AdminProjectsUsers.Validation.ValidationFailed
    [InlineData(401, "ADM03")] // EventCodes.AdminProjectsUsers.Authentication.LoginFailed
    [InlineData(500, "WSH10")] // EventCodes.WebServer.RequestFailed
    [InlineData(503, "WSH10")] // EventCodes.WebServer.RequestFailed
    [InlineData(404, "WSH10")] // Default fallback
    public void ResolveEventCodeFromStatus_ReturnsCorrectEventCode(int statusCode, string expectedEventCode)
    {
        // Act
        var result = _resolver.ResolveEventCodeFromStatus(statusCode, _httpContextMock.Object);

        // Assert
        result.Should().Be(expectedEventCode);
        VerifyDebugLogWasCalled($"Resolved status code {statusCode} to event code {expectedEventCode}");
    }

    private void VerifyDebugLogWasCalled(string expectedMessage)
    {
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
