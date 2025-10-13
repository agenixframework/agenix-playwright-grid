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
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.ErrorHandling;

/// <summary>
/// Integration tests for the enhanced ProblemDetails middleware.
/// Verifies that various error conditions are correctly normalized and include event codes.
/// </summary>
[TestFixture]
public class ProblemDetailsMiddlewareTests : ApiTestBase
{
    [Test]
    public async Task ValidationError_ReturnsProblemDetailsWithEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/test/error/validation");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("ADM91"));
        Assert.That(problem.Extensions.ContainsKey("traceId"), Is.True);
        var traceId = problem.Extensions["traceId"]?.ToString();
        Assert.That(traceId, Is.Not.Null);
        Assert.That(traceId!.Length, Is.GreaterThan(0));

        Assert.That(problem.Errors, Is.Not.Null);
        Assert.That(problem.Errors.Count, Is.GreaterThan(0));
        Assert.That(problem.Errors.ContainsKey("FieldA"), Is.True);
        Assert.That(problem.Errors["FieldA"], Contains.Item("Error 1"));
    }

    [Test]
    public async Task ManualBadRequest_NormalizedToProblemDetailsWithEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/test/error/bad-request");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("ADM91"));

        // The middleware converts { error: "..." } to a ValidationProblemDetails with a "Request" field
        Assert.That(problem.Errors, Is.Not.Null);
        Assert.That(problem.Errors.ContainsKey("Request"), Is.True);
        Assert.That(problem.Errors["Request"], Contains.Item("Manual bad request"));
    }

    [Test]
    public async Task UnhandledException_Returns500WithEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/test/error/unhandled");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("WSH10"));
        Assert.That(problem.Title, Is.EqualTo("Internal Server Error"));
        // Detailed message should NOT be exposed in 500 errors (safe message only)
        Assert.That(problem.Detail, Is.EqualTo("An unexpected error occurred. Please contact support with the trace ID."));
    }

    [Test]
    public async Task DatabaseException_Returns500WithDatabaseEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/test/error/database");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("DB43")); // TransactionFailed
        Assert.That(problem.Detail, Is.EqualTo("An unexpected error occurred. Please contact support with the trace ID."));
    }

    [Test]
    public async Task TimeoutExceptionInLaunch_Returns500WithLaunchEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/launches/test-error-timeout");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("LCH99")); // LaunchOperationFailed
    }

    [Test]
    public async Task TimeoutExceptionInTestItem_Returns500WithTestItemEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/test-items/test-error-timeout");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("ITEM99")); // TestItemOperationFailed
    }

    [Test]
    public async Task NotFound_NormalizedToProblemDetailsWithEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/non-existent-endpoint");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo("WSH10"));
        Assert.That(problem.Title, Is.EqualTo("Not Found"));
    }
}
