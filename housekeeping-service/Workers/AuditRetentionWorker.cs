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
///     Background worker that deletes old audit entries.
/// </summary>
public sealed class AuditRetentionWorker(
    IConfiguration config,
    IHousekeepingDataSource dataSource,
    IProjectSettingsReader settingsReader,
    IDatabase db,
    ILogger<AuditRetentionWorker> logger,
    ChunkedLogger<AuditRetentionWorker> chunkedLogger) : HousekeepingWorkerBase(chunkedLogger, nameof(AuditRetentionWorker))
{
    private readonly IConfiguration _config = config;
    private readonly IHousekeepingDataSource _dataSource = dataSource;
    private readonly IDatabase _db = db;
    private readonly ILogger _logger = logger;
    private readonly IProjectSettingsReader _settingsReader = settingsReader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours =
            int.TryParse(_config["AGENIX_HOUSEKEEPING_AUDIT_RETENTION_CHECK_INTERVAL_HOURS"], out var h)
                ? Math.Max(1, h)
                : 24;
        var interval = TimeSpan.FromHours(intervalHours);

        using var op = BeginWorkerOperation("ExecuteAsync", new Dictionary<string, object>
        {
            ["intervalHours"] = interval.TotalHours
        });

        LogWorkerMilestone(
            EventCodes.Housekeeping.AuditRetentionStarted,
            "Audit retention worker started with interval={IntervalHours}h", interval.TotalHours);

        var leadershipEnabled = string.Equals(_config["AGENIX_HOUSEKEEPING_LEADERSHIP"], "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["AGENIX_HOUSEKEEPING_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["AGENIX_HOUSEKEEPING_INSTANCE_ID"] ??
                         $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "housekeeping:leader:audit_retention";

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
                        _logger.LogDebug("[AuditRetention] Not leader, skipping tick");
                        tickOp.SetOutputs(new Dictionary<string, object> { ["isLeader"] = false });
                        tickOp.Complete();
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.AuditRetentionStarted,
                    "Starting audit retention check");

                await using var conn = await _dataSource.OpenConnectionAsync(stoppingToken);
                var projectKeys = await _settingsReader.GetAllProjectKeysAsync();

                LogWorkerMilestone(
                    EventCodes.Housekeeping.AuditRetentionStarted,
                    "Retrieved {Count} projects for scanning", projectKeys.Count);

                var totalDeleted = 0;

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
                        if (settings == null || settings.KeepAuditDays <= 0)
                        {
                            LogWorkerMilestone(
                                EventCodes.Housekeeping.AuditRetentionStarted,
                                "Skipping project {ProjectKey} (audit retention disabled or no settings)", projectKey);
                            projectOp.Complete();
                            continue;
                        }

                        var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepAuditDays);
                        LogWorkerMilestone(
                            EventCodes.Housekeeping.AuditRetentionStarted,
                            "Processing project {ProjectKey}: deleting audit entries older than {CutoffDate:yyyy-MM-dd}",
                            projectKey, cutoffDate);

                        var deleted = await DeleteOldAuditEntriesAsync(conn, projectKey, cutoffDate, stoppingToken);
                        totalDeleted += deleted;

                        LogWorkerMilestone(
                            EventCodes.Housekeeping.AuditEntriesDeleted,
                            "Deleted {Deleted} audit entries from project {ProjectKey}", deleted, projectKey);

                        projectOp.SetOutputs(new Dictionary<string, object>
                        {
                            ["projectKey"] = projectKey,
                            ["deletedCount"] = deleted,
                            ["retentionDays"] = settings.KeepAuditDays,
                            ["cutoffDate"] = cutoffDate
                        });
                        projectOp.Complete();
                    }
                    catch (Exception exProject)
                    {
                        projectOp.Fail(exProject, ErrorType.Unexpected);
                        _logger.LogWarning(exProject, "[AuditRetention] Error processing project {ProjectKey}", projectKey);
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.AuditRetentionCompleted,
                    "Audit retention check completed: deleted {TotalDeleted} entries across {ProjectCount} projects",
                    totalDeleted, projectKeys.Count);

                tickOp.SetOutputs(new Dictionary<string, object>
                {
                    ["totalDeleted"] = totalDeleted,
                    ["projectCount"] = projectKeys.Count,
                    ["isLeader"] = true,
                    ["success"] = true
                });
                tickOp.Complete();
            }
            catch (PostgresException ex)
            {
                tickOp.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
                _logger.LogError(ex, "[AuditRetention] Database error during retention check");
            }
            catch (Exception ex)
            {
                tickOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogError(ex, "[AuditRetention] Unexpected error during retention check");
            }

            await Task.Delay(interval, stoppingToken);
        }

        op.SetOutputs(new Dictionary<string, object> { ["stopped"] = true });
        op.Complete();
    }

    private async Task<int> DeleteOldAuditEntriesAsync(
        System.Data.Common.DbConnection conn,
        string projectKey,
        DateTime cutoffDate,
        CancellationToken ct)
    {
        const string sql = @"
            DELETE FROM audit_entries
            WHERE project_key = @projectKey
            AND timestamp < @cutoffDate";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("projectKey", projectKey);
        cmd.AddParameter("cutoffDate", cutoffDate);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
