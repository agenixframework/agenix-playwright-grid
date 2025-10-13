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
using Agenix.PlaywrightGrid.Client.Converters;

namespace Agenix.PlaywrightGrid.Client.Resources;

internal abstract class ServiceBaseResource(HttpClient httpClient, string projectName)
{
    protected HttpClient HttpClient { get; } = httpClient;

    protected string ProjectName { get; } = projectName;

    protected Task<TResponse> GetAsJsonAsync<TResponse>(string uri, CancellationToken cancellationToken)
    {
        return SendAsJsonAsync<TResponse, object>(HttpMethod.Get, uri, null!, cancellationToken: cancellationToken);
    }

    protected Task<TResponse> GetAsJsonAsync<TResponse>(string uri, string accept, CancellationToken cancellationToken)
    {
        return SendAsJsonAsync<TResponse, object>(HttpMethod.Get, uri, null!, accept, cancellationToken);
    }

    protected Task<TResponse> PostAsJsonAsync<TResponse, TRequest>(
        string uri, TRequest request, CancellationToken cancellationToken)
    {
        return SendAsJsonAsync<TResponse, TRequest>(HttpMethod.Post, uri, request,
            cancellationToken: cancellationToken);
    }

    protected Task<TResponse> PostAsJsonAsync<TResponse, TRequest>(
        string uri, TRequest request, string accept, CancellationToken cancellationToken)
    {
        return SendAsJsonAsync<TResponse, TRequest>(HttpMethod.Post, uri, request, accept, cancellationToken);
    }

    protected Task<TResponse> PutAsJsonAsync<TResponse, TRequest>(
        string uri, TRequest request, CancellationToken cancellationToken)
    {
        return SendAsJsonAsync<TResponse, TRequest>(HttpMethod.Put, uri, request, cancellationToken: cancellationToken);
    }

    protected Task<TResponse> DeleteAsJsonAsync<TResponse>(string uri, CancellationToken cancellationToken)
    {
        return SendAsJsonAsync<TResponse, object>(HttpMethod.Delete, uri, null!, cancellationToken: cancellationToken);
    }

    private async Task<TResponse> SendAsJsonAsync<TResponse, TRequest>(
        HttpMethod httpMethod, string uri, TRequest request, string accept = "application/json",
        CancellationToken cancellationToken = default)
    {
        HttpContent httpContent = null!;

        if (request != null)
        {
            using var memoryStream = new MemoryStream();

            await ModelSerializer.SerializeAsync<TRequest>(request, memoryStream, cancellationToken)
                .ConfigureAwait(false);
            memoryStream.Seek(0, SeekOrigin.Begin);
            httpContent = new StreamContent(memoryStream);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return await SendHttpRequestAsync<TResponse>(httpMethod, uri, httpContent, accept, cancellationToken)
                .ConfigureAwait(false);
        }

        return await SendHttpRequestAsync<TResponse>(httpMethod, uri, httpContent, accept, cancellationToken)
            .ConfigureAwait(false);
    }

    protected async Task<TResponse> SendHttpRequestAsync<TResponse>(
        HttpMethod httpMethod, string uri, HttpContent httpContent, string accept = "application/json",
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(httpMethod, uri);

        using (httpContent)
        {
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            httpRequest.Content = httpContent;

            using var response = await HttpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            CheckSuccessStatusCode(response, stream);

            return await ModelSerializer.DeserializeAsync<TResponse>(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async Task<byte[]> GetAsBytesAsync(string uri, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);

        using var response = await HttpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        CheckSuccessStatusCode(response, stream);

        using var memoryStream = new MemoryStream();

        await stream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }

    private static void CheckSuccessStatusCode(HttpResponseMessage response, Stream stream)
    {
        if (!response.IsSuccessStatusCode)
        {
            using var reader = new StreamReader(stream);
            var responseBody = reader.ReadToEnd();

            throw new ServiceException(
                "Response status code does not indicate success.",
                response.StatusCode,
                response.RequestMessage!.RequestUri,
                response.RequestMessage.Method,
                responseBody);
        }
    }
}
