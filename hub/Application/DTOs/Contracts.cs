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

namespace PlaywrightHub.Application.DTOs;

/// <summary>
///     Snapshot of the hub's pool capacity and registered workers at a point in time.
/// </summary>
public sealed record PoolStateDto
{
    /// <summary>
    ///     Aggregated pool entries grouped by label key with total and borrowed counts.
    /// </summary>
    public List<PoolEntryDto> Pools { get; init; } = new();

    /// <summary>
    ///     Currently known workers and their per-label capacities.
    /// </summary>
    public List<WorkerStatusDto> Workers { get; init; } = new();

    /// <summary>
    ///     Server time (UTC) when the snapshot was produced.
    /// </summary>
    public DateTime Now { get; init; }
}

/// <summary>
///     Aggregated capacity stats for a specific label key.
/// </summary>
public sealed record PoolEntryDto
{
    /// <summary>
    ///     Label key (e.g., App:Browser:Env[:...]).
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    ///     Total capacity advertised by all workers for this label.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    ///     Currently borrowed browsers for this label.
    /// </summary>
    public int Borrowed { get; set; }

    /// <summary>
    ///     Optional resolved browser version for this label, when known.
    /// </summary>
    public string? BrowserVersion { get; set; }

    /// <summary>
    ///     True if maintenance mode is active for this label (borrows may be denied).
    /// </summary>
    public bool MaintenanceActive { get; set; }
}

/// <summary>
///     Status snapshot for a single worker node.
/// </summary>
public sealed record WorkerStatusDto
{
    /// <summary>
    ///     Worker identifier (NODE_ID).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    ///     Labels registered by this worker.
    /// </summary>
    public List<string> Labels { get; init; } = new();

    /// <summary>
    ///     Last time the hub observed this worker as alive (UTC).
    /// </summary>
    public DateTime LastSeen { get; set; }

    /// <summary>
    ///     True if this worker is currently quarantined (cooldown, no new sessions will be assigned).
    /// </summary>
    public bool Quarantined { get; set; }

    /// <summary>
    ///     When quarantine is expected to end (UTC). Null when not quarantined.
    /// </summary>
    public DateTime? QuarantineUntil { get; set; }

    /// <summary>
    ///     Per-label capacity counts advertised by this worker.
    /// </summary>
    public Dictionary<string, PoolCounts> Pools { get; init; } = new();

    /// <summary>
    ///     Sum of Total across Pools.
    /// </summary>
    public int TotalBrowsers { get; set; }

    /// <summary>
    ///     Reported Playwright version from sidecar (optional).
    /// </summary>
    public string? PlaywrightVersion { get; set; }

    /// <summary>
    ///     Expected Playwright version (from hub policy), when set.
    /// </summary>
    public string? PlaywrightVersionExpected { get; set; }

    /// <summary>
    ///     True when reported version differs from expected.
    /// </summary>
    public bool PlaywrightVersionMismatch { get; set; }
}

/// <summary>
///     Per-label capacity counters used within worker status.
/// </summary>
public sealed record PoolCounts
{
    /// <summary>
    ///     Total capacity for the label on this worker.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    ///     Current number of borrowed sessions for the label on this worker.
    /// </summary>
    public int Borrowed { get; set; }
}

/// <summary>
///     DTO for updating an existing launch.
/// </summary>
public sealed record UpdateLaunchRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string[]? Attributes { get; init; }
    public bool? IsImportant { get; init; }
    public int? RetentionOverrideDays { get; init; }
    public string? Status { get; init; }
}

/// <summary>
///     DTO for bulk updating multiple launches.
/// </summary>
public sealed record BulkUpdateLaunchesRequest
{
    /// <summary>
    ///     List of launch IDs to update (max 10,000)
    /// </summary>
    public required Guid[] LaunchIds { get; init; }

    /// <summary>
    ///     Fields to update on all selected launches
    /// </summary>
    public UpdateLaunchRequest Updates { get; init; } = new();
}

/// <summary>
///     DTO for bulk update response.
/// </summary>
public sealed record BulkUpdateLaunchesResponse
{
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required int TotalRequested { get; init; }
    public string[]? Errors { get; init; }
}

/// <summary>
///     DTO for launch response.
/// </summary>
public sealed record LaunchDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string[] Attributes { get; init; }
    public required string OwnerApiKey { get; init; }
    public string? OwnerUsername { get; init; }
    public required string ProjectKey { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? FinishTime { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public required int LaunchNumber { get; init; }
    public long DbId { get; init; } // Globally unique sequential ID per project (optional for backward compatibility)
    public long Number => DbId; // Alias for numeric URLs
    public required int TotalTestRuns { get; init; }
    public required int FinishedTestRuns { get; init; }
    public required int RunningTestRuns { get; init; }
    public required int StoppedTestRuns { get; init; }
    public required int ErroredTestRuns { get; init; }
    public bool IsImportant { get; init; }
    public int? RetentionOverrideDays { get; init; }
    public double? DurationSeconds { get; init; }
    public required bool IsRunning { get; init; }
    public required string Status { get; init; }

    // Test result aggregations (Azure-style)
    public int TotalTests { get; init; }
    public int PassedTests { get; init; }
    public int FailedTests { get; init; }
    public int SkippedTests { get; init; }
    public int TimedoutTests { get; init; }

    /// <summary>
    ///     Computed status based on test results (InProgress|Passed|Failed|Skipped|Timedout|Cancelled|Errored).
    /// </summary>
    public string? ComputedStatus { get; init; }
}

/// <summary>
///     DTO for suite information.
/// </summary>
public sealed record SuiteDto
{
    public required Guid Id { get; init; }
    public required Guid LaunchId { get; init; }
    public Guid? ParentSuiteId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string[] Attributes { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? FinishTime { get; init; }
    public required int TotalTestRuns { get; init; }
    public required int PassedTestRuns { get; init; }
    public required int FailedTestRuns { get; init; }
    public required int StoppedTestRuns { get; init; }
    public double? DurationSeconds { get; init; }

    // Test result aggregations (Azure-style)
    public int TotalTests { get; init; }
    public int PassedTests { get; init; }
    public int FailedTests { get; init; }
    public int SkippedTests { get; init; }
    public int TimedoutTests { get; init; }

    /// <summary>
    ///     Computed status based on test results (InProgress|Passed|Failed|Skipped|Timedout|Cancelled|Errored).
    /// </summary>
    public string? ComputedStatus { get; init; }
}

/// <summary>
///     Request to generate stub/mock launch data.
/// </summary>
public sealed record GenerateStubLaunchRequest
{
    public required string ProjectKey { get; init; }
    public string? BaseName { get; init; }
}

/// <summary>
///     Request to generate stub/mock test runs for a launch.
/// </summary>
public sealed record GenerateStubTestRunsRequest
{
    public int? Count { get; init; }
}

/// <summary>
///     Saved filter configuration for launches page
/// </summary>
public sealed record LaunchFilterDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ProjectKey { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public List<FilterCriterionDto> Criteria { get; init; } = new();
    public string SortBy { get; init; } = "start_time";
    public bool IsShared { get; init; }
    public bool DisplayOnLaunches { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
///     Single filter criterion (field, operator, value)
/// </summary>
public sealed record FilterCriterionDto
{
    public string Field { get; init; } = "name";
    public string Operator { get; init; } = "contains";
    public string Value { get; init; } = string.Empty;
    public string LogicalOperator { get; init; } = "AND";
    public string? DateRangePreset { get; init; }
    public string? FromDate { get; init; }
    public string? ToDate { get; init; }
    public string? AttributeKey { get; init; }
    public string? AttributeValue { get; init; }
    public List<AttributePairDto>? Attributes { get; init; }
}

public sealed record AttributePairDto
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
///     User's selected filter preference for a project
/// </summary>
public sealed record UserFilterPreferenceDto
{
    public string UserId { get; init; } = string.Empty;
    public string ProjectKey { get; init; } = string.Empty;
    public Guid? SelectedFilterId { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
///     Request to create or update a launch filter
/// </summary>
public sealed record SaveLaunchFilterRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ProjectKey { get; init; } = string.Empty;
    public List<FilterCriterionDto> Criteria { get; init; } = new();
    public string SortBy { get; init; } = "start_time";
    public bool IsShared { get; init; }
    public bool DisplayOnLaunches { get; init; } = true;
}

/// <summary>
///     Request to update user's filter preference
/// </summary>
public sealed record UpdateFilterPreferenceRequest
{
    public string ProjectKey { get; init; } = string.Empty;
    public Guid? SelectedFilterId { get; init; }
}

/// <summary>
///     Request to toggle display on launches for a filter (per-user setting)
/// </summary>
public sealed record ToggleFilterDisplayRequest
{
    public bool DisplayOnLaunches { get; init; }
}

// Diagnostics payloads
/// <summary>
///     Effective configuration values the hub is running with.
/// </summary>
public sealed record HubEffectiveConfigDto
{
    /// <summary>
    ///     Redis connection string used by the hub.
    /// </summary>
    public string RedisUrl { get; init; } = string.Empty;

    /// <summary>
    ///     Whether trailing fallback matching is enabled.
    /// </summary>
    public bool BorrowTrailingFallback { get; init; }

    /// <summary>
    ///     Whether prefix expansion matching is enabled.
    /// </summary>
    public bool BorrowPrefixExpand { get; init; }

    /// <summary>
    ///     Whether wildcard segments are allowed in label matching.
    /// </summary>
    public bool BorrowWildcards { get; init; }

    /// <summary>
    ///     Liveness timeout in seconds after which workers are considered stale.
    /// </summary>
    public int NodeTimeoutSeconds { get; init; }

    /// <summary>
    ///     Quarantine cooldown duration (seconds) applied to failing/flapping nodes.
    /// </summary>
    public int NodeQuarantineSeconds { get; init; }

    /// <summary>
    ///     Dashboard base URL.
    /// </summary>
    public string DashboardUrl { get; init; } = string.Empty;

    /// <summary>
    ///     Hub version string.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    ///     Whether event publishing to RabbitMQ is enabled.
    /// </summary>
    public bool EventPublishingEnabled { get; init; }

    /// <summary>
    ///     RabbitMQ connection URL (if event publishing is enabled).
    /// </summary>
    public string? RabbitMqUrl { get; init; }
}

/// <summary>
///     Composite diagnostics snapshot used by the dashboard.
/// </summary>
public sealed record HubDiagnosticsDto
{
    /// <summary>
    ///     Effective hub configuration values.
    /// </summary>
    public HubEffectiveConfigDto HubConfig { get; init; } = new();

    /// <summary>
    ///     Current worker status list.
    /// </summary>
    public List<WorkerStatusDto> Workers { get; init; } = new();

    /// <summary>
    ///     Server time (UTC) when the snapshot was produced.
    /// </summary>
    public DateTime Now { get; init; }
}
