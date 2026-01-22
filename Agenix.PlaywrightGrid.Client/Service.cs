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

using System.Net.Http.Headers;
using Agenix.PlaywrightGrid.Client.Abstractions;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Resources;

namespace Agenix.PlaywrightGrid.Client;

/// <summary>
///     Main service for interacting with the Playwright Grid Hub.
/// </summary>
public sealed class Service : IClientService
{
    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;

    /// <summary>
    ///     Creates a new service instance with the specified configuration.
    /// </summary>
    /// <param name="baseUri">The base URI of the Playwright Grid Hub (e.g., "https://grid.example.com")</param>
    /// <param name="projectKey">The project key for authentication and routing</param>
    /// <param name="apiKey">The API key for authentication. Will be sent as Bearer token in Authorization header.</param>
    /// <param name="httpClient">Optional pre-configured HttpClient. If null, a new instance will be created.</param>
    public Service(Uri baseUri, string projectKey, string? apiKey = null, HttpClient? httpClient = null)
    {
        if (baseUri == null)
        {
            throw new ArgumentNullException(nameof(baseUri));
        }

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            throw new ArgumentException("Project key cannot be null or whitespace.", nameof(projectKey));
        }

        BaseUri = baseUri;
        ProjectKey = projectKey;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _disposeHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { BaseAddress = baseUri };
            _disposeHttpClient = true;
        }

        // Configure authentication headers
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        _httpClient.DefaultRequestHeaders.Add("X-Project-Key", projectKey);

        // Initialize resources
        Launch = new LaunchResource(_httpClient, projectKey);
        TestItem = new TestItemResource(_httpClient, projectKey);
        LogItem = new ServiceAsyncLogItemResource(_httpClient, projectKey);
    }

    /// <inheritdoc />
    public Uri BaseUri { get; }

    /// <inheritdoc />
    public string ProjectKey { get; }

    /// <inheritdoc />
    public ILaunchResource Launch { get; }

    /// <inheritdoc />
    public ITestItemResource TestItem { get; }

    /// <inheritdoc />
    public ILogItemResource LogItem { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
