using System.Text.Json;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.Background;
using PlaywrightHub.Infrastructure.Adapters.Redis;
using PlaywrightHub.Infrastructure.Adapters.Results;
using PlaywrightHub.Infrastructure.Web;
using Prometheus;
using StackExchange.Redis;

namespace PlaywrightHub.Services;

/// <summary>
/// Responsible for initializing and running the PlaywrightHub service,
/// including setting up logging, Redis connections, hosted services,
/// and ASP.NET Core application settings.
/// </summary>
public static class HubServiceRunner
{
    /// <summary>
    /// Runs the Playwright Hub service with the specified configuration and services.
    /// </summary>
    /// <param name="args">An array of command-line arguments for configuring the application.</param>
    /// <returns>A task representing the asynchronous operation for running the service.</returns>
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        // Suppress verbose framework logs like OkObjectResult JSON writing and EndpointMiddleware exec logs
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

        var redisUrl = builder.Configuration["REDIS_URL"] ?? "redis:6379";
        var mux = await ConnectionMultiplexer.ConnectAsync(redisUrl);
        var db = mux.GetDatabase();

        // Services for dashboard integration
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => mux);
        builder.Services.AddSingleton<IDatabase>(_ => db);
        builder.Services.AddSingleton<IPoolStateReader, RedisPoolStateReader>();
        builder.Services.AddHostedService<RedisPoolStateBroadcastService>();
        builder.Services.AddHostedService<NodeSweeperService>();
        builder.Services.AddHostedService<RunCleanupService>();
        // Results store (configurable: memory (default) or redis)
        var resultsBackend = builder.Configuration["HUB_RESULTS_BACKEND"] ?? "memory";
        if (string.Equals(resultsBackend, "redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IResultsStore, RedisResultsStore>();
            Console.WriteLine("[hub] ResultsStore backend: redis");
        }
        else
        {
            builder.Services.AddSingleton<IResultsStore, InMemoryResultsStore>();
            Console.WriteLine("[hub] ResultsStore backend: memory (default)");
        }

        var app = builder.Build();

        app.UseMetricServer();
        app.UseHttpMetrics();
        app.MapHubEndpoints();

        // Startup diagnostics dump (effective config + labels per node)
        try
        {
            var cfg = app.Configuration;
            var nodeTimeoutSeconds = int.TryParse(cfg["HUB_NODE_TIMEOUT"], out var t) ? t : 60;
            var dashboardUrl = cfg["DASHBOARD_URL"] ?? "http://localhost:3001";
            var enableTrailingFallback = !bool.TryParse(cfg["HUB_BORROW_TRAILING_FALLBACK"], out var tf) || tf; // default true
            var enablePrefixExpand = !bool.TryParse(cfg["HUB_BORROW_PREFIX_EXPAND"], out var pe) || pe; // default true
            var enableWildcards = bool.TryParse(cfg["HUB_BORROW_WILDCARDS"], out var wc) && wc; // default false
            var ver = typeof(HubServiceRunner).Assembly.GetName().Version?.ToString() ?? string.Empty;

            var reader = app.Services.GetRequiredService<IPoolStateReader>();
            var state = await reader.GetStateAsync();

            var dto = new HubDiagnosticsDto
            {
                HubConfig = new HubEffectiveConfigDto
                {
                    RedisUrl = redisUrl,
                    BorrowTrailingFallback = enableTrailingFallback,
                    BorrowPrefixExpand = enablePrefixExpand,
                    BorrowWildcards = enableWildcards,
                    NodeTimeoutSeconds = nodeTimeoutSeconds,
                    DashboardUrl = dashboardUrl,
                    Version = ver
                },
                Workers = state.Workers,
                Now = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("[hub] Startup diagnostics:\n" + json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[hub] Startup diagnostics failed: {ex.Message}");
        }

        await app.RunAsync();
    }

}
