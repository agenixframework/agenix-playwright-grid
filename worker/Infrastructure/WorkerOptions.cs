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
    public sealed record BackoffSettings(int MinSeconds, int MaxSeconds, double Multiplier, int FailureResetSeconds);
    public BackoffSettings SidecarBackoff { get; init; } = new(1, 30, 2.0, 60);
    public enum WsDropPolicy
    {
        DropNewest,
        DropOldest
    }
    /// <summary>
    ///     Hub base URL (e.g., http://hub:5000).
    /// </summary>
    public string HubUrl { get; init; } = "http://hub:5000";

    /// <summary>
    ///     Maximum allowed size in bytes for a single WebSocket message (aggregated across frames).
    ///     Messages exceeding this limit will cause the connection to be closed.
    ///     Controlled by WS_MAX_MESSAGE_BYTES; clamped to 8 KiB .. 16 MiB. Default 2 MiB.
    /// </summary>
    public int WebSocketMaxMessageBytes { get; init; } = 2_097_152;

    /// <summary>
    ///     Idle timeout in seconds. If no frames are received in either direction for this duration,
    ///     the Worker will close the WebSocket connection. Controlled by WS_IDLE_TIMEOUT_SECONDS; clamped
    ///     to 5 .. 600 seconds. Default 60.
    /// </summary>
    public int WebSocketIdleTimeoutSeconds { get; init; } = 60;

    /// <summary>
    ///     Interval in seconds for keepalive pings on WebSocket connections. Applied to both client and server websockets.
    ///     Controlled by WS_PING_INTERVAL_SECONDS; clamped to 5 .. 300 seconds. Default 15.
    /// </summary>
    public int WebSocketPingIntervalSeconds { get; init; } = 15;

    /// <summary>
    ///     Capacity of the bounded channel used to buffer Playwright protocol log messages forwarded to the Hub.
    ///     Controlled by WS_LOG_CHANNEL_CAPACITY; clamped to 16 .. 8192. Default 256.
    /// </summary>
    public int WebSocketLogChannelCapacity { get; init; } = 256;

    /// <summary>
    ///     Drop policy when the log channel is full. Controlled by WS_LOG_DROP_POLICY; allowed values: DropNewest, DropOldest.
    ///     Default DropNewest (drop the attempted write).
    /// </summary>
    public WsDropPolicy WebSocketLogDropPolicy { get; init; } = WsDropPolicy.DropNewest;

    /// <summary>
    ///     Capacity of the bounded channels used to buffer WebSocket frames between client and sidecar.
    ///     Controlled by WS_PROXY_CHANNEL_CAPACITY; clamped to 32 .. 65536. Default 1024.
    /// </summary>
    public int WebSocketProxyChannelCapacity { get; init; } = 1024;

    /// <summary>
    ///     Drop policy when the proxy frame channel is full. Controlled by WS_PROXY_DROP_POLICY; allowed values: DropNewest, DropOldest.
    ///     Default DropNewest.
    /// </summary>
    public WsDropPolicy WebSocketProxyDropPolicy { get; init; } = WsDropPolicy.DropNewest;

    /// <summary>
    ///     Borrow idle timeout used to refresh Redis borrow_idle:{browserId} key during activity.
    ///     Sourced from HUB_IDLE_TIMEOUT_SECONDS; clamped to 10 .. 86400 seconds. Default 120.
    /// </summary>
    public int BorrowIdleTimeoutSeconds { get; init; } = 120;

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
    public int SidecarReadyTimeoutSeconds { get; init; } = 120;

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
        var errors = new List<string>();

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

        // Custom node labels with controlled cardinality
        // Allowed keys: NODE_LABEL_ALLOWED_KEYS (comma/semicolon separated), defaults to "channel,headless"
        // Allowed values per key via NODE_LABEL_VALUES_<KEY> (e.g., NODE_LABEL_VALUES_CHANNEL=stable,canary,beta,dev,other)
        // Input values via aggregated NODE_LABELS (e.g., channel=stable;headless=true) and/or NODE_LABEL_<KEY> overrides.
        var allowedKeysEnv = Environment.GetEnvironmentVariable("NODE_LABEL_ALLOWED_KEYS");
        var allowedKeys = (allowedKeysEnv ?? "channel,headless")
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Build allowed values map (lower-cased) with sensible defaults
        var allowedValues = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var key in allowedKeys)
        {
            var envName = "NODE_LABEL_VALUES_" + key.ToUpperInvariant();
            var valuesEnv = Environment.GetEnvironmentVariable(envName);
            string defaults = key switch
            {
                "channel" => "stable,canary,beta,dev,other",
                "headless" => "true,false,other",
                _ => "other" // by default only allow 'other' to minimize cardinality
            };
            var vals = (valuesEnv ?? defaults)
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => v.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            if (!vals.Contains("other"))
            {
                vals.Add("other");
            }
            allowedValues[key] = vals;
        }

        // Parse provided labels
        var provided = new Dictionary<string, string>(StringComparer.Ordinal);
        var aggEnv = Environment.GetEnvironmentVariable("NODE_LABELS");
        if (!string.IsNullOrWhiteSpace(aggEnv))
        {
            foreach (var part in aggEnv.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (kv.Length == 2)
                {
                    provided[kv[0].ToLowerInvariant()] = kv[1];
                }
            }
        }
        // Explicit overrides NODE_LABEL_<KEY>
        foreach (var key in allowedKeys)
        {
            var envName = "NODE_LABEL_" + key.ToUpperInvariant();
            var val = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(val))
            {
                provided[key] = val;
            }
        }
        // Project to allowed values, coalescing to 'other'
        foreach (var kvp in provided)
        {
            var k = kvp.Key.ToLowerInvariant();
            if (!allowedValues.TryGetValue(k, out var set)) continue; // ignore unknown keys
            var v = (kvp.Value ?? string.Empty).Trim().ToLowerInvariant();
            if (!set.Contains(v))
            {
                v = "other";
            }
            labels[k] = v;
        }

        // Parse optional sidecar ready timeout (seconds)
        var timeoutEnv = Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS");
        var timeoutSeconds = 120;
        if (!string.IsNullOrWhiteSpace(timeoutEnv) && int.TryParse(timeoutEnv.Trim(), out var parsed))
        {
            // Clamp to sane range 5..600
            timeoutSeconds = Math.Min(600, Math.Max(5, parsed));
        }

        // WS limits and ping intervals
        var maxMessageEnv = Environment.GetEnvironmentVariable("WS_MAX_MESSAGE_BYTES");
        var maxMessageBytes = 2_097_152;
        if (!string.IsNullOrWhiteSpace(maxMessageEnv) && int.TryParse(maxMessageEnv.Trim(), out var mm))
        {
            // Clamp to 8 KiB .. 16 MiB
            maxMessageBytes = Math.Min(16 * 1024 * 1024, Math.Max(8 * 1024, mm));
        }

        var idleEnv = Environment.GetEnvironmentVariable("WS_IDLE_TIMEOUT_SECONDS");
        var idleSeconds = 60;
        if (!string.IsNullOrWhiteSpace(idleEnv) && int.TryParse(idleEnv.Trim(), out var isec))
        {
            idleSeconds = Math.Min(600, Math.Max(5, isec));
        }

        var pingEnv = Environment.GetEnvironmentVariable("WS_PING_INTERVAL_SECONDS");
        var pingSeconds = 15;
        if (!string.IsNullOrWhiteSpace(pingEnv) && int.TryParse(pingEnv.Trim(), out var psec))
        {
            pingSeconds = Math.Min(300, Math.Max(5, psec));
        }

        // WS log backpressure
        var logCapEnv = Environment.GetEnvironmentVariable("WS_LOG_CHANNEL_CAPACITY");
        var logCap = 256;
        if (!string.IsNullOrWhiteSpace(logCapEnv) && int.TryParse(logCapEnv.Trim(), out var lcap))
        {
            logCap = Math.Min(8192, Math.Max(16, lcap));
        }

        var logPolicyEnv = Environment.GetEnvironmentVariable("WS_LOG_DROP_POLICY") ?? "DropNewest";
        var logPolicy = WsDropPolicy.DropNewest;
        if (!Enum.TryParse<WsDropPolicy>(logPolicyEnv, true, out logPolicy))
        {
            // Non-fatal: default to DropNewest
            logPolicy = WsDropPolicy.DropNewest;
            errors.Add($"WS_LOG_DROP_POLICY has invalid value '{logPolicyEnv}'. Allowed: DropNewest, DropOldest.");
        }

        // WS proxy backpressure (data path)
        var proxyCapEnv = Environment.GetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY");
        var proxyCap = 1024;
        if (!string.IsNullOrWhiteSpace(proxyCapEnv) && int.TryParse(proxyCapEnv.Trim(), out var pcap))
        {
            proxyCap = Math.Min(65536, Math.Max(32, pcap));
        }
        var proxyPolicyEnv = Environment.GetEnvironmentVariable("WS_PROXY_DROP_POLICY") ?? "DropNewest";
        var proxyPolicy = WsDropPolicy.DropNewest;
        if (!Enum.TryParse<WsDropPolicy>(proxyPolicyEnv, true, out proxyPolicy))
        {
            // Non-fatal: default to DropNewest
            proxyPolicy = WsDropPolicy.DropNewest;
            errors.Add($"WS_PROXY_DROP_POLICY has invalid value '{proxyPolicyEnv}'. Allowed: DropNewest, DropOldest.");
        }

        // Borrow idle refresh timeout (seconds), sourced from HUB_IDLE_TIMEOUT_SECONDS
        var borrowIdleEnv = Environment.GetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS");
        var borrowIdleSeconds = 120;
        if (!string.IsNullOrWhiteSpace(borrowIdleEnv) && int.TryParse(borrowIdleEnv.Trim(), out var bIdle))
        {
            borrowIdleSeconds = Math.Min(86400, Math.Max(10, bIdle));
        }

        // Sidecar restart backoff parsing
        int clamp(int v, int min, int max) => Math.Min(max, Math.Max(min, v));
        double clampd(double v, double min, double max) => Math.Min(max, Math.Max(min, v));
        var backoffMin = clamp(int.TryParse(Environment.GetEnvironmentVariable("SIDECAR_BACKOFF_MIN_SECONDS"), out var bmin) ? bmin : 1, 1, 120);
        var backoffMax = clamp(int.TryParse(Environment.GetEnvironmentVariable("SIDECAR_BACKOFF_MAX_SECONDS"), out var bmax) ? bmax : 30, 1, 600);
        if (backoffMax < backoffMin) backoffMax = backoffMin;
        var backoffMult = clampd(double.TryParse(Environment.GetEnvironmentVariable("SIDECAR_BACKOFF_MULTIPLIER"), out var bmul) ? bmul : 2.0, 1.1, 5.0);
        var backoffReset = clamp(int.TryParse(Environment.GetEnvironmentVariable("SIDECAR_BACKOFF_FAILURE_RESET_SECONDS"), out var brst) ? brst : 60, 10, 600);

        // Critical validations
        var hubUrlEnv = Environment.GetEnvironmentVariable("HUB_URL") ?? "http://hub:5000";
        if (!Uri.TryCreate(hubUrlEnv, UriKind.Absolute, out var hubUri) ||
            !(hubUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || hubUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"HUB_URL must be a valid http/https URL. Value: '{hubUrlEnv}'.");
        }

        var publicWsHost = Environment.GetEnvironmentVariable("PUBLIC_WS_HOST");
        var publicWsPortRaw = Environment.GetEnvironmentVariable("PUBLIC_WS_PORT");
        var publicWsScheme = Environment.GetEnvironmentVariable("PUBLIC_WS_SCHEME") ?? "ws";
        if (!string.IsNullOrWhiteSpace(publicWsScheme) &&
            !(publicWsScheme.Equals("ws", StringComparison.OrdinalIgnoreCase) || publicWsScheme.Equals("wss", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"PUBLIC_WS_SCHEME must be 'ws' or 'wss'. Value: '{publicWsScheme}'.");
        }
        var hostProvided = !string.IsNullOrWhiteSpace(publicWsHost);
        var portProvided = !string.IsNullOrWhiteSpace(publicWsPortRaw);
        if (hostProvided ^ portProvided)
        {
            errors.Add("PUBLIC_WS_HOST and PUBLIC_WS_PORT must be provided together to enable public WS endpoint.");
        }
        int? publicPort = null;
        if (portProvided)
        {
            if (!int.TryParse(publicWsPortRaw!.Trim(), out var p) || p < 1 || p > 65535)
            {
                errors.Add($"PUBLIC_WS_PORT must be an integer between 1 and 65535. Value: '{publicWsPortRaw}'.");
            }
            else
            {
                publicPort = p;
            }
        }

        if (errors.Count > 0 && (errors.Any(e => e.StartsWith("HUB_URL")) || errors.Any(e => e.StartsWith("PUBLIC_WS_"))))
        {
            throw new ArgumentException("Invalid worker configuration:\n - " + string.Join("\n - ", errors));
        }

        return new WorkerOptions
        {
            HubUrl = hubUrlEnv,
            RedisUrl = Environment.GetEnvironmentVariable("REDIS_URL") ?? "redis:6379",
            NodeId = Environment.GetEnvironmentVariable("NODE_ID") ?? $"node-{Guid.NewGuid():N}",
            NodeSecret = Environment.GetEnvironmentVariable("NODE_SECRET") ?? "node-secret",
            NodeNodeSecret = Environment.GetEnvironmentVariable("NODE_NODE_SECRET") ?? "node-node-secret",
            PoolConfigEnv = poolConfigEnv,
            NodeExe = Environment.GetEnvironmentVariable("NODE_EXE") ?? "node",
            SidecarScript =
                Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR") ?? "launch_playwright_server.js",
            SidecarReadyTimeoutSeconds = timeoutSeconds,
            PublicWsHost = publicWsHost,
            PublicWsPort = publicWsPortRaw,
            PublicWsScheme = publicWsScheme,
            Labels = labels,
            PoolConfig = pools,
            WebSocketMaxMessageBytes = maxMessageBytes,
            WebSocketIdleTimeoutSeconds = idleSeconds,
            WebSocketPingIntervalSeconds = pingSeconds,
            WebSocketLogChannelCapacity = logCap,
            WebSocketLogDropPolicy = logPolicy,
            WebSocketProxyChannelCapacity = proxyCap,
            WebSocketProxyDropPolicy = proxyPolicy,
            BorrowIdleTimeoutSeconds = borrowIdleSeconds,
            SidecarBackoff = new BackoffSettings(backoffMin, backoffMax, backoffMult, backoffReset)
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
