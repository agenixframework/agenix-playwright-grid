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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure;
using PlaywrightHub.Infrastructure.Adapters.Audit;
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
        // Apply environment-driven log levels (global + per-category overrides)
        LoggingConfigurator.ApplyFromEnvironment(builder.Logging, builder.Configuration);

        // Kestrel limits and request timeouts (env-driven)
        static long ClampLong(long value, long min, long max) => value < min ? min : (value > max ? max : value);
        static int ClampInt(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        var hubCfg = builder.Configuration;
        long TryGetLong(string key, long def) => long.TryParse(hubCfg[key], out var v) ? v : def;
        int TryGetInt(string key, int def) => int.TryParse(hubCfg[key], out var v) ? v : def;

        var controlLimitBytes = ClampLong(TryGetLong("HUB_MAX_CONTROL_BODY_BYTES", 64 * 1024), 8 * 1024, 1 * 1024 * 1024);
        var logLimitBytes = ClampLong(TryGetLong("HUB_MAX_LOG_BODY_BYTES", 1 * 1024 * 1024), 8 * 1024, 16 * 1024 * 1024);
        var globalMaxRequestBodyBytes = Math.Max(controlLimitBytes, logLimitBytes);

        var headersTimeoutSec = ClampInt(TryGetInt("HUB_REQUEST_HEADERS_TIMEOUT_SECONDS", 15), 5, 120);
        var keepAliveTimeoutSec = ClampInt(TryGetInt("HUB_KEEP_ALIVE_TIMEOUT_SECONDS", 30), 5, 300);
        var defaultRequestTimeoutSec = ClampInt(TryGetInt("HUB_REQUEST_TIMEOUT_SECONDS", 60), 5, 600);

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.AddServerHeader = false;
            o.Limits.MaxRequestBodySize = globalMaxRequestBodyBytes;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(headersTimeoutSec);
            o.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(keepAliveTimeoutSec);
        });

        builder.Services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(defaultRequestTimeoutSec)
            };
        });

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
        // Configure Redis connection with resilience and timeouts
        var redisOptions = ConfigurationOptions.Parse(redisUrl, true);
        redisOptions.AbortOnConnectFail = false; // keep retrying
        redisOptions.ConnectRetry = 3;
        redisOptions.KeepAlive = 15;
        int GetInt(string key, int def)
        {
            return int.TryParse(builder.Configuration[key], out var v) ? Math.Max(0, v) : def;
        }
        redisOptions.ConnectTimeout = GetInt("REDIS_CONNECT_TIMEOUT_MS", 5000);
        redisOptions.SyncTimeout = GetInt("REDIS_SYNC_TIMEOUT_MS", 5000);
        redisOptions.AsyncTimeout = GetInt("REDIS_ASYNC_TIMEOUT_MS", 5000);
        // Exponential reconnect backoff policy (includes jitter internally)
        redisOptions.ReconnectRetryPolicy = new ExponentialRetry(5000);

        var mux = await ConnectionMultiplexer.ConnectAsync(redisOptions);
        var db = mux.GetDatabase();

        // Services for dashboard integration
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => mux);
        builder.Services.AddSingleton<IDatabase>(_ => db);
        builder.Services.AddSingleton<IAuditStore, RedisAuditStore>();
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

        // Results store (configurable: memory (default), redis, or sqlite)
        var resultsBackend = builder.Configuration["HUB_RESULTS_BACKEND"] ?? "memory";
        var selectedResultsBackend = "memory (default)";
        if (string.Equals(resultsBackend, "redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IResultsStore, RedisResultsStore>();
            selectedResultsBackend = "redis";
        }
        else if (string.Equals(resultsBackend, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IResultsStore, SqliteResultsStore>();
            selectedResultsBackend = "sqlite";
        }
        else if (string.Equals(resultsBackend, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IResultsStore, PostgresResultsStore>();
            selectedResultsBackend = "postgres";
        }
        else
        {
            builder.Services.AddSingleton<IResultsStore, InMemoryResultsStore>();
            selectedResultsBackend = "memory (default)";
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

            // Include XML comments from the Hub assembly to enrich Swagger schema/operations docs
            try
            {
                var xmlFile = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".xml";
                var xmlPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, xmlFile);
                if (System.IO.File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
                }
            }
            catch
            {
                // best-effort only; ignore failures
            }
        });

        var app = builder.Build();

        // Initialize privacy/redaction settings (env-driven)
        HubPrivacy.Initialize(app.Configuration);

        // Apply request timeout middleware
        app.UseRequestTimeouts();

        // Enforce per-path request size limits (413) for known endpoints
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var isLogs = path.Contains("/commands", StringComparison.OrdinalIgnoreCase) ||
                             path.Contains("/api-logs", StringComparison.OrdinalIgnoreCase);
                var limit = isLogs ? logLimitBytes : controlLimitBytes;

                // Enforce per-request MaxRequestBodySize for streaming/chunked bodies (no Content-Length)
                var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is not null && !sizeFeature.IsReadOnly)
                {
                    sizeFeature.MaxRequestBodySize = limit;
                }

                var len = context.Request.ContentLength;
                if (len.HasValue && len.Value > limit)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    context.Response.ContentType = "application/problem+json";
                    var pd = new ProblemDetails
                    {
                        Status = StatusCodes.Status413PayloadTooLarge,
                        Title = "Payload Too Large",
                        Detail = $"Request body exceeds limit of {limit} bytes.",
                        Type = "https://httpstatuses.com/413",
                        Instance = context.Request.Path
                    };
                    await JsonSerializer.SerializeAsync(context.Response.Body, pd);
                    return;
                }
            }

            await next();
        });

        // Log selected results backend after app is built (logger available)
        app.Logger.LogInformation("[hub] ResultsStore backend: {Backend}", selectedResultsBackend);

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

            // Allow per-environment overrides via suffix, e.g., HUB_BORROW_WILDCARDS_Development
            static bool GetBoolWithEnvironmentOverride(IConfiguration cfg2, string key, string environment, bool defaultValue)
            {
                string? value = null;
                if (!string.IsNullOrWhiteSpace(environment))
                {
                    var suffixExact = $"_{environment}";
                    var suffixUpper = $"_{environment.ToUpperInvariant()}";
                    var k1 = key + suffixExact;
                    var k2 = key + suffixUpper;
                    var v1 = cfg2[k1];
                    var v2 = cfg2[k2];
                    if (!string.IsNullOrWhiteSpace(v1)) value = v1;
                    else if (!string.IsNullOrWhiteSpace(v2)) value = v2;
                }
                value ??= cfg2[key];
                return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
            }

            var environmentName = app.Environment.EnvironmentName ?? string.Empty;
            var enableTrailingFallback = GetBoolWithEnvironmentOverride(cfg, "HUB_BORROW_TRAILING_FALLBACK", environmentName, true); // default true
            var enablePrefixExpand = GetBoolWithEnvironmentOverride(cfg, "HUB_BORROW_PREFIX_EXPAND", environmentName, true); // default true
            var enableWildcards = GetBoolWithEnvironmentOverride(cfg, "HUB_BORROW_WILDCARDS", environmentName, false); // default false
            static string GetInformationalVersion(Type t)
            {
                try
                {
                    var asm = t.Assembly;
                    var aiv = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm);
                    return aiv?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
            var ver = GetInformationalVersion(typeof(HubServiceRunner));
            static string TruncVer(string? v)
            {
                const int max = 15; // "1.0.1-preview.3".Length
                if (string.IsNullOrEmpty(v)) return v ?? string.Empty;
                return v!.Length <= max ? v : v.Substring(0, max);
            }
            var verShort = TruncVer(ver);

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
                    Version = verShort
                },
                Workers = state.Workers,
                Now = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            app.Logger.LogInformation("[hub] Startup diagnostics:\n{Json}", json);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "[hub] Startup diagnostics failed");
        }

        await app.RunAsync();
    }
}
