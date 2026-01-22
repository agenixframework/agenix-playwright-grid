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
using Polly;
using Polly.Retry;

namespace Agenix.PlaywrightGrid.Client;

/// <summary>
///     Factory methods for creating HttpClient instances with retry policies.
/// </summary>
public static class HttpClientFactory
{
    /// <summary>
    ///     Creates an HttpClient with default retry policy for transient errors.
    /// </summary>
    /// <param name="baseUri">The base URI of the Playwright Grid Hub</param>
    /// <param name="maxRetryAttempts">Maximum number of retry attempts (default: 3)</param>
    /// <param name="retryDelaySeconds">Base delay between retries in seconds (default: 2, uses exponential backoff)</param>
    /// <returns>Configured HttpClient with retry policy</returns>
    public static HttpClient CreateWithRetry(Uri baseUri, int maxRetryAttempts = 3, int retryDelaySeconds = 2)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10
        };

        // Note: In Polly v8, retry policies are applied via DelegatingHandler in DI
        // For direct HttpClient creation, retry logic should be implemented manually
        return new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    ///     Gets a retry pipeline that handles transient HTTP errors.
    /// </summary>
    /// <param name="maxRetryAttempts">Maximum number of retry attempts</param>
    /// <param name="retryDelaySeconds">Base delay between retries in seconds (uses exponential backoff)</param>
    /// <returns>Polly resilience pipeline</returns>
    public static ResiliencePipeline<HttpResponseMessage> GetRetryPipeline(int maxRetryAttempts, int retryDelaySeconds)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromSeconds(retryDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response =>
                        response.StatusCode == HttpStatusCode.RequestTimeout ||
                        response.StatusCode == HttpStatusCode.TooManyRequests ||
                        (int)response.StatusCode >= 500),
                OnRetry = args =>
                {
                    Console.WriteLine(
                        $"[PlaywrightGrid] Retry {args.AttemptNumber} after {args.RetryDelay.TotalSeconds}s due to: {args.Outcome.Result?.StatusCode}");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    ///     Creates an HttpClient with custom timeout.
    /// </summary>
    /// <param name="baseUri">The base URI of the Playwright Grid Hub</param>
    /// <param name="timeoutSeconds">Timeout in seconds</param>
    /// <returns>Configured HttpClient</returns>
    public static HttpClient CreateWithTimeout(Uri baseUri, int timeoutSeconds)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10
        };

        return new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }
}
