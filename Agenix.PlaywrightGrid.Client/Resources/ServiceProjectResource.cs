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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Resources;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses.Project;

namespace Agenix.PlaywrightGrid.Client.Resources;

internal class ServiceProjectResource(HttpClient httpClient, string project)
    : ServiceBaseResource(httpClient, project), IProjectResource
{
    public async Task<ProjectResponse> GetAsync(CancellationToken cancellationToken)
    {
        return await GetAsJsonAsync<ProjectResponse>($"v1/project/{ProjectName}", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MessageResponse> UpdatePreferencesAsync(string username, PreferenceRequest preferences,
        CancellationToken cancellationToken)
    {
        return await PutAsJsonAsync<MessageResponse, PreferenceRequest>(
            $"v1/project/{ProjectName}/preference/{username}",
            preferences,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreferenceResponse> GetAllPreferencesAsync(string username, CancellationToken cancellationToken)
    {
        return await GetAsJsonAsync<PreferenceResponse>(
            $"v1/project/{ProjectName}/preference/{username}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectResponse> GetAsync(string projectName, CancellationToken cancellationToken)
    {
        return await GetAsJsonAsync<ProjectResponse>($"v1/project/{projectName}", cancellationToken)
            .ConfigureAwait(false);
    }
}
