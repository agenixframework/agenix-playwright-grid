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

    private static string NormalizeBrowser(string s)
    {
        return string.IsNullOrWhiteSpace(s) ? "Chromium" : s.Trim();
    }

    private async Task<SidecarStartResult> StartPwServerAsync(string browserType)
    {
        return await sidecarLauncher.StartAsync(browserType);
    }

    private static int SafePid(Process proc)
    {
        try { return proc.Id; }
        catch { return 0; }
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

        var availableKey = $"available:{labelKey}";

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
                Console.WriteLine($"[Warm] Failed to validate WS for {labelKey}; skipping slot initialization");
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

            // Store PlaywrightVersion on node metadata if available
            if (!string.IsNullOrWhiteSpace(res.playwrightVersion))
            {
                await db.HashSetAsync($"node:{options.NodeId}", "PlaywrightVersion", res.playwrightVersion);
            }

            var argsEnv = Environment.GetEnvironmentVariable("CHROMIUM_ARGS");
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

            Console.WriteLine(
                $"+ warm server {id} for {labelKey} (type={browserType}) ws={wsPublic} (container={isContainer} id={containerId ?? "?"})");
        }

        metrics.SetPoolCapacity(options.NodeId, labelKey, map.Count);
        metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));
    }

    private async Task OnSidecarExited(string labelKey, string browserId, string browserType)
    {
        try
        {
            var availableKey = $"available:{labelKey}";
            var inuseKey = $"inuse:{labelKey}";

            var existedInMap = false;
            if (Pools.TryGetValue(labelKey, out var map))
            {
                existedInMap = map.TryRemove(browserId, out _);
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
                Console.WriteLine($"[SidecarExit] Skip replacement for {browserId} ({labelKey}) - already reconciled");
                return;
            }

            // Launch replacement immediately
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
                Console.WriteLine(
                    $"[SidecarExit] Replacement failed WS validation for {labelKey}; capacity temporarily reduced");
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

            // Hook exit for the new process as well
            res.proc.EnableRaisingEvents = true;
            res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, newId, browserType);

            // Update node PlaywrightVersion if available
            if (!string.IsNullOrWhiteSpace(res.playwrightVersion))
            {
                await db.HashSetAsync($"node:{options.NodeId}", "PlaywrightVersion", res.playwrightVersion);
            }

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

            var argsEnv = Environment.GetEnvironmentVariable("CHROMIUM_ARGS");
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

            Console.WriteLine(
                $"  {browserId} with {newId} for {labelKey} (container={isContainer} id={containerId ?? "?"})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SidecarExit] error: {ex.Message}");
        }
    }

    public async Task CleanupLabelListsAsync(string labelKey)
    {
        var keys = new[] { $"available:{labelKey}", $"inuse:{labelKey}" };
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

                        Console.WriteLine(
                            $"[Startup-Cleanup] node={options.NodeId} label={labelKey} list={listName} browserId={browserId} removed stale item (reason={reason})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Startup-Cleanup] node={options.NodeId} label={labelKey} error cleaning key={key}: {ex.Message}");
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
                    var availableKey = $"available:{labelKey}";
                    var inuseKey = $"inuse:{labelKey}";

                    foreach (var kv in map.ToArray())
                    {
                        var browserId = kv.Key;
                        var slot = kv.Value;

                        var replaceRequested = await db.KeyExistsAsync($"recycle:{browserId}");
                        var needsReplace = slot.Proc.HasExited || replaceRequested;
                        if (!needsReplace)
                        {
                            continue;
                        }

                        // Defer recycling if a client is currently connected to this browserId
                        var hasActive = HasActiveConnection(browserId);
                        if (replaceRequested && hasActive)
                        {
                            Console.WriteLine(
                                $"[Reconcile] Defer recycle for {browserId} ({labelKey}) - active WS connection");
                            continue;
                        }

                        if (replaceRequested)
                        {
                            try { await db.KeyDeleteAsync($"recycle:{browserId}"); }
                            catch { }
                        }

                        Console.WriteLine($"[Reconcile] Sidecar {browserId} for {labelKey} exited - replacing");
                        var removed = map.TryRemove(browserId, out _);
                        if (!removed)
                        {
                            Console.WriteLine(
                                $"[Reconcile] Skip replacement for {browserId} ({labelKey}) - already handled by exit handler");
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

                        // Launch a replacement
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

                        // Hook exit for the new process as well
                        res.proc.EnableRaisingEvents = true;
                        res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, newId, slot.BrowserType);

                        // Update node PlaywrightVersion if available
                        if (!string.IsNullOrWhiteSpace(res.playwrightVersion))
                        {
                            await db.HashSetAsync($"node:{options.NodeId}", "PlaywrightVersion", res.playwrightVersion);
                        }

                        var item = JsonSerializer.Serialize(new
                        {
                            nodeId = options.NodeId,
                            browserId = newId,
                            webSocketEndpoint = wsPublic,
                            browserType = slot.BrowserType,
                            res.browserVersion,
                            labels = options.Labels.ToDictionary(k => k.Key, v => v.Value)
                        });
                        await db.ListRightPushAsync(availableKey, item);

                        Console.WriteLine($"[Reconcile] Replaced {browserId} with {newId} for {labelKey}");
                    }

                    metrics.SetPoolCapacity(options.NodeId, labelKey, map.Count);
                    metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Reconcile] error: {ex.Message}");
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
            if (labelMap.TryGetValue(browserId, out slot))
            {
                return true;
            }
        }

        slot = default!;
        return false;
    }

    public Task<long> GetAvailableCountAsync(string labelKey)
    {
        return db.ListLengthAsync($"available:{labelKey}");
    }

    public async Task CleanupAllAsync()
    {
        try
        {
            // Remove this worker from Redis lists and node metadata
            foreach (var label in options.PoolConfig.Keys)
            {
                try { await CleanupLabelListsAsync(label); }
                catch (Exception ex) { Console.WriteLine($"[Shutdown] error cleaning label {label}: {ex.Message}"); }
            }

            try { await db.SetRemoveAsync("nodes", options.NodeId); }
            catch { }

            try { await db.KeyDeleteAsync($"node:{options.NodeId}"); }
            catch { }

            try { await db.KeyDeleteAsync($"node_alive:{options.NodeId}"); }
            catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Shutdown] cleanup error: {ex.Message}");
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
