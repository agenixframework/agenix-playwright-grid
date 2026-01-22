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
using Dashboard.Application;
using Dashboard.Components;
using Dashboard.Models;
using Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace Dashboard.Tests.Components;

[TestFixture]
public class DashboardComponentBaseTests
{
    private IHttpClientFactory _mockHttpClientFactory = null!;
    private IHttpErrorHandler _mockErrorHandler = null!;
    private IRequestDeduplicationService _mockDeduplicator = null!;
    private ILogger<DashboardComponentBase> _mockLogger = null!;
    private TestComponent _component = null!;
    private TestMessageHandler _testMessageHandler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
        _mockErrorHandler = Substitute.For<IHttpErrorHandler>();
        _mockDeduplicator = Substitute.For<IRequestDeduplicationService>();
        _mockLogger = Substitute.For<ILogger<DashboardComponentBase>>();

        _testMessageHandler = new TestMessageHandler();
        var httpClient = new HttpClient(_testMessageHandler) { BaseAddress = new Uri("http://localhost/") };
        _mockHttpClientFactory.CreateClient(HttpClientNames.Hub).Returns(httpClient);

        _component = new TestComponent();
        _component.SetHttpClientFactory(_mockHttpClientFactory);
        _component.SetErrorHandler(_mockErrorHandler);
        _component.SetRequestDeduplicator(_mockDeduplicator);
        _component.SetLogger(_mockLogger);

        // Default behavior for deduplicator: execute the operation
        _mockDeduplicator.ExecuteAsync(Arg.Any<string>(), Arg.Any<Func<Task<TestData?>>>())
            .Returns(async callInfo =>
            {
                var func = callInfo.Arg<Func<Task<TestData?>>>();
                return await func();
            });
    }

    #region GetJsonAsync Tests

    [Test]
    public async Task GetJsonAsync_SuccessfulResponse_ReturnsData()
    {
        // Arrange
        var endpoint = "api/test";
        var expectedData = new TestData { Name = "Test" };
        _testMessageHandler.SendAsyncFunc = (req, ct) => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = JsonContent.Create(expectedData)
        });

        // Act
        var result = await _component.TestGetJsonAsync<TestData>(endpoint);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo(expectedData.Name));
        await _mockDeduplicator.Received(1).ExecuteAsync($"GET:{endpoint}", Arg.Any<Func<Task<TestData?>>>());
    }

    [Test]
    public async Task GetJsonAsync_RetriesOnTransientFailureAndSucceeds()
    {
        // Arrange
        var endpoint = "api/test";
        var expectedData = new TestData { Name = "Success" };
        var callCount = 0;

        _testMessageHandler.SendAsyncFunc = (req, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage { StatusCode = HttpStatusCode.ServiceUnavailable });
            }
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedData)
            });
        };

        // Act
        var result = await _component.TestGetJsonAsync<TestData>(endpoint);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(callCount, Is.EqualTo(2));
        await _mockErrorHandler.DidNotReceive().ShowErrorAsync(Arg.Any<ErrorDetails>());
    }

    [Test]
    public async Task GetJsonAsync_FailsAfterRetries_CallsErrorHandlerAndReturnsDefault()
    {
        // Arrange
        var endpoint = "api/test";
        _testMessageHandler.SendAsyncFunc = (req, ct) => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError
        });

        _mockErrorHandler.HandleExceptionAsync(Arg.Any<Exception>(), Arg.Any<HttpRequestMessage>())
            .Returns(new ErrorDetails { Title = "Error", Message = "Error", Category = ErrorCategory.Unknown });

        // Act
        var result = await _component.TestGetJsonAsync<TestData>(endpoint);

        // Assert
        Assert.That(result, Is.Null);
        await _mockErrorHandler.Received(1).ShowErrorAsync(Arg.Any<ErrorDetails>());
    }

    #endregion

    #region Post/Put/Delete Tests

    [Test]
    public async Task PostJsonAsync_Success_ReturnsResponse()
    {
        // Arrange
        var endpoint = "api/test";
        var data = new TestData { Name = "Post" };
        _testMessageHandler.SendAsyncFunc = (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

        // Act
        var response = await _component.TestPostJsonAsync(endpoint, data);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task PutJsonAsync_Success_ReturnsResponse()
    {
        // Arrange
        var endpoint = "api/test";
        var data = new TestData { Name = "Put" };
        _testMessageHandler.SendAsyncFunc = (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var response = await _component.TestPutJsonAsync(endpoint, data);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DeleteAsync_Success_ReturnsResponse()
    {
        // Arrange
        var endpoint = "api/test";
        _testMessageHandler.SendAsyncFunc = (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

        // Act
        var response = await _component.TestDeleteAsync(endpoint);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    #endregion

    private class TestData
    {
        public string Name { get; set; } = "";
    }

    private class TestMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> SendAsyncFunc { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return SendAsyncFunc(request, cancellationToken);
        }
    }

    private class TestComponent : DashboardComponentBase
    {
        public void SetHttpClientFactory(IHttpClientFactory factory) => HttpClientFactory = factory;
        public void SetErrorHandler(IHttpErrorHandler handler) => ErrorHandler = handler;
        public void SetRequestDeduplicator(IRequestDeduplicationService deduplicator) => RequestDeduplicator = deduplicator;
        public void SetLogger(ILogger<DashboardComponentBase> logger) => Logger = logger;

        public Task<T?> TestGetJsonAsync<T>(string endpoint) => GetJsonAsync<T>(endpoint);
        public Task<HttpResponseMessage> TestPostJsonAsync<T>(string endpoint, T data) => PostJsonAsync(endpoint, data);
        public Task<HttpResponseMessage> TestPutJsonAsync<T>(string endpoint, T data) => PutJsonAsync(endpoint, data);
        public Task<HttpResponseMessage> TestDeleteAsync(string endpoint) => DeleteAsync(endpoint);
    }
}
