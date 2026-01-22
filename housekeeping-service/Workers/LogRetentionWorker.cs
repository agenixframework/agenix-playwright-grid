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
///     Background worker that deletes old log items based on retention settings.
/// </summary>
public sealed class LogRetentionWorker(
    IConfiguration config,
    IHousekeepingDataSource dataSource,
    IProjectSettingsReader settingsReader,
    IDatabase db,
    ILogger<LogRetentionWorker> logger,
    ChunkedLogger<LogRetentionWorker> chunkedLogger) : HousekeepingWorkerBase(chunkedLogger, nameof(LogRetentionWorker))
{
    private readonly IConfiguration _config = config;
    private readonly IHousekeepingDataSource _dataSource = dataSource;
    private readonly IDatabase _db = db;
    private readonly ILogger _logger = logger;
    private readonly IProjectSettingsReader _settingsReader = settingsReader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours =
            int.TryParse(_config["AGENIX_HOUSEKEEPING_LOG_RETENTION_CHECK_INTERVAL_HOURS"], out var h)
                ? Math.Max(1, h)
                : 1;
        var interval = TimeSpan.FromHours(intervalHours);

        using var op = BeginWorkerOperation("ExecuteAsync", new Dictionary<string, object>
        {
            ["intervalHours"] = interval.TotalHours
        });

        LogWorkerMilestone(
            EventCodes.Housekeeping.LogRetentionStarted,
            "Log retention worker started with interval={IntervalHours}h", interval.TotalHours);

        var leadershipEnabled = string.Equals(_config["AGENIX_HOUSEKEEPING_LEADERSHIP"], "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["AGENIX_HOUSEKEEPING_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["AGENIX_HOUSEKEEPING_INSTANCE_ID"] ??
                         $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "housekeeping:leader:log_retention";

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
                        _logger.LogDebug("[LogRetention] Not leader, skipping tick");
                        tickOp.SetOutputs(new Dictionary<string, object> { ["isLeader"] = false });
                        tickOp.Complete();
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LogRetentionStarted,
                    "Starting log retention check");

                await using var conn = await _dataSource.OpenConnectionAsync(stoppingToken);
                var projectKeys = await _settingsReader.GetAllProjectKeysAsync();

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LogRetentionStarted,
                    "Retrieved {Count} projects for scanning", projectKeys.Count);

                var totalDeleted = 0;

                // Delete logs by project
                foreach (var projectKey in projectKeys)
                {
                    using var projectOp = BeginWorkerOperation("ProcessProject", new Dictionary<string, object>
                    {
                        ["projectKey"] = projectKey
                    });

                    try
                    {
                        var settings = await _settingsReader.GetRetentionSettingsAsync(projectKey);
                        if (settings == null || settings.KeepLogsDays <= 0)
                        {
                            LogWorkerMilestone(
                                EventCodes.Housekeeping.LogRetentionStarted,
                                "Skipping project {ProjectKey} (log retention disabled or no settings)", projectKey);
                            projectOp.Complete();
                            continue;
                        }

                        var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepLogsDays);
                        var deleted = await DeleteOldLogItemsForProjectAsync(
                            conn, projectKey, cutoffDate, stoppingToken);

                        totalDeleted += deleted;
                        LogWorkerMilestone(
                            EventCodes.Housekeeping.LogItemsDeleted,
                            "Deleted {Deleted} log items from project {ProjectKey}", deleted, projectKey);

                        projectOp.SetOutputs(new Dictionary<string, object>
                        {
                            ["deletedCount"] = deleted,
                            ["projectKey"] = projectKey,
                            ["retentionDays"] = settings.KeepLogsDays,
                            ["cutoffDate"] = cutoffDate
                        });
                        projectOp.Complete();
                    }
                    catch (Exception exProject)
                    {
                        projectOp.Fail(exProject, ErrorType.Unexpected);
                        _logger.LogWarning(exProject, "[LogRetention] Error processing project {ProjectKey}", projectKey);
                    }
                }

                // Delete orphaned log tokens
                var tokensDeleted = await DeleteOrphanedLogTokensAsync(conn, stoppingToken);
                totalDeleted += tokensDeleted;

                if (tokensDeleted > 0)
                {
                    LogWorkerMilestone(
                        EventCodes.Housekeeping.OrphanedTokensCleaned,
                        "Cleaned {Count} orphaned log tokens", tokensDeleted);
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LogRetentionCompleted,
                    "Log retention check completed: deleted {TotalDeleted} log items and tokens",
                    totalDeleted);

                tickOp.SetOutputs(new Dictionary<string, object>
                {
                    ["totalDeleted"] = totalDeleted,
                    ["isLeader"] = true,
                    ["success"] = true
                });
                tickOp.Complete();
            }
            catch (PostgresException ex)
            {
                tickOp.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
                _logger.LogError(ex, "[LogRetention] Database error during retention check");
            }
            catch (Exception ex)
            {
                tickOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogError(ex, "[LogRetention] Unexpected error during retention check");
            }

            await Task.Delay(interval, stoppingToken);
        }

        op.SetOutputs(new Dictionary<string, object> { ["stopped"] = true });
        op.Complete();
    }

    private async Task<int> DeleteOldLogItemsForProjectAsync(
        System.Data.Common.DbConnection conn,
        string projectKey,
        DateTime cutoffDate,
        CancellationToken ct)
    {
        // Delete log items for launches older than cutoff
        const string sql = @"
            DELETE FROM log_items
            WHERE launch_uuid IN (
                SELECT id FROM launches
                WHERE project_key = @projectKey
                AND start_time < @cutoffDate
            )";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("projectKey", projectKey);
        cmd.AddParameter("cutoffDate", cutoffDate);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<int> DeleteOrphanedLogTokensAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        // Delete tokens that have no associated log items
        const string sql = @"
            DELETE FROM log_tokens lt
            WHERE NOT EXISTS (
                SELECT 1 FROM log_items li
                WHERE li.token_hash = lt.token_hash
            )";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
