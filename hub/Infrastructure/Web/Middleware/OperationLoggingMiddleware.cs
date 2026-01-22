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

using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Web.Middleware;

/// <summary>
/// Middleware that logs operation context (endpoint, duration, status code) for observability.
/// This provides centralized request/response logging without cluttering endpoint code.
/// </summary>
public class OperationLoggingMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(
        HttpContext context,
        ChunkedLogger<OperationLoggingMiddleware> chunkedLogger,
        IEventCodeResolver eventCodeResolver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(chunkedLogger);
        ArgumentNullException.ThrowIfNull(eventCodeResolver);

        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;

        using var op = chunkedLogger.BeginOperation(
            $"{method} {path}",
            inputs: new Dictionary<string, object>
            {
                ["method"] = method,
                ["path"] = path,
                ["traceId"] = context.TraceIdentifier,
                ["HttpTraceId"] = context.TraceIdentifier
            });

        string? eventCode = null;
        try
        {
            await _next(context);

            // Extract event code from various sources
            eventCode = context.Features.Get<IEventCodeFeature>()?.EventCode;

            if (string.IsNullOrEmpty(eventCode) &&
                context.Response.ContentType?.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase) == true)
            {
                eventCode = TryGetEventCodeFromBody(context);
            }

            if (string.IsNullOrEmpty(eventCode))
            {
                eventCode = eventCodeResolver.ResolveEventCodeFromStatus(context.Response.StatusCode, context);
            }

            op.SetOutputs(new Dictionary<string, object>
            {
                ["statusCode"] = context.Response.StatusCode,
                ["eventCode"] = eventCode ?? "Unknown"
            });
        }
        catch (Exception ex)
        {
            eventCode = context.Features.Get<IEventCodeFeature>()?.EventCode;
            if (string.IsNullOrEmpty(eventCode))
            {
                eventCode = eventCodeResolver.ResolveEventCode(ex, context);
            }

            var errorType = context.Response.StatusCode switch
            {
                400 => ErrorType.Validation,
                401 => ErrorType.Unauthorized,
                403 => ErrorType.Unauthorized,
                404 => ErrorType.NotFound,
                409 => ErrorType.Conflict,
                429 => ErrorType.ResourceExhaustion,
                _ => ErrorType.Unexpected
            };

            op.SetOutputs(new Dictionary<string, object>
            {
                ["statusCode"] = context.Response.StatusCode,
                ["eventCode"] = eventCode ?? "Unknown"
            });
            op.Fail(ex, errorType);

            throw;
        }
    }

    private static string? TryGetEventCodeFromBody(HttpContext context)
    {
        if (context.Response.Body is AutoFlushingBufferStream bufferingStream && bufferingStream.IsBuffered)
        {
            var buffer = bufferingStream.Buffer;
            if (buffer != null && buffer.Length > 0)
            {
                var originalPosition = buffer.Position;
                try
                {
                    buffer.Position = 0;
                    using var jsonDoc = JsonDocument.Parse(buffer);
                    if (jsonDoc.RootElement.TryGetProperty("eventCode", out var eventCodeProp))
                    {
                        return eventCodeProp.GetString();
                    }
                }
                catch
                {
                    // Ignore parsing errors, we'll fallback to generic event code
                }
                finally
                {
                    buffer.Position = originalPosition;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Extension methods for registering OperationLoggingMiddleware.
/// </summary>
public static class OperationLoggingMiddlewareExtensions
{
    /// <summary>
    /// Adds operation logging middleware to the application pipeline.
    /// Should be added early in the pipeline to capture all requests.
    /// </summary>
    public static IApplicationBuilder UseOperationLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<OperationLoggingMiddleware>();
    }
}
