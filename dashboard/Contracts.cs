using System;
using System.Collections.Generic;

namespace Dashboard;

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
