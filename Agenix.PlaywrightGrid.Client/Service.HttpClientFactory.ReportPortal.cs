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
using Agenix.PlaywrightGrid.Client.Extensions;
using IHttpClientFactory = Agenix.PlaywrightGrid.Client.IHttpClientFactory;
#if NET462
using System.Net;
#endif

namespace ReportPortal.Client;

partial class Service
{
    private class HttpClientFactory : IHttpClientFactory
    {
        private readonly Uri _baseUri;
        private readonly string _token;

        public HttpClientFactory(Uri baseUri, string token)
        {
            _baseUri = baseUri;
            _token = token;
        }

        public HttpClient Create()
        {
            var httpClientHandler = new HttpClientHandler();

#if !NET462
            httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                return true;
            };
#else
                ServicePointManager.ServerCertificateValidationCallback +=
 (sender, cert, chain, sslPolicyErrors) => true;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif

            var httpClient = new HttpClient(httpClientHandler);

            httpClient.BaseAddress = _baseUri.Normalize();

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + _token);
            httpClient.DefaultRequestHeaders.Add("User-Agent", ".NET Reporter");

            return httpClient;
        }
    }
}
