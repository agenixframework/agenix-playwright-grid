using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace WorkerService.Infrastructure;

public sealed class WorkerOptions
{
    public string HubUrl { get; init; } = "http://hub:5000";
    public string RedisUrl { get; init; } = "redis:6379";
    public string NodeId { get; init; } = $"node-{Guid.NewGuid():N}";
    public string NodeSecret { get; init; } = "node-secret";
    public string NodeNodeSecret { get; init; } = "node-node-secret";
    public string PoolConfigEnv { get; init; } = "AppA:Chromium:Staging=3";

    public string NodeExe { get; init; } = "node";
    public string SidecarScript { get; init; } = "launch_playwright_server.js";
    public int SidecarReadyTimeoutSeconds { get; init; } = 60;

    public string? PublicWsHost { get; init; }
    public string? PublicWsPort { get; init; }
    public string PublicWsScheme { get; init; } = "ws";

    public ConcurrentDictionary<string, string> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, int> PoolConfig { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static WorkerOptions FromEnvironment()
    {
        var poolConfigEnv = Environment.GetEnvironmentVariable("POOL_CONFIG") ?? "AppA:Chromium:Staging=3";
        // Labels
        var labels = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["region"] = Environment.GetEnvironmentVariable("NODE_REGION") ?? "local",
            // Prefer explicit override, else detect from container/host
            ["os"] = Environment.GetEnvironmentVariable("NODE_OS") ?? DetectContainerOs()
        };

        // Pools
        var pools = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in poolConfigEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && int.TryParse(kv[1], out var v))
                pools[kv[0]] = v;
        }
        if (pools.Count == 0) pools["AppA:Chromium:Staging"] = 3;

        // Parse optional sidecar ready timeout (seconds)
        var timeoutEnv = Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS");
        int timeoutSeconds = 60;
        if (!string.IsNullOrWhiteSpace(timeoutEnv) && int.TryParse(timeoutEnv.Trim(), out var parsed))
        {
            // Clamp to sane range 5..600
            timeoutSeconds = Math.Min(600, Math.Max(5, parsed));
        }

        return new WorkerOptions
        {
            HubUrl = Environment.GetEnvironmentVariable("HUB_URL") ?? "http://hub:5000",
            RedisUrl = Environment.GetEnvironmentVariable("REDIS_URL") ?? "redis:6379",
            NodeId = Environment.GetEnvironmentVariable("NODE_ID") ?? $"node-{Guid.NewGuid():N}",
            NodeSecret = Environment.GetEnvironmentVariable("NODE_SECRET") ?? "node-secret",
            NodeNodeSecret = Environment.GetEnvironmentVariable("NODE_NODE_SECRET") ?? "node-node-secret",
            PoolConfigEnv = poolConfigEnv,
            NodeExe = Environment.GetEnvironmentVariable("NODE_EXE") ?? "node",
            SidecarScript = Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR") ?? "launch_playwright_server.js",
            SidecarReadyTimeoutSeconds = timeoutSeconds,
            PublicWsHost = Environment.GetEnvironmentVariable("PUBLIC_WS_HOST"),
            PublicWsPort = Environment.GetEnvironmentVariable("PUBLIC_WS_PORT"),
            PublicWsScheme = Environment.GetEnvironmentVariable("PUBLIC_WS_SCHEME") ?? "ws",
            Labels = labels,
            PoolConfig = pools,
        };
    }

    private static string DetectContainerOs()
    {
        try
        {
            // Linux containers commonly expose /etc/os-release with PRETTY_NAME
            if (OperatingSystem.IsLinux())
            {
                const string path = "/etc/os-release";
                if (System.IO.File.Exists(path))
                {
                    var lines = System.IO.File.ReadAllLines(path);
                    var pretty = lines.Select(l => l.Trim())
                        .FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(pretty))
                    {
                        var val = pretty.Split('=', 2)[1].Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                    var id = lines.FirstOrDefault(l => l.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]?.Trim().Trim('"');
                    var ver = lines.FirstOrDefault(l => l.StartsWith("VERSION_ID=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]?.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver)) return $"{id} {ver}";
                    if (!string.IsNullOrWhiteSpace(id)) return id!;
                }
            }
            // Fallback to runtime-provided description (works across OSes and inside containers)
            return RuntimeInformation.OSDescription;
        }
        catch
        {
            return RuntimeInformation.OSDescription;
        }
    }
}
