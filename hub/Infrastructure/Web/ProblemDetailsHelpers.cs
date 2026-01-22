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

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
/// Static helper methods for creating standardized RFC 7807 ProblemDetails responses
/// with EventCode integration for consistent error identification.
/// </summary>
public static class ProblemDetailsHelpers
{
    /// <summary>
    /// Creates a validation problem (400 Bad Request) with field-level errors and event code.
    /// </summary>
    /// <param name="errors">Dictionary of field names to error messages.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "ADM91", "PRJ02").</param>
    /// <param name="instance">Request path (optional, defaults to null).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 400 Bad Request with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// var errors = new Dictionary&lt;string, string[]&gt; { ["name"] = new[] { "Required" } };
    /// return ProblemDetailsHelpers.ValidationProblem(errors, "ADM91", "/api/users", "trace-id");
    /// </code>
    /// </example>
    public static IResult ValidationProblem(
        Dictionary<string, string[]> errors,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var problemDetails = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Instance = instance,
            Extensions = { ["eventCode"] = eventCode }
        };

        if (!string.IsNullOrEmpty(traceId))
            problemDetails.Extensions["traceId"] = traceId;

        return Results.ValidationProblem(
            errors: errors,
            detail: null,
            statusCode: StatusCodes.Status400BadRequest,
            title: problemDetails.Title,
            type: problemDetails.Type,
            instance: problemDetails.Instance,
            extensions: problemDetails.Extensions);
    }

    /// <summary>
    /// Creates a not found problem (404 Not Found) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "LCH03").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 404 Not Found with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// return ProblemDetailsHelpers.NotFound("Launch not found", "LCH03", "/api/launches/1");
    /// </code>
    /// </example>
    public static IResult NotFound(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            type: "https://httpstatuses.com/404",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates a conflict problem (409 Conflict) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "LCH06").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 409 Conflict with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// return ProblemDetailsHelpers.Conflict("Launch already finished", "LCH06");
    /// </code>
    /// </example>
    public static IResult Conflict(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            type: "https://httpstatuses.com/409",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates a payload too large problem (413 Payload Too Large) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system.</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 413 Payload Too Large with ProblemDetails.</returns>
    public static IResult PayloadTooLarge(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Payload Too Large",
            type: "https://httpstatuses.com/413",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates an unauthorized problem (401 Unauthorized) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "ADM11").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 401 Unauthorized with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// return ProblemDetailsHelpers.Unauthorized("Invalid credentials", "ADM11");
    /// </code>
    /// </example>
    public static IResult Unauthorized(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            type: "https://httpstatuses.com/401",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates a forbidden problem (403 Forbidden) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "ADM12").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 403 Forbidden with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// return ProblemDetailsHelpers.Forbidden("Insufficient permissions", "ADM12");
    /// </code>
    /// </example>
    public static IResult Forbidden(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            type: "https://httpstatuses.com/403",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates an internal server error problem (500) with safe message and event code.
    /// IMPORTANT: Full exception details must be logged server-side separately.
    /// </summary>
    /// <param name="safeMessage">Safe, generic error message for client.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "DB04", "WSH10").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 500 Internal Server Error with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// return ProblemDetailsHelpers.InternalServerError("An unexpected error occurred", "WSH10");
    /// </code>
    /// </example>
    public static IResult InternalServerError(
        string safeMessage,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: safeMessage,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            type: "https://httpstatuses.com/500",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates a service unavailable problem (503) with safe message and event code.
    /// Used for dependency failures (database down, Redis unavailable, etc.).
    /// </summary>
    /// <param name="dependency">Dependency name (e.g., "Database", "Redis", "Worker").</param>
    /// <param name="safeMessage">Safe, generic error message for client.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "DB01", "RDS01").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 503 Service Unavailable with ProblemDetails.</returns>
    /// <example>
    /// <code>
    /// return ProblemDetailsHelpers.ServiceUnavailable("Database", "Database is down", "DB01");
    /// </code>
    /// </example>
    public static IResult ServiceUnavailable(
        string dependency,
        string safeMessage,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode,
            ["dependency"] = dependency
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: safeMessage,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Service Unavailable",
            type: "https://httpstatuses.com/503",
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Creates a problem response with a specific status code and event code.
    /// </summary>
    public static IResult StatusCode(
        int statusCode,
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["eventCode"] = eventCode
        };

        if (!string.IsNullOrEmpty(traceId))
            extensions["traceId"] = traceId;

        return Results.Problem(
            detail: message,
            statusCode: statusCode,
            title: GetTitleForStatusCode(statusCode),
            type: $"https://httpstatuses.com/{statusCode}",
            instance: instance,
            extensions: extensions);
    }

    private static string GetTitleForStatusCode(int statusCode)
    {
        return statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => "An error occurred"
        };
    }
}
