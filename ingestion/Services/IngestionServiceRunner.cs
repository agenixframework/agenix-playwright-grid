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

using System;
using System.Reflection;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using IngestionService.Infrastructure;
using IngestionService.Workers;
using Npgsql;
using Prometheus;
using Serilog;
using StackExchange.Redis;

namespace IngestionService.Services;

/// <summary>
///     Service runner for ingestion service - sets up DI, logging, and hosted services.
/// </summary>
public static class IngestionServiceRunner
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Ensure only Serilog is used for logging to prevent duplication and interleaving
        builder.Logging.ClearProviders();

        // Configure Serilog
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        // Register ChunkedLogger options
        var chunkedOptions = new ChunkedLoggerOptions
        {
            Enabled = builder.Configuration.GetValue("AGENIX_LOGGING_CHUNKED_ENABLED", true),
            MaxEventsPerChunk = builder.Configuration.GetValue("AGENIX_LOGGING_CHUNK_MAX_EVENTS", 1000),
            MaxAgeSeconds = builder.Configuration.GetValue("AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS", 60),
            EventCodePrefix = builder.Configuration.GetValue("AGENIX_LOGGING_EVENT_CODE_PREFIX", true),
            IncludeSourceLocation = builder.Configuration.GetValue("AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION", false)
        };
        builder.Services.AddSingleton(chunkedOptions);

        // Register generic ChunkedLogger<T>
        builder.Services.AddTransient(typeof(ChunkedLogger<>));

        // Register services
        builder.Services.AddSingleton<IRabbitMqConsumer, RabbitMqConsumer>();

        // Register shared Redis connection (reused by both token caches)
        builder.Services.AddSingleton<IDatabase>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var redisConn = config["REDIS_URL"]
                            ?? throw new InvalidOperationException("REDIS_URL required");
            var mux = ConnectionMultiplexer.Connect(redisConn);
            return mux.GetDatabase();
        });

        // Register PostgreSQL connection pool (NpgsqlDataSource)
        builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var pgConn = config["POSTGRES_CONNECTION_STRING"]
                         ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING required");

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(pgConn);
            return dataSourceBuilder.Build();
        });

        // Register Redis log token cache
        builder.Services.AddSingleton<ILogTokenCache>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisLogTokenCache>>();
            var redis = sp.GetRequiredService<IDatabase>();
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var config = sp.GetRequiredService<IConfiguration>();

            // Parse configuration
            var ttl = TimeSpan.FromSeconds(config.GetValue("AGENIX_INGESTION_LOG_TOKEN_TTL_SECONDS", 604800));
            var enableInMemory = config.GetValue("AGENIX_INGESTION_LOG_TOKEN_CACHE_ENABLED", false);
            var maxInMemory = config.GetValue("AGENIX_INGESTION_LOG_TOKEN_CACHE_MAX_SIZE", 10000);

            return new RedisLogTokenCache(redis, dataSource, ttl, enableInMemory, maxInMemory, logger);
        });

        // Register Redis command token cache
        builder.Services.AddSingleton<ICommandTokenCache>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisCommandTokenCache>>();
            var redis = sp.GetRequiredService<IDatabase>();
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var config = sp.GetRequiredService<IConfiguration>();

            // Parse configuration
            var ttl = TimeSpan.FromSeconds(config.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_TTL_SECONDS", 604800));
            var enableInMemory = config.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_CACHE_ENABLED", false);
            var maxInMemory = config.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_CACHE_MAX_SIZE", 10000);

            return new RedisCommandTokenCache(redis, dataSource, ttl, enableInMemory, maxInMemory, logger);
        });

        // Register PostgresBatchWriter with optional MinioStorageService dependency
        builder.Services.AddSingleton<IPostgresBatchWriter>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var logger = sp.GetRequiredService<ILogger<PostgresBatchWriter>>();
            var chunkedLogger = sp.GetRequiredService<ChunkedLogger<PostgresBatchWriter>>();
            var logTokenCache = sp.GetRequiredService<ILogTokenCache>();
            var commandTokenCache = sp.GetRequiredService<ICommandTokenCache>();
            var minioStorage = sp.GetService<MinioStorageService>(); // Optional - may be null if not configured

            return new PostgresBatchWriter(config, dataSource, logger, chunkedLogger, logTokenCache, commandTokenCache, minioStorage);
        });

        builder.Services.AddHostedService<IngestionWorker>();
        builder.Services.AddHostedService<AuditConsumerWorker>();
        builder.Services.AddHostedService<ArtifactUploadWorker>();

        // Register MinIO storage service (optional, for S3-compatible artifact storage)
        var storageBackend = builder.Configuration.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");
        if (storageBackend == "minio")
        {
            builder.Services.AddSingleton<MinioStorageService>();
        }

        // Health checks
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        // Use chunked logger for startup diagnostics
        var chunkedLogger = new ChunkedLogger(app.Logger, "IngestionStartup", chunkedOptions);
        using var startupOp = chunkedLogger.BeginOperation("IngestionStartup");

        // Initialize MinIO bucket if enabled
        if (storageBackend == "minio")
        {
            try
            {
                var minioService = app.Services.GetRequiredService<MinioStorageService>();
                await minioService.EnsureBucketExistsAsync();
                chunkedLogger.LogMilestone(
                    EventCodes.System.BootstrapCompleted,
                    "MinIO bucket verified/created successfully");
            }
            catch (Exception ex)
            {
                startupOp.Fail(ex, ErrorType.DependencyFailure, DependencyName.MinIO);
                app.Logger.LogError(ex,
                    "[ingestion] Failed to initialize MinIO bucket - continuing with degraded functionality");
                // Don't crash - service can still work with local storage fallback
            }
        }

        // Startup diagnostics: effective ingestion service configuration (secrets redacted)
        try
        {
            // Redact secrets
            static string Redact(string? s)
            {
                return string.IsNullOrEmpty(s) ? "" : "***";
            }

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

            static string TruncVer(string? v)
            {
                const int max = 15; // "1.0.1-preview.3".Length
                if (string.IsNullOrEmpty(v))
                {
                    return v ?? string.Empty;
                }

                return v!.Length <= max ? v : v.Substring(0, max);
            }

            var version = GetInformationalVersion(typeof(IngestionServiceRunner));
            var versionShort = TruncVer(version);

            var diag = new
            {
                Version = versionShort,
                RabbitMQ =
                    new
                    {
                        Url = app.Configuration["RABBITMQ_URL"] ?? "(not set)",
                        Username = app.Configuration["RABBITMQ_USERNAME"] ?? "guest",
                        Password = Redact(app.Configuration["RABBITMQ_PASSWORD"]),
                        PrefetchCount = app.Configuration.GetValue("RABBITMQ_PREFETCH_COUNT", 100)
                    },
                Redis =
                    new
                    {
                        Url = app.Configuration["REDIS_URL"] ?? "(not set)",
                        ConnectionString = Redact(app.Configuration["REDIS_URL"])
                    },
                PostgreSQL = new { ConnectionString = Redact(app.Configuration["POSTGRES_CONNECTION_STRING"]) },
                Consumer =
                    new
                    {
                        Concurrency = app.Configuration.GetValue("AGENIX_INGESTION_CONSUMER_CONCURRENCY", 4),
                        Enabled = app.Configuration.GetValue("ENABLE_CONSUMER", true),
                        MaxRetryAttempts = app.Configuration.GetValue("AGENIX_INGESTION_MAX_RETRY_ATTEMPTS", 3),
                        RetryDelayMs = app.Configuration.GetValue("AGENIX_INGESTION_RETRY_DELAY_MS", 1000)
                    },
                LogTokenOptimization =
                    new
                    {
                        Enabled =
                            app.Configuration.GetValue("AGENIX_INGESTION_LOG_TOKEN_OPTIMIZATION_ENABLED", true),
                        RedisTtlSeconds =
                            app.Configuration.GetValue("AGENIX_INGESTION_LOG_TOKEN_TTL_SECONDS", 604800),
                        InMemoryCacheEnabled =
                            app.Configuration.GetValue("AGENIX_INGESTION_LOG_TOKEN_CACHE_ENABLED", false),
                        InMemoryMaxSize =
                            app.Configuration.GetValue("AGENIX_INGESTION_LOG_TOKEN_CACHE_MAX_SIZE", 10000)
                    },
                CommandTokenOptimization =
                    new
                    {
                        Enabled =
                            app.Configuration.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_OPTIMIZATION_ENABLED",
                                true),
                        RedisTtlSeconds =
                            app.Configuration.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_TTL_SECONDS", 604800),
                        InMemoryCacheEnabled =
                            app.Configuration.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_CACHE_ENABLED", false),
                        InMemoryMaxSize =
                            app.Configuration.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_CACHE_MAX_SIZE", 10000)
                    },
                Batching =
                    new
                    {
                        TestItemsBatchSize =
                            app.Configuration.GetValue("AGENIX_INGESTION_BATCH_SIZE_TEST_ITEMS", 200),
                        CommandsBatchSize =
                            app.Configuration.GetValue("AGENIX_INGESTION_BATCH_SIZE_COMMANDS", 500),
                        LogItemsBatchSize =
                            app.Configuration.GetValue("AGENIX_INGESTION_BATCH_SIZE_LOG_ITEMS", 1000)
                    },
                ArtifactStorage = new
                {
                    Backend = storageBackend,
                    MinioEndpoint =
                        storageBackend == "minio" ? app.Configuration["MINIO_ENDPOINT"] : "(disabled)",
                    MinioUseSSL = app.Configuration.GetValue("MINIO_USE_SSL", false),
                    MinioBucket = app.Configuration.GetValue("MINIO_BUCKET_NAME", "playwright-artifacts"),
                    LocalPath = app.Configuration.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts")
                }
            };

            var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
            chunkedLogger.LogMilestone(
                EventCodes.System.BootstrapCompleted,
                "[ingestion] Startup diagnostics:\n{Json}", json);

            startupOp.Complete();
        }
        catch (Exception ex)
        {
            startupOp.Fail(ex, ErrorType.Unexpected);
            app.Logger.LogError(ex, "[ingestion] Startup diagnostics failed");
        }

        // Operation logging middleware for HTTP endpoints
        app.Use(async (context, next) =>
        {
            var logger = context.RequestServices.GetRequiredService<ChunkedLogger<WebApplication>>();
            var method = context.Request.Method;
            var path = context.Request.Path;

            using var op = logger.BeginOperation($"{method} {path}",
                inputs: new Dictionary<string, object>
                {
                    ["method"] = method,
                    ["path"] = path,
                    ["traceId"] = context.TraceIdentifier
                });

            if (OperationContext.Current != null) OperationContext.Current.Properties["HttpTraceId"] = context.TraceIdentifier;

            await next();
        });

        // Minimal endpoints
        app.MapHealthChecks("/health");
        app.MapMetrics();

        app.MapGet("/", () => new { service = "ingestion-service", version = "1.0.0", status = "running" });

        // NEW: Add endpoint to view recent chunked operations
        app.MapGet("/ops", () => Results.Ok(new { status = "ok", service = "ingestion" }));

        // Analytics endpoints (Phase 5)
        app.MapGet("/metrics/timeseries", async (HttpContext ctx) =>
        {
            var config = app.Configuration;
            var connStr = config["POSTGRES_CONNECTION_STRING"];
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return Results.Problem("Database not configured");
            }

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT hour, launch_id, item_type, total_items, passed, failed, skipped, avg_duration_seconds
                    FROM test_items_hourly
                    WHERE hour >= NOW() - INTERVAL '24 hours'
                    ORDER BY hour DESC, launch_id, item_type";

                await using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var results = new List<object>();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        hour = reader.GetDateTime(0),
                        launch_id = reader.GetGuid(1),
                        item_type = reader.GetString(2),
                        total_items = reader.GetInt64(3),
                        passed = reader.GetInt64(4),
                        failed = reader.GetInt64(5),
                        skipped = reader.GetInt64(6),
                        avg_duration_seconds = reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7)
                    });
                }

                return Results.Ok(new { data = results, count = results.Count });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Query failed: {ex.Message}");
            }
        });

        app.MapGet("/metrics/aggregations", async (HttpContext ctx) =>
        {
            var config = app.Configuration;
            var connStr = config["POSTGRES_CONNECTION_STRING"];
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return Results.Problem("Database not configured");
            }

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT day, project_key, total_launches, finished, failed, stopped, success_rate
                    FROM launch_success_rate
                    WHERE day >= NOW() - INTERVAL '30 days'
                    ORDER BY day DESC, project_key";

                await using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var results = new List<object>();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        day = reader.GetDateTime(0),
                        project_key = reader.GetString(1),
                        total_launches = reader.GetInt64(2),
                        finished = reader.GetInt64(3),
                        failed = reader.GetInt64(4),
                        stopped = reader.GetInt64(5),
                        success_rate = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6)
                    });
                }

                return Results.Ok(new { data = results, count = results.Count });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Query failed: {ex.Message}");
            }
        });

        await app.RunAsync();
    }
}
