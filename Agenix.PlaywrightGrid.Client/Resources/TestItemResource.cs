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

using System.Net.Http.Json;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;

namespace Agenix.PlaywrightGrid.Client.Resources;

/// <summary>
///     HTTP implementation of test item resource.
/// </summary>
public sealed class TestItemResource : ITestItemResource
{
    private readonly HttpClient _httpClient;
    private readonly string _projectKey;

    /// <summary>
    ///     Creates a new test item resource.
    /// </summary>
    public TestItemResource(HttpClient httpClient, string projectKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _projectKey = projectKey ?? throw new ArgumentNullException(nameof(projectKey));
    }

    /// <inheritdoc />
    public async Task<TestItemCreatedResponse> StartAsync(StartTestItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/test-items", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TestItemCreatedResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize TestItemCreatedResponse");
    }

    /// <inheritdoc />
    public async Task<TestItemResponse> GetAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/test-items/{itemId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TestItemResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize TestItemResponse");
    }

    /// <inheritdoc />
    public async Task<MessageResponse> FinishAsync(Guid itemId, FinishTestItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/test-items/{itemId}/finish", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize MessageResponse");
    }

    /// <inheritdoc />
    public async Task<List<TestItemResponse>> GetByLaunchAsync(Guid launchId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/launches/{launchId}/test-items", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<TestItemResponse>>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize List<TestItemResponse>");
    }

    /// <inheritdoc />
    public async Task<List<TestItemResponse>> GetBySuiteAsync(Guid suiteId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/suites/{suiteId}/test-items", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<TestItemResponse>>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize List<TestItemResponse>");
    }

    /// <inheritdoc />
    public async Task<List<TestItemResponse>> GetChildrenAsync(Guid parentItemId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/test-items/{parentItemId}/children", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<TestItemResponse>>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize List<TestItemResponse>");
    }

    /// <inheritdoc />
    public async Task<TestItemResponse> GetTreeAsync(Guid itemId, int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _httpClient.GetAsync($"/api/test-items/{itemId}/tree?maxDepth={maxDepth}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TestItemResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize TestItemResponse");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new ServiceException(
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
            response.StatusCode,
            response.RequestMessage?.RequestUri ?? new Uri("about:blank"),
            response.RequestMessage?.Method ?? HttpMethod.Get,
            content
        );
    }
}
