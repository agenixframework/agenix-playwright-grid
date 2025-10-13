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
///     Background worker that periodically deletes old launches based on project retention settings.
/// </summary>
public sealed class LaunchRetentionWorker(
    IConfiguration config,
    IHousekeepingDataSource dataSource,
    IProjectSettingsReader settingsReader,
    IDatabase db,
    ILogger<LaunchRetentionWorker> logger,
    ChunkedLogger<LaunchRetentionWorker> chunkedLogger) : HousekeepingWorkerBase(chunkedLogger, nameof(LaunchRetentionWorker))
{
    private readonly IConfiguration _config = config;
    private readonly IHousekeepingDataSource _dataSource = dataSource;
    private readonly IDatabase _db = db;
    private readonly ILogger _logger = logger;
    private readonly IProjectSettingsReader _settingsReader = settingsReader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours =
            int.TryParse(_config["AGENIX_HOUSEKEEPING_LAUNCH_RETENTION_CHECK_INTERVAL_HOURS"], out var h)
                ? Math.Max(1, h)
                : 6;
        var interval = TimeSpan.FromHours(intervalHours);

        using var op = BeginWorkerOperation("ExecuteAsync", new Dictionary<string, object>
        {
            ["intervalHours"] = interval.TotalHours
        });

        LogWorkerMilestone(
            EventCodes.Housekeeping.LaunchRetentionStarted,
            "Launch retention worker started with interval={IntervalHours}h", interval.TotalHours);

        var leadershipEnabled = string.Equals(_config["AGENIX_HOUSEKEEPING_LEADERSHIP"], "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["AGENIX_HOUSEKEEPING_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["AGENIX_HOUSEKEEPING_INSTANCE_ID"] ??
                         $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "housekeeping:leader:launch_retention";

        if (leadershipEnabled)
        {
            _logger.LogInformation(
                "[LaunchRetention] Leadership enabled. key={LeaderKey} lease={LeaseSeconds}s instance={InstanceId}",
                leaderKey, leaseSeconds, instanceId);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var tickOp = BeginWorkerOperation("RetentionTick", new Dictionary<string, object>
            {
                ["intervalHours"] = interval.TotalHours,
                ["tickCount"] = DateTime.UtcNow.Ticks
            });

            try
            {
                if (leadershipEnabled)
                {
                    var leaseAcquired = await _db.StringSetAsync(leaderKey, instanceId, TimeSpan.FromSeconds(leaseSeconds),
                        When.NotExists);
                    if (!leaseAcquired)
                    {
                        _logger.LogDebug("[LaunchRetention] Not leader, skipping tick");
                        tickOp.SetOutputs(new Dictionary<string, object> { ["isLeader"] = false });
                        tickOp.Complete();
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LaunchRetentionStarted,
                    "Starting launch retention check");

                await using var conn = await _dataSource.OpenConnectionAsync(stoppingToken);
                var projectKeys = await _settingsReader.GetAllProjectKeysAsync();

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LaunchRetentionStarted,
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
                        if (settings == null || settings.KeepLaunchesDays <= 0)
                        {
                            LogWorkerMilestone(
                                EventCodes.Housekeeping.LaunchRetentionStarted,
                                "Skipping project {ProjectKey} (retention disabled or no settings)", projectKey);
                            projectOp.Complete();
                            continue;
                        }

                        var cutoffDate = DateTime.UtcNow.AddDays(-settings.KeepLaunchesDays);
                        LogWorkerMilestone(
                            EventCodes.Housekeeping.LaunchRetentionStarted,
                            "Processing project {ProjectKey}: deleting launches older than {CutoffDate:yyyy-MM-dd}",
                            projectKey, cutoffDate);

                        var deleted = await DeleteOldLaunchesForProjectAsync(conn, projectKey, cutoffDate, stoppingToken);
                        totalDeleted += deleted;

                        LogWorkerMilestone(
                            EventCodes.Housekeeping.LaunchesDeleted,
                            "Deleted {Deleted} launches from project {ProjectKey}", deleted, projectKey);

                        projectOp.SetOutputs(new Dictionary<string, object>
                        {
                            ["projectKey"] = projectKey,
                            ["deletedCount"] = deleted,
                            ["retentionDays"] = settings.KeepLaunchesDays,
                            ["cutoffDate"] = cutoffDate
                        });
                        projectOp.Complete();
                    }
                    catch (Exception exProject)
                    {
                        projectOp.Fail(exProject, ErrorType.Unexpected);
                        _logger.LogWarning(exProject, "[LaunchRetention] Error processing project {ProjectKey}", projectKey);
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LaunchRetentionCompleted,
                    "Launch retention check completed: deleted {TotalDeleted} launches across {ProjectCount} projects",
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
                _logger.LogError(ex, "[LaunchRetention] Database error during retention check");
            }
            catch (Exception ex)
            {
                tickOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogError(ex, "[LaunchRetention] Unexpected error during retention check");
            }

            await Task.Delay(interval, stoppingToken);
        }

        op.SetOutputs(new Dictionary<string, object> { ["stopped"] = true });
        op.Complete();
    }

    private async Task<int> DeleteOldLaunchesForProjectAsync(
        System.Data.Common.DbConnection conn,
        string projectKey,
        DateTime cutoffDate,
        CancellationToken ct)
    {
        // First, delete child test items (if not handled by cascade)
        // Based on the implementation plan, we should do it explicitly
        const string deleteItemsSql = @"
            DELETE FROM test_items
            WHERE launch_id IN (
                SELECT id FROM launches
                WHERE project_key = @projectKey
                AND start_time < @cutoffDate
            )";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = deleteItemsSql;
        cmd.AddParameter("projectKey", projectKey);
        cmd.AddParameter("cutoffDate", cutoffDate);

        var itemsDeleted = await cmd.ExecuteNonQueryAsync(ct);

        if (itemsDeleted > 0)
        {
            LogWorkerMilestone(
                EventCodes.Housekeeping.LaunchesDeleted,
                "Deleted {Count} test items for project {ProjectKey}", itemsDeleted, projectKey);
        }

        // Delete launches themselves
        const string deleteLaunchesSql = @"
            DELETE FROM launches
            WHERE project_key = @projectKey
            AND start_time < @cutoffDate";

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = deleteLaunchesSql;
        cmd2.AddParameter("projectKey", projectKey);
        cmd2.AddParameter("cutoffDate", cutoffDate);

        var launchesDeleted = await cmd2.ExecuteNonQueryAsync(ct);

        return launchesDeleted;
    }
}
