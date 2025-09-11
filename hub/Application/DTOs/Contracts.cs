#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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
    ///     Dashboard base URL.
    /// </summary>
    public string DashboardUrl { get; init; } = string.Empty;

    /// <summary>
    ///     Hub version string.
    /// </summary>
    public string Version { get; init; } = string.Empty;
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
