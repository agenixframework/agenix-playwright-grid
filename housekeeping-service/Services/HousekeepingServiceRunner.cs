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

using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using HousekeepingService.Infrastructure;
using HousekeepingService.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prometheus;
using Serilog;
using Serilog.Extensions.Logging;
using StackExchange.Redis;

namespace HousekeepingService.Services;

public static class HousekeepingServiceRunner
{
    public static WebApplication CreateApp(string[] args, Action<IServiceCollection>? overrideServices = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Ensure only Serilog is used for logging to prevent duplication and interleaving
        builder.Logging.ClearProviders();

        // Configure Serilog early for startup logging
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateBootstrapLogger();

        builder.Host.UseSerilog((ctx, services, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

        // Register ChunkedLogger options
        var chunkedOptions = new ChunkedLoggerOptions
        {
            Enabled = true
        };
        builder.Services.AddSingleton(chunkedOptions);

        // Register generic ChunkedLogger<T>
        builder.Services.AddTransient(typeof(ChunkedLogger<>));

        var pgConnString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
                           ?? "Host=localhost;Database=playwright_grid;Username=postgres;Password=postgres";
        builder.Services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlDataSourceBuilder(pgConnString).Build());
        builder.Services.AddSingleton<IHousekeepingDataSource, HousekeepingDataSource>();

        var redisUrl = builder.Configuration["REDIS_URL"] ?? "localhost:6379";
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisUrl));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        builder.Services.AddSingleton<IProjectSettingsReader, ProjectSettingsReader>();

        // Register MinIO storage service (optional, for S3-compatible artifact storage)
        var storageBackend = builder.Configuration.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");
        if (storageBackend == "minio")
        {
            builder.Services.AddSingleton<IMinioStorageService, MinioStorageService>();
        }

        builder.Services.AddHostedService<LaunchRetentionWorker>();
        builder.Services.AddHostedService<LaunchAutoStopWorker>();
        builder.Services.AddHostedService<LogRetentionWorker>();
        builder.Services.AddHostedService<AttachmentRetentionWorker>();
        builder.Services.AddHostedService<AuditRetentionWorker>();
        builder.Services.AddHealthChecks();

        overrideServices?.Invoke(builder.Services);

        return builder.Build();
    }

    public static async Task RunAsync(string[] args)
    {
        var app = CreateApp(args);

        app.UseMetricServer();

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

        app.MapHealthChecks("/health");

        // Use chunked logger for startup diagnostics
        var chunkedOptions = app.Services.GetRequiredService<ChunkedLoggerOptions>();
        var storageBackend = app.Configuration.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");
        var pgConnString = app.Configuration["POSTGRES_CONNECTION_STRING"] ?? "";
        var redisUrl = app.Configuration["REDIS_URL"] ?? "";

        var startupLogger = new ChunkedLogger(new SerilogLoggerFactory(Log.Logger).CreateLogger("housekeeping"), "housekeeping", chunkedOptions);
        using var startupOp = startupLogger.BeginOperation("HousekeepingStartup");

        // Initialize MinIO bucket if MinIO storage is enabled
        if (storageBackend == "minio")
        {
            try
            {
                var minioService = app.Services.GetRequiredService<IMinioStorageService>();
                await minioService.EnsureBucketExistsAsync();
                startupLogger.LogMilestone(EventCodes.Generic, "MinIO bucket verified/created successfully");
            }
            catch (Exception ex)
            {
                startupLogger.LogError(ex, EventCodes.Generic,
                    "Failed to initialize MinIO bucket - continuing with degraded functionality");
            }
        }

        // Startup diagnostics
        try
        {
            var cfg = app.Configuration;
            var launchInterval =
                int.TryParse(cfg["AGENIX_HOUSEKEEPING_LAUNCH_RETENTION_CHECK_INTERVAL_HOURS"], out var li) ? li : 6;
            var launchAutoStopInterval =
                int.TryParse(cfg["AGENIX_HOUSEKEEPING_LAUNCH_AUTO_STOP_INTERVAL_MINUTES"], out var lasi) ? lasi : 10;
            var logInterval = int.TryParse(cfg["AGENIX_HOUSEKEEPING_LOG_RETENTION_CHECK_INTERVAL_HOURS"], out var logi)
                ? logi
                : 1;
            var attachmentInterval =
                int.TryParse(cfg["AGENIX_HOUSEKEEPING_ATTACHMENT_RETENTION_CHECK_INTERVAL_HOURS"], out var ai) ? ai : 1;
            var auditInterval =
                int.TryParse(cfg["AGENIX_HOUSEKEEPING_AUDIT_RETENTION_CHECK_INTERVAL_HOURS"], out var aui) ? aui : 24;
            var leadershipEnabled = string.Equals(cfg["AGENIX_HOUSEKEEPING_LEADERSHIP"], "true",
                StringComparison.OrdinalIgnoreCase);
            var leaseSeconds = int.TryParse(cfg["AGENIX_HOUSEKEEPING_LEASE_SECONDS"], out var ls)
                ? Math.Max(5, ls)
                : 30;
            var instanceId = cfg["AGENIX_HOUSEKEEPING_INSTANCE_ID"] ??
                             $"{System.Environment.MachineName}:{System.Environment.ProcessId}";
            var port = int.TryParse(cfg["AGENIX_HOUSEKEEPING_PORT"], out var p) ? p : 8082;

            var dto = new
            {
                HousekeepingConfig =
                    new
                    {
                        PostgreSQL = HideSensitiveInfo(pgConnString),
                        RedisUrl = redisUrl,
                        Port = port,
                        LeadershipEnabled = leadershipEnabled,
                        LeaseSeconds = leaseSeconds,
                        InstanceId = instanceId
                    },
                Workers = new
                {
                    LaunchRetention = new { IntervalHours = launchInterval },
                    LaunchAutoStop = new { IntervalMinutes = launchAutoStopInterval },
                    LogRetention = new { IntervalHours = logInterval },
                    AttachmentRetention = new { IntervalHours = attachmentInterval },
                    AuditRetention = new { IntervalHours = auditInterval }
                },
                Now = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            startupLogger.LogMilestone(
                EventCodes.Housekeeping.BootstrapCompleted,
                "[housekeeping] Startup diagnostics:\n{Json}", json);

            startupOp.SetOutputs(new Dictionary<string, object>
            {
                ["workersCount"] = 5,
                ["leadershipEnabled"] = leadershipEnabled
            });
            startupOp.Complete();
        }
        catch (Exception ex)
        {
            startupLogger.FailOperation(startupOp.Context, ex, ErrorType.Unexpected);
            app.Logger.LogError(ex, "[housekeeping] Startup diagnostics failed");
        }

        await app.RunAsync();
    }

    private static string HideSensitiveInfo(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = "***";
        }

        return builder.ConnectionString;
    }
}
