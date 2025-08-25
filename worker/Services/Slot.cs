using System.Diagnostics;

namespace WorkerService.Services;

/// <summary>
/// Represents a slot containing information about a specific browser instance.
/// </summary>
public sealed record Slot(
    Process Proc,
    string BrowserType,
    string InternalWs,
    string PublicWs,
    DateTime StartedAt
);
