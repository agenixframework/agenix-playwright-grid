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

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dashboard.Models;
using Dashboard.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace Dashboard.Tests.Services;

[TestFixture]
public class HttpErrorHandlerTests
{
    private ILogger<HttpErrorHandler> _mockLogger = null!;
    private HttpErrorHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<HttpErrorHandler>>();
        _handler = new HttpErrorHandler(_mockLogger);
    }

    #region Classification Tests

    [Test]
    public async Task HandleExceptionAsync_HttpRequestException_NoStatusCode_ReturnsNetwork()
    {
        // Arrange
        var exception = new HttpRequestException("Network fail");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");

        // Act
        var error = await _handler.HandleExceptionAsync(exception, request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.Network));
            Assert.That(error.IsRetryable, Is.True);
            Assert.That(error.Title, Is.Not.Empty);
            Assert.That(error.Message, Is.Not.Empty);
            Assert.That(error.HttpMethod, Is.EqualTo("GET"));
            Assert.That(error.Endpoint, Is.EqualTo("https://api.example.com/data"));
        });

        _mockLogger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Test]
    public async Task HandleExceptionAsync_Http500_ReturnsServer()
    {
        // Arrange
        var exception = new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError);

        // Act
        var error = await _handler.HandleExceptionAsync(exception, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.Server));
            Assert.That(error.IsRetryable, Is.True);
            Assert.That(error.StatusCode, Is.EqualTo(500));
        });

        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Test]
    public async Task HandleExceptionAsync_Http404_ReturnsClient()
    {
        // Arrange
        var exception = new HttpRequestException("Not found", null, HttpStatusCode.NotFound);

        // Act
        var error = await _handler.HandleExceptionAsync(exception, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.Client));
            Assert.That(error.IsRetryable, Is.False);
            Assert.That(error.StatusCode, Is.EqualTo(404));
        });

        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Test]
    public async Task HandleExceptionAsync_Http429_ReturnsRateLimit()
    {
        // Arrange
        var exception = new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests);

        // Act
        var error = await _handler.HandleExceptionAsync(exception, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.RateLimit));
            Assert.That(error.IsRetryable, Is.True);
            Assert.That(error.StatusCode, Is.EqualTo(429));
        });

        _mockLogger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Test]
    public async Task HandleExceptionAsync_JsonException_ReturnsValidation()
    {
        // Arrange
        var exception = new JsonException("Invalid JSON");

        // Act
        var error = await _handler.HandleExceptionAsync(exception, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.Validation));
            Assert.That(error.IsRetryable, Is.False);
        });

        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Test]
    public async Task HandleExceptionAsync_UnknownException_ReturnsUnknown()
    {
        // Arrange
        var exception = new Exception("Something weird");

        // Act
        var error = await _handler.HandleExceptionAsync(exception, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.Unknown));
            Assert.That(error.IsRetryable, Is.False);
        });

        _mockLogger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    #endregion

    #region ProblemDetails Parsing Tests

    [Test]
    public async Task HandleExceptionAsync_ProblemDetailsJson_ParsesCorrectly()
    {
        // Arrange
        // Note: RFC 7807 extensions are at the root level of the JSON object
        var json = @"{
          ""type"": ""https://example/errors/db"",
          ""title"": ""Database error"",
          ""status"": 500,
          ""detail"": ""Timeout while querying"",
          ""traceId"": ""0HMWC4...:00000001"",
          ""eventCode"": ""DB04""
        }";
        // We also set the StatusCode on the exception so Classify() returns Server
        var exception = new HttpRequestException($"Error: {json}", null, HttpStatusCode.InternalServerError);

        // Act
        var error = await _handler.HandleExceptionAsync(exception, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error.StatusCode, Is.EqualTo(500));
            Assert.That(error.Title, Is.EqualTo("Database error"));
            Assert.That(error.Details, Is.EqualTo("Timeout while querying"));
            Assert.That(error.RequestId, Is.EqualTo("0HMWC4...:00000001"));
            Assert.That(error.EventCode, Is.EqualTo("DB04"));
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.Server));
        });

        _mockLogger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            exception,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    #endregion

    #region IsRetryableAsync Tests

    [TestCaseSource(nameof(GetRetryScenarios))]
    public async Task IsRetryableAsync_VariousExceptions_ReturnsExpectedValue(Exception ex, bool expected)
    {
        // Act
        var result = await _handler.IsRetryableAsync(ex);

        // Assert
        Assert.That(result, Is.EqualTo(expected), $"Exception {ex.GetType().Name} should have retryable={expected}");
    }

    private static object[] GetRetryScenarios()
    {
        return new object[]
        {
            new object[] { new HttpRequestException("Net"), true }, // Network
            new object[] { new HttpRequestException("500", null, HttpStatusCode.InternalServerError), true }, // Server
            new object[] { new HttpRequestException("429", null, HttpStatusCode.TooManyRequests), true }, // RateLimit
            new object[] { new HttpRequestException("400", null, HttpStatusCode.BadRequest), false }, // Client
            new object[] { new JsonException("JSON"), false }, // Validation
            new object[] { new Exception("Other"), false } // Unknown
        };
    }

    #endregion

    #region ShowErrorAsync Tests

    [Test]
    public async Task ShowErrorAsync_InvokesOnErrorRaisedEvent()
    {
        // Arrange
        var error = new ErrorDetails
        {
            Title = "Test",
            Message = "Test Message",
            Category = ErrorCategory.Server,
            RequestId = "REQ123",
            EventCode = "CODE456"
        };

        ErrorDetails? capturedError = null;
        _handler.OnErrorRaised += (e) =>
        {
            capturedError = e;
            return Task.CompletedTask;
        };

        // Act
        await _handler.ShowErrorAsync(error);

        // Assert
        Assert.That(capturedError, Is.Not.Null);
        Assert.That(capturedError, Is.SameAs(error));

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            (Exception?)null,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Test]
    public async Task ShowErrorAsync_NoSubscribers_LogsWarning()
    {
        // Arrange
        var error = new ErrorDetails
        {
            Title = "Test",
            Message = "Test Message",
            Category = ErrorCategory.Server
        };

        // Act
        await _handler.ShowErrorAsync(error);

        // Assert
        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            (Exception?)null,
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    #endregion
}
