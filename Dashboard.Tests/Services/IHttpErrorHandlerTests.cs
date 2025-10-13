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
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Dashboard.Models;
using Dashboard.Services;
using NUnit.Framework;

namespace Dashboard.Tests.Services;

/// <summary>
/// Tests for <see cref="IHttpErrorHandler"/> to ensure contract stability.
/// </summary>
[TestFixture]
public class IHttpErrorHandlerTests
{
    private class FakeHttpErrorHandler : IHttpErrorHandler
    {
        public Task<ErrorDetails> HandleExceptionAsync(Exception ex, HttpRequestMessage? request)
        {
            return Task.FromResult(new ErrorDetails
            {
                Message = ex.Message,
                Title = "Error",
                Category = ErrorCategory.Unknown,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        public Task ShowErrorAsync(ErrorDetails error)
        {
            return Task.CompletedTask;
        }

        public Task<bool> IsRetryableAsync(Exception ex)
        {
            return Task.FromResult(false);
        }
    }

    [Test]
    public void InterfaceCanBeImplemented()
    {
        IHttpErrorHandler handler = new FakeHttpErrorHandler();
        Assert.That(handler, Is.Not.Null);
        Assert.That(handler, Is.InstanceOf<IHttpErrorHandler>());
    }

    [Test]
    public async Task AllInterfaceMethodsCanBeCalled()
    {
        // Arrange
        IHttpErrorHandler handler = new FakeHttpErrorHandler();
        var exception = new Exception("Test exception");

        // Act & Assert

        // 1. HandleExceptionAsync with null request
        var resultWithNull = await handler.HandleExceptionAsync(exception, null);
        Assert.That(resultWithNull, Is.Not.Null);
        Assert.That(resultWithNull.Message, Is.EqualTo(exception.Message));
        Assert.That(resultWithNull.Title, Is.Not.Null);

        // 2. HandleExceptionAsync with valid request
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost");
        var resultWithRequest = await handler.HandleExceptionAsync(exception, request);
        Assert.That(resultWithRequest, Is.Not.Null);
        Assert.That(resultWithRequest.Message, Is.EqualTo(exception.Message));

        // 3. IsRetryableAsync
        bool isRetryable = await handler.IsRetryableAsync(exception);
        Assert.That(isRetryable, Is.False);

        // 4. ShowErrorAsync
        var errorDetails = new ErrorDetails
        {
            Message = "Test message",
            Title = "Test title",
            Category = ErrorCategory.Unknown,
            Timestamp = DateTimeOffset.UtcNow
        };
        Assert.DoesNotThrowAsync(async () => await handler.ShowErrorAsync(errorDetails));
    }

    [Test]
    public void MethodSignaturesMatchExactly()
    {
        var type = typeof(IHttpErrorHandler);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.That(methods.Length, Is.EqualTo(3), "IHttpErrorHandler should have exactly 3 public methods.");

        // HandleExceptionAsync
        var handleEx = methods.SingleOrDefault(m => m.Name == "HandleExceptionAsync");
        Assert.That(handleEx, Is.Not.Null, "Method HandleExceptionAsync not found");
        Assert.That(handleEx!.ReturnType, Is.EqualTo(typeof(Task<ErrorDetails>)));
        var handleExParams = handleEx.GetParameters();
        Assert.That(handleExParams.Length, Is.EqualTo(2));
        Assert.That(handleExParams[0].ParameterType, Is.EqualTo(typeof(Exception)));
        Assert.That(handleExParams[1].ParameterType, Is.EqualTo(typeof(HttpRequestMessage)));

        // Verify nullable request (using NullabilityInfoContext if available in net8.0)
        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(handleExParams[1]);
        Assert.That(nullabilityInfo.ReadState, Is.EqualTo(NullabilityState.Nullable), "Request parameter should be nullable (read)");
        Assert.That(nullabilityInfo.WriteState, Is.EqualTo(NullabilityState.Nullable), "Request parameter should be nullable (write)");

        // ShowErrorAsync
        var showError = methods.SingleOrDefault(m => m.Name == "ShowErrorAsync");
        Assert.That(showError, Is.Not.Null, "Method ShowErrorAsync not found");
        Assert.That(showError!.ReturnType, Is.EqualTo(typeof(Task)));
        var showErrorParams = showError.GetParameters();
        Assert.That(showErrorParams.Length, Is.EqualTo(1));
        Assert.That(showErrorParams[0].ParameterType, Is.EqualTo(typeof(ErrorDetails)));

        // IsRetryableAsync
        var isRetryable = methods.SingleOrDefault(m => m.Name == "IsRetryableAsync");
        Assert.That(isRetryable, Is.Not.Null, "Method IsRetryableAsync not found");
        Assert.That(isRetryable!.ReturnType, Is.EqualTo(typeof(Task<bool>)));
        var isRetryableParams = isRetryable.GetParameters();
        Assert.That(isRetryableParams.Length, Is.EqualTo(1));
        Assert.That(isRetryableParams[0].ParameterType, Is.EqualTo(typeof(Exception)));
    }
}
