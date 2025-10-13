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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PlaywrightHub.Infrastructure.Web;

namespace PlaywrightHub.Tests.Infrastructure.Web;

public class ProblemDetailsHelpersTests
{
    [Fact]
    public void ValidationProblem_WithErrors_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            ["field1"] = ["error1", "error2"],
            ["field2"] = ["error3"]
        };
        var eventCode = "VAL001";
        var instance = "/api/test";
        var traceId = "trace-123";

        // Act
        var result = ProblemDetailsHelpers.ValidationProblem(errors, eventCode, instance, traceId);

        // Assert
        Assert.NotNull(result);
        var details = (result as IValueHttpResult)?.Value as HttpValidationProblemDetails;
        Assert.NotNull(details);

        Assert.Equal("application/problem+json", (result as IContentTypeHttpResult)?.ContentType);
        Assert.Equal("One or more validation errors occurred.", details.Title);
        Assert.Equal("https://tools.ietf.org/html/rfc7231#section-6.5.1", details.Type);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);

        var validationDetails = Assert.IsType<HttpValidationProblemDetails>(details);
        Assert.Equal(errors, validationDetails.Errors);
    }

    [Fact]
    public void NotFound_WithMessage_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var message = "Item not found";
        var eventCode = "ERR404";
        var instance = "/api/items/1";
        var traceId = "trace-404";

        // Act
        var result = ProblemDetailsHelpers.NotFound(message, eventCode, instance, traceId);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
        Assert.Equal("application/problem+json", problemResult.ContentType);

        var details = problemResult.ProblemDetails;
        Assert.Equal("Not Found", details.Title);
        Assert.Equal(message, details.Detail);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);
    }

    [Fact]
    public void Conflict_WithMessage_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var message = "Conflict occurred";
        var eventCode = "ERR409";
        var instance = "/api/items";
        var traceId = "trace-409";

        // Act
        var result = ProblemDetailsHelpers.Conflict(message, eventCode, instance, traceId);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);

        var details = problemResult.ProblemDetails;
        Assert.Equal("Conflict", details.Title);
        Assert.Equal(message, details.Detail);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);
    }

    [Fact]
    public void Unauthorized_WithMessage_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var message = "Unauthorized access";
        var eventCode = "ERR401";
        var instance = "/api/secure";
        var traceId = "trace-401";

        // Act
        var result = ProblemDetailsHelpers.Unauthorized(message, eventCode, instance, traceId);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemResult.StatusCode);

        var details = problemResult.ProblemDetails;
        Assert.Equal("Unauthorized", details.Title);
        Assert.Equal(message, details.Detail);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);
    }

    [Fact]
    public void Forbidden_WithMessage_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var message = "Forbidden access";
        var eventCode = "ERR403";
        var instance = "/api/admin";
        var traceId = "trace-403";

        // Act
        var result = ProblemDetailsHelpers.Forbidden(message, eventCode, instance, traceId);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problemResult.StatusCode);

        var details = problemResult.ProblemDetails;
        Assert.Equal("Forbidden", details.Title);
        Assert.Equal(message, details.Detail);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);
    }

    [Fact]
    public void InternalServerError_WithSafeMessage_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var message = "Internal error";
        var eventCode = "ERR500";
        var instance = "/api/error";
        var traceId = "trace-500";

        // Act
        var result = ProblemDetailsHelpers.InternalServerError(message, eventCode, instance, traceId);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemResult.StatusCode);

        var details = problemResult.ProblemDetails;
        Assert.Equal("Internal Server Error", details.Title);
        Assert.Equal(message, details.Detail);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);
    }

    [Fact]
    public void ServiceUnavailable_WithDependency_ReturnsProblemDetailsWithEventCode()
    {
        // Arrange
        var dependency = "Database";
        var message = "Database is down";
        var eventCode = "ERR503";
        var instance = "/api/db";
        var traceId = "trace-503";

        // Act
        var result = ProblemDetailsHelpers.ServiceUnavailable(dependency, message, eventCode, instance, traceId);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problemResult.StatusCode);

        var details = problemResult.ProblemDetails;
        Assert.Equal("Service Unavailable", details.Title);
        Assert.Equal(message, details.Detail);
        Assert.Equal(instance, details.Instance);
        Assert.Equal(eventCode, details.Extensions["eventCode"]);
        Assert.Equal(dependency, details.Extensions["dependency"]);
        Assert.Equal(traceId, details.Extensions["traceId"]);
    }
}
