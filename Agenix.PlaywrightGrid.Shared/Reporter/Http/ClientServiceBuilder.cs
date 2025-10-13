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

using Agenix.PlaywrightGrid.Shared.Configuration;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions;

namespace Agenix.PlaywrightGrid.Shared.Reporter.Http;

/// <summary>
///     Builder for <see cref="IClientService" /> instance with configuration.
/// </summary>
/// <remarks>
///     Constructor to create an instance of <see cref="ClientServiceBuilder" /> class.
/// </remarks>
/// <param name="configuration">Well-known list of properties.</param>
public class ClientServiceBuilder(IConfiguration configuration)
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    private HttpClientFactory _httpClientFactory;

    private HttpClientHandlerFactory _httpClientHandlerFactory;

    private static ITraceLogger TraceLogger => TraceLogManager.Instance.GetLogger<ClientServiceBuilder>();

    /// <summary>
    ///     Sets <see cref="HttpClientHandlerFactory" /> instance to be used for building Web API client.
    /// </summary>
    /// <param name="httpClientHandlerFactory"></param>
    /// <returns></returns>
    public ClientServiceBuilder UseHttpClientHandlerFactory(HttpClientHandlerFactory httpClientHandlerFactory)
    {
        _httpClientHandlerFactory = httpClientHandlerFactory;

        return this;
    }

    /// <summary>
    ///     Sets <see cref="HttpClientFactory" /> instance to be used for building Web API client.
    /// </summary>
    /// <param name="httpClientFactory"></param>
    /// <returns></returns>
    public ClientServiceBuilder UseHttpClientFactory(HttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        return this;
    }

    /// <summary>
    ///     Parses configuration and builds an instance of <see cref="IClientService" />.
    /// </summary>
    /// <returns>Client to interact with Web API.</returns>
    public IClientService Build()
    {
        var url = _configuration.GetValue<string>(ConfigurationPath.ServerUrl);

        var project = _configuration.GetValue<string>(ConfigurationPath.ServerProject);

        var apiKey = _configuration.GetValue<string>(ConfigurationPath.ServerAuthenticationKey, null);
        if (apiKey is null)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            apiKey = _configuration.GetValue<string>(ConfigurationPath.ServerAuthenticationUuid, null);
#pragma warning restore CS0618 // Type or member is obsolete
            if (apiKey is null)
            {
                // Trigger proper exception throwing or use 'null'.
                apiKey = _configuration.GetValue<string>(ConfigurationPath.ServerAuthenticationKey);
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

        _httpClientHandlerFactory ??= new HttpClientHandlerFactory(_configuration);

        _httpClientFactory ??= new HttpClientFactory(_configuration, _httpClientHandlerFactory.Create());

        IClientService service = new Service(new Uri(url), project, apiKey, _httpClientFactory);

        return service;
    }
}
