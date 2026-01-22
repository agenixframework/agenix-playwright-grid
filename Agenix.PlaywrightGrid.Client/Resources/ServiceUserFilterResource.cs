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

using Agenix.PlaywrightGrid.Client.Abstractions.Filtering;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Resources;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;

namespace Agenix.PlaywrightGrid.Client.Resources;

internal class ServiceUserFilterResource : ServiceBaseResource, IUserFilterResource
{
    public ServiceUserFilterResource(HttpClient httpClient, string project) : base(httpClient, project)
    {
    }

    public async Task<Content<UserFilterResponse>> GetAsync(CancellationToken cancellationToken)
    {
        return await GetAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Content<UserFilterResponse>> GetAsync(FilterOption filterOption,
        CancellationToken cancellationToken)
    {
        var uri = $"v1/{ProjectName}/filter";
        if (filterOption != null)
        {
            uri += $"?{filterOption}";
        }

        return await GetAsJsonAsync<Content<UserFilterResponse>>(uri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserFilterCreatedResponse> CreateAsync(CreateUserFilterRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsJsonAsync<UserFilterCreatedResponse, CreateUserFilterRequest>(
            $"v1/{ProjectName}/filter", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MessageResponse> UpdateAsync(long id, UpdateUserFilterRequest request,
        CancellationToken cancellationToken)
    {
        return await PutAsJsonAsync<MessageResponse, UpdateUserFilterRequest>(
            $"v1/{ProjectName}/filter/{id}", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserFilterResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        return await GetAsJsonAsync<UserFilterResponse>($"v1/{ProjectName}/filter/{id}", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MessageResponse> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        return await DeleteAsJsonAsync<MessageResponse>($"v1/{ProjectName}/filter/{id}", cancellationToken)
            .ConfigureAwait(false);
    }
}
