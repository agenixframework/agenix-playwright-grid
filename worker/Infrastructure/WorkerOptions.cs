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

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Agenix.PlaywrightGrid.Domain;

namespace WorkerService.Infrastructure;

/// <summary>
///     Strongly-typed options for the Worker service. Values are typically sourced from environment variables
///     in containerized deployments; see <see cref="FromEnvironment" /> for parsing rules and defaults.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>
    ///     Hub base URL (e.g., http://hub:5000).
    /// </summary>
    public string HubUrl { get; init; } = "http://hub:5000";

    /// <summary>
    ///     Redis connection endpoint (e.g., redis:6379).
    /// </summary>
    public string RedisUrl { get; init; } = "redis:6379";

    /// <summary>
    ///     Unique identifier of this worker node.
    /// </summary>
    public string NodeId { get; init; } = $"node-{Guid.NewGuid():N}";

    /// <summary>
    ///     Secret used by the worker to authenticate with the hub.
    /// </summary>
    public string NodeSecret { get; init; } = "node-secret";

    /// <summary>
    ///     Secondary secret used for node-to-node scenarios (if applicable).
    /// </summary>
    public string NodeNodeSecret { get; init; } = "node-node-secret";

    /// <summary>
    ///     Raw POOL_CONFIG value (e.g., "AppA:Chromium:Staging=3,AppB:Firefox:UAT=2").
    /// </summary>
    public string PoolConfigEnv { get; init; } = "AppA:Chromium:Staging=3";

    /// <summary>
    ///     Node.js executable path used by the Playwright sidecar.
    /// </summary>
    public string NodeExe { get; init; } = "node";

    /// <summary>
    ///     Script file that launches the Playwright server sidecar.
    /// </summary>
    public string SidecarScript { get; init; } = "launch_playwright_server.js";

    /// <summary>
    ///     Timeout in seconds to wait for the sidecar to become ready.
    /// </summary>
    public int SidecarReadyTimeoutSeconds { get; init; } = 60;

    /// <summary>
    ///     Public host name advertised for client WebSocket connections (optional).
    /// </summary>
    public string? PublicWsHost { get; init; }

    /// <summary>
    ///     Public port advertised for client WebSocket connections (optional).
    /// </summary>
    public string? PublicWsPort { get; init; }

    /// <summary>
    ///     Public WebSocket scheme (ws or wss). Defaults to ws.
    /// </summary>
    public string PublicWsScheme { get; init; } = "ws";

    /// <summary>
    ///     Arbitrary labels describing this node (e.g., region, os). Case-insensitive keys.
    /// </summary>
    public ConcurrentDictionary<string, string> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Parsed pool configuration mapping label keys to desired capacity counts.
    /// </summary>
    public ConcurrentDictionary<string, int> PoolConfig { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Constructs WorkerOptions by parsing environment variables and applying defaults and clamping rules.
    /// </summary>
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

        // Pools (normalize/validate label keys via shared Domain model)
        var pools = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in poolConfigEnv.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && int.TryParse(kv[1], out var v))
            {
                var rawKey = kv[0];
                if (LabelKey.TryParse(rawKey, out var lk))
                {
                    pools[lk!.Normalized] = v;
                }
                else
                {
                    // Back-compat: accept raw keys that may not conform to full LabelKey rules (e.g., simple test keys like "X")
                    pools[rawKey] = v;
                }
            }
        }

        if (pools.Count == 0)
        {
            pools["AppA:Chromium:Staging"] = 3;
        }

        // Parse optional sidecar ready timeout (seconds)
        var timeoutEnv = Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS");
        var timeoutSeconds = 60;
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
            SidecarScript =
                Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR") ?? "launch_playwright_server.js",
            SidecarReadyTimeoutSeconds = timeoutSeconds,
            PublicWsHost = Environment.GetEnvironmentVariable("PUBLIC_WS_HOST"),
            PublicWsPort = Environment.GetEnvironmentVariable("PUBLIC_WS_PORT"),
            PublicWsScheme = Environment.GetEnvironmentVariable("PUBLIC_WS_SCHEME") ?? "ws",
            Labels = labels,
            PoolConfig = pools
        };
    }

    /// <summary>
    ///     Attempts to detect the OS description inside containerized environments, falling back to runtime info.
    /// </summary>
    private static string DetectContainerOs()
    {
        try
        {
            // Linux containers commonly expose /etc/os-release with PRETTY_NAME
            if (OperatingSystem.IsLinux())
            {
                const string path = "/etc/os-release";
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    var pretty = lines.Select(l => l.Trim())
                        .FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(pretty))
                    {
                        var val = pretty.Split('=', 2)[1].Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }
                    }

                    var id = lines.FirstOrDefault(l => l.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
                        ?.Split('=', 2)[1]?.Trim().Trim('"');
                    var ver = lines.FirstOrDefault(l => l.StartsWith("VERSION_ID=", StringComparison.OrdinalIgnoreCase))
                        ?.Split('=', 2)[1]?.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                    {
                        return $"{id} {ver}";
                    }

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id!;
                    }
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
