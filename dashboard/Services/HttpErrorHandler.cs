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
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dashboard.Models;
using Microsoft.Extensions.Logging;

namespace Dashboard.Services;

/// <summary>
/// Implementation of <see cref="IHttpErrorHandler"/> that provides centralized error classification,
/// ProblemDetails parsing, and indirect UI notification via events.
/// </summary>
public sealed class HttpErrorHandler : IHttpErrorHandler
{
    private readonly ILogger<HttpErrorHandler> _logger;

    /// <summary>
    /// Event raised when an error should be displayed in the UI.
    /// Components (like ErrorModal) can subscribe to this event.
    /// </summary>
    public event Func<ErrorDetails, Task>? OnErrorRaised;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpErrorHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostics.</param>
    public HttpErrorHandler(ILogger<HttpErrorHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorDetails> HandleExceptionAsync(Exception ex, HttpRequestMessage? request)
    {
        var category = Classify(ex);
        var problem = TryParseProblemDetails(ex);

        var statusCode = problem?.Status;
        if (statusCode == null && ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            statusCode = (int)httpEx.StatusCode.Value;
        }

        var error = new ErrorDetails
        {
            Title = problem?.Title ?? GetDefaultTitle(category),
            Message = GetUserFriendlyMessage(category, problem?.Detail ?? ex.Message),
            Details = problem?.Detail,
            StatusCode = statusCode,
            RequestId = problem?.GetExtensionString("traceId"),
            EventCode = problem?.GetExtensionString("eventCode"),
            HttpMethod = request?.Method.Method,
            Endpoint = request?.RequestUri?.ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Category = category,
            IsRetryable = await IsRetryableAsync(ex),
            StackTrace = ex.ToString()
        };

        LogHandledException(ex, error);

        return error;
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(ErrorDetails error)
    {
        _logger.LogInformation("[HttpError] Showing error modal. EventCode: {EventCode}, RequestId: {RequestId}",
            error.EventCode, error.RequestId);

        if (OnErrorRaised != null)
        {
            await OnErrorRaised.Invoke(error);
        }
        else
        {
            _logger.LogWarning("[HttpError] No subscribers for OnErrorRaised event. Error modal will not be shown to the user.");
        }
    }

    /// <inheritdoc />
    public Task<bool> IsRetryableAsync(Exception ex)
    {
        var category = Classify(ex);
        bool isRetryable = category switch
        {
            ErrorCategory.Network => true,
            ErrorCategory.Server => true,
            ErrorCategory.RateLimit => true,
            ErrorCategory.Client => false,
            ErrorCategory.Validation => false,
            _ => false // Default for Unknown
        };

        return Task.FromResult(isRetryable);
    }

    private ErrorCategory Classify(Exception ex)
    {
        if (ex is JsonException)
        {
            return ErrorCategory.Validation;
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue)
            {
                int code = (int)httpEx.StatusCode.Value;
                if (code == 429) return ErrorCategory.RateLimit;
                if (code >= 500) return ErrorCategory.Server;
                if (code >= 400) return ErrorCategory.Client;
            }
            return ErrorCategory.Network;
        }

        return ErrorCategory.Unknown;
    }

    private void LogHandledException(Exception ex, ErrorDetails error)
    {
        if (error.Category == ErrorCategory.Network || error.Category == ErrorCategory.RateLimit)
        {
            _logger.LogWarning(ex,
                "[HttpError] Handled {Category} error. Status: {StatusCode}, Method: {Method}, Endpoint: {Endpoint}, RequestId: {RequestId}, EventCode: {EventCode}",
                error.Category, error.StatusCode, error.HttpMethod, error.Endpoint, error.RequestId, error.EventCode);
        }
        else
        {
            _logger.LogError(ex,
                "[HttpError] Handled {Category} error. Status: {StatusCode}, Method: {Method}, Endpoint: {Endpoint}, RequestId: {RequestId}, EventCode: {EventCode}",
                error.Category, error.StatusCode, error.HttpMethod, error.Endpoint, error.RequestId, error.EventCode);
        }
    }

    private ProblemDetailsDto? TryParseProblemDetails(Exception ex)
    {
        if (ex is not HttpRequestException) return null;

        var message = ex.Message;
        int jsonStart = message.IndexOf('{');
        if (jsonStart < 0) return null;

        try
        {
            string json = message.Substring(jsonStart);
            return JsonSerializer.Deserialize<ProblemDetailsDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private string GetDefaultTitle(ErrorCategory category) => category switch
    {
        ErrorCategory.Network => "Network Connection Error",
        ErrorCategory.Server => "Server Error",
        ErrorCategory.Client => "Request Error",
        ErrorCategory.RateLimit => "Too Many Requests",
        ErrorCategory.Validation => "Validation Error",
        _ => "Unexpected Error"
    };

    private string GetUserFriendlyMessage(ErrorCategory category, string originalMessage) => category switch
    {
        ErrorCategory.Network => "A connection error occurred. Please check your network and try again.",
        ErrorCategory.RateLimit => "You have made too many requests. Please wait a moment and try again.",
        _ => originalMessage
    };

    private class ProblemDetailsDto
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public int? Status { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? Extensions { get; set; }

        public string? GetExtensionString(string key)
        {
            if (Extensions != null && Extensions.TryGetValue(key, out var value))
            {
                if (value is JsonElement element)
                {
                    return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                }
                return value.ToString();
            }
            return null;
        }
    }
}
