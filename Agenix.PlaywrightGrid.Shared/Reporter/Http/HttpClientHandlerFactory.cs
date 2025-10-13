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

using System.Net;
using Agenix.PlaywrightGrid.Shared.Configuration;

namespace Agenix.PlaywrightGrid.Shared.Reporter.Http;

/// <summary>
///     Class to create <see cref="HttpClientHandler" /> instance based on <see cref="IConfiguration" /> object.
/// </summary>
public class HttpClientHandlerFactory
{
    /// <summary>
    ///     Creates an instance of <see cref="HttpClientHandlerFactory" /> class.
    /// </summary>
    /// <param name="configuration">Flatten configuration values.</param>
    public HttpClientHandlerFactory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Configuration = configuration;
    }

    /// <summary>
    ///     Flatten configuration values.
    /// </summary>
    protected IConfiguration Configuration { get; }

    /// <summary>
    ///     Parses all well-known configuration values and returns new instance of <see cref="HttpClientHandler" /> class.
    /// </summary>
    /// <returns></returns>
    public virtual HttpClientHandler Create()
    {
        var httpClientHandler = new HttpClientHandler
        {
            Proxy = GetProxy()
        };

        var ignoreSslErrors = Configuration.GetValue("Server:IgnoreSslErrors", false);

#if NET462
            if (ignoreSslErrors)
            {
                ServicePointManager.ServerCertificateValidationCallback +=
 (sender, cert, chain, sslPolicyErrors) => true;
            }
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#else
        if (ignoreSslErrors)
        {
            httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        }
#endif

        return httpClientHandler;
    }

    /// <summary>
    ///     Identify whether a proxy is configured.
    /// </summary>
    /// <returns>Object of <see cref="IWebProxy" />. Null if the proxy is not configured.</returns>
    protected virtual IWebProxy GetProxy()
    {
        WebProxy webProxy = null;

        var proxyUrl = Configuration.GetValue<string>("Server:Proxy:Url", null);

        if (proxyUrl != null)
        {
            webProxy = new WebProxy(proxyUrl);

            var username = Configuration.GetValue<string>("Server:Proxy:Username", null);

            if (username != null)
            {
                var password = Configuration.GetValue<string>("Server:Proxy:Password", null);

                var domain = Configuration.GetValue<string>("Server:Proxy:Domain", null);

                var credential = new NetworkCredential(username, password, domain);

                webProxy.Credentials = credential;
            }
        }

        return webProxy;
    }
}
