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

public sealed record PoolStateDto
{
    public List<PoolEntryDto> Pools { get; init; } = new();
    public List<WorkerStatusDto> Workers { get; init; } = new();
    public DateTime Now { get; init; }
}

public sealed record PoolEntryDto
{
    public string Label { get; init; } = "";
    public int Total { get; set; }
    public int Borrowed { get; set; }
    public string? BrowserVersion { get; set; }
    public bool MaintenanceActive { get; set; }
}

public sealed record WorkerStatusDto
{
    public string Id { get; init; } = "";
    public List<string> Labels { get; init; } = new();
    public DateTime LastSeen { get; set; }
    public Dictionary<string, PoolCounts> Pools { get; init; } = new();
    public int TotalBrowsers { get; set; }
    public string? PlaywrightVersion { get; set; }
}

public sealed record PoolCounts
{
    public int Total { get; set; }
    public int Borrowed { get; set; }
}

// Diagnostics payloads
public sealed record HubEffectiveConfigDto
{
    public string RedisUrl { get; init; } = "";
    public bool BorrowTrailingFallback { get; init; }
    public bool BorrowPrefixExpand { get; init; }
    public bool BorrowWildcards { get; init; }
    public int NodeTimeoutSeconds { get; init; }
    public string DashboardUrl { get; init; } = "";
    public string Version { get; init; } = "";
}

public sealed record HubDiagnosticsDto
{
    public HubEffectiveConfigDto HubConfig { get; init; } = new();
    public List<WorkerStatusDto> Workers { get; init; } = new();
    public DateTime Now { get; init; }
}
