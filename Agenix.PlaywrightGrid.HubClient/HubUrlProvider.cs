using Microsoft.Extensions.Configuration;

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
/// Provides the Hub URL by checking environment and configuration.
/// Precedence: Environment variable HUB_URL first, then configuration key "Hub:Url".
/// Throws if neither is provided.
/// </summary>
public static class HubUrlProvider
{
    /// <summary>
    /// Resolve the Hub URL.
    /// </summary>
    /// <param name="configuration">An IConfiguration instance to read Hub:Url from.</param>
    /// <returns>Hub base URL.</returns>
    /// <exception cref="InvalidOperationException">Thrown if HUB_URL and configuration["Hub:Url"] are both missing or empty.</exception>
    public static string Get(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable("HUB_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var fromConfig = configuration["Hub:Url"];
        return !string.IsNullOrWhiteSpace(fromConfig)
            ? fromConfig
            : throw new InvalidOperationException("Hub URL is not configured. Set HUB_URL or 'Hub:Url'.");
    }
}
