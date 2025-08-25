using System;

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
/// Options for configuring HubClient when registering via DI.
/// </summary>
public sealed class HubClientOptions
{
    /// <summary>
    /// The base URL of the Hub (e.g., http://127.0.0.1:5100).
    /// </summary>
    public string? HubUrl { get; set; }

    /// <summary>
    /// The x-hub-secret header value used to authenticate runner requests.
    /// If not set, HUB_RUNNER_SECRET environment variable or "runner-secret" is used.
    /// </summary>
    public string? RunnerSecret { get; set; }

    /// <summary>
    /// The request timeout to use on HttpClient.
    /// Defaults to 15 seconds if not specified.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
