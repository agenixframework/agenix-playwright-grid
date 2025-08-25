using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agenix.PlaywrightGrid.HubClient;

/// <summary>
/// DI registration helpers for HubClient.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers HubClient as a typed HttpClient using provided options.
    /// </summary>
    public static IServiceCollection AddHubClient(this IServiceCollection services, Action<HubClientOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var opts = new HubClientOptions();
        configure(opts);
        if (string.IsNullOrWhiteSpace(opts.HubUrl))
            throw new InvalidOperationException("HubUrl must be configured for HubClient.");

        var baseUrl = NormalizeHubUrlOrThrow(opts.HubUrl);
        var timeout = opts.Timeout ?? TimeSpan.FromSeconds(15);
        var secret = (opts.RunnerSecret ?? Environment.GetEnvironmentVariable("HUB_RUNNER_SECRET") ?? "runner-secret").Trim();

        services.AddHttpClient<HubClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Remove("x-hub-secret");
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", secret);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        });
        return services;
    }

    /// <summary>
    /// Registers HubClient using IConfiguration (HubUrlProvider.Get) with optional overrides.
    /// </summary>
    public static IServiceCollection AddHubClient(this IServiceCollection services, IConfiguration configuration, string? runnerSecret = null, TimeSpan? timeout = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        var hubUrl = HubUrlProvider.Get(configuration);
        return services.AddHubClient(o => { o.HubUrl = hubUrl; o.RunnerSecret = runnerSecret; o.Timeout = timeout; });
    }

    private static string NormalizeHubUrlOrThrow(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
            throw new ArgumentException("Invalid hub URL", nameof(rawUrl));

        if (string.Equals(parsed.Host, "0.0.0.0", StringComparison.Ordinal) ||
            string.Equals(parsed.Host, "*", StringComparison.Ordinal) ||
            string.Equals(parsed.Host, "+", StringComparison.Ordinal))
        {
            var b = new UriBuilder(parsed) { Host = "localhost" };
            return b.Uri.ToString().TrimEnd('/');
        }
        return parsed.ToString().TrimEnd('/');
    }
}
