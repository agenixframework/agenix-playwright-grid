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

namespace Agenix.PlaywrightGrid.Client.Resources;

internal class ServiceAsyncLaunchResource(HttpClient httpClient, string project)
    : ServiceBaseResource(httpClient, project), IAsyncLaunchResource
{
    public async Task<LaunchCreatedResponse> StartAsync(StartLaunchRequest request, CancellationToken cancellationToken)
    {
        return await PostAsJsonAsync<LaunchCreatedResponse, StartLaunchRequest>(
                $"v2/{ProjectName}/launch", request, "application/x.agenix.launch.v2+json", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LaunchFinishedResponse> FinishAsync(string uuid, FinishLaunchRequest request,
        CancellationToken cancellationToken)
    {
        return await PutAsJsonAsync<LaunchFinishedResponse, FinishLaunchRequest>(
            $"v2/{ProjectName}/launch/{uuid}/finish", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LaunchResponse> MergeAsync(MergeLaunchesRequest request, CancellationToken cancellationToken)
    {
        return await PostAsJsonAsync<LaunchResponse, MergeLaunchesRequest>(
                $"v2/{ProjectName}/launch/merge", request, "application/x.agenix.launch.v2+json", cancellationToken)
            .ConfigureAwait(false);
    }
}
