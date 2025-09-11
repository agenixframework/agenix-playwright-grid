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
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agenix.PlaywrightGrid.Domain;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class PoolManager(
    WorkerOptions options,
    IDatabase db,
    IMetricsPort metrics,
    ISidecarLauncher sidecarLauncher)
{
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = Microsoft.Extensions.Logging.LoggerFactory
        .Create(b => b.AddSimpleConsole())
        .CreateLogger("worker.pool");

    private sealed record BackoffState(int Failures, DateTime LastFailureUtc, TimeSpan NextDelay);

    // Tracks restart backoff per labelKey
    private readonly ConcurrentDictionary<string, BackoffState> _restartBackoff = new(StringComparer.OrdinalIgnoreCase);
    // Tracks active client WebSocket connections per browserId to prevent recycling in-use slots
    private readonly ConcurrentDictionary<string, int> _activeWs = new(StringComparer.OrdinalIgnoreCase);

    // labelKey -> (browserId -> Slot)
    public ConcurrentDictionary<string, ConcurrentDictionary<string, Slot>> Pools { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void MarkConnectionStart(string browserId)
    {
        _activeWs.AddOrUpdate(browserId, 1, static (_, v) => v + 1);
    }

    public void MarkConnectionEnd(string browserId)
    {
        _activeWs.AddOrUpdate(browserId, 0, static (_, v) => v > 1 ? v - 1 : 0);
        if (_activeWs.TryGetValue(browserId, out var cnt) && cnt <= 0)
        {
            _activeWs.TryRemove(browserId, out _);
        }
    }

    public bool HasActiveConnection(string browserId)
    {
        return _activeWs.TryGetValue(browserId, out var v) && v > 0;
    }

    public bool HasAnyActiveConnections()
    {
        try { return !_activeWs.IsEmpty; }
        catch { return _activeWs.Count > 0; }
    }

    public IEnumerable<string> GetActiveBrowserIds()
    {
        try { return _activeWs.Keys.ToArray(); }
        catch { return _activeWs.Keys; }
    }

    private static string NormalizeBrowser(string s)
    {
        return string.IsNullOrWhiteSpace(s) ? "Chromium" : s.Trim();
    }

    private async Task<SidecarStartResult> StartPwServerAsync(string browserType)
    {
        return await sidecarLauncher.StartAsync(browserType);
    }

    private async Task UpdatePlaywrightVersionTelemetryAsync(string? actual)
    {
        try
        {
            // Only act when sidecar reported a version; otherwise skip to avoid noisy metrics/tests
            if (string.IsNullOrWhiteSpace(actual)) return;

            var expected = Environment.GetEnvironmentVariable("PLAYWRIGHT_VERSION");
            var mismatch = !string.IsNullOrWhiteSpace(expected) && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

            // Authoritative: store sidecar-reported version on node metadata
            await db.HashSetAsync(RedisKeys.Node(options.NodeId), "PlaywrightVersion", actual);

            // Also store expected and mismatch flags for Hub/Dashboard consumption
            try
            {
                await db.HashSetAsync(RedisKeys.Node(options.NodeId), new HashEntry[]
            {
                new("PlaywrightVersionExpected", expected ?? string.Empty),
                new("PlaywrightVersionMismatch", mismatch ? "1" : "0")
            });
            }
            catch { }

            // Metrics: gauge 0/1 with labels expected/actual
            try { metrics.SetPlaywrightVersionMismatch(options.NodeId, expected ?? string.Empty, actual ?? string.Empty, mismatch ? 1 : 0); }
            catch { }
        }
        catch
        {
            // swallow any telemetry errors; should not impact pool warmup
        }
    }

    private static int SafePid(Process proc)
    {
        try { return proc.Id; }
        catch { return 0; }
    }

    private TimeSpan ComputeNextDelaySeconds(int failures)
    {
        if (failures <= 0) return TimeSpan.Zero;
        var min = Math.Max(0, options.SidecarBackoff.MinSeconds);
        var max = Math.Max(min, options.SidecarBackoff.MaxSeconds);
        var mult = options.SidecarBackoff.Multiplier <= 0 ? 2.0 : options.SidecarBackoff.Multiplier;
        var seconds = (int)Math.Round(min * Math.Pow(mult, Math.Max(0, failures - 1)));
        seconds = Math.Clamp(seconds, min, max);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<BackoffState> RegisterFailureAsync(string labelKey, TimeSpan uptime)
    {
        var resetSeconds = Math.Max(1, options.SidecarBackoff.FailureResetSeconds);
        var failures = 1;
        if (_restartBackoff.TryGetValue(labelKey, out var prev))
        {
            failures = uptime.TotalSeconds >= resetSeconds ? 1 : prev.Failures + 1;
        }
        var next = ComputeNextDelaySeconds(failures);
        var state = new BackoffState(failures, DateTime.UtcNow, next);
        _restartBackoff[labelKey] = state;
        try
        {
            await db.HashSetAsync($"node:{options.NodeId}", new HashEntry[]
            {
                new($"SidecarFailures:{labelKey}", failures),
                new($"SidecarNextDelaySeconds:{labelKey}", (int)next.TotalSeconds),
                new($"SidecarLastFailureUtc:{labelKey}", state.LastFailureUtc.ToString("o")),
                new($"SidecarHealth:{labelKey}", $"Backoff { (int)next.TotalSeconds }s")
            });
        }
        catch { }
        return state;
    }

    private async Task RegisterSuccessAsync(string labelKey)
    {
        _restartBackoff.TryRemove(labelKey, out _);
        try
        {
            await db.HashSetAsync($"node:{options.NodeId}", new HashEntry[]
            {
                new($"SidecarNextDelaySeconds:{labelKey}", 0),
                new($"SidecarHealth:{labelKey}", "OK")
            });
        }
        catch { }
    }

    public IDictionary<string, (int failures, DateTime? lastFailureUtc, TimeSpan nextDelay)> GetBackoffAll()
    {
        var copy = new Dictionary<string, (int, DateTime?, TimeSpan)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _restartBackoff)
        {
            copy[kv.Key] = (kv.Value.Failures, kv.Value.LastFailureUtc, kv.Value.NextDelay);
        }
        return copy;
    }

    public async Task InitializeAsync()
    {
        foreach (var kv in options.PoolConfig)
        {
            await CleanupLabelListsAsync(kv.Key);
            await WarmLabelAsync(kv.Key, kv.Value);
        }
    }

    public async Task WarmLabelAsync(string labelKey, int count)
    {
        var map = Pools.GetOrAdd(labelKey, _ => new ConcurrentDictionary<string, Slot>());
        string browserType;
        if (LabelKey.TryParse(labelKey, out var lk, new LabelKeyParsingOptions { EnforceBrowserSecond = false }))
        {
            browserType = string.IsNullOrWhiteSpace(lk!.Browser) ? "Chromium" : lk.Browser;
        }
        else
        {
            var parts = labelKey.Split(':', StringSplitOptions.TrimEntries);
            browserType = NormalizeBrowser(parts.Length >= 2 ? parts[1] : "Chromium");
        }

        var availableKey = RedisKeys.Available(labelKey);

        // Local helper: validate that the internal WS endpoint accepts a WebSocket handshake
        static async Task<bool> ValidateWsAsync(string wsUrl, TimeSpan timeout)
        {
            try
            {
                using var cws = new ClientWebSocket();
                cws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                using var cts = new CancellationTokenSource(timeout);
                await cws.ConnectAsync(new Uri(wsUrl), cts.Token);
                var ok = cws.State == WebSocketState.Open;
                try
                {
                    if (ok)
                    {
                        await cws.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok",
                            CancellationToken.None);
                    }
                }
                catch { }

                return ok;
            }
            catch { return false; }
        }

        // Local helper: detect container provenance (best-effort)
        static (bool isContainer, string? containerId) GetContainerInfo()
        {
            var isContainer = false;
            string? containerId = null;
            try
            {
                isContainer = File.Exists("/.dockerenv") ||
                              Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
                try
                {
                    // Prefer hostname as container id hint
                    if (File.Exists("/etc/hostname"))
                    {
                        var h = File.ReadAllText("/etc/hostname").Trim();
                        if (!string.IsNullOrWhiteSpace(h))
                        {
                            containerId = h;
                        }
                    }
                    else if (File.Exists("/proc/self/cgroup"))
                    {
                        var cg = File.ReadAllText("/proc/self/cgroup");
                        var idx = cg.LastIndexOf('/', cg.Length - 1);
                        if (idx >= 0 && idx + 1 < cg.Length)
                        {
                            var tail = cg[(idx + 1)..].Trim();
                            if (!string.IsNullOrWhiteSpace(tail))
                            {
                                containerId = tail;
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }

            return (isContainer, containerId);
        }

        for (var i = 0; i < count; i++)
        {
            var res = await StartPwServerAsync(browserType);
            var id = Guid.NewGuid().ToString("N");

            res.proc.EnableRaisingEvents = true;
            res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, id, browserType);

            // Optionally validate WS reachability (bounded retries); restart sidecar once if enabled
            var validateWarm = string.Equals(Environment.GetEnvironmentVariable("WORKER_VALIDATE_WS"), "true",
                StringComparison.OrdinalIgnoreCase);
            var validated = !validateWarm;
            if (validateWarm)
            {
                for (var restart = 0; restart <= 1 && !validated; restart++)
                {
                    for (var attempt = 0; attempt < 10 && !validated; attempt++)
                    {
                        validated = await ValidateWsAsync(res.ws, TimeSpan.FromSeconds(3));
                        if (!validated)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
                        }
                    }

                    if (!validated && restart == 0)
                    {
                        try
                        {
                            if (!res.proc.HasExited)
                            {
                                res.proc.Kill(true);
                            }
                        }
                        catch { }

                        await Task.Delay(200);
                        res = await StartPwServerAsync(browserType);
                        // keep same id and exit handler already attached for previous proc id; attach again for the new one
                        res.proc.EnableRaisingEvents = true;
                        res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, id, browserType);
                    }
                }
            }

            if (!validated)
            {
                Logger.LogWarning("[Warm] Failed to validate WS for {labelKey}; skipping slot initialization", labelKey);
                continue;
            }

            // Public endpoint goes through this worker’s proxy (single stable port).
            string wsPublic;
            if (!string.IsNullOrWhiteSpace(options.PublicWsHost) && !string.IsNullOrWhiteSpace(options.PublicWsPort))
            {
                wsPublic = $"{options.PublicWsScheme}://{options.PublicWsHost}:{options.PublicWsPort}/ws/{id}";
            }
            else if (!string.IsNullOrWhiteSpace(options.NodeId))
            {
                wsPublic =
                    $"{options.PublicWsScheme}://{options.NodeId}:5000/ws/{id}"; // default to proxy via worker service name
            }
            else
            {
                wsPublic = res.ws; // last resort fallback
            }

            var slot = new Slot(res.proc, browserType, res.ws, wsPublic, DateTime.UtcNow);
            map[id] = slot;

            // Update Playwright version telemetry (authoritative from sidecar) and mismatch metric
            await UpdatePlaywrightVersionTelemetryAsync(res.playwrightVersion);

            var argsEnv = browserType.Equals("chromium", StringComparison.OrdinalIgnoreCase)
                ? (Environment.GetEnvironmentVariable("CHROMIUM_ARGS") ?? Environment.GetEnvironmentVariable("CHROME_ARGS"))
                : browserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                    ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                    : Environment.GetEnvironmentVariable("FIREFOX_ARGS");
            var (isContainer, containerId) = GetContainerInfo();
            var item = JsonSerializer.Serialize(new
            {
                nodeId = options.NodeId,
                browserId = id,
                webSocketEndpoint = wsPublic,
                browserType,
                res.browserVersion,
                args = argsEnv,
                labels = options.Labels.ToDictionary(k => k.Key, v => v.Value),
                isContainer,
                containerId,
                workerPid = Process.GetCurrentProcess().Id,
                sidecarPid = SafePid(res.proc)
            });
            await db.ListRightPushAsync(availableKey, item);

            Logger.LogInformation("+ warm server {browserId} for {labelKey} (type={browserType}) ws={wsPublic} (container={isContainer} id={containerId})", id, labelKey, browserType, wsPublic, isContainer, containerId ?? "?");
        }

        metrics.SetPoolCapacity(options.NodeId, labelKey, map.Count);
        metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));
    }

    private async Task OnSidecarExited(string labelKey, string browserId, string browserType)
    {
        try
        {
            var availableKey = RedisKeys.Available(labelKey);
            var inuseKey = RedisKeys.InUse(labelKey);

            var existedInMap = false;
            Slot? removedSlot = null;
            if (Pools.TryGetValue(labelKey, out var map))
            {
                if (map.TryRemove(browserId, out var os))
                {
                    existedInMap = true;
                    removedSlot = os;
                }
            }

            // Prune stale entries for the dead browserId
            static async Task PruneAsync(IDatabase db, string listKey, string browserId)
            {
                var entries = await db.ListRangeAsync(listKey);
                var pattern = $"\"browserId\":\"{browserId}\"";
                foreach (var e in entries)
                {
                    if (e.ToString().Contains(pattern, StringComparison.Ordinal))
                    {
                        await db.ListRemoveAsync(listKey, e, 1);
                    }
                }
            }

            await PruneAsync(db, availableKey, browserId);
            await PruneAsync(db, inuseKey, browserId);

            // If reconcile already processed this slot (we didn't find it in map), skip spawning to avoid double replacement
            if (!existedInMap)
            {
                Logger.LogInformation("[SidecarExit] Skip replacement for {browserId} ({labelKey}) - already reconciled", browserId, labelKey);
                return;
            }

            // Launch replacement with backoff
            var uptime = removedSlot is not null ? DateTime.UtcNow - removedSlot.StartedAt : TimeSpan.Zero;
            var boState = await RegisterFailureAsync(labelKey, uptime);
            if (boState.NextDelay > TimeSpan.Zero)
            {
                Logger.LogWarning("[SidecarExit] Backing off {seconds}s before respawn for {labelKey} (failures={failures})", boState.NextDelay.TotalSeconds.ToString("F0"), labelKey, boState.Failures);
                try { await Task.Delay(boState.NextDelay); } catch { }
            }

            var res = await StartPwServerAsync(browserType);
            var newId = Guid.NewGuid().ToString("N");

            // Validate internal WS reachability; allow one restart
            var validated = false;
            for (var restart = 0; restart <= 1 && !validated; restart++)
            {
                for (var attempt = 0; attempt < 10 && !validated; attempt++)
                {
                    try
                    {
                        using var cws = new ClientWebSocket();
                        cws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await cws.ConnectAsync(new Uri(res.ws), cts.Token);
                        validated = cws.State == WebSocketState.Open;
                        try
                        {
                            if (validated)
                            {
                                await cws.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok",
                                    CancellationToken.None);
                            }
                        }
                        catch { }
                    }
                    catch { }

                    if (!validated)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
                    }
                }

                if (!validated && restart == 0)
                {
                    try
                    {
                        if (!res.proc.HasExited)
                        {
                            res.proc.Kill(true);
                        }
                    }
                    catch { }

                    await Task.Delay(200);
                    res = await StartPwServerAsync(browserType);
                    res.proc.EnableRaisingEvents = true;
                    res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, newId, browserType);
                }
            }

            if (!validated)
            {
                Logger.LogWarning("[SidecarExit] Replacement failed WS validation for {labelKey}; capacity temporarily reduced", labelKey);
                return;
            }

            string wsPublic;
            if (!string.IsNullOrWhiteSpace(options.PublicWsHost) && !string.IsNullOrWhiteSpace(options.PublicWsPort))
            {
                wsPublic = $"{options.PublicWsScheme}://{options.PublicWsHost}:{options.PublicWsPort}/ws/{newId}";
            }
            else if (!string.IsNullOrWhiteSpace(options.NodeId))
            {
                wsPublic = $"{options.PublicWsScheme}://{options.NodeId}:5000/ws/{newId}";
            }
            else
            {
                wsPublic = res.ws;
            }

            var newSlot = new Slot(res.proc, browserType, res.ws, wsPublic, DateTime.UtcNow);
            var map2 = Pools.GetOrAdd(labelKey, _ => new ConcurrentDictionary<string, Slot>());
            map2[newId] = newSlot;
            await RegisterSuccessAsync(labelKey);

            // Hook exit for the new process as well
            res.proc.EnableRaisingEvents = true;
            res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, newId, browserType);

            // Update Playwright version telemetry (authoritative from sidecar) and mismatch metric
            await UpdatePlaywrightVersionTelemetryAsync(res.playwrightVersion);

            // Container provenance
            var isContainer = File.Exists("/.dockerenv") ||
                              Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            string? containerId = null;
            try
            {
                if (File.Exists("/etc/hostname"))
                {
                    var h = File.ReadAllText("/etc/hostname").Trim();
                    if (!string.IsNullOrWhiteSpace(h))
                    {
                        containerId = h;
                    }
                }
                else if (File.Exists("/proc/self/cgroup"))
                {
                    var cg = File.ReadAllText("/proc/self/cgroup");
                    var idx = cg.LastIndexOf('/', cg.Length - 1);
                    if (idx >= 0 && idx + 1 < cg.Length)
                    {
                        var tail = cg[(idx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(tail))
                        {
                            containerId = tail;
                        }
                    }
                }
            }
            catch { }

            var argsEnv = browserType.Equals("chromium", StringComparison.OrdinalIgnoreCase)
                ? (Environment.GetEnvironmentVariable("CHROMIUM_ARGS") ?? Environment.GetEnvironmentVariable("CHROME_ARGS"))
                : browserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                    ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                    : Environment.GetEnvironmentVariable("FIREFOX_ARGS");
            var item = JsonSerializer.Serialize(new
            {
                nodeId = options.NodeId,
                browserId = newId,
                webSocketEndpoint = wsPublic,
                browserType,
                res.browserVersion,
                args = argsEnv,
                labels = options.Labels.ToDictionary(k => k.Key, v => v.Value),
                isContainer,
                containerId,
                workerPid = Process.GetCurrentProcess().Id,
                sidecarPid = SafePid(res.proc)
            });
            await db.ListRightPushAsync(availableKey, item);

            metrics.SetPoolCapacity(options.NodeId, labelKey, map2.Count);
            metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));

            Logger.LogInformation("{browserId} replaced with {newId} for {labelKey} (container={isContainer} id={containerId})", browserId, newId, labelKey, isContainer, containerId ?? "?");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SidecarExit] error: {message}", ex.Message);
        }
    }

    public async Task CleanupLabelListsAsync(string labelKey)
    {
        var keys = new[] { RedisKeys.Available(labelKey), RedisKeys.InUse(labelKey) };
        foreach (var key in keys)
        {
            try
            {
                var items = await db.ListRangeAsync(key);
                foreach (var item in items)
                {
                    var s = item.ToString();
                    // Remove entries that belong to this node (stale old format) OR point to localhost
                    var isThisNode = s.Contains($"\"nodeId\":\"{options.NodeId}\"", StringComparison.Ordinal);
                    var isLocalhost = s.Contains("\"webSocketEndpoint\":\"ws://localhost",
                        StringComparison.OrdinalIgnoreCase);
                    if (isThisNode || isLocalhost)
                    {
                        await db.ListRemoveAsync(key, item);

                        // Try to extract browserId for clearer logging
                        var match = Regex.Match(s, "\"browserId\":\"(?<id>[^\"]+)\"");
                        var browserId = match.Success ? match.Groups["id"].Value : "unknown";
                        var listName = key.Contains(':') ? key.Split(':')[0] : key;
                        var reason = isThisNode ? "belongs to this node (old format)" : "localhost endpoint";

                        Logger.LogInformation("[Startup-Cleanup] node={nodeId} label={labelKey} list={listName} browserId={browserId} removed stale item (reason={reason})", options.NodeId, labelKey, listName, browserId, reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Startup-Cleanup] node={nodeId} label={labelKey} error cleaning key={key}: {message}", options.NodeId, labelKey, key, ex.Message);
            }
        }
    }

    public async Task ReconcileLoopAsync(CancellationToken ct)
    {
        var checkInterval = TimeSpan.FromSeconds(15);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var labelEntry in Pools)
                {
                    var labelKey = labelEntry.Key;
                    var map = labelEntry.Value;
                    var availableKey = RedisKeys.Available(labelKey);
                    var inuseKey = RedisKeys.InUse(labelKey);

                    foreach (var kv in map.ToArray())
                    {
                        var browserId = kv.Key;
                        var slot = kv.Value;

                        var replaceRequested = await db.KeyExistsAsync(RedisKeys.Recycle(browserId));
                        var needsReplace = slot.Proc.HasExited || replaceRequested;
                        if (!needsReplace)
                        {
                            continue;
                        }

                        // Defer recycling if a client is currently connected to this browserId
                        var hasActive = HasActiveConnection(browserId);
                        if (replaceRequested && hasActive)
                        {
                            Logger.LogInformation("[Reconcile] Defer recycle for {browserId} ({labelKey}) - active WS connection", browserId, labelKey);
                            continue;
                        }

                        if (replaceRequested)
                        {
                            try { await db.KeyDeleteAsync(RedisKeys.Recycle(browserId)); }
                            catch { }
                        }

                        Logger.LogInformation("[Reconcile] Sidecar {browserId} for {labelKey} exited - replacing", browserId, labelKey);
                        var removed = map.TryRemove(browserId, out _);
                        if (!removed)
                        {
                            Logger.LogInformation("[Reconcile] Skip replacement for {browserId} ({labelKey}) - already handled by exit handler", browserId, labelKey);
                            continue;
                        }

                        try
                        {
                            if (!slot.Proc.HasExited)
                            {
                                slot.Proc.Kill(true);
                            }
                        }
                        catch
                        {
                            // ignored
                        }

                        // Prune stale entries from both available and inuse lists
                        foreach (var listKey in new[] { availableKey, inuseKey })
                        {
                            var entries = await db.ListRangeAsync(listKey);
                            var pattern = $"\"browserId\":\"{browserId}\"";
                            foreach (var e in entries)
                            {
                                if (e.ToString().Contains(pattern, StringComparison.Ordinal))
                                {
                                    await db.ListRemoveAsync(listKey, e);
                                }
                            }
                        }

                        // Launch a replacement with backoff
                        var uptime = DateTime.UtcNow - slot.StartedAt;
                        var boState = await RegisterFailureAsync(labelKey, uptime);
                        if (boState.NextDelay > TimeSpan.Zero)
                        {
                            Logger.LogWarning("[Reconcile] Backing off {seconds}s before respawn for {labelKey} (failures={failures})", boState.NextDelay.TotalSeconds.ToString("F0"), labelKey, boState.Failures);
                            try { await Task.Delay(boState.NextDelay, ct); } catch { }
                        }

                        var res = await StartPwServerAsync(slot.BrowserType);
                        var newId = Guid.NewGuid().ToString("N");

                        string wsPublic;
                        if (!string.IsNullOrWhiteSpace(options.PublicWsHost) &&
                            !string.IsNullOrWhiteSpace(options.PublicWsPort))
                        {
                            wsPublic =
                                $"{options.PublicWsScheme}://{options.PublicWsHost}:{options.PublicWsPort}/ws/{newId}";
                        }
                        else if (!string.IsNullOrWhiteSpace(options.NodeId))
                        {
                            wsPublic = $"{options.PublicWsScheme}://{options.NodeId}:5000/ws/{newId}";
                        }
                        else
                        {
                            wsPublic = res.ws;
                        }

                        var newSlot = new Slot(res.proc, slot.BrowserType, res.ws, wsPublic, DateTime.UtcNow);
                        map[newId] = newSlot;
                        await RegisterSuccessAsync(labelKey);

                        // Hook exit for the new process as well
                        res.proc.EnableRaisingEvents = true;
                        res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, newId, slot.BrowserType);

                        // Update Playwright version telemetry (authoritative from sidecar) and mismatch metric
                        await UpdatePlaywrightVersionTelemetryAsync(res.playwrightVersion);

                        var argsEnv = slot.BrowserType.Equals("chromium", StringComparison.OrdinalIgnoreCase)
                            ? (Environment.GetEnvironmentVariable("CHROMIUM_ARGS") ?? Environment.GetEnvironmentVariable("CHROME_ARGS"))
                            : slot.BrowserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                                ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                                : Environment.GetEnvironmentVariable("FIREFOX_ARGS");

                        var item = JsonSerializer.Serialize(new
                        {
                            nodeId = options.NodeId,
                            browserId = newId,
                            webSocketEndpoint = wsPublic,
                            browserType = slot.BrowserType,
                            res.browserVersion,
                            args = argsEnv,
                            labels = options.Labels.ToDictionary(k => k.Key, v => v.Value)
                        });
                        await db.ListRightPushAsync(availableKey, item);

                        Logger.LogInformation("[Reconcile] Replaced {browserId} with {newId} for {labelKey}", browserId, newId, labelKey);
                    }

                    metrics.SetPoolCapacity(options.NodeId, labelKey, map.Count);
                    metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Reconcile] error: {message}", ex.Message);
            }

            try { await Task.Delay(checkInterval, ct); }
            catch { }
        }
    }

    public bool TryGetFirstSlot(string labelKey, out string browserId, out Slot slot)
    {
        browserId = string.Empty;
        slot = default!;
        if (!Pools.TryGetValue(labelKey, out var map) || map.IsEmpty)
        {
            return false;
        }

        var first = map.FirstOrDefault();
        if (first.Equals(default(KeyValuePair<string, Slot>)))
        {
            return false;
        }

        browserId = first.Key;
        slot = first.Value;
        return true;
    }

    public bool TryFindSlotById(string browserId, out Slot slot)
    {
        foreach (var labelMap in Pools.Values)
        {
            if (labelMap.TryGetValue(browserId, out var found))
            {
                slot = found!;
                return true;
            }
        }

        slot = default!;
        return false;
    }

    /// <summary>
    ///     Try to resolve the labelKey for a given browserId currently known by the worker.
    ///     This scans the in-memory Pools map (labelKey -> { browserId -> Slot }).
    /// </summary>
    /// <param name="browserId">The browser instance identifier.</param>
    /// <param name="labelKey">Resolved label key when found; empty string otherwise.</param>
    /// <returns>True when a matching labelKey was found; false otherwise.</returns>
    public bool TryFindLabelByBrowserId(string browserId, out string labelKey)
    {
        foreach (var kv in Pools)
        {
            if (kv.Value.ContainsKey(browserId))
            {
                labelKey = kv.Key;
                return true;
            }
        }
        labelKey = string.Empty;
        return false;
    }

    public Task<long> GetAvailableCountAsync(string labelKey)
    {
        return db.ListLengthAsync(RedisKeys.Available(labelKey));
    }

    public async Task CleanupAllAsync()
    {
        try
        {
            // Remove this worker from Redis lists and node metadata
            foreach (var label in options.PoolConfig.Keys)
            {
                try { await CleanupLabelListsAsync(label); }
                catch (Exception ex) { Logger.LogWarning(ex, "[Shutdown] error cleaning label {label}: {message}", label, ex.Message); }
            }

            try { await db.SetRemoveAsync("nodes", options.NodeId); }
            catch { }

            try { await db.KeyDeleteAsync(RedisKeys.Node(options.NodeId)); }
            catch { }

            try { await db.KeyDeleteAsync(RedisKeys.NodeAlive(options.NodeId)); }
            catch { }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Shutdown] cleanup error: {message}", ex.Message);
        }
    }

    public void KillAll()
    {
        foreach (var label in Pools.Keys)
        {
            if (!Pools.TryGetValue(label, out var map))
            {
                continue;
            }

            foreach (var s in map.Values)
            {
                try
                {
                    if (!s.Proc.HasExited)
                    {
                        s.Proc.Kill(true);
                    }
                }
                catch { }
            }
        }
    }
}
