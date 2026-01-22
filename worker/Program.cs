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

using Agenix.PlaywrightGrid.Shared.Logging;
using Serilog;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Infrastructure.Adapters;
using WorkerService.Services;
using WorkerService.Tools;

namespace WorkerService;

/// <summary>
///     The Program class serves as the entry point for the WorkerService application.
///     This class invokes the asynchronous execution of the service.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        // Load local .env variables for developer convenience (no-op if DISABLE_DOTENV=1)
        DotEnv.Load();

        // Ensure AGENIX_WORKER_NODE_ID is set for log file expansion (used in appsettings.json via %AGENIX_WORKER_NODE_ID%)
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGENIX_WORKER_NODE_ID")))
        {
            var nodeId = Environment.GetEnvironmentVariable("NODE_ID") ?? Environment.GetEnvironmentVariable("HOSTNAME");
            Environment.SetEnvironmentVariable("AGENIX_WORKER_NODE_ID",
                !string.IsNullOrWhiteSpace(nodeId) ? nodeId : Guid.NewGuid().ToString("N")[..8]);
        }

        // CLI subcommand: validate-pool-config [--pool "..."] [--json]
        if (args.Length > 0 && string.Equals(args[0], "validate-pool-config", StringComparison.OrdinalIgnoreCase))
        {
            var code = PoolConfigValidator.Run(args);
            Environment.ExitCode = code;
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateBootstrapLogger();

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        try
        {
            // Register ChunkedLogger options from configuration
            var config = builder.Configuration;
            var chunkedOptions = new ChunkedLoggerOptions
            {
                Enabled = config.GetValue("AGENIX_LOGGING_CHUNKED_ENABLED", true),
                MaxEventsPerChunk = config.GetValue("AGENIX_LOGGING_CHUNK_MAX_EVENTS", 1000),
                MaxAgeSeconds = config.GetValue("AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS", 60),
                EventCodePrefix = config.GetValue("AGENIX_LOGGING_EVENT_CODE_PREFIX", true),
                IncludeSourceLocation = config.GetValue("AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION", false)
            };
            builder.Services.AddSingleton(chunkedOptions);

            // Register generic ChunkedLogger<T> - this is the key pattern from hub
            builder.Services.AddTransient(typeof(ChunkedLogger<>));

            // Register HTTP client factory
            builder.Services.AddHttpClient("HubClient")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false
                });

            // Register worker services
            builder.Services.AddSingleton<WorkerOptions>(_ => WorkerOptions.FromEnvironment());
            builder.Services.AddSingleton<IHubClient, HubHttpClient>();
            builder.Services.AddSingleton<IMetricsPort, PrometheusMetrics>();
            builder.Services.AddSingleton<ISidecarLauncher, SidecarLauncher>();
            builder.Services.AddSingleton<IPlaywrightProtocolClientFactory, PlaywrightProtocolClientFactory>();

            // Build app to get service provider
            using var app = builder.Build();

            // Run worker
            await new WorkerServiceRunner(app.Services).RunAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Worker service terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
