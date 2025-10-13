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
using HousekeepingService.Infrastructure;
using HousekeepingService.Shared;
using Npgsql;
using StackExchange.Redis;

namespace HousekeepingService.Workers;

/// <summary>
///     Background worker that deletes old artifact files from storage (MinIO/S3 or local filesystem).
/// </summary>
public sealed class AttachmentRetentionWorker(
    IConfiguration config,
    IHousekeepingDataSource dataSource,
    IProjectSettingsReader settingsReader,
    IDatabase db,
    IMinioStorageService? minioService,
    ILogger<AttachmentRetentionWorker> logger,
    ChunkedLogger<AttachmentRetentionWorker> chunkedLogger) : HousekeepingWorkerBase(chunkedLogger, nameof(AttachmentRetentionWorker))
{
    private readonly IConfiguration _config = config;
    private readonly IHousekeepingDataSource _dataSource = dataSource;
    private readonly IDatabase _db = db;
    private readonly IMinioStorageService? _minioService = minioService;
    private readonly ILogger _logger = logger;
    private readonly IProjectSettingsReader _settingsReader = settingsReader;
    private readonly bool _useMinIO = config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local") == "minio";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours =
            int.TryParse(_config["AGENIX_HOUSEKEEPING_ATTACHMENT_RETENTION_CHECK_INTERVAL_HOURS"], out var h)
                ? Math.Max(1, h)
                : 6;
        var interval = TimeSpan.FromHours(intervalHours);

        using var op = BeginWorkerOperation("ExecuteAsync", new Dictionary<string, object>
        {
            ["intervalHours"] = interval.TotalHours,
            ["storageBackend"] = _useMinIO ? "minio" : "local"
        });

        LogWorkerMilestone(
            EventCodes.Housekeeping.ArtifactRetentionStarted,
            "Attachment retention worker started with interval={IntervalHours}h, backend={Backend}",
            interval.TotalHours, _useMinIO ? "minio" : "local");

        var leadershipEnabled = string.Equals(_config["AGENIX_HOUSEKEEPING_LEADERSHIP"], "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["AGENIX_HOUSEKEEPING_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["AGENIX_HOUSEKEEPING_INSTANCE_ID"] ??
                         $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "housekeeping:leader:attachment_retention";

        while (!stoppingToken.IsCancellationRequested)
        {
            using var tickOp = BeginWorkerOperation("RetentionTick");

            try
            {
                if (leadershipEnabled)
                {
                    var leaseAcquired = await _db.StringSetAsync(leaderKey, instanceId, TimeSpan.FromSeconds(leaseSeconds),
                        When.NotExists);
                    if (!leaseAcquired)
                    {
                        _logger.LogDebug("[AttachmentRetention] Not leader, skipping tick");
                        tickOp.SetOutputs(new Dictionary<string, object> { ["isLeader"] = false });
                        tickOp.Complete();
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.ArtifactRetentionStarted,
                    "Starting attachment retention check");

                await using var conn = await _dataSource.OpenConnectionAsync(stoppingToken);
                var projectKeys = await _settingsReader.GetAllProjectKeysAsync();

                LogWorkerMilestone(
                    EventCodes.Housekeeping.ArtifactRetentionStarted,
                    "Retrieved {Count} projects for scanning", projectKeys.Count);

                var totalDeleted = 0;
                var totalBytesFreed = 0L;

                foreach (var projectKey in projectKeys)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    using var projectOp = BeginWorkerOperation("ProcessProject", new Dictionary<string, object>
                    {
                        ["projectKey"] = projectKey
                    });

                    try
                    {
                        var settings = await _settingsReader.GetRetentionSettingsAsync(projectKey);
                        if (settings == null || settings.KeepAttachmentsDays <= 0)
                        {
                            LogWorkerMilestone(
                                EventCodes.Housekeeping.ArtifactRetentionStarted,
                                "Skipping project {ProjectKey} (artifact retention disabled or no settings)", projectKey);
                            projectOp.Complete();
                            continue;
                        }

                        var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepAttachmentsDays);
                        var (deleted, bytesFreed) = await DeleteOldAttachmentsForProjectAsync(
                            conn, projectKey, cutoffDate, stoppingToken);

                        totalDeleted += deleted;
                        totalBytesFreed += bytesFreed;

                        LogWorkerMilestone(
                            EventCodes.Housekeeping.ArtifactsDeleted,
                            "Deleted {Deleted} artifacts ({BytesFreed} bytes) from project {ProjectKey}",
                            deleted, bytesFreed, projectKey);

                        projectOp.SetOutputs(new Dictionary<string, object>
                        {
                            ["deletedCount"] = deleted,
                            ["bytesFreed"] = bytesFreed,
                            ["projectKey"] = projectKey,
                            ["retentionDays"] = settings.KeepAttachmentsDays,
                            ["cutoffDate"] = cutoffDate
                        });
                        projectOp.Complete();
                    }
                    catch (Exception exProject)
                    {
                        projectOp.Fail(exProject, ErrorType.Unexpected);
                        _logger.LogWarning(exProject, "[AttachmentRetention] Error processing project {ProjectKey}", projectKey);
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.ArtifactRetentionCompleted,
                    "Attachment retention check completed: deleted {TotalDeleted} artifacts ({TotalBytes} bytes)",
                    totalDeleted, totalBytesFreed);

                tickOp.SetOutputs(new Dictionary<string, object>
                {
                    ["totalDeleted"] = totalDeleted,
                    ["totalBytesFreed"] = totalBytesFreed,
                    ["isLeader"] = true,
                    ["success"] = true
                });
                tickOp.Complete();
            }
            catch (Exception ex) when (IsStorageException(ex))
            {
                tickOp.Fail(ex, ErrorType.DependencyFailure, _useMinIO ? DependencyName.MinIO : DependencyName.FileSystem);
                _logger.LogError(ex, "[AttachmentRetention] Storage error during retention check");
            }
            catch (PostgresException ex)
            {
                tickOp.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
                _logger.LogError(ex, "[AttachmentRetention] Database error during retention check");
            }
            catch (Exception ex)
            {
                tickOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogError(ex, "[AttachmentRetention] Unexpected error during retention check");
            }

            await Task.Delay(interval, stoppingToken);
        }

        op.SetOutputs(new Dictionary<string, object> { ["stopped"] = true });
        op.Complete();
    }

    private async Task<(int deleted, long bytesFreed)> DeleteOldAttachmentsForProjectAsync(
        System.Data.Common.DbConnection conn,
        string projectKey,
        DateTime cutoffDate,
        CancellationToken ct)
    {
        // Get attachments to delete
        const string sql = @"
            SELECT art.id, art.storage_path, art.file_size
            FROM test_artifacts art
            JOIN test_items itm ON art.test_item_id = itm.run_id
            JOIN launches l ON itm.launch_id = l.id
            WHERE l.project_key = @projectKey
            AND art.uploaded_at < @cutoffDate";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("projectKey", projectKey);
        cmd.AddParameter("cutoffDate", cutoffDate);

        var artifactsToDelete = new List<(Guid id, string storagePath, long fileSize)>();

        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                artifactsToDelete.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2)));
            }
        }

        var deletedCount = 0;
        long bytesFreed = 0;

        foreach (var artifact in artifactsToDelete)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (_useMinIO && _minioService != null)
                {
                    // Handle prefixed paths if they exist
                    var cleanPath = artifact.storagePath.Replace("s3://", "").Replace("minio://", "");
                    // If it still contains a bucket name (from original complex logic), we might need to handle it,
                    // but MinioStorageService assumes the bucket is already configured.
                    // Let's try to just use the object key.
                    var parts = cleanPath.Split('/', 2);
                    var objectKey = parts.Length == 2 ? parts[1] : cleanPath;

                    await _minioService.DeleteArtifactAsync(objectKey, ct);

                    LogWorkerMilestone(
                        EventCodes.Housekeeping.ArtifactsDeleted,
                        "Deleted artifact from MinIO: {Path}", artifact.storagePath);
                }
                else
                {
                    // Delete from local filesystem
                    var basePath = _config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts") ?? "./data/artifacts";
                    var fullPath = Path.Combine(basePath, artifact.storagePath);

                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        LogWorkerMilestone(
                            EventCodes.Housekeeping.ArtifactsDeleted,
                            "Deleted artifact file: {Path}", fullPath);
                    }
                    else if (Path.IsPathRooted(artifact.storagePath) && File.Exists(artifact.storagePath))
                    {
                        File.Delete(artifact.storagePath);
                        LogWorkerMilestone(
                            EventCodes.Housekeeping.ArtifactsDeleted,
                            "Deleted rooted artifact file: {Path}", artifact.storagePath);
                    }
                }

                // Delete from database
                using var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM test_artifacts WHERE id = @artifactId";
                deleteCmd.AddParameter("artifactId", artifact.id);
                await deleteCmd.ExecuteNonQueryAsync(ct);

                deletedCount++;
                bytesFreed += artifact.fileSize;
            }
            catch (Exception ex)
            {
                LogWorkerMilestone(
                    EventCodes.Housekeeping.ArtifactRetentionStarted,
                    "Failed to delete artifact {ArtifactId}: {Message}", artifact.id, ex.Message);
            }
        }

        return (deletedCount, bytesFreed);
    }

    private bool IsStorageException(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("minio", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("s3", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("file", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("storage", StringComparison.OrdinalIgnoreCase);
    }
}
