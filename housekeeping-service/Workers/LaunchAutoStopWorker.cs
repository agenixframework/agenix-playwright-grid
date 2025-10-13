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
///     Background worker that automatically stops launches that exceed max duration or become inactive.
/// </summary>
public sealed class LaunchAutoStopWorker(
    IConfiguration config,
    IDatabase redis,
    IHousekeepingDataSource dataSource,
    IProjectSettingsReader projectSettings,
    ILogger<LaunchAutoStopWorker> logger,
    ChunkedLogger<LaunchAutoStopWorker> chunkedLogger) : HousekeepingWorkerBase(chunkedLogger, nameof(LaunchAutoStopWorker))
{
    private readonly IConfiguration _config = config;
    private readonly IDatabase _redis = redis;
    private readonly IHousekeepingDataSource _pgDataSource = dataSource;
    private readonly IProjectSettingsReader _projectSettings = projectSettings;
    private readonly ILogger _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var intervalMinutes = int.TryParse(_config["AGENIX_HOUSEKEEPING_LAUNCH_AUTO_STOP_INTERVAL_MINUTES"], out var im)
            ? Math.Max(1, im)
            : 10;
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        using var op = BeginWorkerOperation("ExecuteAsync", new Dictionary<string, object>
        {
            ["intervalMinutes"] = interval.TotalMinutes
        });

        LogWorkerMilestone(
            EventCodes.Housekeeping.LaunchAutoStopStarted,
            "Launch auto-stop worker started with interval={IntervalMinutes}m", interval.TotalMinutes);

        var leadershipEnabled = string.Equals(_config["AGENIX_HOUSEKEEPING_LEADERSHIP"], "true",
            StringComparison.OrdinalIgnoreCase);
        var leaseSeconds = int.TryParse(_config["AGENIX_HOUSEKEEPING_LEASE_SECONDS"], out var ls)
            ? Math.Max(5, ls)
            : 30;
        var instanceId = _config["AGENIX_HOUSEKEEPING_INSTANCE_ID"] ??
                         $"{Environment.MachineName}:{Environment.ProcessId}";
        var leaderKey = "housekeeping:leader:launch_auto_stop";

        while (!ct.IsCancellationRequested)
        {
            using var tickOp = BeginWorkerOperation("AutoStopTick");

            try
            {
                if (leadershipEnabled)
                {
                    var acquired = await _redis.StringSetAsync(leaderKey, instanceId, TimeSpan.FromSeconds(leaseSeconds),
                        When.NotExists);
                    if (!acquired)
                    {
                        _logger.LogDebug("[LaunchAutoStop] Not leader, skipping tick");
                        tickOp.SetOutputs(new Dictionary<string, object> { ["isLeader"] = false });
                        tickOp.Complete();
                        await Task.Delay(interval, ct);
                        continue;
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LaunchAutoStopStarted,
                    "Starting launch auto-stop check");

                var projects = await GetAllProjectsWithInProgressLaunchesAsync(ct);
                var launchesChecked = 0;
                var launchesStopped = 0;

                foreach (var projectKey in projects)
                {
                    if (ct.IsCancellationRequested) break;

                    using var projectOp = BeginWorkerOperation("ProcessProject", new Dictionary<string, object>
                    {
                        ["projectKey"] = projectKey
                    });

                    try
                    {
                        var settings = await _projectSettings.GetRetentionSettingsAsync(projectKey);
                        // Inactivity timeout from settings or default 1 day
                        var timeout = ParseTimeout(settings?.KeepLaunchesDays > 0 ? $"{settings.KeepLaunchesDays}d" : "1d");

                        var stoppedCount = await StopInactiveLaunchesForProjectAsync(projectKey, timeout, ct);
                        launchesStopped += stoppedCount;
                        launchesChecked++; // This is actually projects checked in this context

                        projectOp.SetOutputs(new Dictionary<string, object>
                        {
                            ["projectKey"] = projectKey,
                            ["stoppedCount"] = stoppedCount
                        });
                        projectOp.Complete();
                    }
                    catch (Exception ex)
                    {
                        projectOp.Fail(ex, ErrorType.Unexpected);
                        _logger.LogWarning(ex, "[LaunchAutoStop] Error processing project {ProjectKey}", projectKey);
                    }
                }

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LaunchAutoStopCompleted,
                    "Launch auto-stop check completed: stopped {Stopped} launches across {Checked} projects",
                    launchesStopped, launchesChecked);

                tickOp.SetOutputs(new Dictionary<string, object>
                {
                    ["checkedCount"] = launchesChecked,
                    ["stoppedCount"] = launchesStopped,
                    ["isLeader"] = true,
                    ["success"] = true
                });
                tickOp.Complete();
            }
            catch (PostgresException ex)
            {
                tickOp.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
                _logger.LogError(ex, "[LaunchAutoStop] Database error");
            }
            catch (Exception ex)
            {
                tickOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogError(ex, "[LaunchAutoStop] Unexpected error");
            }

            await Task.Delay(interval, ct);
        }

        op.SetOutputs(new Dictionary<string, object> { ["stopped"] = true });
        op.Complete();
    }

    private async Task<int> StopInactiveLaunchesForProjectAsync(string projectKey, TimeSpan timeout, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - timeout;
        var stopped = 0;

        await using var conn = await _pgDataSource.OpenConnectionAsync(ct);

        // Find inactive launches
        const string findSql = @"
            SELECT id, name, COALESCE(last_activity, start_time) as activity
            FROM launches
            WHERE project_key = @projectKey
              AND status = 'InProgress'
              AND COALESCE(last_activity, start_time) < @cutoff
            LIMIT 20";

        using var findCmd = conn.CreateCommand();
        findCmd.CommandText = findSql;
        findCmd.AddParameter("projectKey", projectKey);
        findCmd.AddParameter("cutoff", cutoff);

        var launches = new List<(Guid id, string name, DateTime lastActivity)>();
        using (var reader = await findCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                launches.Add((reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2)));
            }
        }

        // Stop each launch
        foreach (var (launchId, launchName, lastActivity) in launches)
        {
            if (ct.IsCancellationRequested) break;

            using var launchOp = BeginWorkerOperation("StopLaunch", new Dictionary<string, object>
            {
                ["launchId"] = launchId,
                ["projectKey"] = projectKey
            });

            try
            {
                await StopLaunchAsync(conn, launchId, launchName, projectKey, now - lastActivity, ct);
                stopped++;

                LogWorkerMilestone(
                    EventCodes.Housekeeping.LaunchAutoStopped,
                    "Auto-stopped launch {LaunchId} ({LaunchName}): inactive for {Duration}",
                    launchId, launchName, FormatDuration(now - lastActivity));

                launchOp.Complete();
            }
            catch (Exception ex)
            {
                launchOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogWarning(ex, "[LaunchAutoStop] Failed to stop launch {LaunchId}", launchId);
            }
        }

        return stopped;
    }

    private async Task StopLaunchAsync(System.Data.Common.DbConnection conn, Guid launchId, string launchName,
        string projectKey, TimeSpan inactivity, CancellationToken ct)
    {
        using var tx = await conn.BeginTransactionAsync(ct);

        // 1. Update launch status
        const string updateLaunchSql = @"
            UPDATE launches
            SET status = 'Stopped',
                finish_time = @endTime
            WHERE id = @launchId";

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = updateLaunchSql;
            cmd.AddParameter("launchId", launchId);
            cmd.AddParameter("endTime", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Update all active test items for this launch
        const string updateItemsSql = @"
            UPDATE test_items
            SET session_status = 'AutoStopped',
                computed_status = 'Cancelled',
                finish_time = @endTime
            WHERE launch_id = @launchId
              AND session_status IN ('Queued', 'Running')";

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = updateItemsSql;
            cmd.AddParameter("launchId", launchId);
            cmd.AddParameter("endTime", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3. Audit log
        const string auditSql = @"
            INSERT INTO audit_entries (timestamp, project_key, category, action, severity, details)
            VALUES (@timestamp, @projectKey, 'housekeeping', 'launch.autoStop', 'Warning', @details::jsonb)";

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = auditSql;
            cmd.AddParameter("timestamp", DateTime.UtcNow);
            cmd.AddParameter("projectKey", projectKey);
            cmd.AddParameter("details", System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["launchId"] = launchId.ToString(),
                ["launchName"] = launchName,
                ["inactivityDuration"] = inactivity.ToString(),
                ["reason"] = "launch-inactivity"
            }));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private async Task<List<string>> GetAllProjectsWithInProgressLaunchesAsync(CancellationToken ct)
    {
        var projects = new List<string>();
        using var conn = await _pgDataSource.OpenConnectionAsync(ct);

        const string sql = "SELECT DISTINCT project_key FROM launches WHERE status = 'InProgress'";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            projects.Add(reader.GetString(0));
        }

        return projects;
    }

    private static TimeSpan ParseTimeout(string timeout)
    {
        if (timeout.EndsWith("d"))
        {
            if (int.TryParse(timeout[..^1], out var days)) return TimeSpan.FromDays(days);
        }
        if (timeout.EndsWith("h"))
        {
            if (int.TryParse(timeout[..^1], out var hours)) return TimeSpan.FromHours(hours);
        }

        return TimeSpan.FromDays(1);
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMinutes < 1) return $"{ts.TotalSeconds:F0}s";
        if (ts.TotalHours < 1) return $"{ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalDays < 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalDays}d {ts.Hours}h";
    }
}
