using System;
using System.Collections.Generic;

namespace PlaywrightHub.Application.DTOs;

public sealed record ResultRunSummaryDto
{
    public string RunId { get; init; } = string.Empty;
    public string? ExternalId { get; init; }
    public string App { get; init; } = string.Empty;
    public string Browser { get; init; } = string.Empty; // Chromium|Firefox|WebKit
    public string Env { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? OS { get; init; }
    public string Status { get; set; } = "Queued"; // Queued|Running|Passed|Failed|Aborted|Stopped|AutoStopped
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? WorkerNodeId { get; set; }
    public string? PlaywrightVersion { get; set; }
    public string? BrowserVersion { get; set; }
}

public sealed record CommandLogEventDto
{
    public string RunId { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public string Kind { get; init; } =
        string.Empty; // ServerLaunch|Borrow|Connect|Disconnect|Return|WSProxy|API|Trace|Custom

    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string>? Props { get; init; }
    public string? TestId { get; init; }
}

public sealed record ResultTestCaseDto
{
    public string RunId { get; init; } = string.Empty;
    public string TestId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string? Project { get; init; }
    public string Status { get; set; } = "Queued"; // Queued|Running|Passed|Failed|Skipped
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStack { get; set; }
}
