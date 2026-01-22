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
///     HTTP implementation of launch resource.
/// </summary>
public sealed class LaunchResource : ILaunchResource
{
    private readonly HttpClient _httpClient;
    private readonly string _projectKey;

    /// <summary>
    ///     Creates a new launch resource.
    /// </summary>
    public LaunchResource(HttpClient httpClient, string projectKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _projectKey = projectKey ?? throw new ArgumentNullException(nameof(projectKey));
    }

    /// <inheritdoc />
    public async Task<LaunchCreatedResponse> StartAsync(StartLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/launches", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<LaunchCreatedResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize LaunchCreatedResponse");
    }

    /// <inheritdoc />
    public async Task<LaunchResponse> GetAsync(Guid launchId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/launches/{launchId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<LaunchResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize LaunchResponse");
    }

    /// <inheritdoc />
    public async Task<MessageResponse> FinishAsync(Guid launchId, FinishLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize MessageResponse");
    }

    /// <inheritdoc />
    public async Task<MessageResponse> UpdateAsync(Guid launchId, UpdateLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/launches/{launchId}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize MessageResponse");
    }

    /// <inheritdoc />
    public async Task<List<LaunchResponse>> GetAllAsync(int offset = 0, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/launches?offset={offset}&limit={limit}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<LaunchResponse>>(cancellationToken)
               ?? throw new ServiceException("Failed to deserialize List<LaunchResponse>");
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
