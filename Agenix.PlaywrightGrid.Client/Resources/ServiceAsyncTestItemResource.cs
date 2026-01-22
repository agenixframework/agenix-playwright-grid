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

/// <summary>
///     Provides asynchronous operations for managing test items in the Playwright Grid context.
///     This resource handles creation, updating, and completion of test item entities associated with a specified project.
/// </summary>
/// <remarks>
///     Inherits from <see cref="ServiceBaseResource" /> and implements <see cref="IAsyncTestItemResource" /> to provide
///     the necessary functionality for interaction with test-item-related endpoints on the server.
/// </remarks>
/// <example>
///     Not applicable per request.
/// </example>
internal class ServiceAsyncTestItemResource(HttpClient httpClient, string project)
    : ServiceBaseResource(httpClient, project), IAsyncTestItemResource
{
    /// <summary>
    ///     Asynchronously starts a test item in the specified project using the provided request.
    /// </summary>
    /// <param name="request">The request object containing details necessary to start the test item.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the request.</param>
    /// <returns>
    ///     A task representing the asynchronous operation, which upon completion contains the response with details of
    ///     the created test item.
    /// </returns>
    public async Task<TestItemCreatedResponse> StartAsync(StartTestItemRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsJsonAsync<TestItemCreatedResponse, StartTestItemRequest>(
            $"v2/{ProjectName}/item", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Asynchronously starts a test item within the specified project using the given unique identifier and request
    ///     details.
    /// </summary>
    /// <param name="uuid">The unique identifier of the test item to be started.</param>
    /// <param name="request">The request object containing data required to initialize the test item.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation if needed.</param>
    /// <returns>
    ///     A task representing the asynchronous operation, which upon completion contains the response with details of
    ///     the created test item.
    /// </returns>
    public async Task<TestItemCreatedResponse> StartAsync(string uuid, StartTestItemRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsJsonAsync<TestItemCreatedResponse, StartTestItemRequest>(
            $"v2/{ProjectName}/item/{uuid}", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Asynchronously finishes a test item in the specified project using the provided request and unique identifier.
    /// </summary>
    /// <param name="uuid">The unique identifier of the test item to be finished.</param>
    /// <param name="request">The request object containing details necessary to finish the test item.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the request.</param>
    /// <returns>
    ///     A task representing the asynchronous operation, which upon completion contains the response message regarding
    ///     the completion status of the test item.
    /// </returns>
    public async Task<MessageResponse> FinishAsync(string uuid, FinishTestItemRequest request,
        CancellationToken cancellationToken)
    {
        return await PutAsJsonAsync<MessageResponse, FinishTestItemRequest>(
            $"v2/{ProjectName}/item/{uuid}", request, cancellationToken).ConfigureAwait(false);
    }
}
