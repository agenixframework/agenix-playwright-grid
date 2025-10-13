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

using System.Reflection;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure;
using PlaywrightHub.Infrastructure.Adapters.Admin;
using PlaywrightHub.Infrastructure.Adapters.Audit;
using PlaywrightHub.Infrastructure.Adapters.Background;
using PlaywrightHub.Infrastructure.Adapters.Messaging;
using PlaywrightHub.Infrastructure.Adapters.Redis;
using PlaywrightHub.Infrastructure.Adapters.Results;
using PlaywrightHub.Infrastructure.Caching;
using PlaywrightHub.Infrastructure.Metrics;
using PlaywrightHub.Infrastructure.Services;
using PlaywrightHub.Infrastructure.Web;
using PlaywrightHub.Infrastructure.Web.Middleware;
using Prometheus;
using Serilog;
using Serilog.Extensions.Logging;
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

        // Ensure only Serilog is used for logging to prevent duplication and interleaving
        builder.Logging.ClearProviders();

        // Configure Serilog early for startup logging
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateBootstrapLogger();

        // Configure Serilog for the host
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        try
        {
            // Use bootstrap logger for startup operation to avoid redundant sink instances
            var startupLogger = new ChunkedLogger(new SerilogLoggerFactory(Log.Logger).CreateLogger("hub"), "hub");
            using var startupOp = startupLogger.BeginOperation("Startup");

            // Kestrel limits and request timeouts (env-driven)
            static long ClampLong(long value, long min, long max)
            {
                return value < min ? min : value > max ? max : value;
            }

            static int ClampInt(int value, int min, int max)
            {
                return value < min ? min : value > max ? max : value;
            }

            var hubCfg = builder.Configuration;

            long TryGetLong(string key, long def)
            {
                return long.TryParse(hubCfg[key], out var v) ? v : def;
            }

            int TryGetInt(string key, int def)
            {
                return int.TryParse(hubCfg[key], out var v) ? v : def;
            }

            var controlLimitBytes =
                ClampLong(TryGetLong("AGENIX_HUB_MAX_CONTROL_BODY_BYTES", 64 * 1024), 8 * 1024, 1 * 1024 * 1024);
            var logLimitBytes =
                ClampLong(TryGetLong("AGENIX_HUB_MAX_LOG_BODY_BYTES", 1 * 1024 * 1024), 8 * 1024, 16 * 1024 * 1024);
            var globalMaxRequestBodyBytes = Math.Max(controlLimitBytes, logLimitBytes);

            var headersTimeoutSec = ClampInt(TryGetInt("AGENIX_HUB_REQUEST_HEADERS_TIMEOUT_SECONDS", 15), 5, 120);
            var keepAliveTimeoutSec = ClampInt(TryGetInt("AGENIX_HUB_KEEP_ALIVE_TIMEOUT_SECONDS", 30), 5, 300);
            var defaultRequestTimeoutSec = ClampInt(TryGetInt("AGENIX_HUB_REQUEST_TIMEOUT_SECONDS", 60), 5, 600);

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

            // Configure JSON serialization options for case-insensitive deserialization
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
            });

            // OpenTelemetry setup (env-driven exporters)
            const string hubServiceName = "agenix-test-platform";
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
                    // Subscribe to custom ActivitySources used in the Hub (admin operations, etc.)
                    t.AddSource("agenix-test-platform.admin");
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

            // Redis topology and TLS options (standalone/cluster/sentinel)
            static bool IsTrue(string? v)
            {
                return !string.IsNullOrEmpty(v) && (string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase));
            }

            var mode = builder.Configuration["REDIS_MODE"]?.Trim().ToLowerInvariant(); // standalone|cluster|sentinel
            var useCluster = IsTrue(builder.Configuration["REDIS_USE_CLUSTER"]) || mode == "cluster";
            var useSentinel = IsTrue(builder.Configuration["REDIS_USE_SENTINEL"]) || mode == "sentinel";
            if (useCluster && useSentinel)
            {
                // Prefer sentinel if both are set; log a warning later after logging is initialized
                useCluster = false;
            }

            // Helper to apply standard timeouts and resilience
            ConfigurationOptions ApplyCommon(ConfigurationOptions opts)
            {
                opts.AbortOnConnectFail = false; // keep retrying
                opts.ConnectRetry = 3;
                opts.KeepAlive = 15;

                int GetInt(string key, int def)
                {
                    return int.TryParse(builder.Configuration[key], out var v) ? Math.Max(0, v) : def;
                }

                opts.ConnectTimeout = GetInt("REDIS_CONNECT_TIMEOUT_MS", 5000);
                opts.SyncTimeout = GetInt("REDIS_SYNC_TIMEOUT_MS", 5000);
                opts.AsyncTimeout = GetInt("REDIS_ASYNC_TIMEOUT_MS", 5000);
                // Exponential reconnect backoff policy (includes jitter internally)
                opts.ReconnectRetryPolicy = new ExponentialRetry(5000);

                // TLS support: honor rediss:// or explicit REDIS_SSL=1 and optional REDIS_SSL_HOST
                var sslExplicit = IsTrue(builder.Configuration["REDIS_SSL"]);
                var isRediss = redisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);
                if (sslExplicit || isRediss)
                {
                    opts.Ssl = true;
                    var sslHost = builder.Configuration["REDIS_SSL_HOST"];
                    if (!string.IsNullOrWhiteSpace(sslHost))
                    {
                        opts.SslHost = sslHost;
                    }
                }

                return opts;
            }

            IDatabase db;
            IConnectionMultiplexer mux;

            if (useSentinel)
            {
                var sentinels = builder.Configuration["REDIS_SENTINELS"]; // comma-separated host:port
                var masterName = builder.Configuration["REDIS_SENTINEL_MASTER"];
                if (string.IsNullOrWhiteSpace(sentinels) || string.IsNullOrWhiteSpace(masterName))
                {
                    throw new InvalidOperationException(
                        "REDIS_MODE=sentinel requires REDIS_SENTINELS and REDIS_SENTINEL_MASTER to be set.");
                }

                var sentinelOptions = new ConfigurationOptions { CommandMap = CommandMap.Sentinel };
                foreach (var ep in sentinels.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    sentinelOptions.EndPoints.Add(ep.Trim());
                }

                ApplyCommon(sentinelOptions);
                // Optional auth for sentinel endpoints
                var sentinelPassword = builder.Configuration["REDIS_SENTINEL_PASSWORD"];
                if (!string.IsNullOrEmpty(sentinelPassword))
                {
                    sentinelOptions.Password = sentinelPassword;
                }

                await using var sentinelMux = await ConnectionMultiplexer.ConnectAsync(sentinelOptions);
                var sentinelEndpoints = sentinelMux.GetEndPoints();
                if (sentinelEndpoints.Length == 0)
                {
                    throw new InvalidOperationException("No reachable Sentinel endpoints.");
                }

                var sentinelServer = sentinelMux.GetServer(sentinelEndpoints[0]);
                var masterEp = await sentinelServer.SentinelGetMasterAddressByNameAsync(masterName);
                if (masterEp is null)
                {
                    throw new InvalidOperationException($"Sentinel did not return a master for '{masterName}'.");
                }

                // Parse base options for db/user/pass from REDIS_URL if provided
                var baseOptions = ConfigurationOptions.Parse(redisUrl, true);
                var finalOptions = new ConfigurationOptions();
                finalOptions.EndPoints.Add(masterEp);
                finalOptions.User = baseOptions.User;
                finalOptions.Password = baseOptions.Password;
                finalOptions.DefaultDatabase = baseOptions.DefaultDatabase;
                ApplyCommon(finalOptions);

                mux = await ConnectionMultiplexer.ConnectAsync(finalOptions);
                db = mux.GetDatabase();
            }
            else
            {
                // Standalone or cluster: allow multiple endpoints in REDIS_URL for cluster
                var options = ConfigurationOptions.Parse(redisUrl, true);
                ApplyCommon(options);
                // No special flags needed for cluster; StackExchange.Redis auto-discovers topology from multiple endpoints
                mux = await ConnectionMultiplexer.ConnectAsync(options);
                db = mux.GetDatabase();
            }

            // Services for dashboard integration
            builder.Services.AddSignalR();
            builder.Services.AddResponseCaching(); // For artifact Cache-Control headers

            // Error handling and EventCode resolution for standardized ProblemDetails
            builder.Services.AddSingleton<IEventCodeResolver, EventCodeResolver>();

            builder.Services.AddSingleton<IConnectionMultiplexer>(_ => mux);
            builder.Services.AddSingleton<IDatabase>(_ => db);

            // Audit system: Event-driven architecture (RabbitMQ → Ingestion service)
            builder.Services.AddSingleton<IAuditStore, AuditEventPublisher>();
            startupLogger.LogMilestone(EventCodes.Generic, "Audit configured: Event-driven (RabbitMQ → Ingestion service)");

            // Register PostgresAuditStore separately for audit query endpoints (read operations)
            builder.Services.AddSingleton<PostgresAuditStore>();
            startupLogger.LogMilestone(EventCodes.Generic, "PostgresAuditStore registered for audit queries");

            builder.Services.AddSingleton<IPoolStateReader, RedisPoolStateReader>();
            builder.Services.AddSingleton<IEmailService, EmailService>();
            builder.Services.AddSingleton<IBrowserPoolService, BrowserPoolService>();
            builder.Services.AddSingleton<IApiKeyAuthenticationService, ApiKeyAuthenticationService>();
            builder.Services.AddSingleton<ICacheInvalidationOutbox, PostgresCacheInvalidationOutbox>();

            // Artifact caching service (Redis-based distributed cache)
            builder.Services.AddSingleton<RedisArtifactCache>(sp =>
            {
                var redis = sp.GetRequiredService<IDatabase>();
                var logger = sp.GetRequiredService<ILogger<RedisArtifactCache>>();
                var config = sp.GetRequiredService<IConfiguration>();

                var maxContentSizeMB = config.GetValue("AGENIX_ARTIFACTS_CACHE_MAX_CONTENT_SIZE_MB", 5);
                var maxContentSizeBytes = maxContentSizeMB * 1024 * 1024;
                var contentTtl = config.GetValue("AGENIX_ARTIFACTS_CACHE_CONTENT_TTL_SECONDS", 3600);
                var metadataTtl = config.GetValue("AGENIX_ARTIFACTS_CACHE_METADATA_TTL_SECONDS", 3600);
                var presignedUrlTtl = config.GetValue("AGENIX_ARTIFACTS_CACHE_PRESIGNED_URL_TTL_SECONDS", 3000);
                var compressionEnabled = config.GetValue("AGENIX_ARTIFACTS_CACHE_COMPRESSION_ENABLED", true);

                return new RedisArtifactCache(
                    redis,
                    logger,
                    maxContentSizeBytes,
                    contentTtl,
                    metadataTtl,
                    presignedUrlTtl,
                    compressionEnabled
                );
            });

            // Register Artifact Prefetch Service (optional Redis dependency)
            builder.Services.AddSingleton<IArtifactPrefetchService>(sp =>
            {
                var store = sp.GetRequiredService<IResultsStore>();
                var cache = sp.GetService<RedisArtifactCache>(); // May be null if Redis disabled
                var minioStorage = sp.GetService<MinioStorageService>(); // May be null if using local storage
                var config = sp.GetRequiredService<IConfiguration>();
                var logger = sp.GetRequiredService<ILogger<ArtifactPrefetchService>>();
                var redis = sp.GetService<IDatabase>(); // May be null if Redis disabled

                var enabled = config.GetValue("AGENIX_ARTIFACTS_PREFETCH_ENABLED", true);
                var maxConcurrency = config.GetValue("AGENIX_ARTIFACTS_PREFETCH_MAX_CONCURRENCY", 5);
                var maxPerItem = config.GetValue("AGENIX_ARTIFACTS_PREFETCH_MAX_PER_ITEM", 10);

                return new ArtifactPrefetchService(
                    store,
                    cache,
                    minioStorage,
                    config,
                    logger,
                    redis,
                    enabled,
                    maxConcurrency,
                    maxPerItem
                );
            });

            // Register IHttpClientFactory for WorkerOrphanDetector and other services
            builder.Services.AddHttpClient();

            builder.Services.AddHostedService<RedisPoolStateBroadcastService>();
            builder.Services.AddHostedService<NodeSweeperService>();
            builder.Services.AddHostedService<BorrowTtlSweeperService>();
            builder.Services.AddHostedService<BrowserAutoStopService>();
            builder.Services.AddHostedService<WorkerOrphanDetector>();
            builder.Services.AddHostedService<CacheInvalidationWorker>();
            // NOTE: LaunchAutoStopService moved to housekeeping-service (2025-12-07)

            // ProblemDetails for consistent error payloads
            builder.Services.AddProblemDetails(options =>
            {
                options.CustomizeProblemDetails = ctx =>
                {
                    var traceId = ctx.HttpContext.TraceIdentifier;
                    ctx.ProblemDetails.Extensions["traceId"] = traceId;
                };
            });

            // Results store (PostgresSQL - always used, no backend selection)
            var postgresConnString = builder.Configuration["POSTGRES_CONNECTION_STRING"];
            if (string.IsNullOrEmpty(postgresConnString))
            {
                throw new InvalidOperationException("POSTGRES_CONNECTION_STRING environment variable is required");
            }

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgresConnString);
            var dataSource = dataSourceBuilder.Build();
            builder.Services.AddSingleton(dataSource);
            builder.Services.AddSingleton<IResultsStore, PostgresResultsStore>();

            // Event Publisher: Always use RabbitMQ for async processing
            var rabbitUrl = builder.Configuration["RABBITMQ_URL"];
            if (string.IsNullOrWhiteSpace(rabbitUrl))
            {
                throw new InvalidOperationException("RABBITMQ_URL environment variable is required");
            }

            builder.Services.AddSingleton<IEventPublisher>(sp =>
                new RabbitMqEventPublisher(
                    rabbitUrl,
                    sp.GetRequiredService<ILogger<RabbitMqEventPublisher>>()));
            startupLogger.LogMilestone(EventCodes.Generic, "Event publisher configured: RabbitMQ ({Url})", rabbitUrl);

            // Register MinIO storage service (optional, for S3-compatible artifact storage)
            var storageBackend = builder.Configuration.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");
            if (storageBackend == "minio")
            {
                builder.Services.AddSingleton<MinioStorageService>();
            }

            // Register TestRunMetrics for OpenTelemetry metrics
            builder.Services.AddSingleton<TestRunMetrics>();

            // Add memory cache for test item caching (Phase 5 enhancements)
            builder.Services.AddMemoryCache(options =>
            {
                options.SizeLimit = 100; // Max 100 cached items
                options.CompactionPercentage = 0.25; // Compact 25% when limit reached
                options.ExpirationScanFrequency = TimeSpan.FromMinutes(5); // Scan every 5 minutes
            });

            // Add test item cache singleton (Phase 5 enhancements)
            builder.Services.AddSingleton<TestItemCache>();

            // Register ChunkedLogger options and the logger itself
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

            // Register ILogger (non-generic) as a factory that creates a logger for the requesting type
            // This is needed because ChunkedLogger<T> base class constructor expects ILogger (non-generic)
            builder.Services.AddTransient(typeof(Microsoft.Extensions.Logging.ILogger), sp =>
            {
                var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
                return loggerFactory.CreateLogger("Default");
            });

            // Register generic ChunkedLogger<T> with automatic constructor resolution
            // The DI container will automatically inject ILogger<T> (from Serilog) and ChunkedLoggerOptions (registered above)
            builder.Services.AddTransient(typeof(ChunkedLogger<>));

            // Register ChunkedLogger for operation-based logging with visual chunks
            builder.Services.AddScoped(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ChunkedLogger>>();
                return new ChunkedLogger(logger, "agenix-test-platform", chunkedOptions);
            });

            // Run database migrations once at startup (before any services are initialized)
            await DbUpMigrations.ApplyAsync(postgresConnString, CancellationToken.None);

            // Admin durable store (PostgresSQL mirror - enabled by default)
            var adminMirrorRaw = builder.Configuration["AGENIX_HUB_ADMIN_MIRROR"];
            var adminMirrorEnabled = string.IsNullOrWhiteSpace(adminMirrorRaw) ||
                                     string.Equals(adminMirrorRaw, "1", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(adminMirrorRaw, "true", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(adminMirrorRaw, "yes", StringComparison.OrdinalIgnoreCase);

            if (adminMirrorEnabled)
            {
                builder.Services.AddSingleton<IAdminDurableStore, PostgresAdminStore>();
            }

            // OpenAPI/Swagger (minimal surface)
            builder.Services.AddEndpointsApiExplorer();

            var app = builder.Build();

            // Initialize MinIO bucket if MinIO storage is enabled
            if (storageBackend == "minio")
            {
                try
                {
                    var minioService = app.Services.GetRequiredService<MinioStorageService>();
                    await minioService.EnsureBucketExistsAsync();
                    startupLogger.LogMilestone(EventCodes.Generic, "MinIO bucket verified/created successfully");
                }
                catch (Exception ex)
                {
                    startupLogger.LogError(ex, EventCodes.Generic,
                        "Failed to initialize MinIO bucket - continuing with degraded functionality");
                }
            }

            // Initialize privacy/redaction settings (env-driven)
            HubPrivacy.Initialize(app.Configuration);

            // Apply request timeout middleware
            app.UseRequestTimeouts();

            // Response caching for artifact endpoints (browser-level caching via Cache-Control headers)
            app.UseResponseCaching();

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
                    if (len > limit)
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

            // Log results backend
            startupLogger.LogMilestone(EventCodes.Generic, "ResultsStore backend: PostgresSQL");
            startupLogger.LogMilestone(EventCodes.Generic, "Admin durable store: {Status}",
                adminMirrorEnabled ? "PostgresSQL (enabled)" : "disabled");

            // Log event publishing configuration (always enabled)
            startupLogger.LogMilestone(EventCodes.Generic, "Event publishing: enabled (RabbitMQ: {Url})", rabbitUrl);

            if (!adminMirrorEnabled)
            {
                startupLogger.LogMilestone(EventCodes.Generic, "Admin durable mirroring disabled via HUB_ADMIN_MIRROR={Value}",
                    adminMirrorRaw ?? "(empty)");
            }

            // Convert unhandled exceptions into RFC7807 ProblemDetails
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var feature = context.Features.Get<IExceptionHandlerFeature>();
                    var ex = feature?.Error;
                    const int status = 500;

                    // Log the full exception for debugging
                    var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("UnhandledExceptions");
                    logger.LogError(ex, "Unhandled exception in request {Method} {Path}", context.Request.Method,
                        context.Request.Path);

                    var pd = new ProblemDetails
                    {
                        Status = status,
                        Title = "An unexpected error occurred.",
                        Detail = ex?.Message,
                        Type = "https://httpstatuses.com/500",
                        Instance = context.Request.Path,
                        Extensions = { ["traceId"] = context.TraceIdentifier }
                    };
                    context.Response.StatusCode = status;
                    context.Response.ContentType = "application/problem+json";
                    context.Response.ContentLength = null;
                    await JsonSerializer.SerializeAsync(context.Response.Body, pd);
                });
            });

            // Normalize all 4xx/5xx responses to ProblemDetails and preserve any existing error text as detail
            app.Use(async (context, next) =>
            {
                var eventCodeResolver = context.RequestServices.GetRequiredService<IEventCodeResolver>();
                var chunkedLogger = context.RequestServices.GetRequiredService<ChunkedLogger>();

                // Register event code feature to share resolved code with other middleware (e.g., OperationLogging)
                var eventCodeFeature = new EventCodeFeature();
                context.Features.Set<IEventCodeFeature>(eventCodeFeature);

                var originalBody = context.Response.Body;
                await using var bufferingStream = new AutoFlushingBufferStream(originalBody, context);
                context.Response.Body = bufferingStream;

                try
                {
                    await next();

                    bufferingStream.EnsureInitialized();

                    if (bufferingStream.IsBuffered)
                    {
                        // If we buffered the response, the original Content-Length (if any) is no longer valid
                        // because we might change the body or normalization might alter its size.
                        // Clearing it forces chunked encoding which is safer when the body is intercepted/modified.
                        context.Response.ContentLength = null;

                        var status = context.Response.StatusCode;
                        var buffer = bufferingStream.Buffer!;
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

                        if (status >= 400)
                        {
                            // Get event code from resolver
                            var eventCode = eventCodeResolver.ResolveEventCodeFromStatus(status, context);
                            eventCodeFeature.EventCode = eventCode;

                            if (!isProblem)
                            {
                                ProblemDetails pd;

                                // Try to extract validation errors from the existing body
                                Dictionary<string, string[]>? validationErrors = null;
                                if (!string.IsNullOrWhiteSpace(existingBody))
                                {
                                    try
                                    {
                                        var json = JsonDocument.Parse(existingBody);

                                        // Check for validation error format (from our endpoint ValidationProblem calls)
                                        if (json.RootElement.TryGetProperty("errors", out var errorsElement))
                                        {
                                            validationErrors =
                                                JsonSerializer
                                                    .Deserialize<Dictionary<string, string[]>>(errorsElement.GetRawText());
                                        }
                                        // Check for a simple error object format (backward compatibility)
                                        else if (json.RootElement.TryGetProperty("error", out var errorElement))
                                        {
                                            validationErrors = new Dictionary<string, string[]>
                                            {
                                                ["Request"] = [errorElement.GetString() ?? "Validation error"]
                                            };
                                        }
                                    }
                                    catch (JsonException)
                                    {
                                        // Not JSON or invalid format - will use existingBody as Detail below
                                    }
                                }

                                if (validationErrors is { Count: > 0 })
                                {
                                    // Create ValidationProblemDetails with structured errors
                                    var vpd = new HttpValidationProblemDetails(validationErrors)
                                    {
                                        Status = status,
                                        Title = "One or more validation errors occurred.",
                                        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                                        Instance = context.Request.Path,
                                        Extensions =
                                        {
                                        ["eventCode"] = eventCode,
                                        ["traceId"] = context.TraceIdentifier
                                        }
                                    };
                                    pd = vpd;
                                }
                                else
                                {
                                    // Build generic ProblemDetails with reason phrase and prior content as detail
                                    var title = ReasonPhrases.GetReasonPhrase(status) ?? "Error";
                                    pd = new ProblemDetails
                                    {
                                        Status = status,
                                        Title = title,
                                        Detail = string.IsNullOrWhiteSpace(existingBody) ? null : existingBody,
                                        Type = $"https://httpstatuses.com/{status}",
                                        Instance = context.Request.Path,
                                        Extensions =
                                        {
                                        ["eventCode"] = eventCode,
                                        ["traceId"] = context.TraceIdentifier
                                        }
                                    };
                                }

                                context.Response.ContentType = "application/problem+json";

                                // Replace body with ProblemDetails JSON
                                buffer.SetLength(0);
                                await JsonSerializer.SerializeAsync(buffer, pd, pd.GetType());
                                buffer.Position = 0;
                                await buffer.CopyToAsync(originalBody);
                            }
                            else
                            {
                                // It's already a problem details, but check if it's missing eventCode or traceId
                                if (!string.IsNullOrWhiteSpace(existingBody))
                                {
                                    try
                                    {
                                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                        // Use HttpValidationProblemDetails to preserve 'errors' if present
                                        var pd = JsonSerializer.Deserialize<HttpValidationProblemDetails>(existingBody, options);
                                        if (pd != null)
                                        {
                                            bool modified = false;
                                            if (!pd.Extensions.ContainsKey("eventCode"))
                                            {
                                                pd.Extensions["eventCode"] = eventCode;
                                                modified = true;
                                            }

                                            if (!pd.Extensions.ContainsKey("traceId"))
                                            {
                                                pd.Extensions["traceId"] = context.TraceIdentifier;
                                                modified = true;
                                            }

                                            if (modified)
                                            {
                                                buffer.SetLength(0);
                                                await JsonSerializer.SerializeAsync(buffer, pd, pd.GetType());
                                                buffer.Position = 0;
                                                await buffer.CopyToAsync(originalBody);
                                            }
                                            else
                                            {
                                                await buffer.CopyToAsync(originalBody);
                                            }
                                        }
                                        else
                                        {
                                            await buffer.CopyToAsync(originalBody);
                                        }
                                    }
                                    catch (JsonException)
                                    {
                                        await buffer.CopyToAsync(originalBody);
                                    }
                                }
                                else
                                {
                                    await buffer.CopyToAsync(originalBody);
                                }
                            }
                        }
                        else
                        {
                            // Success response or status < 400
                            await buffer.CopyToAsync(originalBody);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var eventCode = eventCodeResolver.ResolveEventCode(ex, context);
                    eventCodeFeature.EventCode = eventCode;

                    chunkedLogger.LogError(ex, eventCode, "Request failed with event code {EventCode}", eventCode);

                    var pd = new ProblemDetails
                    {
                        Status = 500,
                        Title = "Internal Server Error",
                        Detail = "An unexpected error occurred. Please contact support with the trace ID.",
                        Type = "https://httpstatuses.com/500",
                        Instance = context.Request.Path,
                        Extensions =
                        {
                        ["eventCode"] = eventCode,
                        ["traceId"] = context.TraceIdentifier
                        }
                    };

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/problem+json";
                    context.Response.ContentLength = null;

                    // Replace body with ProblemDetails JSON
                    await JsonSerializer.SerializeAsync(originalBody, pd);
                }
                finally
                {
                    context.Response.Body = originalBody;
                }
            });
            app.UseMetricServer();
            app.UseHttpMetrics();

            // Add OperationLoggingMiddleware BEFORE routing for HTTP operation tracking
            app.UseMiddleware<OperationLoggingMiddleware>();

            app.MapHubEndpoints();

            // Startup diagnostics dump (effective config + labels per node)
            try
            {
                var cfg = app.Configuration;
                var nodeTimeoutSeconds = int.TryParse(cfg["AGENIX_HUB_NODE_TIMEOUT"], out var t) ? t : 60;
                var nodeQuarantineSeconds = int.TryParse(cfg["AGENIX_HUB_NODE_QUARANTINE_SECONDS"], out var qs) ? qs : 120;
                var dashboardUrl = cfg["AGENIX_DASHBOARD_PUBLIC_URL"] ?? "http://localhost:3001";

                // Allow per-environment overrides via suffix, e.g., HUB_BORROW_WILDCARDS_Development
                static bool GetBoolWithEnvironmentOverride(IConfiguration cfg2, string key, string environment,
                    bool defaultValue)
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
                        if (!string.IsNullOrWhiteSpace(v1))
                        {
                            value = v1;
                        }
                        else if (!string.IsNullOrWhiteSpace(v2))
                        {
                            value = v2;
                        }
                    }

                    value ??= cfg2[key];
                    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
                }

                var environmentName = app.Environment.EnvironmentName ?? string.Empty;
                var enableTrailingFallback =
                    GetBoolWithEnvironmentOverride(cfg, "AGENIX_HUB_BORROW_TRAILING_FALLBACK", environmentName,
                        true); // default true
                var enablePrefixExpand =
                    GetBoolWithEnvironmentOverride(cfg, "AGENIX_HUB_BORROW_PREFIX_EXPAND", environmentName,
                        true); // default true
                var enableWildcards =
                    GetBoolWithEnvironmentOverride(cfg, "AGENIX_HUB_BORROW_WILDCARDS", environmentName,
                        false); // default false

                static string GetInformationalVersion(Type t)
                {
                    try
                    {
                        var asm = t.Assembly;
                        var aiv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
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
                    if (string.IsNullOrEmpty(v))
                    {
                        return v ?? string.Empty;
                    }

                    return v.Length <= max ? v : v[..max];
                }

                var verShort = TruncVer(ver);

                var reader = app.Services.GetRequiredService<IPoolStateReader>();
                var state = await reader.GetStateAsync();

                // Get RabbitMQ configuration
                var rabbitMqUrl = cfg["RABBITMQ_URL"];

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
                        Version = verShort,
                        EventPublishingEnabled = true, // Always enabled
                        RabbitMqUrl = rabbitMqUrl
                    },
                    Workers = state.Workers,
                    Now = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                startupLogger.LogMilestone(EventCodes.Generic, "Startup diagnostics:\n{Json}", json);
            }
            catch (Exception ex)
            {
                startupLogger.LogError(ex, EventCodes.Generic, "Startup diagnostics failed");
            }

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Hub host terminated unexpectedly");
            throw;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
