#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
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
using Agenix.PlaywrightGrid.Shared.Logging;
using StackExchange.Redis;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class PoolManager(
    WorkerOptions options,
    IDatabase db,
    IMetricsPort metrics,
    ISidecarLauncher sidecarLauncher,
    ILogger<PoolManager> logger,
    ChunkedLogger<PoolManager>? chunkedLogger = null,
    PidRedisManager? pidRedisManager = null)
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger = logger;
    private readonly ChunkedLogger<PoolManager>? _chunkedLogger = chunkedLogger;

    private readonly PidRedisManager? _pidRedisManager = pidRedisManager;

    // Tracks active client WebSocket connections per browserId to prevent recycling in-use slots
    private readonly ConcurrentDictionary<string, int> _activeWs = new(StringComparer.OrdinalIgnoreCase);

    // Tracks restart backoff per labelKey
    private readonly ConcurrentDictionary<string, BackoffState> _restartBackoff = new(StringComparer.OrdinalIgnoreCase);

    // labelKey -> (browserId -> Slot)
    public ConcurrentDictionary<string, ConcurrentDictionary<string, Slot>> Pools { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void MarkConnectionStart(string browserId)
    {
        _chunkedLogger?.LogMilestone(EventCodes.Worker.BrowserBorrowed, "[pool] Browser borrowed by client: {browserId}", browserId);
        _activeWs.AddOrUpdate(browserId, 1, static (_, v) => v + 1);
    }

    public void MarkConnectionEnd(string browserId)
    {
        _chunkedLogger?.LogMilestone(EventCodes.Worker.BrowserReturned, "[pool] Browser returned by client: {browserId}", browserId);
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
            if (string.IsNullOrWhiteSpace(actual))
            {
                return;
            }

            var expected = Environment.GetEnvironmentVariable("PLAYWRIGHT_VERSION");
            var mismatch = !string.IsNullOrWhiteSpace(expected) &&
                           !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

            // Authoritative: store a sidecar-reported version on node metadata
            await db.HashSetAsync(RedisKeys.Node(options.NodeId), "PlaywrightVersion", actual);

            // Also store expected and mismatch flags for Hub/Dashboard consumption
            try
            {
                await db.HashSetAsync(RedisKeys.Node(options.NodeId),
                [
                    new HashEntry("PlaywrightVersionExpected", expected ?? string.Empty),
                        new HashEntry("PlaywrightVersionMismatch", mismatch ? "1" : "0")
                ]);
            }
            catch { }

            // Metrics: gauge 0/1 with labels expected/actual
            try
            {
                metrics.SetPlaywrightVersionMismatch(options.NodeId, expected ?? string.Empty, actual ?? string.Empty,
                    mismatch ? 1 : 0);
            }
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
        if (failures <= 0)
        {
            return TimeSpan.Zero;
        }

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
            await db.HashSetAsync($"node:{options.NodeId}",
            [
                new($"SidecarFailures:{labelKey}", failures),
                    new($"SidecarNextDelaySeconds:{labelKey}", (int)next.TotalSeconds),
                    new($"SidecarLastFailureUtc:{labelKey}", state.LastFailureUtc.ToString("o")),
                    new($"SidecarHealth:{labelKey}", $"Backoff {(int)next.TotalSeconds}s")
            ]);
        }
        catch { }

        return state;
    }

    private async Task RegisterSuccessAsync(string labelKey)
    {
        _restartBackoff.TryRemove(labelKey, out _);
        try
        {
            await db.HashSetAsync($"node:{options.NodeId}",
            [
                new($"SidecarNextDelaySeconds:{labelKey}", 0), new($"SidecarHealth:{labelKey}", "OK")
            ]);
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
        using var op = _chunkedLogger?.BeginOperation("PoolInitialize", new Dictionary<string, object>
        {
            ["LabelCount"] = options.PoolConfig.Count,
            ["PoolConfig"] = options.PoolConfig.ToDictionary(k => k.Key, v => v.Value)
        });

        _chunkedLogger?.LogMilestone(EventCodes.Worker.PoolWarmingStarted, "[pool] InitializeAsync starting for {count} labels", options.PoolConfig.Count);

        // Layer 3: Detect and kill orphaned sidecar processes from previous runs
        await DetectAndCleanOrphanedProcessesAsync();

        // Enforce capacity constraint: remove labels no longer present in config
        var labelsToRemove = Pools.Keys.Where(k => !options.PoolConfig.ContainsKey(k)).ToList();
        foreach (var label in labelsToRemove)
        {
            _chunkedLogger?.LogMilestone(EventCodes.Worker.LabelRemoved, "[pool] Removing label {label} - not present in current config", label);
            await CleanupLabelListsAsync(label);
            if (Pools.TryRemove(label, out var map))
            {
                foreach (var kv in map)
                {
                    try { kv.Value.Proc.Kill(true); }
                    catch { }

                    if (_pidRedisManager != null)
                    {
                        await _pidRedisManager.UntrackPidAsync(kv.Value.Proc.Id);
                    }
                }
            }
        }

        var totalBrowsersStarted = 0;
        foreach (var kv in options.PoolConfig)
        {
            _chunkedLogger?.LogInformation(null, "[pool] Processing label {label} with count {count}", kv.Key, kv.Value);
            await CleanupLabelListsAsync(kv.Key);
            await WarmLabelAsync(kv.Key, kv.Value);
            totalBrowsersStarted += kv.Value;
        }

        _chunkedLogger?.LogMilestone(EventCodes.Worker.PoolWarmingCompleted, "[pool] InitializeAsync completed. Total browsers warmed: {total}", totalBrowsersStarted);
        LogPoolState();
        op?.Complete();
    }

    private void LogPoolState()
    {
        if (_chunkedLogger == null) return;

        var state = new Dictionary<string, object>();
        foreach (var pool in Pools)
        {
            var poolInfo = new Dictionary<string, object>
            {
                ["TargetCapacity"] = options.PoolConfig.GetValueOrDefault(pool.Key, 0),
                ["CurrentCount"] = pool.Value.Count,
                ["Browsers"] = pool.Value.Select(b => new
                {
                    BrowserId = b.Key,
                    b.Value.BrowserType,
                    b.Value.PublicWs,
                    b.Value.StartedAt,
                    InUse = HasActiveConnection(b.Key)
                }).ToList()
            };
            state[pool.Key] = poolInfo;
        }

        _chunkedLogger.LogInformation(null, "[pool] Current Pool State: {State}", JsonSerializer.Serialize(state));
    }

    private async Task<bool> ValidateWsAsync(string wsUrl, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var cws = new ClientWebSocket();
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

    private (bool isContainer, string? containerId) GetContainerInfo()
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

    private string BuildPublicWsUrl(string id, string internalWs)
    {
        if (!string.IsNullOrWhiteSpace(options.PublicWsHost) && !string.IsNullOrWhiteSpace(options.PublicWsPort))
        {
            return $"{options.PublicWsScheme}://{options.PublicWsHost}:{options.PublicWsPort}/ws/{id}";
        }
        else if (!string.IsNullOrWhiteSpace(options.NodeId))
        {
            return $"{options.PublicWsScheme}://{options.NodeId}:5000/ws/{id}"; // default to proxy via worker service name
        }
        else
        {
            return internalWs; // last resort fallback
        }
    }

    private string BuildRedisItem(string id, string wsPublic, string browserType, string? browserVersion, Process proc)
    {
        var argsEnv = browserType.Equals("chromium", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable("AGENIX_WORKER_CHROMIUM_ARGS") ??
              Environment.GetEnvironmentVariable("CHROME_ARGS")
            : browserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                : Environment.GetEnvironmentVariable("AGENIX_WORKER_FIREFOX_ARGS");

        var (isContainer, containerId) = GetContainerInfo();
        return JsonSerializer.Serialize(new
        {
            nodeId = options.NodeId,
            browserId = id,
            webSocketEndpoint = wsPublic,
            browserType,
            browserVersion,
            args = argsEnv,
            labels = options.Labels.ToDictionary(k => k.Key, v => v.Value),
            isContainer,
            containerId,
            workerPid = Process.GetCurrentProcess().Id,
            sidecarPid = SafePid(proc)
        });
    }

    private async Task<bool> StartAndRegisterSlotAsync(
        string labelKey,
        ConcurrentDictionary<string, Slot> map,
        string browserType,
        string availableKey,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var res = await StartPwServerAsync(browserType);

            if (_pidRedisManager != null)
            {
                await _pidRedisManager.TrackPidAsync(res.proc.Id, browserType, labelKey);
            }

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
                            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), ct);
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

                        await Task.Delay(200, ct);
                        res = await StartPwServerAsync(browserType);
                        // keep same id and exit handler already attached for previous proc id; attach again for the new one
                        res.proc.EnableRaisingEvents = true;
                        res.proc.Exited += async (_, __) => await OnSidecarExited(labelKey, id, browserType);
                    }
                }
            }

            if (!validated)
            {
                _chunkedLogger?.LogWarning(null, "[pool] Failed to validate WS for {labelKey}; skipping slot initialization",
                    labelKey);
                return false;
            }

            var wsPublic = BuildPublicWsUrl(id, res.ws);
            var slot = new Slot(res.proc, browserType, res.ws, wsPublic, DateTime.UtcNow);
            map[id] = slot;

            await UpdatePlaywrightVersionTelemetryAsync(res.playwrightVersion);

            var item = BuildRedisItem(id, wsPublic, browserType, res.browserVersion, res.proc);
            await db.ListRightPushAsync(availableKey, item);

            return true;
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogWarning(ex, null, "[pool] Failed to start and register browser for {labelKey}: {message}", labelKey, ex.Message);
            return false;
        }
    }

    public async Task WarmLabelAsync(string labelKey, int count)
    {
        using var op = _chunkedLogger?.BeginOperation("WarmLabel", new Dictionary<string, object>
        {
            ["LabelKey"] = labelKey,
            ["TargetCount"] = count
        });

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

        // Reconcile: identify and keep only healthy browsers already in memory for this label
        var healthyExisting = new List<(string id, Slot slot)>();
        foreach (var kv in map)
        {
            try
            {
                if (!kv.Value.Proc.HasExited)
                {
                    healthyExisting.Add((kv.Key, kv.Value));
                }
                else
                {
                    _chunkedLogger?.LogInformation(null, "[pool] Browser {browserId} for label {label} found to have exited - removing from memory", kv.Key, labelKey);
                    map.TryRemove(kv.Key, out _);
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger?.LogWarning(ex, null, "[pool] Error checking process health of browser {browserId} for label {label}", kv.Key, labelKey);
                map.TryRemove(kv.Key, out _);
            }
        }

        // Enforce capacity constraint: prune extra browsers if they exceed requested count
        if (healthyExisting.Count > count)
        {
            var toKillCount = healthyExisting.Count - count;
            _chunkedLogger?.LogMilestone(EventCodes.Worker.BrowserPruned,
                "[pool] WarmLabelAsync: pruning {toKill} extra browsers for label {label} (existing={healthy}, target={target})",
                toKillCount, labelKey, healthyExisting.Count, count);

            for (var i = 0; i < toKillCount; i++)
            {
                var (id, slot) = healthyExisting[healthyExisting.Count - 1 - i];
                if (map.TryRemove(id, out _))
                {
                    _chunkedLogger?.LogInformation(null, "[pool] Killing pruned browser {browserId} for label {label}", id, labelKey);
                    try
                    {
                        if (!slot.Proc.HasExited)
                        {
                            slot.Proc.Kill(true);
                        }
                    }
                    catch { }

                    if (_pidRedisManager != null)
                    {
                        await _pidRedisManager.UntrackPidAsync(slot.Proc.Id);
                    }
                }
            }
            healthyExisting.RemoveRange(healthyExisting.Count - toKillCount, toKillCount);
        }

        var toStart = Math.Max(0, count - healthyExisting.Count);
        _chunkedLogger?.LogInformation(null,
            "[pool] WarmLabelAsync reconcile for label={label}: target={count}, healthy_existing={healthy}, to_start={toStart}",
            labelKey, count, healthyExisting.Count, toStart);

        // Repopulate Redis with existing healthy browsers (since InitializeAsync called CleanupLabelListsAsync)
        var (provIsContainer, provContainerId) = GetContainerInfo();
        var workerPid = Process.GetCurrentProcess().Id;
        var labelsDict = options.Labels.ToDictionary(k => k.Key, v => v.Value);

        foreach (var (id, slot) in healthyExisting)
        {
            var argsEnv = slot.BrowserType.Equals("chromium", StringComparison.OrdinalIgnoreCase)
                ? Environment.GetEnvironmentVariable("AGENIX_WORKER_CHROMIUM_ARGS") ??
                  Environment.GetEnvironmentVariable("CHROME_ARGS")
                : slot.BrowserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                    ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                    : Environment.GetEnvironmentVariable("AGENIX_WORKER_FIREFOX_ARGS");

            var item = JsonSerializer.Serialize(new
            {
                nodeId = options.NodeId,
                browserId = id,
                webSocketEndpoint = slot.PublicWs,
                browserType = slot.BrowserType,
                // browserVersion and playwrightVersion are not stored in Slot, but they are optional metadata
                args = argsEnv,
                labels = labelsDict,
                isContainer = provIsContainer,
                containerId = provContainerId,
                workerPid,
                sidecarPid = SafePid(slot.Proc)
            });

            // Decide whether to re-add to Available or InUse based on active WebSocket connections
            var isCurrentlyInUse = HasActiveConnection(id);
            var listKey = isCurrentlyInUse ? RedisKeys.InUse(labelKey) : availableKey;

            // [REDIS-DEBUG] Log existing browser re-add to Redis
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] Re-adding existing browser to Redis. Key={key}, BrowserId={browserId}, InUse={inUse}", listKey, id, isCurrentlyInUse);
            var lengthBefore = await db.ListLengthAsync(listKey);
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] List length before re-add: {length}", lengthBefore);

            await db.ListRightPushAsync(listKey, item);

            var lengthAfter = await db.ListLengthAsync(listKey);
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] List length after re-add: {length}", lengthAfter);
            var keyExists = await db.KeyExistsAsync(listKey);
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] Key exists after re-add: {exists}", keyExists);
        }

        for (var i = 0; i < toStart; i++)
        {
            _chunkedLogger?.LogInformation(null, "[pool] Starting browser {index}/{total} for label {label}", i + 1, toStart,
                labelKey);
            var res = await StartPwServerAsync(browserType);
            _chunkedLogger?.LogInformation(null, "[pool] Successfully started browser {index}/{total}, PID={pid}, WS={ws}", i + 1,
                toStart, res.proc.Id, res.ws);
            var id = Guid.NewGuid().ToString("N");

            // Track PID for orphan detection (Layer 3 - Redis)
            if (_pidRedisManager != null)
            {
                _chunkedLogger?.LogDebug(null, "[pool] About to track PID {pid} in Redis for browser {browser}", res.proc.Id, browserType);
                await _pidRedisManager.TrackPidAsync(res.proc.Id, browserType, labelKey);
                _chunkedLogger?.LogDebug(null, "[pool] Successfully tracked PID {pid} in Redis", res.proc.Id);
            }
            else
            {
                _logger.LogInformation("[pool] PidRedisManager is null, skipping PID tracking");
            }

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
                _chunkedLogger?.LogWarning(null, "[Warm] Failed to validate WS for {labelKey}; skipping slot initialization",
                    labelKey);
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
                ? Environment.GetEnvironmentVariable("AGENIX_WORKER_CHROMIUM_ARGS") ??
                  Environment.GetEnvironmentVariable("CHROME_ARGS")
                : browserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                    ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                    : Environment.GetEnvironmentVariable("AGENIX_WORKER_FIREFOX_ARGS");
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

            // [REDIS-DEBUG] Detailed logging to diagnose key persistence issue
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] About to write to Redis. Key={key}, NodeId={nodeId}, BrowserId={browserId}", availableKey, options.NodeId, id);
            var lengthBefore = await db.ListLengthAsync(availableKey);
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] List length before write: {length}", lengthBefore);

            await db.ListRightPushAsync(availableKey, item);

            var lengthAfter = await db.ListLengthAsync(availableKey);
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] List length after write: {length}", lengthAfter);

            // Verify key exists immediately after write
            var keyExistsAfterWrite = await db.KeyExistsAsync(availableKey);
            _chunkedLogger?.LogDebug(null, "[REDIS-DEBUG] Key exists after write: {exists}", keyExistsAfterWrite);

            _chunkedLogger?.LogInformation(null,
                "+ warm server nodeId={nodeId} {browserId} for {labelKey} (type={browserType}) ws={wsPublic} (container={isContainer} id={containerId})",
                options.NodeId, id, labelKey, browserType, wsPublic, isContainer, containerId ?? "?");
        }

        metrics.SetPoolCapacity(options.NodeId, labelKey, map.Count);
        metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));
        op?.Complete();
    }

    internal async Task OnSidecarExited(string labelKey, string browserId, string browserType)
    {
        using var op = _chunkedLogger?.BeginOperation("OnSidecarExited", new Dictionary<string, object>
        {
            ["BrowserId"] = browserId,
            ["LabelKey"] = labelKey,
            ["BrowserType"] = browserType
        });
        _chunkedLogger?.LogMilestone(EventCodes.Worker.SidecarExited, "[SidecarExit] Sidecar {browserId} ({labelKey}) exited", browserId, labelKey);

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

                    // Untrack PID when sidecar exits (Layer 3 - Redis)
                    if (os.Proc != null && _pidRedisManager != null)
                    {
                        await _pidRedisManager.UntrackPidAsync(os.Proc.Id);
                    }

                    // Delete recycle flag to prevent confusion with ReconcileLoop
                    // (Option C: Optimize Integration - improve coordination)
                    try
                    {
                        await db.KeyDeleteAsync(RedisKeys.Recycle(browserId));
                    }
                    catch
                    {
                        // Non-critical - recycle flag will expire after TTL anyway
                    }
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
                _chunkedLogger?.LogInformation(null,
                    "[SidecarExit] Skip replacement for {browserId} ({labelKey}) - already reconciled", browserId,
                    labelKey);
                op?.Complete();
                return;
            }

            // Launch replacement with backoff
            var uptime = removedSlot is not null ? DateTime.UtcNow - removedSlot.StartedAt : TimeSpan.Zero;

            // Enforce capacity constraint: check if we still need this replacement
            if (!options.PoolConfig.TryGetValue(labelKey, out var targetCount))
            {
                targetCount = 0;
            }

            if (map!.Count >= targetCount)
            {
                _chunkedLogger?.LogInformation(null,
                    "[SidecarExit] No replacement for {browserId} ({labelKey}) - target capacity {target} reached or exceeded (current={current})",
                    browserId, labelKey, targetCount, map!.Count);
                op?.Complete();
                return;
            }

            var boState = await RegisterFailureAsync(labelKey, uptime);
            if (boState.NextDelay > TimeSpan.Zero)
            {
                _chunkedLogger?.LogWarning(null,
                    "[SidecarExit] Backing off {seconds}s before respawn for {labelKey} (failures={failures})",
                    boState.NextDelay.TotalSeconds.ToString("F0"), labelKey, boState.Failures);
                try { await Task.Delay(boState.NextDelay); }
                catch { }
            }

            var res = await StartPwServerAsync(browserType);
            var newId = Guid.NewGuid().ToString("N");

            // Track PID for orphan detection (Layer 3 - Redis)
            if (_pidRedisManager != null)
            {
                await _pidRedisManager.TrackPidAsync(res.proc.Id, browserType, labelKey);
            }

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
                _chunkedLogger?.LogWarning(null,
                    "[SidecarExit] Replacement failed WS validation for {labelKey}; capacity temporarily reduced",
                    labelKey);
                op?.Complete();
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
                ? Environment.GetEnvironmentVariable("AGENIX_WORKER_CHROMIUM_ARGS") ??
                  Environment.GetEnvironmentVariable("CHROME_ARGS")
                : browserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                    ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                    : Environment.GetEnvironmentVariable("AGENIX_WORKER_FIREFOX_ARGS");
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
                workerPid = Environment.ProcessId,
                sidecarPid = SafePid(res.proc)
            });
            await db.ListRightPushAsync(availableKey, item);

            metrics.SetPoolCapacity(options.NodeId, labelKey, map2.Count);
            metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));

            _chunkedLogger?.LogInformation(null,
                "nodeId={nodeId} {browserId} replaced with {newId} for {labelKey} (container={isContainer} id={containerId})",
                options.NodeId, browserId, newId, labelKey, isContainer, containerId ?? "?");
            op?.Complete();
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogWarning(ex, null, "[SidecarExit] error: {message}", ex.Message);
            op?.Fail(ex, ErrorType.Unexpected);
        }
    }

    public async Task CleanupLabelListsAsync(string labelKey)
    {
        using var op = _chunkedLogger?.BeginOperation("CleanupLabelLists", new Dictionary<string, object>
        {
            ["LabelKey"] = labelKey
        });

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

                        _chunkedLogger?.LogInformation(null,
                            "[Startup-Cleanup] node={nodeId} label={labelKey} list={listName} browserId={browserId} removed stale item (reason={reason})",
                            options.NodeId, labelKey, listName, browserId, reason);
                    }
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger?.LogWarning(ex, null,
                    "[Startup-Cleanup] node={nodeId} label={labelKey} error cleaning key={key}: {message}",
                    options.NodeId, labelKey, key, ex.Message);
            }
        }
        op?.Complete();
    }

    public async Task ReconcileLoopAsync(CancellationToken ct)
    {
        // Configurable interval (Phase 1: Optional Improvements)
        // Default 5s (reduced from original 15s for faster response to recycle flags)
        var normalInterval = TimeSpan.FromSeconds(options.ReconcileLoopIntervalSeconds);

        // Adaptive polling (Phase 3: Optional Improvements)
        // Speed up to 1s when activity detected, slow down to normal when idle
        var fastInterval = TimeSpan.FromSeconds(1);
        var currentInterval = normalInterval;
        var consecutiveIdleIterations = 0;
        const int hysteresisThreshold = 3; // Require 3 idle iterations before slowing down

        while (!ct.IsCancellationRequested)
        {
            var activityDetected = false;

            try
            {
                foreach (var labelEntry in Pools)
                {
                    var labelKey = labelEntry.Key;
                    var map = labelEntry.Value;
                    var availableKey = RedisKeys.Available(labelKey);
                    var inuseKey = RedisKeys.InUse(labelKey);

                    // Dynamic capacity enforcement: prune extras if target count was reduced
                    if (options.PoolConfig.TryGetValue(labelKey, out var targetCount))
                    {
                        if (map.Count > targetCount)
                        {
                            var oldCapacity = map.Count;
                            _chunkedLogger?.LogMilestone(EventCodes.Worker.PoolResizeStarted,
                                "[Reconcile] Target capacity for {labelKey} was reduced from {old} to {new}. Pruning extra browsers...",
                                labelKey, oldCapacity, targetCount);

                            var toKill = map.Count - targetCount;
                            // Find healthy slots that are not in use to kill
                            var candidates = map.Where(kv => !HasActiveConnection(kv.Key))
                                .Take(toKill)
                                .ToList();

                            foreach (var kv in candidates)
                            {
                                if (map.TryRemove(kv.Key, out var slot))
                                {
                                    _chunkedLogger?.LogMilestone(EventCodes.Worker.BrowserPruned,
                                        "[Reconcile] Pruning extra browser {browserId} for {labelKey} (target={target})",
                                        kv.Key, labelKey, targetCount);

                                    try
                                    {
                                        if (!slot.Proc.HasExited)
                                        {
                                            slot.Proc.Kill(true);
                                        }
                                    }
                                    catch { }

                                    if (_pidRedisManager != null)
                                    {
                                        await _pidRedisManager.UntrackPidAsync(slot.Proc.Id);
                                    }

                                    // Prune from Redis
                                    foreach (var lk in new[] { availableKey, inuseKey })
                                    {
                                        var entries = await db.ListRangeAsync(lk);
                                        var pattern = $"\"browserId\":\"{kv.Key}\"";
                                        foreach (var e in entries)
                                        {
                                            if (e.ToString().Contains(pattern, StringComparison.Ordinal))
                                            {
                                                await db.ListRemoveAsync(lk, e);
                                            }
                                        }
                                    }
                                }
                            }

                            _chunkedLogger?.LogMilestone(EventCodes.Worker.PoolResizeCompleted,
                                "[Reconcile] Finished pruning extra browsers for {labelKey}. Current capacity: {current}",
                                labelKey, map.Count);
                            LogPoolState();
                        }
                    }

                    foreach (var kv in map.ToArray())
                    {
                        var browserId = kv.Key;
                        var slot = kv.Value;

                        var recycleKey = RedisKeys.Recycle(browserId);
                        var replaceRequested = await db.KeyExistsAsync(recycleKey);
                        var needsReplace = slot.Proc.HasExited || replaceRequested;
                        if (!needsReplace)
                        {
                            continue;
                        }

                        // Activity detected - browser needs replacement (Phase 3: Adaptive polling)
                        activityDetected = true;

                        // Defer recycling if a client is currently connected to this browserId
                        var hasActive = HasActiveConnection(browserId);
                        if (replaceRequested && hasActive)
                        {
                            _chunkedLogger?.LogInformation(null,
                                "[Reconcile] Defer recycle for {browserId} ({labelKey}) - active WS connection",
                                browserId, labelKey);
                            continue;
                        }

                        // Enforce capacity constraint before replacement
                        if (!options.PoolConfig.TryGetValue(labelKey, out var currentTarget))
                        {
                            currentTarget = 0;
                        }

                        if (map.Count > currentTarget)
                        {
                            _chunkedLogger?.LogInformation(null,
                                "[Reconcile] No replacement for {browserId} ({labelKey}) - target capacity {target} reached or exceeded (current={current})",
                                browserId, labelKey, currentTarget, map.Count);

                            if (map.TryRemove(browserId, out var removedSlot))
                            {
                                try
                                {
                                    if (!removedSlot.Proc.HasExited)
                                    {
                                        removedSlot.Proc.Kill(true);
                                    }
                                }
                                catch { }

                                if (_pidRedisManager != null)
                                {
                                    await _pidRedisManager.UntrackPidAsync(removedSlot.Proc.Id);
                                }

                                // Delete recycle flag
                                try { await db.KeyDeleteAsync(recycleKey); } catch { }

                                // Prune from Redis
                                foreach (var lk in new[] { availableKey, inuseKey })
                                {
                                    var entries = await db.ListRangeAsync(lk);
                                    var pattern = $"\"browserId\":\"{browserId}\"";
                                    foreach (var e in entries)
                                    {
                                        if (e.ToString().Contains(pattern, StringComparison.Ordinal))
                                        {
                                            await db.ListRemoveAsync(lk, e);
                                        }
                                    }
                                }
                            }

                            continue;
                        }

                        using var op = _chunkedLogger?.BeginOperation("ReconcileReplace", new Dictionary<string, object>
                        {
                            ["BrowserId"] = browserId,
                            ["LabelKey"] = labelKey,
                            ["Reason"] = slot.Proc.HasExited ? "ProcessExited" : "RecycleRequested"
                        });

                        try
                        {
                            // Record recycle latency metric if triggered by health check
                            // (Option C: Optimize Integration - latency monitoring)
                            if (replaceRequested)
                            {
                                try
                                {
                                    var flagValue = await db.StringGetAsync(recycleKey);
                                    if (!flagValue.IsNullOrEmpty && long.TryParse(flagValue, out var setTimestamp))
                                    {
                                        var nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                        var latencySeconds = nowTimestamp - setTimestamp;
                                        metrics.RecordBrowserRecycleLatency(options.NodeId, labelKey, latencySeconds);
                                    }

                                    await db.KeyDeleteAsync(recycleKey);
                                }
                                catch { }
                            }

                            _chunkedLogger?.LogMilestone(EventCodes.Worker.ReconcileStarted, "[Reconcile] nodeId={nodeId} Sidecar {browserId} for {labelKey} {reason} - replacing",
                                options.NodeId, browserId, labelKey, slot.Proc.HasExited ? "exited" : "recycle requested");
                            var removed = map.TryRemove(browserId, out _);
                            if (!removed)
                            {
                                _chunkedLogger?.LogInformation(null,
                                    "[Reconcile] nodeId={nodeId} Skip replacement for {browserId} ({labelKey}) - already handled by exit handler",
                                    options.NodeId, browserId, labelKey);
                                op?.Complete();
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
                                _chunkedLogger?.LogWarning(null,
                                    "[Reconcile] Backing off {seconds}s before respawn for {labelKey} (failures={failures})",
                                    boState.NextDelay.TotalSeconds.ToString("F0"), labelKey, boState.Failures);
                                try { await Task.Delay(boState.NextDelay, ct); }
                                catch { }
                            }

                            var res = await StartPwServerAsync(slot.BrowserType);
                            var newId = Guid.NewGuid().ToString("N");

                            // Track PID for orphan detection (Layer 3 - Redis)
                            if (_pidRedisManager != null)
                            {
                                await _pidRedisManager.TrackPidAsync(res.proc.Id, slot.BrowserType, labelKey);
                            }

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
                                ? Environment.GetEnvironmentVariable("AGENIX_WORKER_CHROMIUM_ARGS") ??
                                  Environment.GetEnvironmentVariable("CHROME_ARGS")
                                : slot.BrowserType.Equals("webkit", StringComparison.OrdinalIgnoreCase)
                                    ? Environment.GetEnvironmentVariable("WEBKIT_ARGS")
                                    : Environment.GetEnvironmentVariable("AGENIX_WORKER_FIREFOX_ARGS");

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

                            _chunkedLogger?.LogMilestone(EventCodes.Worker.ReconcileCompleted, "[Reconcile] nodeId={nodeId} Replaced {browserId} with {newId} for {labelKey}",
                                options.NodeId, browserId, newId, labelKey);
                            op?.Complete();
                        }
                        catch (Exception ex)
                        {
                            _chunkedLogger?.LogWarning(ex, null, "[ReconcileReplace] error for {browserId}: {message}", browserId, ex.Message);
                            op?.Fail(ex, ErrorType.Unexpected);
                        }
                    }

                    metrics.SetPoolCapacity(options.NodeId, labelKey, map.Count);
                    metrics.SetPoolAvailable(options.NodeId, labelKey, await db.ListLengthAsync(availableKey));
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger?.LogWarning(ex, null, "[Reconcile] error: {message}", ex.Message);
            }

            // Adaptive polling logic (Phase 3: Optional Improvements)
            // Speed up when activity detected, slow down with hysteresis when idle
            if (activityDetected)
            {
                // Activity detected - switch to fast polling immediately
                currentInterval = fastInterval;
                consecutiveIdleIterations = 0;

                _chunkedLogger?.LogDebug(null, "[Reconcile] Activity detected - using fast interval ({FastMs}ms)",
                    fastInterval.TotalMilliseconds);
            }
            else
            {
                // No activity - increment idle counter
                consecutiveIdleIterations++;

                // Slow down only after consecutive idle iterations (hysteresis prevents thrashing)
                if (consecutiveIdleIterations >= hysteresisThreshold && currentInterval != normalInterval)
                {
                    currentInterval = normalInterval;
                    _chunkedLogger?.LogDebug(null, "[Reconcile] {IdleCount} idle iterations - switching to normal interval ({NormalMs}ms)",
                        consecutiveIdleIterations, normalInterval.TotalMilliseconds);
                }
            }

            try { await Task.Delay(currentInterval, ct); }
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
        using var op = _chunkedLogger?.BeginOperation("CleanupAll");
        try
        {
            // Remove this worker from Redis lists and node metadata
            foreach (var label in options.PoolConfig.Keys)
            {
                try { await CleanupLabelListsAsync(label); }
                catch (Exception ex)
                {
                    _chunkedLogger?.LogWarning(ex, null, "[Shutdown] error cleaning label {label}: {message}", label, ex.Message);
                }
            }

            try { await db.SetRemoveAsync("nodes", options.NodeId); }
            catch { }

            try { await db.KeyDeleteAsync(RedisKeys.Node(options.NodeId)); }
            catch { }

            try { await db.KeyDeleteAsync(RedisKeys.NodeAlive(options.NodeId)); }
            catch { }
            op?.Complete();
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogWarning(ex, null, "[Shutdown] cleanup error: {message}", ex.Message);
            op?.Fail(ex, ErrorType.Unexpected);
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

    /// <summary>
    /// Detects and kills orphaned Node.js sidecar processes from previous worker runs.
    /// Uses Redis PID tracking if available (Layer 3), falls back to ps scanning (Layer 2).
    /// </summary>
    public async Task DetectAndCleanOrphanedProcessesAsync()
    {
        using var op = _chunkedLogger?.BeginOperation("DetectAndCleanOrphans");
        _chunkedLogger?.LogMilestone(EventCodes.Worker.OrphanCleanupStarted, "[Startup] Checking for orphaned sidecar processes...");

        try
        {
            // Layer 3: Use Redis for centralized, multi-hub aware detection
            if (_pidRedisManager != null)
            {
                var pidsFromRedis = await _pidRedisManager.InitializeAsync();

                if (pidsFromRedis.Count == 0)
                {
                    _chunkedLogger?.LogInformation(null, "[Startup] No PIDs found in Redis for this worker");
                    op?.Complete();
                    return;
                }

                var killedCount = await _pidRedisManager.DetectAndKillOrphansAsync(pidsFromRedis);

                if (killedCount > 0)
                {
                    _chunkedLogger?.LogMilestone(EventCodes.Worker.OrphanCleanupCompleted, "[Startup] [Redis] Killed {Count} orphaned sidecar processes", killedCount);
                }
                else
                {
                    _chunkedLogger?.LogInformation(null, "[Startup] [Redis] No orphaned processes needed cleanup");
                }

                op?.Complete();
                return;
            }

            // Layer 2: Fallback to ps scanning if PidRedisManager not available
            _chunkedLogger?.LogInformation(null, "[Startup] PidRedisManager not available, using ps scan fallback");

            // Get all running node processes matching our sidecar script pattern
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"ps aux | grep 'node launch_playwright_server' | grep -v grep\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                _chunkedLogger?.LogWarning(null, "[Startup] Failed to start process scanner");
                op?.Complete();
                return;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (string.IsNullOrWhiteSpace(output))
            {
                _chunkedLogger?.LogInformation(null, "[Startup] No orphaned sidecar processes found");
                op?.Complete();
                return;
            }

            // Parse PIDs from ps output (format: user PID %cpu %mem ...)
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var psKilledCount = 0;

            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (int.TryParse(parts[1], out var pid))
                {
                    try
                    {
                        var orphan = System.Diagnostics.Process.GetProcessById(pid);
                        orphan.Kill(true); // Kill process tree
                        _chunkedLogger?.LogWarning(null, "[Startup] Killed orphaned sidecar process PID={Pid}", pid);
                        psKilledCount++;
                    }
                    catch (ArgumentException)
                    {
                        // Process already exited
                    }
                    catch (Exception ex)
                    {
                        _chunkedLogger?.LogWarning(ex, null, "[Startup] Failed to kill orphaned process PID={Pid}", pid);
                    }
                }
            }

            if (psKilledCount > 0)
            {
                _chunkedLogger?.LogMilestone(EventCodes.Worker.OrphanCleanupCompleted, "[Startup] Killed {Count} orphaned sidecar processes", psKilledCount);
            }
            else
            {
                _chunkedLogger?.LogInformation(null, "[Startup] No orphaned processes needed cleanup");
            }
            op?.Complete();
        }
        catch (Exception ex)
        {
            _chunkedLogger?.LogWarning(ex, null, "[Startup] Orphan cleanup error: {message}", ex.Message);
            op?.Fail(ex, ErrorType.Unexpected);
        }
    }

    private sealed record BackoffState(int Failures, DateTime LastFailureUtc, TimeSpan NextDelay);
}
