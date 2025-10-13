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
using Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Polly;
using Polly.Retry;

namespace Dashboard.Components;

/// <summary>
/// Abstract base class for all data-driven dashboard UI components.
/// Standardizes HTTP access patterns, resiliency, request deduplication, and error handling.
/// </summary>
public abstract class DashboardComponentBase : ComponentBase
{
    [Inject] protected IHttpClientFactory HttpClientFactory { get; set; } = null!;
    [Inject] protected IHttpErrorHandler ErrorHandler { get; set; } = null!;
    [Inject] protected IRequestDeduplicationService RequestDeduplicator { get; set; } = null!;
    [Inject] protected ILogger<DashboardComponentBase> Logger { get; set; } = null!;

    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardComponentBase"/> class with a configured Polly retry policy.
    /// </summary>
    protected DashboardComponentBase()
    {
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>() // Network failures
            .OrResult(r => (int)r.StatusCode >= 500 || (int)r.StatusCode == 429) // HTTP 5xx or 429
            .WaitAndRetryAsync(
                3,
                retryAttempt => retryAttempt switch
                {
                    1 => TimeSpan.FromSeconds(2),
                    2 => TimeSpan.FromSeconds(4),
                    3 => TimeSpan.FromSeconds(8),
                    _ => TimeSpan.FromSeconds(8)
                },
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    var endpoint = context.ContainsKey("endpoint") ? context["endpoint"].ToString() : "unknown";
                    var reason = outcome.Exception != null ? outcome.Exception.Message : outcome.Result.StatusCode.ToString();

                    Logger.LogWarning(
                        "[HttpRetry] Attempt {Attempt} for {Endpoint} failed with {Reason}. Retrying in {Delay}s",
                        retryAttempt, endpoint, reason, timespan.TotalSeconds);
                });
    }

    /// <summary>
    /// Executes a GET request with request deduplication and resilience.
    /// </summary>
    protected async Task<T?> GetJsonAsync<T>(string endpoint)
    {
        var key = $"GET:{endpoint}";
        try
        {
            return await RequestDeduplicator.ExecuteAsync(key, async () =>
            {
                using var response = await ExecuteWithResilienceAsync(HttpMethod.Get, endpoint);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<T>();
                }
                return default;
            });
        }
        catch (Exception ex)
        {
            // Logging and error display already handled in ExecuteWithResilienceAsync
            Logger.LogDebug(ex, "[Component] GetJsonAsync<{Type}> failed for {Endpoint}", typeof(T).Name, endpoint);
            return default;
        }
    }

    /// <summary>
    /// Executes a POST request with resilience.
    /// </summary>
    protected async Task<HttpResponseMessage> PostJsonAsync<T>(string endpoint, T data)
    {
        return await ExecuteWithResilienceAsync(HttpMethod.Post, endpoint, data);
    }

    /// <summary>
    /// Executes a PUT request with resilience.
    /// </summary>
    protected async Task<HttpResponseMessage> PutJsonAsync<T>(string endpoint, T data)
    {
        return await ExecuteWithResilienceAsync(HttpMethod.Put, endpoint, data);
    }

    /// <summary>
    /// Executes a DELETE request with resilience.
    /// </summary>
    protected async Task<HttpResponseMessage> DeleteAsync(string endpoint)
    {
        return await ExecuteWithResilienceAsync(HttpMethod.Delete, endpoint);
    }

    /// <summary>
    /// Internal helper that applies the Polly retry policy and handles errors uniformly.
    /// </summary>
    private async Task<HttpResponseMessage> ExecuteWithResilienceAsync(HttpMethod method, string endpoint, object? data = null)
    {
        var context = new Context { ["endpoint"] = endpoint };

        try
        {
            var response = await _retryPolicy.ExecuteAsync(async (ctx) =>
            {
                var client = HttpClientFactory.CreateClient(HttpClientNames.Hub);
                var request = CreateRequest(method, endpoint, data);
                return await client.SendAsync(request);
            }, context);

            if (!response.IsSuccessStatusCode)
            {
                var ex = new HttpRequestException($"Request failed with status {response.StatusCode}", null, response.StatusCode);
                await HandleFailureAsync(ex, method, endpoint, data);
            }

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[HttpError] Unhandled exception during {Method} {Endpoint}: {Message}", method, endpoint, ex.Message);
            await HandleFailureAsync(ex, method, endpoint, data);

            // Return a failed response to avoid crashing the component
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Forwards the error to the IHttpErrorHandler and triggers UI notification.
    /// </summary>
    private async Task HandleFailureAsync(Exception ex, HttpMethod method, string endpoint, object? data)
    {
        using var contextRequest = CreateRequest(method, endpoint, data);
        var errorDetails = await ErrorHandler.HandleExceptionAsync(ex, contextRequest);
        await ErrorHandler.ShowErrorAsync(errorDetails);
    }

    /// <summary>
    /// Helper to create a new HttpRequestMessage for each attempt.
    /// </summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, object? data = null)
    {
        var request = new HttpRequestMessage(method, endpoint);
        if (data != null)
        {
            request.Content = JsonContent.Create(data);
        }
        return request;
    }
}
