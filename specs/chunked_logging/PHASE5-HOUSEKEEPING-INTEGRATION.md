# Phase 5: Housekeeping Service Integration - Chunked Logging

## Overview

Phase 5 integrates chunked logging into the Housekeeping service, providing operation-scoped logging for retention cleanup workers (launches, logs, artifacts, audit).

## Status: 📋 PLANNED

**Dependencies**: Phase 1 ✅, Phase 2-4 ⏳
**Timeline**: 1 hour
**Impact**: Housekeeping service only

---

## Goals

1. **Retention Worker Ticks** - Each cleanup tick as a discrete operation
2. **Deletion Tracking** - Track items deleted, Redis keys cleaned, files removed
3. **Performance Metrics** - Cleanup duration, throughput, batch sizes
4. **Error Classification** - Database timeouts, storage failures, etc.

---

## Implementation Plan

### 5.1 - LaunchRetentionWorker

#### File: `housekeeping-service/Workers/LaunchRetentionWorker.cs` (MODIFY)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(LaunchRetentionWorker));

    while (!stoppingToken.IsCancellationRequested)
    {
        using var op = chunkedLogger.BeginOperation("LaunchRetention:Tick");

        try
        {
            var projects = await GetProjectsWithRetentionSettingsAsync(stoppingToken);

            chunkedLogger.LogMilestone(
                EventCodes.Housekeeping.LaunchRetentionStarted,
                "projectCount={Count}",
                projects.Count);

            int totalDeleted = 0;

            foreach (var project in projects)
            {
                var settings = await GetRetentionSettingsAsync(project.Key);
                var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepLaunchesDays);

                var deleted = await DeleteOldLaunchesAsync(project.Key, cutoffDate, stoppingToken);

                if (deleted > 0)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Housekeeping.LaunchesDeleted,
                        "projectKey={ProjectKey} count={Count} cutoff={Cutoff}",
                        project.Key, deleted, cutoffDate);

                    totalDeleted += deleted;
                }
            }

            chunkedLogger.LogMilestone(
                EventCodes.Housekeeping.LaunchRetentionCompleted,
                "totalDeleted={Total} projects={Projects}",
                totalDeleted, projects.Count);

            var outputs = new Dictionary<string, object>
            {
                ["launchesDeleted"] = totalDeleted,
                ["projectsProcessed"] = projects.Count
            };

            ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
        {
            ((ChunkedLogger.OperationScope)op).Fail(
                ex,
                ErrorType.Timeout,
                DependencyName.Database);
        }
        catch (Exception ex)
        {
            ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.Unexpected);
        }

        await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
    }
}
```

**Expected Output**:
```
╔═ Operation: LaunchRetention:Tick  OperationId=abc...
║ Start: 2025-12-23T04:00:00.000Z
║
║ [INF][HKEP01] Launch retention check started - projectCount=5
║ [INF][HKEP02] Launches deleted - projectKey=admin_default count=12 cutoff=2024-11-23
║ [INF][HKEP02] Launches deleted - projectKey=project_a count=5 cutoff=2024-11-23
║ [INF][HKEP03] Launch retention completed - totalDeleted=17 projects=5
║
╚═ End: SUCCESS  Duration=1.2s  launchesDeleted=17 projectsProcessed=5  KeyEvents=[HKEP01,HKEP02,HKEP03]
```

---

### 5.2 - LogRetentionWorker

#### File: `housekeeping-service/Workers/LogRetentionWorker.cs` (MODIFY)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(LogRetentionWorker));

    while (!stoppingToken.IsCancellationRequested)
    {
        using var op = chunkedLogger.BeginOperation("LogRetention:Tick");

        try
        {
            var projects = await GetProjectsWithRetentionSettingsAsync(stoppingToken);

            chunkedLogger.LogMilestone(
                EventCodes.Housekeeping.LogRetentionStarted,
                "projectCount={Count}",
                projects.Count);

            int totalLogItems = 0, totalLogTokens = 0, totalCommandTokens = 0;

            foreach (var project in projects)
            {
                var settings = await GetRetentionSettingsAsync(project.Key);
                var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepLogsDays);

                var (logItems, logTokens, commandTokens, deletedLogHashes, deletedCmdHashes) =
                    await DeleteOldLogItemsAsync(project.Key, cutoffDate, stoppingToken);

                if (logItems > 0)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Housekeeping.LogItemsDeleted,
                        "projectKey={ProjectKey} logItems={LogItems} logTokens={LogTokens} cmdTokens={CmdTokens}",
                        project.Key, logItems, logTokens, commandTokens);

                    // Clean Redis keys for orphaned tokens
                    if (deletedLogHashes.Length > 0)
                    {
                        await CleanRedisTokensAsync(deletedLogHashes, "log_token");
                    }

                    if (deletedCmdHashes.Length > 0)
                    {
                        await CleanRedisTokensAsync(deletedCmdHashes, "command_token");
                    }

                    chunkedLogger.LogMilestone(
                        EventCodes.Housekeeping.OrphanedTokensCleaned,
                        "logTokens={LogTokens} commandTokens={CommandTokens}",
                        deletedLogHashes.Length, deletedCmdHashes.Length);

                    totalLogItems += logItems;
                    totalLogTokens += logTokens;
                    totalCommandTokens += commandTokens;
                }
            }

            chunkedLogger.LogMilestone(
                EventCodes.Housekeeping.LogRetentionCompleted,
                "logItems={LogItems} logTokens={LogTokens} commandTokens={CommandTokens}",
                totalLogItems, totalLogTokens, totalCommandTokens);

            var outputs = new Dictionary<string, object>
            {
                ["logItemsDeleted"] = totalLogItems,
                ["logTokensDeleted"] = totalLogTokens,
                ["commandTokensDeleted"] = totalCommandTokens
            };

            ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
        }
        catch (Exception ex)
        {
            ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.Unexpected);
        }

        await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
    }
}
```

**Expected Output**:
```
╔═ Operation: LogRetention:Tick  OperationId=def...
║ Start: 2025-12-23T05:00:00.000Z
║
║ [INF][HKEP10] Log retention check started - projectCount=5
║ [INF][HKEP11] Log items deleted - projectKey=admin_default logItems=1543 logTokens=89 cmdTokens=12
║ [INF][HKEP12] Orphaned tokens cleaned - logTokens=89 commandTokens=12
║ [INF][HKEP13] Log retention completed - logItems=1543 logTokens=89 commandTokens=12
║
╚═ End: SUCCESS  Duration=2.8s  KeyEvents=[HKEP10,HKEP11,HKEP12,HKEP13]
```

---

### 5.3 - AttachmentRetentionWorker

#### File: `housekeeping-service/Workers/AttachmentRetentionWorker.cs` (MODIFY)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(AttachmentRetentionWorker));

    while (!stoppingToken.IsCancellationRequested)
    {
        using var op = chunkedLogger.BeginOperation("AttachmentRetention:Tick");

        try
        {
            var projects = await GetProjectsWithRetentionSettingsAsync(stoppingToken);

            chunkedLogger.LogMilestone(
                EventCodes.Housekeeping.ArtifactRetentionStarted,
                "projectCount={Count}",
                projects.Count);

            int totalArtifacts = 0, totalPhysicalFiles = 0;

            foreach (var project in projects)
            {
                var settings = await GetRetentionSettingsAsync(project.Key);
                var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepAttachmentsDays);

                var artifacts = await DeleteOldArtifactsAsync(project.Key, cutoffDate, stoppingToken);

                if (artifacts.Length > 0)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Housekeeping.ArtifactsDeleted,
                        "projectKey={ProjectKey} count={Count}",
                        project.Key, artifacts.Length);

                    // Delete physical files
                    int filesDeleted = 0;
                    foreach (var artifact in artifacts)
                    {
                        var deleted = await DeletePhysicalFileAsync(artifact.StoragePath);
                        if (deleted) filesDeleted++;
                    }

                    chunkedLogger.LogMilestone(
                        EventCodes.Housekeeping.PhysicalFilesDeleted,
                        "projectKey={ProjectKey} filesDeleted={Files} totalSize={Size}MB",
                        project.Key, filesDeleted, artifacts.Sum(a => a.FileSize) / (1024 * 1024));

                    totalArtifacts += artifacts.Length;
                    totalPhysicalFiles += filesDeleted;
                }
            }

            chunkedLogger.LogMilestone(
                EventCodes.Housekeeping.ArtifactRetentionCompleted,
                "artifactsDeleted={Artifacts} filesDeleted={Files}",
                totalArtifacts, totalPhysicalFiles);

            var outputs = new Dictionary<string, object>
            {
                ["artifactsDeleted"] = totalArtifacts,
                ["filesDeleted"] = totalPhysicalFiles
            };

            ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
        }
        catch (MinioException ex)
        {
            ((ChunkedLogger.OperationScope)op).Fail(
                ex,
                ErrorType.DependencyFailure,
                DependencyName.MinIO);
        }
        catch (Exception ex)
        {
            ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.Unexpected);
        }

        await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
    }
}
```

---

### 5.4 - Serilog Configuration

#### File: `housekeeping-service/appsettings.json` (MODIFY)

```json
{
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Sinks.File",
      "Agenix.PlaywrightGrid.Shared"
    ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "ChunkedConsole",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "/tmp/pg-housekeeping-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithMachineName",
      "WithThreadId",
      "WithOperationContext",
      "WithEventCode"
    ]
  }
}
```

---

## Testing Phase 5

```bash
# Start housekeeping with chunked logging
export AGENIX_LOGGING_CHUNKED_ENABLED=true
dotnet run --project housekeeping-service
```

**Expected Console Output** (on tick):

```
╔═ Operation: LaunchRetention:Tick  OperationId=abc...
║ [INF][HKEP01] Launch retention check started - projectCount=3
║ [INF][HKEP02] Launches deleted - projectKey=admin_default count=8
║ [INF][HKEP03] Launch retention completed - totalDeleted=8
╚═ End: SUCCESS  Duration=1.1s  KeyEvents=[HKEP01,HKEP02,HKEP03]
```

---

## Success Criteria

- [ ] All retention workers log ticks as discrete operations
- [ ] Deletion counts tracked with event codes
- [ ] Physical file deletions logged separately
- [ ] Redis cleanup operations visible
- [ ] Database timeouts classified correctly
- [ ] Storage failures (MinIO) classified correctly
- [ ] All operations have duration and summary

---

**Status**: 📋 PLANNED
**Estimated Effort**: 1 hour
**Dependencies**: Phase 1 ✅, Phase 2-4 ⏳
