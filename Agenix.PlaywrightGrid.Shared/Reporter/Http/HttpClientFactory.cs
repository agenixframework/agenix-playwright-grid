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
using Agenix.PlaywrightGrid.Shared.Configuration;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using IHttpClientFactory = Agenix.PlaywrightGrid.Client.IHttpClientFactory;

namespace Agenix.PlaywrightGrid.Shared.Reporter.Http;

/// <summary>
///     Class to create <see cref="HttpClient" /> instance based on <see cref="IConfiguration" /> object.
/// </summary>
public class HttpClientFactory : IHttpClientFactory
{
    /// <summary>
    ///     Creates an instance of <see cref="HttpClientFactory" /> class.
    /// </summary>
    /// <param name="configuration">Flatten configuration values.</param>
    /// <param name="httpClientHandler">Inner <see cref="HttpClientHandler" /> to use by <see cref="HttpClient" />.</param>
    public HttpClientFactory(IConfiguration configuration, HttpClientHandler httpClientHandler)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ArgumentNullException.ThrowIfNull(httpClientHandler);

        Configuration = configuration;
        HttpClientHandler = httpClientHandler;
    }

    private static ITraceLogger TraceLogger => TraceLogManager.Instance.GetLogger<HttpClientFactory>();

    /// <summary>
    ///     Flatten configuration values.
    /// </summary>
    protected IConfiguration Configuration { get; }

    /// <summary>
    ///     Inner http client handler to use.
    /// </summary>
    protected HttpClientHandler HttpClientHandler { get; }

    /// <summary>
    ///     Parses all well-known configuration values and returns new instance of <see cref="HttpClient" /> class.
    /// </summary>
    /// <returns></returns>
    public virtual HttpClient Create()
    {
        var httpClient = new HttpClient(HttpClientHandler);

        var url = Configuration.GetValue<string>(ConfigurationPath.ServerUrl);

        var apiKey = Configuration.GetValue<string>(ConfigurationPath.ServerAuthenticationKey, null);
        if (apiKey is null)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            apiKey = Configuration.GetValue<string>(ConfigurationPath.ServerAuthenticationUuid, null);
#pragma warning restore CS0618 // Type or member is obsolete
            if (apiKey is null)
            {
                // Trigger proper exception throwing or use 'null'.
                apiKey = Configuration.GetValue<string>(ConfigurationPath.ServerAuthenticationKey);
            }
            else
            {
#pragma warning disable CS0618 // Type or member is obsolete
                TraceLogger.Warn(
                    $"Configuration parameter '${ConfigurationPath.ServerAuthenticationUuid}' is deprecated. " +
                    $"Use '${ConfigurationPath.ServerAuthenticationKey}' instead.");
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        httpClient.BaseAddress = new Uri(url);

        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Agenix .NET Reporter");

        var timeout = GetTimeout();
        if (timeout.HasValue)
        {
            httpClient.Timeout = timeout.Value;
        }

        return httpClient;
    }

    /// <summary>
    ///     Parses timeout in configuration (in seconds).
    /// </summary>
    /// <returns></returns>
    protected virtual TimeSpan? GetTimeout()
    {
        TimeSpan? timeout = null;

        var seconds = Configuration.GetValue("Server:Timeout", double.NaN);

        if (!double.IsNaN(seconds))
        {
            timeout = TimeSpan.FromSeconds(seconds);
        }

        return timeout;
    }
}
