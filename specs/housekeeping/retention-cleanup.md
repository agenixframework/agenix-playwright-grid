# Phase 2: Retention Cleanup - Standalone Housekeeping Service

## Status
- ❌ Create housekeeping-service project structure
- ❌ Create ProjectSettingsReader infrastructure
- ❌ Create LaunchRetentionWorker
- ❌ Create LogRetentionWorker
- ❌ Create AttachmentRetentionWorker
- ❌ Create AuditRetentionWorker (NEW)
- ❌ Add database cleanup functions (V39 migration)
- ❌ Update docker-compose.yml and .env files
- ❌ Don't audit field in project settings just env variable.
- ❌ Integration testing

## Summary

**What**: Create `housekeeping-service`, a standalone microservice for automated data retention cleanup.

**Why**: Prevent unbounded database/storage growth, reduce costs, maintain compliance with data retention policies.

**How**: 4 background workers read per-project retention settings from Redis and execute PostgreSQL cleanup functions on a schedule.

**Pattern**: Follows the same architecture as `ingestion` service (standalone microservice, not part of hub).

## Goal

Create a **standalone microservice** (similar to `ingestion` service) that automatically deletes old data based on per-project retention policies stored in Redis. This prevents unbounded storage growth, reduces costs, and maintains compliance with data retention policies.

**Key Features**:
- ✅ Standalone microservice (separate from hub)
- ✅ 4 retention workers (Launches, Logs, Attachments, Audit)
- ✅ Per-project retention policies (stored in Redis)
- ✅ Configurable cleanup intervals
- ✅ Redis-based leadership election (multi-instance support)
- ✅ Prometheus metrics and health checks
- ✅ Database functions for atomic deletions

## Architecture

```
housekeeping-service/ (NEW standalone microservice)
├── Program.cs                          # Entry point (loads .env, calls runner)
├── HousekeepingService.csproj          # Project file with dependencies
├── Dockerfile                          # Multi-stage build for Docker
├── appsettings.json                    # Default configuration
├── Services/
│   └── HousekeepingServiceRunner.cs   # ASP.NET Core host, DI setup
├── Workers/                            # Background worker services
│   ├── LaunchRetentionWorker.cs       # Deletes old launches
│   ├── LogRetentionWorker.cs          # Deletes old log items
│   ├── AttachmentRetentionWorker.cs   # Deletes old test artifacts
│   └── AuditRetentionWorker.cs        # Deletes old audit entries (NEW)
├── Infrastructure/
│   ├── DotEnv.cs                       # .env file loader (copied from ingestion)
│   └── ProjectSettingsReader.cs       # Reads retention settings from Redis
└── Shared/
    └── RetentionSettings.cs            # POCO for retention policy
```

**Pattern**: Follows the same structure as `ingestion` service for consistency.

## Retention Policies (Per-Project in Redis)

**Storage Location**: `project:{projectKey}:settings`

**Schema** (Updated):
```json
{
  "launchInactivityTimeout": "1d",   // When to auto-stop (Phase 1)
  "keepLaunches": "30",              // Days to keep completed launches
  "keepLogs": "7",                   // Days to keep log items
  "keepAttachments": "7",            // Days to keep test artifacts
  "keepAudit": "90"                  // Days to keep audit entries (NEW)
}
```

**Retention Hierarchy**: `Attachments ≤ Logs ≤ Launches ≤ Audit`

Enforced in UI (ProjectSettings.razor) - invalid configurations auto-cleared.

**Rationale for Audit Retention**:
- Audit logs grow indefinitely without cleanup
- Compliance requirements typically 90 days for system audit trails
- Separate from test data retention (launches/logs/attachments)
- Longest retention period (audit is least frequently accessed)

## Workers to Create (4)

| Worker | Deletes | Frequency | Reads Setting | Database Function |
|--------|---------|-----------|---------------|-------------------|
| **LaunchRetentionWorker** | Launches + ALL descendants (suites, tests, steps, logs, artifacts) | 6 hours | `keepLaunches` | `delete_old_launches()` |
| **LogRetentionWorker** | Log items + Orphaned log_tokens + Orphaned command_tokens | 6 hours | `keepLogs` | `delete_old_log_items()` |
| **AttachmentRetentionWorker** | Test artifacts (database + physical files from MinIO/local) | 6 hours | `keepAttachments` | `delete_old_attachments()` |
| **AuditRetentionWorker** | Audit entries older than N days | 24 hours | `keepAudit` | `delete_old_audit_entries()` (NEW) |

## Deletion Scope Clarifications

### 1. Launch Deletion (Complete Cascade)
**What gets deleted**:
- ✅ Launches table row
- ✅ Test items (suites, tests, steps) via `ON DELETE CASCADE`
- ✅ Log items via `ON DELETE CASCADE` from test_items
- ✅ Test artifacts via `ON DELETE CASCADE` from test_items

**Result**: Complete launch deletion including all test execution data

### 2. Log Items Deletion (With Token Cleanup)
**What gets deleted**:
- ✅ `log_items` rows older than cutoff
- ✅ **Orphaned `log_tokens`** (tokens with NO remaining log_items references)
- ✅ **Orphaned `command_tokens`** (tokens with NO remaining references)

**Important**: Launch structure (test_items, test_artifacts) remains intact. Only logs are deleted.

### 3. Artifacts Deletion (Hard Delete)
**What gets deleted**:
- ✅ `test_artifacts` table rows (HARD DELETE, not soft delete)
- ✅ Physical files from MinIO or local storage
- ✅ Handles storage errors gracefully (log warning, continue)

**Changed from soft delete**: No `deleted_at` column needed. Direct hard deletion.

### 4. Audit Deletion
**What gets deleted**:
- ✅ `audit_entries` rows older than cutoff for specific project

## Files to Create

### 1. Project Files

**`housekeeping-service/HousekeepingService.csproj`** (~40 lines)
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <!-- Database -->
    <PackageReference Include="Npgsql" Version="9.0.2" />
    <!-- Cache -->
    <PackageReference Include="StackExchange.Redis" Version="2.6.90" />
    <!-- Storage -->
    <PackageReference Include="Minio" Version="6.0.3" />
    <!-- Logging -->
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
    <PackageReference Include="Serilog.Expressions" Version="5.0.0" />
    <!-- Metrics -->
    <PackageReference Include="prometheus-net.AspNetCore" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**`housekeeping-service/Program.cs`** (~15 lines)
```csharp
using HousekeepingService.Infrastructure;
using HousekeepingService.Services;

namespace HousekeepingService;

public static class Program
{
    public static Task Main(string[] args)
    {
        // Load local .env variables for developer convenience
        DotEnv.Load();
        return HousekeepingServiceRunner.RunAsync(args);
    }
}
```

**`housekeeping-service/Dockerfile`** (~30 lines)
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["housekeeping-service/HousekeepingService.csproj", "housekeeping-service/"]
RUN dotnet restore "housekeeping-service/HousekeepingService.csproj"
COPY housekeeping-service/ housekeeping-service/
WORKDIR "/src/housekeeping-service"
RUN dotnet build "HousekeepingService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HousekeepingService.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HousekeepingService.dll"]
```

### 2. Infrastructure: ProjectSettingsReader
**`housekeeping-service/Infrastructure/ProjectSettingsReader.cs`** (150 lines)

```csharp
using System.Text.Json;
using StackExchange.Redis;

namespace HousekeepingService.Shared;

/// <summary>
/// Reads per-project retention settings from Redis.
/// Pattern copied from LaunchAutoStopService lines 423-505.
/// </summary>
public sealed class ProjectSettingsReader
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<ProjectSettingsReader> _logger;

    public ProjectSettingsReader(
        IDatabase db,
        IConnectionMultiplexer mux,
        ILogger<ProjectSettingsReader> logger)
    {
        _db = db;
        _mux = mux;
        _logger = logger;
    }

    /// <summary>
    /// Gets all project keys from Redis by scanning for project:*:settings keys.
    /// </summary>
    public async Task<List<string>> GetAllProjectKeysAsync()
    {
        var projects = new List<string>();
        try
        {
            var server = _mux.GetServer(_mux.GetEndPoints()[0]);
            var cursor = 0L;

            do
            {
                var result = await server.KeysAsync(
                    pattern: "project:*:settings",
                    pageSize: 100,
                    cursor: cursor
                ).ToListAsync();

                foreach (var key in result)
                {
                    var keyStr = key.ToString();
                    // Extract project key from "project:{projectKey}:settings"
                    var parts = keyStr.Split(':');
                    if (parts.Length >= 3 && parts[0] == "project" && parts[2] == "settings")
                    {
                        projects.Add(parts[1]);
                    }
                }

                // Note: cursor pagination not used here (scanning all keys)
                break;
            } while (cursor != 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get all projects from Redis");
        }

        return projects.Distinct().ToList();
    }

    /// <summary>
    /// Gets retention settings for a specific project.
    /// Returns null if project doesn't exist or has no settings.
    /// </summary>
    public async Task<RetentionSettings?> GetRetentionSettingsAsync(string projectKey)
    {
        try
        {
            var settingsKey = $"project:{projectKey}:settings";
            var json = await _db.StringGetAsync(settingsKey);

            if (json.IsNullOrEmpty)
            {
                _logger.LogDebug("No settings found for project {ProjectKey}", projectKey);
                return null;
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json.ToString());
            if (settings == null)
            {
                return null;
            }

            // Parse retention periods (in days)
            var keepLaunches = TryGetInt(settings, "keepLaunches", 30);
            var keepLogs = TryGetInt(settings, "keepLogs", 7);
            var keepAttachments = TryGetInt(settings, "keepAttachments", 7);
            var keepAudit = TryGetInt(settings, "keepAudit", 90);  // NEW

            return new RetentionSettings
            {
                ProjectKey = projectKey,
                KeepLaunchesDays = keepLaunches,
                KeepLogsDays = keepLogs,
                KeepAttachmentsDays = keepAttachments,
                KeepAuditDays = keepAudit  // NEW
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get retention settings for project {ProjectKey}", projectKey);
            return null;
        }
    }

    private static int TryGetInt(Dictionary<string, JsonElement> dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), out var intValue))
            {
                return intValue;
            }
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt32();
            }
        }
        return defaultValue;
    }
}

/// <summary>
/// Retention policy settings for a project.
/// </summary>
public sealed record RetentionSettings
{
    public required string ProjectKey { get; init; }
    public required int KeepLaunchesDays { get; init; }
    public required int KeepLogsDays { get; init; }
    public required int KeepAttachmentsDays { get; init; }
    public required int KeepAuditDays { get; init; }  // NEW
}
```

### 3. Service Runner
**`housekeeping-service/Services/HousekeepingServiceRunner.cs`** (~200 lines)

```csharp
using HousekeepingService.Infrastructure;
using HousekeepingService.Workers;
using Npgsql;
using Prometheus;
using Serilog;
using StackExchange.Redis;

namespace HousekeepingService.Services;

public static class HousekeepingServiceRunner
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Serilog
        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/housekeeping-.log", rollingInterval: RollingInterval.Day));

        // PostgreSQL connection
        var pgConnString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING not configured");
        builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(pgConnString).Build());

        // Redis connection
        var redisConnString = builder.Configuration["REDIS_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("REDIS_CONNECTION_STRING not configured");
        var redis = await ConnectionMultiplexer.ConnectAsync(redisConnString);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddSingleton(_ => redis.GetDatabase());

        // Register infrastructure services
        builder.Services.AddSingleton<ProjectSettingsReader>();

        // Register 4 retention workers
        builder.Services.AddHostedService<LaunchRetentionWorker>();
        builder.Services.AddHostedService<LogRetentionWorker>();
        builder.Services.AddHostedService<AttachmentRetentionWorker>();
        builder.Services.AddHostedService<AuditRetentionWorker>();  // NEW

        // Health check endpoint
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.Logger.LogInformation("[Housekeeping] Starting service...");
        app.Logger.LogInformation("[Housekeeping] PostgreSQL: {PG}", pgConnString);
        app.Logger.LogInformation("[Housekeeping] Redis: {Redis}", redisConnString);

        // Prometheus metrics endpoint
        app.UseMetricServer();
        app.MapHealthChecks("/health");

        await app.RunAsync();
    }
}
```

### 4. LaunchRetentionWorker
**`housekeeping-service/Workers/LaunchRetentionWorker.cs`** (200 lines)

```csharp
using HousekeepingService.Shared;
using Npgsql;
using StackExchange.Redis;

namespace HousekeepingService.Services;

/// <summary>
/// Periodically deletes launches (and all descendants) older than per-project retention period.
/// Reads keepLaunches from Redis: project:{key}:settings
/// </summary>
public sealed class LaunchRetentionCleanupService : BackgroundService
{
    private readonly ProjectSettingsReader _settingsReader;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDatabase _db;
    private readonly IConfiguration _config;
    private readonly ILogger<LaunchRetentionCleanupService> _logger;

    public LaunchRetentionCleanupService(
        ProjectSettingsReader settingsReader,
        NpgsqlDataSource dataSource,
        IDatabase db,
        IConfiguration config,
        ILogger<LaunchRetentionCleanupService> logger)
    {
        _settingsReader = settingsReader;
        _dataSource = dataSource;
        _db = db;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = int.TryParse(_config["LAUNCH_RETENTION_CHECK_INTERVAL_HOURS"], out var h)
            ? Math.Max(1, h)
            : 6; // Default: check every 6 hours

        var interval = TimeSpan.FromHours(intervalHours);

        _logger.LogInformation(
            "[LaunchRetention] Starting. interval={IntervalHours}h",
            interval.TotalHours);

        // Leader election support
        var leadershipEnabled = string.Equals(
            _config["HUB_SWEEPER_LEADERSHIP"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["HUB_SWEEPER_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["HUB_INSTANCE_ID"] ?? $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "sweeper:leader:launch_retention";

        if (leadershipEnabled)
        {
            _logger.LogInformation(
                "[LaunchRetention] Leadership enabled. key={LeaderKey} lease={LeaseSeconds}s instance={InstanceId}",
                leaderKey, leaseSeconds, instanceId);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Leader election
            if (leadershipEnabled)
            {
                var leaseAcquired = await _db.StringSetAsync(
                    leaderKey,
                    instanceId,
                    TimeSpan.FromSeconds(leaseSeconds),
                    When.NotExists);

                if (!leaseAcquired)
                {
                    _logger.LogDebug("[LaunchRetention] Not leader, skipping tick");
                    try { await Task.Delay(interval, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }
            }

            var tickStart = DateTime.UtcNow;
            int projectsScanned = 0, launchesDeleted = 0, errors = 0;

            try
            {
                // Get all projects
                var projects = await _settingsReader.GetAllProjectKeysAsync();
                projectsScanned = projects.Count;

                foreach (var projectKey in projects)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        // Get retention settings
                        var settings = await _settingsReader.GetRetentionSettingsAsync(projectKey);
                        if (settings == null)
                        {
                            _logger.LogDebug(
                                "[LaunchRetention] Skip project {ProjectKey}: no settings",
                                projectKey);
                            continue;
                        }

                        // Calculate cutoff date
                        var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepLaunchesDays);

                        // Delete old launches
                        var deleted = await DeleteOldLaunchesAsync(
                            projectKey,
                            cutoffDate,
                            stoppingToken);

                        if (deleted > 0)
                        {
                            _logger.LogInformation(
                                "[LaunchRetention] Deleted {Count} launches for project {ProjectKey} (older than {Days} days)",
                                deleted, projectKey, settings.KeepLaunchesDays);
                            launchesDeleted += deleted;
                        }
                    }
                    catch (Exception exProject)
                    {
                        errors++;
                        _logger.LogWarning(exProject,
                            "[LaunchRetention] Error processing project {ProjectKey}: {Message}",
                            projectKey, exProject.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "[LaunchRetention] Loop error");
            }

            var tookMs = (int)(DateTime.UtcNow - tickStart).TotalMilliseconds;
            _logger.LogInformation(
                "[LaunchRetention] Tick: projects={Projects} deleted={Deleted} errors={Errors} took={Ms}ms",
                projectsScanned, launchesDeleted, errors, tookMs);

            try { await Task.Delay(interval, stoppingToken); }
            catch { break; }
        }
    }

    private async Task<int> DeleteOldLaunchesAsync(
        string projectKey,
        DateTime cutoffDate,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Use database function to delete launches and cascade
        var sql = @"
            SELECT delete_old_launches(
                @projectKey::text,
                @cutoffDate::timestamptz
            );";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectKey", projectKey);
        cmd.Parameters.AddWithValue("cutoffDate", cutoffDate);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int count ? count : 0;
    }
}
```

### 5. LogRetentionWorker
**`housekeeping-service/Workers/LogRetentionWorker.cs`** (180 lines)

Similar structure to LaunchRetentionWorker, but:
- Reads `keepLogs` setting
- Calls `delete_old_log_items(projectKey, cutoffDate)` function
- Only deletes from `log_items` table (launch structure remains)
- Leadership key: `housekeeping:leader:log_retention`

### 6. AttachmentRetentionWorker
**`housekeeping-service/Workers/AttachmentRetentionWorker.cs`** (220 lines)

Similar structure, but also:
- Reads `keepAttachments` setting
- Queries `test_artifacts` table for old attachments
- Deletes from MinIO/S3 storage backend
- Deletes from `test_artifacts` table
- Handles storage errors gracefully (log warning, don't fail)
- Leadership key: `housekeeping:leader:attachment_retention`

### 7. AuditRetentionWorker (NEW)
**`housekeeping-service/Workers/AuditRetentionWorker.cs`** (180 lines)

```csharp
using HousekeepingService.Infrastructure;
using Npgsql;
using StackExchange.Redis;

namespace HousekeepingService.Workers;

/// <summary>
/// Periodically deletes audit entries older than per-project retention period.
/// Reads keepAudit from Redis: project:{key}:settings
/// </summary>
public sealed class AuditRetentionWorker : BackgroundService
{
    private readonly ProjectSettingsReader _settingsReader;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDatabase _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuditRetentionWorker> _logger;

    public AuditRetentionWorker(
        ProjectSettingsReader settingsReader,
        NpgsqlDataSource dataSource,
        IDatabase db,
        IConfiguration config,
        ILogger<AuditRetentionWorker> logger)
    {
        _settingsReader = settingsReader;
        _dataSource = dataSource;
        _db = db;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = int.TryParse(_config["AUDIT_RETENTION_CHECK_INTERVAL_HOURS"], out var h)
            ? Math.Max(1, h)
            : 24; // Default: check every 24 hours

        var interval = TimeSpan.FromHours(intervalHours);

        _logger.LogInformation(
            "[AuditRetention] Starting. interval={IntervalHours}h",
            interval.TotalHours);

        // Leader election support
        var leadershipEnabled = string.Equals(
            _config["HOUSEKEEPING_LEADERSHIP"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["HOUSEKEEPING_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["HOUSEKEEPING_INSTANCE_ID"] ?? $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "housekeeping:leader:audit_retention";

        if (leadershipEnabled)
        {
            _logger.LogInformation(
                "[AuditRetention] Leadership enabled. key={LeaderKey} lease={LeaseSeconds}s instance={InstanceId}",
                leaderKey, leaseSeconds, instanceId);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Leader election
            if (leadershipEnabled)
            {
                var leaseAcquired = await _db.StringSetAsync(
                    leaderKey,
                    instanceId,
                    TimeSpan.FromSeconds(leaseSeconds),
                    When.NotExists);

                if (!leaseAcquired)
                {
                    _logger.LogDebug("[AuditRetention] Not leader, skipping tick");
                    try { await Task.Delay(interval, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }
            }

            var tickStart = DateTime.UtcNow;
            int projectsScanned = 0, auditEntriesDeleted = 0, errors = 0;

            try
            {
                // Get all projects
                var projects = await _settingsReader.GetAllProjectKeysAsync();
                projectsScanned = projects.Count;

                foreach (var projectKey in projects)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        // Get retention settings
                        var settings = await _settingsReader.GetRetentionSettingsAsync(projectKey);
                        if (settings == null)
                        {
                            _logger.LogDebug(
                                "[AuditRetention] Skip project {ProjectKey}: no settings",
                                projectKey);
                            continue;
                        }

                        // Calculate cutoff date
                        var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepAuditDays);

                        // Delete old audit entries
                        var deleted = await DeleteOldAuditEntriesAsync(
                            projectKey,
                            cutoffDate,
                            stoppingToken);

                        if (deleted > 0)
                        {
                            _logger.LogInformation(
                                "[AuditRetention] Deleted {Count} audit entries for project {ProjectKey} (older than {Days} days)",
                                deleted, projectKey, settings.KeepAuditDays);
                            auditEntriesDeleted += deleted;
                        }
                    }
                    catch (Exception exProject)
                    {
                        errors++;
                        _logger.LogWarning(exProject,
                            "[AuditRetention] Error processing project {ProjectKey}: {Message}",
                            projectKey, exProject.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "[AuditRetention] Loop error");
            }

            var tookMs = (int)(DateTime.UtcNow - tickStart).TotalMilliseconds;
            _logger.LogInformation(
                "[AuditRetention] Tick: projects={Projects} deleted={Deleted} errors={Errors} took={Ms}ms",
                projectsScanned, auditEntriesDeleted, errors, tookMs);

            try { await Task.Delay(interval, stoppingToken); }
            catch { break; }
        }
    }

    private async Task<int> DeleteOldAuditEntriesAsync(
        string projectKey,
        DateTime cutoffDate,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Use database function to delete audit entries
        var sql = @"
            SELECT delete_old_audit_entries(
                @projectKey::text,
                @cutoffDate::timestamptz
            );";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectKey", projectKey);
        cmd.Parameters.AddWithValue("cutoffDate", cutoffDate);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int count ? count : 0;
    }
}
```

### 8. Database Migration: Cleanup Functions
**`hub/Infrastructure/Adapters/Results/Migrations/V39__retention_cleanup_functions.sql`** (250+ lines)

```sql
-- ========================================
-- Function: delete_old_launches
-- ========================================
-- Deletes launches older than cutoff date for a specific project.
-- Cascade deletes all descendants: suites, test items, logs, artifacts.
-- Returns count of deleted launches.

CREATE OR REPLACE FUNCTION delete_old_launches(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS INTEGER AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    -- Delete launches (CASCADE will handle descendants)
    WITH deleted AS (
        DELETE FROM launches
        WHERE project_key = p_project_key
          AND finish_time IS NOT NULL
          AND finish_time < p_cutoff_date
          AND status IN ('Finished', 'Failed', 'Stopped', 'AutoStopped')
        RETURNING id
    )
    SELECT COUNT(*) INTO deleted_count FROM deleted;

    RETURN deleted_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION delete_old_launches IS
'Deletes completed launches older than cutoff date. Cascades to all test items, logs, and artifacts.';

-- ========================================
-- Function: delete_old_log_items
-- ========================================
-- Deletes log items older than cutoff date for a specific project.
-- Also deletes orphaned log_tokens and command_tokens.
-- Launch structure remains intact (only logs deleted).
-- Returns JSON with deletion counts: {log_items, log_tokens, command_tokens}

CREATE OR REPLACE FUNCTION delete_old_log_items(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS JSONB AS $$
DECLARE
    log_items_deleted INTEGER := 0;
    log_tokens_deleted INTEGER := 0;
    command_tokens_deleted INTEGER := 0;
    result JSONB;
BEGIN
    -- Step 1: Delete old log items
    WITH deleted_logs AS (
        DELETE FROM log_items
        WHERE test_item_uuid IN (
            SELECT run_id FROM test_items ti
            JOIN launches l ON ti.launch_id = l.id
            WHERE l.project_key = p_project_key
        )
        AND time < p_cutoff_date
        RETURNING id
    )
    SELECT COUNT(*) INTO log_items_deleted FROM deleted_logs;

    -- Step 2: Delete orphaned log_tokens (no remaining log_items references)
    -- Assuming log_items has a token_hash column referencing log_tokens
    WITH orphaned_log_tokens AS (
        DELETE FROM log_tokens
        WHERE token_hash NOT IN (
            SELECT DISTINCT token_hash
            FROM log_items
            WHERE token_hash IS NOT NULL
        )
        RETURNING token_hash
    )
    SELECT COUNT(*) INTO log_tokens_deleted FROM orphaned_log_tokens;

    -- Step 3: Delete orphaned command_tokens (no remaining references)
    -- Assuming there's a command_token_hash column in log_items or separate command_items table
    WITH orphaned_command_tokens AS (
        DELETE FROM command_tokens
        WHERE token_hash NOT IN (
            SELECT DISTINCT command_token_hash
            FROM log_items
            WHERE command_token_hash IS NOT NULL
        )
        RETURNING token_hash
    )
    SELECT COUNT(*) INTO command_tokens_deleted FROM orphaned_command_tokens;

    -- Return JSON result
    result := jsonb_build_object(
        'log_items_deleted', log_items_deleted,
        'log_tokens_deleted', log_tokens_deleted,
        'command_tokens_deleted', command_tokens_deleted
    );

    RETURN result;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION delete_old_log_items IS
'Deletes log items older than cutoff date. Also cleans up orphaned log_tokens and command_tokens. Launch structure remains intact. Returns JSON with deletion counts.';

-- ========================================
-- Function: delete_old_attachments
-- ========================================
-- HARD DELETES test artifacts from database (returns artifact details for physical file deletion).
-- Worker must delete physical files from MinIO/local storage using returned storage_path.
-- Returns JSONB array of artifacts: [{id, storage_path, file_name, file_size}, ...]

CREATE OR REPLACE FUNCTION delete_old_attachments(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS JSONB AS $$
DECLARE
    artifacts_json JSONB;
BEGIN
    -- HARD DELETE artifacts from database and return details for physical file deletion
    WITH deleted_artifacts AS (
        DELETE FROM test_artifacts
        WHERE test_item_id IN (
            SELECT run_id FROM test_items ti
            JOIN launches l ON ti.launch_id = l.id
            WHERE l.project_key = p_project_key
        )
        AND uploaded_at < p_cutoff_date
        RETURNING id, storage_path, file_name, file_size
    )
    SELECT jsonb_agg(
        jsonb_build_object(
            'id', id,
            'storage_path', storage_path,
            'file_name', file_name,
            'file_size', file_size
        )
    ) INTO artifacts_json
    FROM deleted_artifacts;

    -- Return empty array if no artifacts deleted
    IF artifacts_json IS NULL THEN
        artifacts_json := '[]'::jsonb;
    END IF;

    RETURN artifacts_json;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION delete_old_attachments IS
'HARD DELETES test artifacts from database and returns artifact details (id, storage_path, file_name, file_size) as JSONB array for physical file deletion by worker.';

-- ========================================
-- Function: delete_old_audit_entries (NEW)
-- ========================================
-- Deletes audit entries older than cutoff date for a specific project.
-- Audit entries are filtered by project_key extracted from details JSONB.
-- Returns count of deleted audit entries.

CREATE OR REPLACE FUNCTION delete_old_audit_entries(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS INTEGER AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    -- Delete audit entries where details->>'projectKey' matches
    WITH deleted AS (
        DELETE FROM audit_entries
        WHERE timestamp < p_cutoff_date
          AND (details->>'projectKey' = p_project_key
               OR details->>'project_key' = p_project_key
               OR details->>'ProjectKey' = p_project_key)
        RETURNING id
    )
    SELECT COUNT(*) INTO deleted_count FROM deleted;

    RETURN deleted_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION delete_old_audit_entries IS
'Deletes audit entries older than cutoff date for a specific project. Project key extracted from details JSONB field.';
```

**Note**: The function handles multiple casing variations (`projectKey`, `project_key`, `ProjectKey`) since audit entry details are free-form JSONB.

## Docker Configuration

**Add to `docker-compose.yml`:**

```yaml
  housekeeping:
    build:
      context: .
      dockerfile: housekeeping-service/Dockerfile
    container_name: playwright-grid-housekeeping
    restart: unless-stopped
    environment:
      # ========================================
      # DATABASE & CACHE
      # ========================================
      - POSTGRES_CONNECTION_STRING=${POSTGRES_CONNECTION_STRING}
      - REDIS_CONNECTION_STRING=${REDIS_CONNECTION_STRING}

      # ========================================
      # STORAGE (for attachment cleanup)
      # ========================================
      - MINIO_ENDPOINT=${MINIO_ENDPOINT}
      - MINIO_ACCESS_KEY=${MINIO_ACCESS_KEY}
      - MINIO_SECRET_KEY=${MINIO_SECRET_KEY}
      - MINIO_USE_SSL=${MINIO_USE_SSL:-false}

      # ========================================
      # RETENTION CLEANUP INTERVALS
      # ========================================
      - LAUNCH_RETENTION_CHECK_INTERVAL_HOURS=6
      - LOG_RETENTION_CHECK_INTERVAL_HOURS=6
      - ATTACHMENT_RETENTION_CHECK_INTERVAL_HOURS=6
      - AUDIT_RETENTION_CHECK_INTERVAL_HOURS=24  # NEW

      # ========================================
      # LEADERSHIP ELECTION
      # ========================================
      - HOUSEKEEPING_LEADERSHIP=true
      - HOUSEKEEPING_LEASE_SECONDS=30
      - HOUSEKEEPING_INSTANCE_ID=housekeeping-1

    depends_on:
      - postgres
      - redis
      - minio
    ports:
      - "8082:8080"  # Health check and metrics endpoint
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 10s
    networks:
      - playwright-grid

# Retention periods read from Redis per-project:
#   project:{key}:settings → keepLaunches, keepLogs, keepAttachments, keepAudit
```

## Testing Strategy

### Unit Tests

**Test ProjectSettingsReader:**
```csharp
[Test]
public async Task GetAllProjectKeys_ReturnsDistinctProjects()
{
    // Arrange: Redis with 3 projects
    // Act: var projects = await reader.GetAllProjectKeysAsync();
    // Assert: projects.Count == 3
}

[Test]
public async Task GetRetentionSettings_ParsesCorrectly()
{
    // Arrange: Redis with keepLaunches=30, keepLogs=7
    // Act: var settings = await reader.GetRetentionSettingsAsync("test");
    // Assert: settings.KeepLaunchesDays == 30
}
```

**Test LaunchRetentionWorker:**
```csharp
[Test]
public async Task DeleteOldLaunches_DeletesOnlyOldLaunches()
{
    // Arrange: 2 launches (1 old, 1 new)
    // Act: await worker.DeleteOldLaunchesAsync("test", cutoff);
    // Assert: Only old launch deleted
}
```

**Test AuditRetentionWorker (NEW):**
```csharp
[Test]
public async Task DeleteOldAuditEntries_DeletesOnlyOldEntries()
{
    // Arrange: 2 audit entries (1 old, 1 new)
    // Act: await worker.DeleteOldAuditEntriesAsync("test", cutoff);
    // Assert: Only old audit entry deleted
}

[Test]
public async Task DeleteOldAuditEntries_HandlesMultipleCasingVariations()
{
    // Arrange: Audit entries with projectKey, project_key, ProjectKey
    // Act: await worker.DeleteOldAuditEntriesAsync("test", cutoff);
    // Assert: All matching entries deleted regardless of casing
}
```

### Integration Tests

**Test End-to-End Cleanup:**
```bash
# Setup: Create test data
# - Launch finished 35 days ago (project keepLaunches=30)
# - Launch finished 20 days ago (project keepLaunches=30)

# Run: Wait for cleanup service (or manually trigger)

# Verify:
# - Old launch (35 days) DELETED
# - Recent launch (20 days) RETAINED
# - All descendants (test items, logs, artifacts) DELETED for old launch
```

**Test Retention Hierarchy:**
```bash
# Setup: Configure project
# - keepLaunches=30 days
# - keepLogs=7 days
# - keepAttachments=7 days

# Create launch with logs and attachments
# Wait 8 days

# Verify:
# - Logs DELETED (older than 7 days)
# - Attachments DELETED (older than 7 days)
# - Launch RETAINED (less than 30 days old)
# - Launch structure intact (test items, suites exist)
```

### Manual Verification

**Check Database Functions:**
```sql
-- Test delete_old_launches function
SELECT delete_old_launches('admin_default', NOW() - INTERVAL '30 days');

-- Verify launches deleted
SELECT COUNT(*) FROM launches
WHERE project_key = 'admin_default'
  AND finish_time < NOW() - INTERVAL '30 days';
-- Expected: 0

-- Test delete_old_log_items function
SELECT delete_old_log_items('admin_default', NOW() - INTERVAL '7 days');

-- Verify logs deleted
SELECT COUNT(*) FROM log_items li
JOIN test_items ti ON li.test_item_id = ti.run_id
JOIN launches l ON ti.launch_id = l.id
WHERE l.project_key = 'admin_default'
  AND li.timestamp < NOW() - INTERVAL '7 days';
-- Expected: 0

-- Test delete_old_audit_entries function (NEW)
SELECT delete_old_audit_entries('admin_default', NOW() - INTERVAL '90 days');

-- Verify audit entries deleted
SELECT COUNT(*) FROM audit_entries
WHERE timestamp < NOW() - INTERVAL '90 days'
  AND (details->>'projectKey' = 'admin_default'
       OR details->>'project_key' = 'admin_default'
       OR details->>'ProjectKey' = 'admin_default');
-- Expected: 0
```

## Monitoring

### Metrics to Add

```csharp
// In LaunchRetentionWorker
private static readonly Counter LaunchesDeleted = Metrics.CreateCounter(
    "housekeeping_launches_deleted_total",
    "Total number of launches deleted by retention policy",
    new CounterConfiguration { LabelNames = new[] { "project" } });

LaunchesDeleted.WithLabels(projectKey).Inc(deleted);

// In LogRetentionWorker
private static readonly Counter LogItemsDeleted = Metrics.CreateCounter(
    "housekeeping_log_items_deleted_total",
    "Total number of log items deleted by retention policy",
    new CounterConfiguration { LabelNames = new[] { "project" } });

// In AttachmentRetentionWorker
private static readonly Counter AttachmentsDeleted = Metrics.CreateCounter(
    "housekeeping_attachments_deleted_total",
    "Total number of test artifacts deleted by retention policy",
    new CounterConfiguration { LabelNames = new[] { "project" } });

// In AuditRetentionWorker (NEW)
private static readonly Counter AuditEntriesDeleted = Metrics.CreateCounter(
    "housekeeping_audit_entries_deleted_total",
    "Total number of audit entries deleted by retention policy",
    new CounterConfiguration { LabelNames = new[] { "project" } });
```

### Prometheus Queries

```promql
# Launches deleted per day
sum(rate(housekeeping_launches_deleted_total[24h])) by (project)

# Projects with active cleanup
count(housekeeping_launches_deleted_total > 0) by (project)

# Cleanup execution time
housekeeping_tick_duration_seconds{service="launch_retention"}
```

### Alerting Rules

```yaml
# Alert if cleanup fails repeatedly
- alert: HousekeepingCleanupFailing
  expr: housekeeping_tick_errors_total > 10
  for: 1h
  annotations:
    summary: "Housekeeping cleanup failing"
    description: "{{ $labels.service }} has {{ $value }} errors in the last hour"

# Alert if retention queue growing
- alert: RetentionQueueGrowing
  expr: sum(launches{status=~"Finished|Failed"}) by (project) > 10000
  for: 6h
  annotations:
    summary: "Retention queue growing for {{ $labels.project }}"
    description: "{{ $value }} completed launches waiting for cleanup"
```

## Performance Considerations

### Batch Size
- Delete max 100 launches per project per tick (prevent long-running transactions)
- If more than 100, they'll be deleted in next tick (6 hours later)

### Indexes Required
Already exist from V1__init.sql:
```sql
-- launches table
CREATE INDEX idx_launches_project_finish_time ON launches(project_key, finish_time);
CREATE INDEX idx_launches_status ON launches(status);

-- log_items table
CREATE INDEX idx_log_items_timestamp ON log_items(timestamp);
CREATE INDEX idx_log_items_test_item_id ON log_items(test_item_id);

-- test_artifacts table (NEW)
CREATE INDEX idx_test_artifacts_created_at ON test_artifacts(created_at);
CREATE INDEX idx_test_artifacts_deleted_at ON test_artifacts(deleted_at);
```

### Storage Cleanup
Attachment deletion happens in two phases:
1. **Database**: Mark as deleted (`deleted_at` timestamp set)
2. **Storage**: Service queries deleted_at IS NOT NULL, deletes files, then deletes row

This prevents foreign key violations and allows retry on storage failures.

## Build Steps

1. **Create housekeeping-service project structure**
   - HousekeepingService.csproj
   - Program.cs
   - Dockerfile
   - appsettings.json

2. **Create infrastructure layer**
   - DotEnv.cs (copy from ingestion)
   - ProjectSettingsReader.cs
   - RetentionSettings.cs

3. **Create service runner**
   - HousekeepingServiceRunner.cs

4. **Create 4 retention workers**
   - LaunchRetentionWorker.cs
   - LogRetentionWorker.cs
   - AttachmentRetentionWorker.cs
   - AuditRetentionWorker.cs (NEW)

5. **Create V39 database migration**
   - delete_old_launches() function
   - delete_old_log_items() function
   - delete_old_attachments() function
   - delete_old_audit_entries() function (NEW)

6. **Update docker-compose.yml**
   - Add housekeeping service configuration
   - Add environment variables

7. **Update ProjectSettings.razor**
   - Add keepAudit field (default: 90)
   - Add validation: keepAudit >= keepLaunches

8. **Add to PlaywrightGrid.sln**
   - Add housekeeping-service project reference

9. **Build and test**
   - Unit tests for workers
   - Integration tests for retention cleanup
   - Manual verification of database functions

10. **Deploy and monitor**
   - Deploy to staging
   - Verify metrics in Prometheus/Grafana
   - Monitor logs for errors

**Time Estimate**: 6-8 hours

## Success Criteria

- ✅ **housekeeping-service** builds without errors
- ✅ All 4 retention workers compile successfully (Launch, Log, Attachment, Audit)
- ✅ ProjectSettingsReader correctly reads per-project settings from Redis
- ✅ All 4 database cleanup functions execute without errors
- ✅ Old launches deleted after retention period (cascade to descendants)
- ✅ Old logs deleted independently (launch structure retained)
- ✅ Old attachments deleted from storage + database
- ✅ Old audit entries deleted after retention period (NEW)
- ✅ Retention hierarchy enforced (Attachments ≤ Logs ≤ Launches ≤ Audit)
- ✅ Leader election works (only one instance runs cleanup per worker)
- ✅ Metrics exported to Prometheus (4 counters)
- ✅ No foreign key violations or constraint errors
- ✅ Docker container starts successfully and passes health checks
- ✅ ProjectSettings.razor includes keepAudit field with validation

## Key Differences from Hub Background Services

| Aspect | Hub Background Services | Housekeeping Service |
|--------|------------------------|---------------------|
| **Architecture** | In-process (part of hub) | Standalone microservice |
| **Scaling** | Scales with hub | Independent scaling |
| **Resource Isolation** | Shares hub resources | Isolated resources |
| **Deployment** | Deployed with hub | Separate deployment |
| **Dependencies** | Tightly coupled to hub | Loose coupling (Redis + PostgreSQL) |
| **Leadership** | Redis-based (same pattern) | Redis-based (same pattern) |
| **Pattern** | Follows ingestion service | Follows ingestion service |

## Next Steps

After Phase 2 is complete, proceed to:
- **Phase 3**: Comprehensive testing and production rollout
  - Load testing with large datasets
  - Verify retention hierarchy enforcement
  - Monitor metrics and alerting
  - Document operational procedures
