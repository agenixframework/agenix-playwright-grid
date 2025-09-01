#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.Background;
using PlaywrightHub.Infrastructure.Adapters.Redis;
using PlaywrightHub.Infrastructure.Adapters.Results;
using PlaywrightHub.Infrastructure.Web;
using Prometheus;
using StackExchange.Redis;

namespace PlaywrightHub.Services;

/// <summary>
///     Responsible for initializing and running the PlaywrightHub service,
///     including setting up logging, Redis connections, hosted services,
///     and ASP.NET Core application settings.
/// </summary>
public static class HubServiceRunner
{
    /// <summary>
    ///     Runs the Playwright Hub service with the specified configuration and services.
    /// </summary>
    /// <param name="args">An array of command-line arguments for configuring the application.</param>
    /// <returns>A task representing the asynchronous operation for running the service.</returns>
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        // Suppress verbose framework logs like OkObjectResult JSON writing and EndpointMiddleware exec logs
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

        // OpenTelemetry setup (env-driven exporters)
        var hubServiceName = "playwright-hub";
        var hubServiceVersion = typeof(HubServiceRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var enableOtlp = string.Equals(builder.Configuration["ENABLE_OTLP"], "1", StringComparison.OrdinalIgnoreCase);
        var enablePromOtel = string.Equals(builder.Configuration["ENABLE_PROMETHEUS_OTEL"], "1",
            StringComparison.OrdinalIgnoreCase);
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
        var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(hubServiceName, serviceVersion: hubServiceVersion);


        builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(hubServiceName, serviceVersion: hubServiceVersion))
            .WithTracing(t =>
            {
                t.SetResourceBuilder(resourceBuilder);
                t.AddAspNetCoreInstrumentation();
                t.AddHttpClientInstrumentation();
                if (enableOtlp)
                {
                    t.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                            ? OtlpExportProtocol.HttpProtobuf
                            : OtlpExportProtocol.Grpc;
                    });
                }
            })
            .WithMetrics(m =>
            {
                m.SetResourceBuilder(resourceBuilder);
                m.AddAspNetCoreInstrumentation();
                m.AddRuntimeInstrumentation();
                if (enableOtlp)
                {
                    m.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                            ? OtlpExportProtocol.HttpProtobuf
                            : OtlpExportProtocol.Grpc;
                    });
                }
            });


        var redisUrl = builder.Configuration["REDIS_URL"] ?? "redis:6379";
        var mux = await ConnectionMultiplexer.ConnectAsync(redisUrl);
        var db = mux.GetDatabase();

        // Services for dashboard integration
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => mux);
        builder.Services.AddSingleton<IDatabase>(_ => db);
        builder.Services.AddSingleton<IPoolStateReader, RedisPoolStateReader>();
        builder.Services.AddHostedService<RedisPoolStateBroadcastService>();
        builder.Services.AddHostedService<NodeSweeperService>();
        builder.Services.AddHostedService<BorrowTtlSweeperService>();
        builder.Services.AddHostedService<RunCleanupService>();

        // ProblemDetails for consistent error payloads
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                var traceId = ctx.HttpContext.TraceIdentifier;
                ctx.ProblemDetails.Extensions["traceId"] = traceId;
            };
        });

        // Results store (configurable: memory (default) or redis)
        var resultsBackend = builder.Configuration["HUB_RESULTS_BACKEND"] ?? "memory";
        if (string.Equals(resultsBackend, "redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IResultsStore, RedisResultsStore>();
            Console.WriteLine("[hub] ResultsStore backend: redis");
        }
        else
        {
            builder.Services.AddSingleton<IResultsStore, InMemoryResultsStore>();
            Console.WriteLine("[hub] ResultsStore backend: memory (default)");
        }

        // OpenAPI/Swagger (minimal surface)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1",
                new OpenApiInfo
                {
                    Title = "Playwright Grid Hub API",
                    Version = "v1",
                    Description = "Minimal API surface for borrowing and returning sessions."
                });

            var scheme = new OpenApiSecurityScheme
            {
                Name = "x-hub-secret",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Description =
                    "Shared secret header required for most endpoints (HUB_RUNNER_SECRET for runners, HUB_NODE_SECRET for nodes).",
                Reference = new OpenApiReference { Id = "HubSecret", Type = ReferenceType.SecurityScheme }
            };

            c.AddSecurityDefinition("HubSecret", scheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { { scheme, Array.Empty<string>() } });
        });

        var app = builder.Build();

        // Convert unhandled exceptions into RFC7807 ProblemDetails
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                var ex = feature?.Error;
                var status = 500;
                var pd = new ProblemDetails
                {
                    Status = status,
                    Title = "An unexpected error occurred.",
                    Detail = ex?.Message,
                    Type = "https://httpstatuses.com/500",
                    Instance = context.Request.Path
                };
                pd.Extensions["traceId"] = context.TraceIdentifier;
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/problem+json";
                await JsonSerializer.SerializeAsync(context.Response.Body, pd);
            });
        });

        // Normalize all 4xx/5xx responses to ProblemDetails and preserve any existing error text as detail
        app.Use(async (context, next) =>
        {
            var originalBody = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next();

                var status = context.Response.StatusCode;
                buffer.Position = 0;
                var existingBody = string.Empty;
                if (buffer.Length > 0)
                {
                    using var reader = new StreamReader(buffer, leaveOpen: true);
                    existingBody = await reader.ReadToEndAsync();
                    buffer.Position = 0;
                }

                var contentType = context.Response.ContentType ?? string.Empty;
                var isProblem = contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase);
                if (status >= 400 && !isProblem)
                {
                    // Build ProblemDetails with reason phrase and prior content as detail if present
                    var title = ReasonPhrases.GetReasonPhrase(status) ?? "Error";
                    var pd = new ProblemDetails
                    {
                        Status = status,
                        Title = title,
                        Detail = string.IsNullOrWhiteSpace(existingBody) ? null : existingBody,
                        Type = $"https://httpstatuses.com/{status}",
                        Instance = context.Request.Path
                    };
                    pd.Extensions["traceId"] = context.TraceIdentifier;

                    context.Response.ContentType = "application/problem+json";

                    // Replace body with ProblemDetails JSON
                    buffer.SetLength(0);
                    await JsonSerializer.SerializeAsync(buffer, pd);
                    buffer.Position = 0;
                }

                await buffer.CopyToAsync(originalBody);
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        });

        app.UseMetricServer();
        app.UseHttpMetrics();

        // Swagger middleware (enabled in Development or when HUB_SWAGGER=1)
        var enableSwagger = app.Environment.IsDevelopment() ||
                            string.Equals(app.Configuration["HUB_SWAGGER"], "1", StringComparison.OrdinalIgnoreCase);
        if (enableSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hub API v1");
                c.DocumentTitle = "Playwright Grid Hub API";
            });
        }

        app.MapHubEndpoints();

        // Startup diagnostics dump (effective config + labels per node)
        try
        {
            var cfg = app.Configuration;
            var nodeTimeoutSeconds = int.TryParse(cfg["HUB_NODE_TIMEOUT"], out var t) ? t : 60;
            var dashboardUrl = cfg["DASHBOARD_URL"] ?? "http://localhost:3001";
            var enableTrailingFallback =
                !bool.TryParse(cfg["HUB_BORROW_TRAILING_FALLBACK"], out var tf) || tf; // default true
            var enablePrefixExpand = !bool.TryParse(cfg["HUB_BORROW_PREFIX_EXPAND"], out var pe) || pe; // default true
            var enableWildcards = bool.TryParse(cfg["HUB_BORROW_WILDCARDS"], out var wc) && wc; // default false
            var ver = typeof(HubServiceRunner).Assembly.GetName().Version?.ToString() ?? string.Empty;

            var reader = app.Services.GetRequiredService<IPoolStateReader>();
            var state = await reader.GetStateAsync();

            var dto = new HubDiagnosticsDto
            {
                HubConfig = new HubEffectiveConfigDto
                {
                    RedisUrl = redisUrl,
                    BorrowTrailingFallback = enableTrailingFallback,
                    BorrowPrefixExpand = enablePrefixExpand,
                    BorrowWildcards = enableWildcards,
                    NodeTimeoutSeconds = nodeTimeoutSeconds,
                    DashboardUrl = dashboardUrl,
                    Version = ver
                },
                Workers = state.Workers,
                Now = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("[hub] Startup diagnostics:\n" + json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[hub] Startup diagnostics failed: {ex.Message}");
        }

        await app.RunAsync();
    }
}
