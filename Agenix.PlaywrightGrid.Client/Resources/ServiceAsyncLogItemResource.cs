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
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Client.Abstractions.Resources;
using Agenix.PlaywrightGrid.Client.Abstractions.Responses;
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Resources;

internal class ServiceAsyncLogItemResource(HttpClient httpClient, string project)
    : ServiceBaseResource(httpClient, project), ILogItemResource, IAsyncLogItemResource
{
    public async Task<LogItemCreatedResponse> CreateAsync(CreateLogItemRequest request,
        CancellationToken cancellationToken)
    {
        var uri = $"v1/{ProjectName}/log";

        if (request.Attach == null)
        {
            return await PostAsJsonAsync<LogItemCreatedResponse, CreateLogItemRequest>(uri, request, cancellationToken)
                .ConfigureAwait(false);
        }

        var results = await CreateAsync([request], cancellationToken).ConfigureAwait(false);
        return results.Responses.First();
    }

    /// <summary>
    ///     Creates log items using the provided request payloads and uploads any attached files if applicable.
    /// </summary>
    /// <param name="requests">
    ///     An array of <see cref="CreateLogItemRequest" /> containing the details of the log items to be
    ///     created.
    /// </param>
    /// <param name="cancellationToken">A cancellation token that can be used to observe the request operation.</param>
    /// <returns>
    ///     A <see cref="LogItemsCreatedResponse" /> containing the results of the created log items.
    /// </returns>
    public async Task<LogItemsCreatedResponse> CreateAsync(CreateLogItemRequest[] requests,
        CancellationToken cancellationToken)
    {
        var uri = $"v1/{ProjectName}/log";

        var multipartContent = new MultipartFormDataContent();

        using var memoryStream = new MemoryStream();
        await ModelSerializer.SerializeAsync<CreateLogItemRequest[]>(requests, memoryStream, cancellationToken)
            .ConfigureAwait(false);
        memoryStream.Seek(0, SeekOrigin.Begin);
        var httpContent = new StreamContent(memoryStream);
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        multipartContent.Add(httpContent, "json_request_part");

        foreach (var request in requests)
        {
            if (request.Attach != null)
            {
                var byteArrayContent = new ByteArrayContent(request.Attach.Data, 0, request.Attach.Data.Length);
                byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue(request.Attach.MimeType);
                multipartContent.Add(byteArrayContent, "file", request.Attach.Name);
            }
        }

        return await SendHttpRequestAsync<LogItemsCreatedResponse>(HttpMethod.Post, uri, multipartContent,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
