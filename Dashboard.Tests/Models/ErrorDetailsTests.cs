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

using Dashboard.Models;
using NUnit.Framework;

namespace Dashboard.Tests.Models;

[TestFixture]
public class ErrorDetailsTests
{
    [Test]
    public void Construction_WithAllProperties_PreservesValues()
    {
        // Arrange
        var message = "Test Message";
        var title = "Test Title";
        var details = "Test Details";
        var stackTrace = "Test StackTrace";
        var statusCode = 500;
        var requestId = "req-123";
        var eventCode = "EVT-001";
        var httpMethod = "GET";
        var endpoint = "/api/test";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var isRetryable = true;
        var category = ErrorCategory.Server;

        // Act
        var errorDetails = new ErrorDetails
        {
            Message = message,
            Title = title,
            Details = details,
            StackTrace = stackTrace,
            StatusCode = statusCode,
            RequestId = requestId,
            EventCode = eventCode,
            HttpMethod = httpMethod,
            Endpoint = endpoint,
            Timestamp = timestamp,
            IsRetryable = isRetryable,
            Category = category
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(errorDetails.Message, Is.EqualTo(message));
            Assert.That(errorDetails.Title, Is.EqualTo(title));
            Assert.That(errorDetails.Details, Is.EqualTo(details));
            Assert.That(errorDetails.StackTrace, Is.EqualTo(stackTrace));
            Assert.That(errorDetails.StatusCode, Is.EqualTo(statusCode));
            Assert.That(errorDetails.RequestId, Is.EqualTo(requestId));
            Assert.That(errorDetails.EventCode, Is.EqualTo(eventCode));
            Assert.That(errorDetails.HttpMethod, Is.EqualTo(httpMethod));
            Assert.That(errorDetails.Endpoint, Is.EqualTo(endpoint));
            Assert.That(errorDetails.Timestamp, Is.EqualTo(timestamp));
            Assert.That(errorDetails.IsRetryable, Is.EqualTo(isRetryable));
            Assert.That(errorDetails.Category, Is.EqualTo(category));
        });
    }

    [Test]
    public void Construction_WithRequiredProperties_SetsDefaultValues()
    {
        // Arrange
        var message = "Minimal Message";
        var title = "Minimal Title";
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        // Act
        var errorDetails = new ErrorDetails
        {
            Message = message,
            Title = title
        };

        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(errorDetails.Message, Is.EqualTo(message));
            Assert.That(errorDetails.Title, Is.EqualTo(title));
            Assert.That(errorDetails.Timestamp, Is.GreaterThanOrEqualTo(before));
            Assert.That(errorDetails.Timestamp, Is.LessThanOrEqualTo(after));
            Assert.That(errorDetails.Category, Is.EqualTo(default(ErrorCategory))); // ErrorCategory.Network
            Assert.That(errorDetails.Details, Is.Null);
            Assert.That(errorDetails.StackTrace, Is.Null);
            Assert.That(errorDetails.StatusCode, Is.Null);
            Assert.That(errorDetails.IsRetryable, Is.False);
        });
    }

    [Test]
    public void Immutability_UsingWithExpression_CreatesNewInstanceAndKeepsOriginal()
    {
        // Arrange
        var original = new ErrorDetails
        {
            Message = "Original Message",
            Title = "Original Title"
        };

        // Act
        var modified = original with { Title = "New Title" };

        // Assert
        Assert.That(modified.Title, Is.EqualTo("New Title"));
        Assert.That(original.Title, Is.EqualTo("Original Title"));
        Assert.That(modified, Is.Not.SameAs(original));

        /*
        // Compile-time immutability check:
        // The following line would fail to compile because properties are init-only:
        // original.Title = "Something Else";
        */
    }
}
