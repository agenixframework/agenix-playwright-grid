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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Web.Middleware;
using Xunit;

namespace PlaywrightHub.Tests.Infrastructure.Web.Middleware;

public class OperationLoggingMiddlewareTests
{
    private readonly Mock<IEventCodeResolver> _mockEventCodeResolver = new();

    [Fact]
    public async Task InvokeAsync_SuccessfulRequest_LogsInformation()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        var requestDelegateInvoked = false;

        _mockEventCodeResolver.Setup(x => x.ResolveEventCodeFromStatus(It.IsAny<int>(), It.IsAny<HttpContext>()))
            .Returns("GEN00");

        var middleware = new OperationLoggingMiddleware(Next);
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = "GET";
        httpContext.Response.StatusCode = 200;

        // Act
        await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object);

        // Assert
        Assert.True(requestDelegateInvoked);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("GET") && v.ToString()!.Contains("/api/test")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
        return;

        Task Next(HttpContext _)
        {
            requestDelegateInvoked = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task InvokeAsync_FailedRequest_LogsError()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        var expectedException = new InvalidOperationException("Test error");

        _mockEventCodeResolver.Setup(x => x.ResolveEventCode(It.IsAny<Exception>(), It.IsAny<HttpContext>()))
            .Returns("WSH10");

        var middleware = new OperationLoggingMiddleware(Next);
        httpContext.Request.Path = "/api/fail";
        httpContext.Request.Method = "POST";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object));

        // Verify start of operation
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("POST") && v.ToString()!.Contains("/api/fail")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // Verify failure
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("FAILED")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
        return;

        Task Next(HttpContext _) => throw expectedException;
    }

    [Fact]
    public async Task InvokeAsync_SetsHttpTraceIdInOperationContext()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        var expectedTraceId = "test-trace-123";
        httpContext.TraceIdentifier = expectedTraceId;

        string? capturedHttpTraceId = null;
        Task Next(HttpContext _)
        {
            capturedHttpTraceId = OperationContext.Current?.Properties["HttpTraceId"] as string;
            return Task.CompletedTask;
        }

        var middleware = new OperationLoggingMiddleware(Next);

        // Act
        await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object);

        // Assert
        Assert.Equal(expectedTraceId, capturedHttpTraceId);
    }

    [Fact]
    public async Task InvokeAsync_HandlesNullTraceIdentifier()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = null!;

        bool nextInvoked = false;
        Task Next(HttpContext _)
        {
            nextInvoked = true;
            return Task.CompletedTask;
        }

        var middleware = new OperationLoggingMiddleware(Next);

        // Act
        await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object);

        // Assert
        Assert.True(nextInvoked);
    }

    [Fact]
    public void Constructor_NullNext_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OperationLoggingMiddleware(null!));
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var middleware = new OperationLoggingMiddleware(Next);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await middleware.InvokeAsync(null!, chunkedLogger, _mockEventCodeResolver.Object));
        return;
        Task Next(HttpContext _) => Task.CompletedTask;
    }

    [Fact]
    public async Task InvokeAsync_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var middleware = new OperationLoggingMiddleware(Next);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await middleware.InvokeAsync(httpContext, null!, _mockEventCodeResolver.Object));
        return;
        Task Next(HttpContext _) => Task.CompletedTask;
    }

    [Fact]
    public async Task InvokeAsync_WithEventCodeFeature_UsesFeatureValue()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        var expectedEventCode = "TEST01";

        httpContext.Features.Set<IEventCodeFeature>(new EventCodeFeature { EventCode = expectedEventCode });

        var scopeValues = new List<object>();
        mockLogger.Setup(x => x.BeginScope(It.IsAny<It.IsAnyType>()))
            .Callback(new InvocationAction(invocation => scopeValues.Add(invocation.Arguments[0])))
            .Returns(new Mock<IDisposable>().Object);

        var middleware = new OperationLoggingMiddleware(Next);
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = "GET";
        httpContext.Response.StatusCode = 200;

        // Act
        await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object);

        // Assert
        // Verify that the eventCode was passed to the operation outputs
        Assert.Contains(scopeValues, v =>
            v is IEnumerable<KeyValuePair<string, object>> dict &&
            dict.Any(kvp => kvp.Key == "Output_eventCode" && kvp.Value.ToString() == expectedEventCode));
        return;
        Task Next(HttpContext _) => Task.CompletedTask;
    }

    [Fact]
    public async Task InvokeAsync_WithProblemDetailsInBody_ExtractsEventCode()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        var expectedEventCode = "BODY01";

        var body = "{\"eventCode\":\"BODY01\"}";

        // IMPORTANT: Set status code BEFORE creating the buffering stream or before first write
        httpContext.Response.StatusCode = 400;
        httpContext.Response.ContentType = "application/problem+json";

        var originalBody = new MemoryStream();
        var bufferingStream = new AutoFlushingBufferStream(originalBody, httpContext);
        bufferingStream.EnsureInitialized();
        await bufferingStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(body));
        httpContext.Response.Body = bufferingStream;

        var scopeValues = new List<object>();
        mockLogger.Setup(x => x.BeginScope(It.IsAny<It.IsAnyType>()))
            .Callback(new InvocationAction(invocation => scopeValues.Add(invocation.Arguments[0])))
            .Returns(new Mock<IDisposable>().Object);

        var middleware = new OperationLoggingMiddleware(Next);
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = "POST";

        // Act
        await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object);

        // Assert
        Assert.Contains(scopeValues, v =>
            v is IEnumerable<KeyValuePair<string, object>> dict &&
            dict.Any(kvp => kvp.Key == "Output_eventCode" && kvp.Value.ToString() == expectedEventCode));
        return;
        Task Next(HttpContext _) => Task.CompletedTask;
    }

    [Fact]
    public async Task InvokeAsync_ExceptionPath_UsesFeatureValue()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OperationLoggingMiddleware>>();
        var chunkedLogger = new ChunkedLogger<OperationLoggingMiddleware>(mockLogger.Object);
        var httpContext = new DefaultHttpContext();
        var expectedEventCode = "ERR01";
        var exception = new Exception("Test");

        httpContext.Features.Set<IEventCodeFeature>(new EventCodeFeature { EventCode = expectedEventCode });

        var scopeValues = new List<object>();
        mockLogger.Setup(x => x.BeginScope(It.IsAny<It.IsAnyType>()))
            .Callback(new InvocationAction(invocation => scopeValues.Add(invocation.Arguments[0])))
            .Returns(new Mock<IDisposable>().Object);

        var middleware = new OperationLoggingMiddleware(_ => throw exception);
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = "GET";

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
            await middleware.InvokeAsync(httpContext, chunkedLogger, _mockEventCodeResolver.Object));

        Assert.Contains(scopeValues, v =>
            v is IEnumerable<KeyValuePair<string, object>> dict &&
            dict.Any(kvp => kvp.Key == "Output_eventCode" && kvp.Value.ToString() == expectedEventCode));
    }
}
