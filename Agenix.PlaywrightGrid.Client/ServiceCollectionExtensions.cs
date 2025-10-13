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
using Agenix.PlaywrightGrid.Client.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agenix.PlaywrightGrid.Client;

/// <summary>
///     Extension methods for registering Playwright Grid Client services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds Playwright Grid Client services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlaywrightGridClient(
        this IServiceCollection services,
        Action<PlaywrightGridClientOptions> configureOptions)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions == null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        services.Configure(configureOptions);
        RegisterClientServices(services);

        return services;
    }

    /// <summary>
    ///     Adds Playwright Grid Client services to the service collection using configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration section containing client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlaywrightGridClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.Configure<PlaywrightGridClientOptions>(configuration);
        RegisterClientServices(services);

        return services;
    }

    /// <summary>
    ///     Adds Playwright Grid Client services to the service collection with direct options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">Pre-configured client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlaywrightGridClient(
        this IServiceCollection services,
        PlaywrightGridClientOptions options)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();
        services.AddSingleton(Options.Create(options));
        RegisterClientServices(services);

        return services;
    }

    private static void RegisterClientServices(IServiceCollection services)
    {
        // Register HttpClient with retry policy
        var httpClientBuilder = services.AddHttpClient<IClientService, Service>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PlaywrightGridClientOptions>>().Value;
                options.Validate();

                httpClient.BaseAddress = options.BaseUri;
                httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }

                httpClient.DefaultRequestHeaders.Add("X-Project-Key", options.ProjectKey);
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PlaywrightGridClientOptions>>().Value;
                return new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = options.MaxConcurrentRequests
                };
            });

        // Add standard resilience handler with retry configuration
        httpClientBuilder.AddStandardResilienceHandler();

        // Register service factory
        services.AddSingleton<IClientService>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PlaywrightGridClientOptions>>().Value;
            var httpClientFactory = serviceProvider.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();

            return new Service(options.BaseUri, options.ProjectKey, options.ApiKey, httpClient);
        });
    }
}
