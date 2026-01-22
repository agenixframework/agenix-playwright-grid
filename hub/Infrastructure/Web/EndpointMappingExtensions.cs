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

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.Audit;
using PlaywrightHub.Infrastructure.Adapters.SignalR;
using Prometheus;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

internal static class EndpointCapacityQueue
{
    private const string ConsumerGroup = "borrow-cg";
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Queue<Waiter>> Queues = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> PendingPerRun = new(StringComparer.Ordinal);

    // Fair sharing round-robin state over labels that currently have waiters
    private static readonly List<string> RoundRobinLabels = new();
    private static int _rrIndex = -1;

    // Limits for queued waiters and per-run
    private static int _perLabelCap = 100;
    private static int _perRunCap = 5;

    // Concurrency caps (active grants) per requested label and current in-flight counters
    private static int _defaultConcurrencyCap; // 0 = unlimited
    private static readonly Dictionary<string, int> ConcurrencyCaps = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> InflightPerLabel = new(StringComparer.Ordinal);

    // Redis Streams backend (optional for horizontal scaling)
    private static bool _useRedisStreams;
    private static IDatabase? _redisDb;
    private static string _instanceId = Environment.MachineName + ":" + Environment.ProcessId;
    private static int _notifyTtlSeconds = 120;

    public static void ConfigureRedis(IDatabase db, bool enabled, string? instanceId = null,
        int? notifyTtlSeconds = null)
    {
        lock (Sync)
        {
            _redisDb = db;
            _useRedisStreams = enabled;
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                _instanceId = instanceId;
            }

            if (notifyTtlSeconds is { } s and > 0)
            {
                _notifyTtlSeconds = s;
            }
        }
    }

    public static void Configure(int perLabelCap, int perRunCap)
    {
        lock (Sync)
        {
            _perLabelCap = perLabelCap > 0 ? perLabelCap : 100;
            _perRunCap = perRunCap > 0 ? perRunCap : 5;
        }
    }

    public static void Configure(int perLabelCap, int perRunCap, int defaultConcurrencyCap,
        Dictionary<string, int>? concurrencyCaps)
    {
        lock (Sync)
        {
            _perLabelCap = perLabelCap > 0 ? perLabelCap : 100;
            _perRunCap = perRunCap > 0 ? perRunCap : 5;
            _defaultConcurrencyCap = defaultConcurrencyCap < 0 ? 0 : defaultConcurrencyCap;
            ConcurrencyCaps.Clear();
            if (concurrencyCaps is not null)
            {
                foreach (var kv in concurrencyCaps)
                {
                    if (kv.Value > 0 && !string.IsNullOrWhiteSpace(kv.Key))
                    {
                        ConcurrencyCaps[kv.Key.Trim()] = kv.Value;
                    }
                }
            }
        }
    }

    // Test helper: reset static state for deterministic tests
    internal static void Reset()
    {
        lock (Sync)
        {
            Queues.Clear();
            PendingPerRun.Clear();
            RoundRobinLabels.Clear();
            _rrIndex = -1;
            InflightPerLabel.Clear();
            _perLabelCap = 100;
            _perRunCap = 5;
            _defaultConcurrencyCap = 0;
            ConcurrencyCaps.Clear();
        }
    }

    public static (object? token, string? reason) TryEnqueue(string label, string runId)
    {
        lock (Sync)
        {
            // Always maintain local structures to keep metrics/fairness working even when Redis is enabled
            if (!Queues.TryGetValue(label, out var q))
            {
                q = new Queue<Waiter>();
                Queues[label] = q;
            }

            if (!RoundRobinLabels.Contains(label))
            {
                RoundRobinLabels.Add(label);
                if (_rrIndex >= RoundRobinLabels.Count)
                {
                    _rrIndex = RoundRobinLabels.Count - 1;
                }
            }

            if (q.Count >= _perLabelCap)
            {
                return (null, "per-label-cap");
            }

            var pending = PendingPerRun.TryGetValue(runId, out var pr) ? pr : 0;
            if (pending >= _perRunCap)
            {
                return (null, "per-run-cap");
            }

            if (_useRedisStreams && _redisDb is not null)
            {
                // Enqueue a distributed waiter via Redis Streams and return a RedisWaiter token
                var requestId = Guid.NewGuid().ToString("N");
                try
                {
                    // Ensure consumer group exists for this label stream
                    _redisDb.StreamCreateConsumerGroup(RedisKeys.BorrowStream(label), ConsumerGroup, "0-0");
                }
                catch
                {
                    /* BUSYGROUP or create errors are non-fatal */
                }

                try
                {
                    _redisDb.StreamAdd(
                        RedisKeys.BorrowStream(label),
                        [
                            new NameValueEntry("requestId", requestId),
                            new NameValueEntry("runId", runId),
                            new NameValueEntry("ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
                        ]);
                }
                catch
                {
                    // ignored
                }

                PendingPerRun[runId] = pending + 1;
                // Also add a local waiter to keep RR fairness working if this instance has capacity to signal
                var local = new Waiter { Label = label, RunId = runId };
                q.Enqueue(local);
                return (new RedisWaiter { Label = label, RunId = runId, RequestId = requestId }, null);
            }

            var w = new Waiter { Label = label, RunId = runId };
            q.Enqueue(w);
            PendingPerRun[runId] = pending + 1;
            return (w, null);
        }
    }

    public static void Remove(object token)
    {
        if (token is Waiter w)
        {
            lock (Sync)
            {
                w.Canceled = true;
                if (PendingPerRun.TryGetValue(w.RunId, out var pr))
                {
                    pr = Math.Max(0, pr - 1);
                    if (pr == 0)
                    {
                        PendingPerRun.Remove(w.RunId);
                    }
                    else
                    {
                        PendingPerRun[w.RunId] = pr;
                    }
                }
            }

            return;
        }

        if (token is RedisWaiter rw)
        {
            lock (Sync)
            {
                rw.Canceled = true;
                if (PendingPerRun.TryGetValue(rw.RunId, out var pr))
                {
                    pr = Math.Max(0, pr - 1);
                    if (pr == 0)
                    {
                        PendingPerRun.Remove(rw.RunId);
                    }
                    else
                    {
                        PendingPerRun[rw.RunId] = pr;
                    }
                }
            }

            try
            {
                if (_redisDb is not null)
                {
                    var notifyKey = RedisKeys.BorrowNotify(rw.RequestId);
                    _redisDb.KeyExpire(notifyKey, TimeSpan.FromSeconds(_notifyTtlSeconds));
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    public static void Signal(string label)
    {
        try
        {
            if (_useRedisStreams && _redisDb is not null) { _ = Task.Run(() => StreamGrantAsync(label)); }
        }
        catch { }

        lock (Sync)
        {
            // Fair sharing: round-robin across labels with pending waiters and under their concurrency caps.
            if (RoundRobinLabels.Count == 0)
            {
                return;
            }

            var count = RoundRobinLabels.Count;
            // Start from next index to avoid bias to the same label
            var start = (_rrIndex + 1 + count) % count;
            for (var i = 0; i < count; i++)
            {
                var idx = (start + i) % count;
                var lbl = RoundRobinLabels[idx];
                if (ReachedCap(lbl))
                {
                    continue;
                }

                if (!Queues.TryGetValue(lbl, out var q) || q.Count == 0)
                {
                    continue;
                }

                // Find first non-canceled waiter for this label
                while (q.Count > 0)
                {
                    var w = q.Dequeue();
                    if (w.Canceled)
                    {
                        continue;
                    }

                    // Decrement pending-per-run when granting
                    if (PendingPerRun.TryGetValue(w.RunId, out var pr))
                    {
                        pr = Math.Max(0, pr - 1);
                        if (pr == 0)
                        {
                            PendingPerRun.Remove(w.RunId);
                        }
                        else
                        {
                            PendingPerRun[w.RunId] = pr;
                        }
                    }

                    _rrIndex = idx; // move cursor to the label we just served
                    _ = w.Tcs.TrySetResult(true);
                    return;
                }
            }
        }
    }

    public static int GetQueueLength(string label)
    {
        lock (Sync)
        {
            return Queues.TryGetValue(label, out var q) ? q.Count : 0;
        }
    }

    private static int GetCapFor(string label)
    {
        if (ConcurrencyCaps.TryGetValue(label, out var cap))
        {
            return cap;
        }

        return _defaultConcurrencyCap;
    }

    public static bool ReachedCap(string label)
    {
        lock (Sync)
        {
            var cap = GetCapFor(label);
            if (cap <= 0)
            {
                return false; // unlimited
            }

            var inflight = InflightPerLabel.TryGetValue(label, out var x) ? x : 0;
            return inflight >= cap;
        }
    }

    public static void OnStarted(string label)
    {
        lock (Sync)
        {
            var current = InflightPerLabel.TryGetValue(label, out var x) ? x : 0;
            InflightPerLabel[label] = current + 1;
        }
    }

    public static void OnFinished(string label)
    {
        lock (Sync)
        {
            if (InflightPerLabel.TryGetValue(label, out var x))
            {
                x = Math.Max(0, x - 1);
                if (x == 0)
                {
                    InflightPerLabel.Remove(label);
                }
                else
                {
                    InflightPerLabel[label] = x;
                }
            }
        }
    }

    private static async Task StreamGrantAsync(string label)
    {
        try
        {
            var db = _redisDb;
            if (db is null)
            {
                return;
            }

            var key = RedisKeys.BorrowStream(label);
            try { await db.StreamCreateConsumerGroupAsync(key, ConsumerGroup, "0-0"); }
            catch { }

            StreamEntry[] entries;
            try
            {
                entries = await db.StreamReadGroupAsync(key, ConsumerGroup, _instanceId, ">", 1);
            }
            catch { return; }

            if (entries is { Length: > 0 })
            {
                var e = entries[0];
                var requestId = string.Empty;
                foreach (var nv in e.Values)
                {
                    if (nv.Name == "requestId")
                    {
                        requestId = nv.Value;
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    var notifyKey = RedisKeys.BorrowNotify(requestId);
                    try
                    {
                        await db.ListRightPushAsync(notifyKey, "1");
                        await db.KeyExpireAsync(notifyKey, TimeSpan.FromSeconds(_notifyTtlSeconds));
                    }
                    catch
                    {
                        // ignored
                    }
                }

                try { await db.StreamAcknowledgeAsync(key, ConsumerGroup, e.Id); }
                catch { }
            }
        }
        catch
        {
            // ignored
        }
    }

    public static async Task<bool> WaitAsync(object token, TimeSpan timeout, CancellationToken ct = default)
    {
        if (token is RedisWaiter rw && _useRedisStreams && _redisDb is not null)
        {
            var notifyKey = RedisKeys.BorrowNotify(rw.RequestId);
            var stopAt = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < stopAt && !ct.IsCancellationRequested)
            {
                try
                {
                    var v = await _redisDb.ListLeftPopAsync(notifyKey);
                    if (!v.IsNull)
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignored
                }

                try { await Task.Delay(100, ct); }
                catch
                {
                    // ignored
                }
            }

            return false;
        }

        if (token is not Waiter w)
        {
            return false;
        }

        var delayTask = Task.Delay(timeout, ct);
        var completed = await Task.WhenAny(w.Tcs.Task, delayTask).ConfigureAwait(false);
        if (completed == w.Tcs.Task)
        {
            return w.Tcs.Task.IsCompletedSuccessfully && w.Tcs.Task.Result;
        }

        return false;
    }

    public static int GetInflightCount(string label)
    {
        lock (Sync)
        {
            return InflightPerLabel.GetValueOrDefault(label, 0);
        }
    }

    private sealed class RedisWaiter
    {
        public volatile bool Canceled;
        public required string Label { get; init; }
        public required string RunId { get; init; }
        public required string RequestId { get; init; }
    }

    private sealed class Waiter
    {
        public volatile bool Canceled;
        public required string Label { get; init; }
        public required string RunId { get; init; }
        public TaskCompletionSource<bool> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public static class EndpointMappingExtensions
{
    private static long _redisBreakerUntilTicks;
    private static int _redisConsecutiveFailures;
    private static volatile bool _acceptingBorrows = true;

    public static void MapHubEndpoints(this WebApplication app)
    {
        var config = app.Configuration;
        var services = app.Services;

        // API versioning alias: allow /api/v1/... to route to existing endpoints without breaking existing clients.
        // This reserves the /api/vX space for future breaking changes while keeping current mappings intact.
        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1", out var remainder))
            {
                context.Request.Path = remainder;
            }

            return next();
        });

        var db = services.GetRequiredService<IDatabase>();
        var mux = services.GetRequiredService<IConnectionMultiplexer>();
        var resultsStore = services.GetRequiredService<IResultsStore>();
        var resultsHubCtx = services.GetRequiredService<IHubContext<ResultsHub, IResultsClient>>();
        var auditStore = services.GetRequiredService<IAuditStore>();
        var auditQueryStore = services.GetRequiredService<PostgresAuditStore>(); // For read operations
        var logger = app.Logger;
        var chunkedLogger = new ChunkedLogger(logger, "Hub");

        // Admin: Projects & Users endpoints (minimal CRUD)
        AdminProjectsUsersEndpoints.MapProjectsUsersAdminEndpoints(app);

        // Launches endpoints
        app.MapLaunchesEndpoints();

        // Test items endpoints (hierarchy)
        app.MapTestItemsEndpoints();

        // Log items endpoints (ReportPortal-style)
        app.MapLogItemsEndpoints();

        // Enhanced log items endpoints with caching (Phase 5 enhancements)
        app.MapEnhancedLogItemsEndpoints();

        // Artifacts endpoints (download artifacts)
        app.MapArtifactsEndpoints();
        app.MapArtifactCacheStatsEndpoints();

        // Suites endpoints
        var suitesGroup = app.MapGroup("/api/suites").WithTags("Suites");
        suitesGroup.MapSuitesEndpoints();

        // Launch filters endpoints
        app.MapLaunchFiltersEndpoints();

        // Project settings endpoints
        app.MapProjectSettingsEndpoints();

        // Password reset endpoints
        app.MapPasswordResetEndpoints();

        // Test error endpoints for integration tests
        app.MapTestErrorEndpoints();

        // Graceful shutdown: stop accepting new borrow requests when application is stopping
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            _acceptingBorrows = false;
            try { logger.LogInformation("[hub] ApplicationStopping: stop accepting new borrows"); }
            catch { }
        });

        var hubNodeSecret = config["AGENIX_HUB_NODE_SECRET"] ?? "node-secret";
        var nodeTimeoutSeconds = int.TryParse(config["AGENIX_HUB_NODE_TIMEOUT"], out var t) ? t : 60;
        var nodeQuarantineSeconds = int.TryParse(config["AGENIX_HUB_NODE_QUARANTINE_SECONDS"], out var q) ? q : 120;
        var dashboardUrl = config["AGENIX_DASHBOARD_PUBLIC_URL"] ?? "http://localhost:3001";

        // Audit secrets on startup (fingerprint only; never log raw secrets)
        Task.Run(async () =>
        {
            try
            {
                static string Fingerprint(string raw)
                {
                    using var sha = SHA256.Create();
                    var bytes = Encoding.UTF8.GetBytes(raw ?? string.Empty);
                    return Convert.ToHexString(sha.ComputeHash(bytes));
                }

                static string NormalizeSecrets(string value)
                {
                    var parts = (value ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return string.Join(',', parts);
                }

                var nodeNorm = NormalizeSecrets(hubNodeSecret);
                var nodeFp = Fingerprint(nodeNorm);
                var nodeCount = string.IsNullOrEmpty(nodeNorm) ? 0 : nodeNorm.Split(',').Length;

                var prevNode = await db.StringGetAsync(RedisKeys.AuditSecretsNodeFingerprint());

                if (prevNode.IsNullOrEmpty)
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "secrets",
                        Action = "node.loaded",
                        Details = new Dictionary<string, string>
                        {
                            ["count"] = nodeCount.ToString(),
                            ["fingerprint"] = nodeFp[..Math.Min(8, nodeFp.Length)]
                        }
                    });
                }
                else if (!string.Equals(prevNode.ToString(), nodeFp, StringComparison.Ordinal))
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "secrets",
                        Action = "node.changed",
                        Severity = "Warning",
                        Details = new Dictionary<string, string>
                        {
                            ["count"] = nodeCount.ToString(),
                            ["fingerprint"] = nodeFp.Substring(0, Math.Min(8, nodeFp.Length))
                        }
                    });
                }

                try { await db.StringSetAsync(RedisKeys.AuditSecretsNodeFingerprint(), nodeFp); }
                catch { }

                if (nodeCount > 1)
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "secrets",
                        Action = "node.rotation.enabled",
                        Details = new Dictionary<string, string> { ["count"] = nodeCount.ToString() }
                    });
                }
            }
            catch
            {
                // ignore audit failures on startup
            }
        });

        // Borrow matching configuration
        // Support per-environment overrides via suffix: e.g., HUB_BORROW_WILDCARDS_Development
        static bool GetBoolWithEnvironmentOverride(IConfiguration cfg, string key, string environment,
            bool defaultValue)
        {
            string? value = null;
            if (!string.IsNullOrWhiteSpace(environment))
            {
                var suffixExact = $"_{environment}";
                var suffixUpper = $"_{environment.ToUpperInvariant()}";
                var k1 = key + suffixExact;
                var k2 = key + suffixUpper;
                var v1 = cfg[k1];
                var v2 = cfg[k2];
                if (!string.IsNullOrWhiteSpace(v1))
                {
                    value = v1;
                }
                else if (!string.IsNullOrWhiteSpace(v2))
                {
                    value = v2;
                }
            }

            value ??= cfg[key];
            return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
        }

        var environmentName = app.Environment.EnvironmentName ?? string.Empty;
        var enableTrailingFallback =
            GetBoolWithEnvironmentOverride(config, "AGENIX_HUB_BORROW_TRAILING_FALLBACK", environmentName,
                true); // default true
        var enablePrefixExpand =
            GetBoolWithEnvironmentOverride(config, "AGENIX_HUB_BORROW_PREFIX_EXPAND", environmentName,
                true); // default true
        var enableWildcards =
            GetBoolWithEnvironmentOverride(config, "AGENIX_HUB_BORROW_WILDCARDS", environmentName,
                false); // default false

        // Borrow TTL configuration (seconds)
        var defaultBorrowTtlSeconds =
            int.TryParse(config["AGENIX_HUB_BORROW_TTL_SECONDS"], out var bttl) ? Math.Max(60, bttl) : 900;

        // New: idle timeout (seconds) and max session TTL (seconds)
        var idleTimeoutSeconds = int.TryParse(config["AGENIX_HUB_IDLE_TIMEOUT_SECONDS"], out var idle)
            ? Math.Max(10, idle)
            : 120; // default 2 minutes
        var maxSessionTtlSeconds = int.TryParse(config["AGENIX_HUB_MAX_SESSION_TTL_SECONDS"], out var maxTtl)
            ? Math.Max(60, maxTtl)
            : 24 * 60 * 60; // default 24h

        // Capacity queue configuration
        var queueTimeoutSeconds = int.TryParse(config["AGENIX_HUB_BORROW_QUEUE_TIMEOUT_SECONDS"], out var qto)
            ? Math.Max(1, qto)
            : 30;
        var queuePerLabelCap = int.TryParse(config["AGENIX_HUB_BORROW_QUEUE_MAX_PER_LABEL"], out var qpl)
            ? Math.Max(1, qpl)
            : 100;
        var queuePerRunCap = int.TryParse(config["AGENIX_HUB_BORROW_QUEUE_MAX_PER_RUN"], out var qpr)
            ? Math.Max(1, qpr)
            : 5;

        // Concurrency caps configuration (per requested label)
        var defaultConcurrencyCap =
            int.TryParse(config["AGENIX_HUB_BORROW_CONCURRENCY_DEFAULT"], out var dcc)
                ? Math.Max(0, dcc)
                : 0; // 0 = unlimited
        var capsDict = new Dictionary<string, int>(StringComparer.Ordinal);
        var capsRaw =
            config["AGENIX_HUB_BORROW_CONCURRENCY_CAPS"] ??
            string.Empty; // e.g. "AppA:Chromium:UAT=2,AppB:Firefox:UAT=1"
        if (!string.IsNullOrWhiteSpace(capsRaw))
        {
            foreach (var part in capsRaw.Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kvp = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kvp.Length == 2 && int.TryParse(kvp[1], out var cap) && cap > 0)
                {
                    // Normalize label keys using LabelKey parser if possible
                    var key = kvp[0];
                    if (LabelKey.TryParse(key, out var lk))
                    {
                        capsDict[lk!.Normalized] = cap;
                    }
                    else
                    {
                        capsDict[key] = cap;
                    }
                }
            }
        }

        EndpointCapacityQueue.Configure(queuePerLabelCap, queuePerRunCap, defaultConcurrencyCap, capsDict);

        // Redis Streams queue backend toggle and idempotency TTL
        var queueBackend = config["AGENIX_HUB_BORROW_QUEUE_BACKEND"] ?? "memory";
        var useRedisQueue = string.Equals(queueBackend, "redis", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(queueBackend, "redis-streams", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(queueBackend, "redisstreams", StringComparison.OrdinalIgnoreCase);
        var notifyTtlSeconds = int.TryParse(config["AGENIX_HUB_BORROW_NOTIFY_TTL_SECONDS"], out var nts)
            ? Math.Max(10, nts)
            : 120;
        var instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
        EndpointCapacityQueue.ConfigureRedis(db, useRedisQueue, instanceId, notifyTtlSeconds);

        var borrowIdempTtlSeconds = int.TryParse(config["AGENIX_HUB_BORROW_IDEMP_TTL_SECONDS"], out var idtt)
            ? Math.Clamp(idtt, 60, 86400)
            : 600;

        // Metrics used by endpoints
        var borrowRequests = Prometheus.Metrics.CreateCounter(
            "hub_borrow_requests_total",
            "Total borrow requests",
            new CounterConfiguration { LabelNames = ["label"] });

        var borrowLatency = Prometheus.Metrics.CreateHistogram(
            "hub_borrow_latency_seconds",
            "Borrow latency",
            new HistogramConfiguration { LabelNames = ["label"] });

        // Borrow outcome metrics: outcome in {success, timeout, denied}
        var borrowOutcomes = Prometheus.Metrics.CreateCounter(
            "hub_borrow_outcomes_total",
            "Borrow outcomes by requested label and outcome",
            new CounterConfiguration { LabelNames = ["label", "outcome"] });

        // Pool metrics
        var poolAvailableGauge = Prometheus.Metrics.CreateGauge(
            "hub_pool_available_total",
            "Available endpoints",
            new GaugeConfiguration { LabelNames = ["label"] });

        var poolUtilizationGauge = Prometheus.Metrics.CreateGauge(
            "hub_pool_utilization_ratio",
            "Pool utilization ratio per label (borrowed/total)",
            new GaugeConfiguration { LabelNames = ["label"] });

        // Borrow queue length (no queue yet → reports 0 for now)
        var borrowQueueGauge = Prometheus.Metrics.CreateGauge(
            "hub_borrow_queue_length",
            "Borrow queue length per label (0 until queueing feature is enabled)",
            new GaugeConfiguration { LabelNames = ["label"] });

        // Simple Lua scripts operate on available:{labelKey}
        var luaFindPop = @"
local listKey = KEYS[1]
local inuseKey = KEYS[2]
local len = redis.call('LLEN', listKey)
if len == 0 then return nil end
local item = redis.call('LPOP', listKey)
if item then redis.call('RPUSH', inuseKey, item); return item end
return nil
";

        var luaReturn = @"
local inuse = KEYS[1]
local avail = KEYS[2]
local browserId = ARGV[1]
local list = redis.call('LRANGE', inuse, 0, -1)
for i,item in ipairs(list) do
  if string.find(item, browserId, 1, true) then
    redis.call('LREM', inuse, 1, item)
    redis.call('RPUSH', avail, item)
    return item
  end
end
return nil
";

        // Helper method to clean up stale browser entries when a worker re-registers
        async Task<int> CleanupWorkerBrowsersAsync(string nodeId, IDatabase db, ChunkedLogger logger)
        {
            var totalRemoved = 0;

            logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                "[CLEANUP-START] Starting cleanup for node {NodeId}", nodeId);

            try
            {
                // Get Redis server to scan for keys
                var server = db.Multiplexer.GetServer(db.Multiplexer.GetEndPoints().First());
                logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                    "[CLEANUP-SERVER] Got Redis server: {Endpoint}", server.EndPoint);

                // Scan available:* keys
                var availableCount = 0;
                await foreach (var key in server.KeysAsync(pattern: "available:*"))
                {
                    availableCount++;
                    logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                        "[CLEANUP-KEY] Scanning key: {Key}", key.ToString());
                    var removed = await RemoveWorkerEntriesFromListAsync(key, nodeId, db, logger);
                    totalRemoved += removed;
                }
                logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                    "[CLEANUP-AVAILABLE] Scanned {Count} available keys", availableCount);

                // Scan inuse:* keys
                var inuseCount = 0;
                await foreach (var key in server.KeysAsync(pattern: "inuse:*"))
                {
                    inuseCount++;
                    logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                        "[CLEANUP-KEY] Scanning key: {Key}", key.ToString());
                    var removed = await RemoveWorkerEntriesFromListAsync(key, nodeId, db, logger);
                    totalRemoved += removed;
                }
                logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                    "[CLEANUP-INUSE] Scanned {Count} inuse keys", inuseCount);

                logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                    "[CLEANUP-DONE] Total removed: {TotalRemoved}", totalRemoved);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, EventCodes.Node.RegistrationCleanup,
                    "[CLEANUP-ERROR] Failed to cleanup browsers for node {NodeId}: {Message}",
                    nodeId, ex.Message);
            }

            return totalRemoved;
        }

        async Task<int> RemoveWorkerEntriesFromListAsync(RedisKey listKey, string nodeId, IDatabase db, ChunkedLogger logger)
        {
            var items = await db.ListRangeAsync(listKey);
            var removed = 0;

            logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                "[CLEANUP-LIST] Processing list {ListKey} with {ItemCount} items for node {NodeId}",
                listKey.ToString(), items.Length, nodeId);

            foreach (var item in items)
            {
                var json = item.ToString();
                // Check if this entry belongs to the re-registering worker
                if (json.Contains($"\"nodeId\":\"{nodeId}\""))
                {
                    logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                        "[CLEANUP-MATCH] Found match in {ListKey}, removing...", listKey.ToString());

                    await db.ListRemoveAsync(listKey, item);
                    removed++;

                    // Extract browserId for logging
                    var match = System.Text.RegularExpressions.Regex.Match(json, "\"browserId\":\"(?<id>[^\"]+)\"");
                    var browserId = match.Success ? match.Groups["id"].Value : "unknown";

                    logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                        "Removed browser {BrowserId} from {ListKey} for node {NodeId}",
                        browserId, listKey.ToString(), nodeId);
                }
            }

            logger.LogDebug(EventCodes.Node.RegistrationCleanup,
                "[CLEANUP-LIST-DONE] Removed {RemovedCount} items from {ListKey}",
                removed, listKey.ToString());

            return removed;
        }

        // Node register (nodes set and node:{nodeId} hash)
        app.MapPost("/node/register", async (HttpRequest req) =>
        {
            var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var startedAt = DateTime.UtcNow;

            try
            {
                if (!CheckSecret(req, "x-hub-secret", hubNodeSecret))
                {
                    chunkedLogger.LogWarning(EventCodes.Node.RegistrationFailed, "401 Unauthorized from {RemoteIp}", remoteIp);
                    try
                    {
                        await auditStore.AppendAsync(new AuditEntryDto
                        {
                            Timestamp = DateTime.Now,
                            Category = "node",
                            Action = "register.denied",
                            RemoteIp = remoteIp,
                            Severity = "Warning",
                            Details = new Dictionary<string, string> { ["reason"] = "unauthorized" }
                        });
                    }
                    catch { }

                    return ProblemDetailsHelpers.Unauthorized(
                        "Unauthorized",
                        eventCode: EventCodes.Node.RegistrationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Dictionary<string, object?>? body;
                try
                {
                    body = await req.ReadFromJsonAsync<Dictionary<string, object?>>();
                }
                catch (JsonException jex)
                {
                    chunkedLogger.LogWarning(EventCodes.Node.RegistrationFailed, "400 Invalid JSON from {RemoteIp}: {Message}", remoteIp, jex.Message);
                    try
                    {
                        await auditStore.AppendAsync(new AuditEntryDto
                        {
                            Timestamp = DateTime.Now,
                            Category = "node",
                            Action = "register.denied",
                            RemoteIp = remoteIp,
                            Severity = "Warning",
                            Details = new Dictionary<string, string> { ["reason"] = "invalid-json" }
                        });
                    }
                    catch
                    {
                        // ignored
                    }

                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["invalid JSON"] },
                        eventCode: EventCodes.Node.RegistrationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (body is null)
                {
                    chunkedLogger.LogWarning(EventCodes.Node.RegistrationFailed, "400 Empty body from {RemoteIp}", remoteIp);
                    try
                    {
                        await auditStore.AppendAsync(new AuditEntryDto
                        {
                            Timestamp = DateTime.Now,
                            Category = "node",
                            Action = "register.denied",
                            RemoteIp = remoteIp,
                            Severity = "Warning",
                            Details = new Dictionary<string, string> { ["reason"] = "empty-body" }
                        });
                    }
                    catch { }

                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["empty body"] },
                        eventCode: EventCodes.Node.RegistrationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var nodeIdRaw = body.GetValueOrDefault("NodeId")?.ToString();
                if (string.IsNullOrWhiteSpace(nodeIdRaw) && body.TryGetValue("nodeId", out var nidTok))
                {
                    nodeIdRaw = nidTok?.ToString();
                }

                var nodeId = string.IsNullOrWhiteSpace(nodeIdRaw) ? null : nodeIdRaw.Trim();
                if (string.IsNullOrEmpty(nodeId))
                {
                    chunkedLogger.LogWarning(EventCodes.Node.RegistrationFailed, "400 Missing NodeId from {RemoteIp}", remoteIp);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["NodeId"] = ["missing NodeId"] },
                        eventCode: EventCodes.Node.RegistrationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var apps = Array.Empty<string>();
                if (body.TryGetValue("Apps", out var a) || body.TryGetValue("apps", out a))
                {
                    apps = a switch
                    {
                        JsonElement { ValueKind: JsonValueKind.Array } je => je.Deserialize<string[]>() ?? [],
                        JsonElement jeStr when jeStr.ValueKind == JsonValueKind.String =>
                            (jeStr.GetString() ?? string.Empty).Split(',',
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        string s => s.Split(',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        _ => apps
                    };
                }

                var capacity = int.TryParse(body.GetValueOrDefault("Capacity")?.ToString(), out var c) ? c : 1;
                var labels =
                    (body.GetValueOrDefault("Labels") as JsonElement?)?.Deserialize<Dictionary<string, string>>() ??
                    new Dictionary<string, string>();

                var key = RedisKeys.Node(nodeId);
                var existed = await db.KeyExistsAsync(key);
                var wasAlive = await db.KeyExistsAsync(RedisKeys.NodeAlive(nodeId));

                // Clean up stale browser entries ONLY if this is an actual re-registration
                // (node existed but was not alive = worker crashed/restarted)
                // Do NOT clean up if node is still alive (normal heartbeat/registration)
                if (existed && !wasAlive)
                {
                    var removed = await CleanupWorkerBrowsersAsync(nodeId, db, chunkedLogger);
                    if (removed > 0)
                    {
                        chunkedLogger.LogInformation(EventCodes.Node.RegistrationCleanup,
                            "Cleaned up {Count} stale browser entries for re-registering node {NodeId}",
                            removed, nodeId);
                    }
                }

                // Optional base URL where the worker exposes its HTTP endpoints
                string? baseUrl = null;
                if (body.TryGetValue("BaseUrl", out var bu) || body.TryGetValue("baseUrl", out bu))
                {
                    baseUrl = bu?.ToString();
                }

                // Optional RegionOs for browser metadata
                string? regionOs = null;
                if (body.TryGetValue("RegionOs", out var ro) || body.TryGetValue("regionOs", out ro))
                {
                    regionOs = ro?.ToString();
                }

                // Optional PlaywrightVersion for version tracking
                string? playwrightVersion = null;
                if (body.TryGetValue("PlaywrightVersion", out var pv) || body.TryGetValue("playwrightVersion", out pv))
                {
                    playwrightVersion = pv?.ToString();
                }

                var meta = new Dictionary<string, string?>
                {
                    ["Apps"] = string.Join(',', apps),
                    ["Capacity"] = Math.Max(0, capacity).ToString(),
                    ["Labels"] = JsonSerializer.Serialize(labels),
                    ["LastSeen"] = DateTime.UtcNow.ToString("o"),
                    ["BaseUrl"] = baseUrl,
                    ["RegionOs"] = regionOs,
                    ["PlaywrightVersion"] = playwrightVersion,
                    ["PlaywrightVersionExpected"] = playwrightVersion,
                    ["PlaywrightVersionMismatch"] = "0"
                };

                foreach (var kv in meta)
                {
                    await db.HashSetAsync(key, kv.Key, kv.Value);
                }

                await db.SetAddAsync("nodes", nodeId);

                // Provide an initial alive TTL to prevent immediate Sweeper expiry after registration
                var ttlSeconds = Math.Max(90, nodeTimeoutSeconds);
                await db.StringSetAsync(RedisKeys.NodeAlive(nodeId), "1", TimeSpan.FromSeconds(ttlSeconds));

                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                var sampleApps = string.Join(',', apps.Take(3));
                var appPreview = apps.Length <= 3 ? sampleApps : $"{sampleApps}(+{apps.Length - 3})";

                chunkedLogger.LogMilestone(
                    existed ? EventCodes.Node.RegistrationUpdate : EventCodes.Node.RegistrationSuccess,
                    "{Kind} nodeId={NodeId} apps={AppsCount}:{AppPreview} capacity={Capacity} labels={LabelsCount} ip={RemoteIp} in {ElapsedMs}ms",
                    existed ? "update" : "new", nodeId, apps.Length, appPreview, capacity, labels.Count, remoteIp,
                    elapsedMs);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "node",
                        Action = existed ? "register.update" : "register.success",
                        Actor = nodeId,
                        RemoteIp = remoteIp,
                        Details = new Dictionary<string, string>
                        {
                            ["apps"] = apps.Length.ToString(),
                            ["capacity"] = capacity.ToString(),
                            ["labels"] = labels.Count.ToString()
                        }
                    });
                }
                catch
                {
                    // ignored
                }

                return Results.Ok(new { registered = nodeId });
            }
            catch (Exception ex)
            {
                var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                chunkedLogger.LogError(ex, EventCodes.Node.RegistrationFailed, "500 Error from {RemoteIp} after {ElapsedMs}ms", remoteIp, elapsedMs);
                return ProblemDetailsHelpers.InternalServerError(
                    "registration failed",
                    eventCode: EventCodes.Node.RegistrationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Borrow expects { "labelKey": "App:Chromium:Staging" }
        app.MapPost("/session/borrow", async (HttpRequest req,
                [FromServices] IEventPublisher eventPublisher) =>
            {
                // During graceful shutdown, deny new borrows with 503
                if (!_acceptingBorrows)
                {
                    try { req.HttpContext.Response.Headers.Append("Retry-After", "30"); }
                    catch { }

                    try { borrowOutcomes.WithLabels("unknown", "denied").Inc(); }
                    catch { }

                    return ProblemDetailsHelpers.ServiceUnavailable(
                        dependency: "Hub",
                        safeMessage: "Not accepting new borrows during shutdown",
                        eventCode: EventCodes.BrowserPool.BorrowFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var body = await req.ReadFromJsonAsync<Dictionary<string, string>>() ??
                           new Dictionary<string, string>();
                if (!body.TryGetValue("labelKey", out var labelKey) || string.IsNullOrEmpty(labelKey))
                {
                    try { borrowOutcomes.WithLabels("unknown", "denied").Inc(); }
                    catch
                    {
                        // ignored
                    }

                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["labelKey"] = ["missing labelKey"] },
                        eventCode: EventCodes.BrowserPool.BorrowFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                LabelKey? parsed;
                bool parsedOk;
                string? parseError;
                if (enableWildcards && labelKey.Contains('*'))
                {
                    // Allow wildcard segment in any position by relaxing browser enforcement
                    var parseOpts = new LabelKeyParsingOptions { EnforceBrowserSecond = false };
                    parsedOk = LabelKey.TryParseDetailed(labelKey, out parsed, out parseError, parseOpts);
                }
                else
                {
                    parsedOk = LabelKey.TryParseDetailed(labelKey, out parsed, out parseError);
                }

                if (!parsedOk)
                {
                    try { borrowOutcomes.WithLabels(labelKey, "denied").Inc(); }
                    catch
                    {
                        // ignored
                    }

                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["labelKey"] = [$"invalid labelKey: {parseError}"] },
                        eventCode: EventCodes.BrowserPool.BorrowFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Normalize formatting (trim, case policy) for internal use
                labelKey = parsed!.Normalized;

                // Metrics use requested label key for request/latency tracking
                borrowRequests.WithLabels(labelKey).Inc();
                using var tmr = borrowLatency.WithLabels(labelKey).NewTimer();

                // Optional RunName from request (validated and normalized once)
                string? runNameNormalized = null;
                if (body.TryGetValue("runName", out var runNameRaw))
                {
                    if (!RunNameRules.TryNormalize(runNameRaw, out var rnNormalized, out var rnError))
                    {
                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]> { ["runName"] = [rnError ?? "invalid runName"] },
                            eventCode: EventCodes.BrowserPool.BorrowFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    runNameNormalized = rnNormalized; // may be null if empty after trim
                }

                // Idempotency handling: short-circuit duplicate borrows if a previous successful response exists
                var idempotencyKey = req.Headers["Idempotency-Key"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    var idem = idempotencyKey;
                    try
                    {
                        var saved = await db.StringGetAsync(RedisKeys.BorrowIdempotency(idem));
                        if (!saved.IsNullOrEmpty)
                        {
                            try { borrowOutcomes.WithLabels(labelKey, "success").Inc(); }
                            catch
                            {
                                // ignored
                            }

                            return Results.Text(saved.ToString(), "application/json");
                        }

                        // Mark pending to deduplicate concurrent retries during queue wait
                        await db.StringSetAsync(RedisKeys.BorrowIdempotencyPending(idem), labelKey,
                            TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                    }
                    catch
                    {
                        // ignored
                    }
                }

                // Helper that attempts to pop one item from a specific label list
                async Task<JsonElement?> TryBorrowForAsync(string candidate)
                {
                    // Maintenance gate: if pool is under maintenance, try to auto-clear if finished;
                    // otherwise skip this candidate (treat as unavailable).
                    try
                    {
                        if (await db.KeyExistsAsync(RedisKeys.MaintenanceFlag(candidate)))
                        {
                            var targetStr = await db.StringGetAsync(RedisKeys.MaintenanceTarget(candidate));
                            if (!targetStr.IsNullOrEmpty && long.TryParse(targetStr.ToString(), out var target))
                            {
                                var availNow = await db.ListLengthAsync(RedisKeys.Available(candidate));
                                var inuseNow = await db.ListLengthAsync(RedisKeys.InUse(candidate));
                                if (inuseNow == 0 && availNow == target)
                                {
                                    try
                                    {
                                        await db.KeyDeleteAsync(RedisKeys.MaintenanceFlag(candidate));
                                        await db.KeyDeleteAsync(RedisKeys.MaintenanceTarget(candidate));
                                        await db.KeyDeleteAsync(RedisKeys.MaintenanceSnapAvail(candidate));
                                        await db.KeyDeleteAsync(RedisKeys.MaintenanceSnapInuse(candidate));
                                        await db.KeyDeleteAsync(RedisKeys.MaintenanceSince(candidate));
                                    }
                                    catch
                                    {
                                        // ignored
                                    }
                                }
                            }

                            // If still under maintenance, skip borrowing from this candidate
                            if (await db.KeyExistsAsync(RedisKeys.MaintenanceFlag(candidate)))
                            {
                                return null;
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    var listKey = RedisKeys.Available(candidate);
                    var inuseKey = RedisKeys.InUse(candidate);

                    // Pop-and-scan loop: skip entries from quarantined nodes by reverting the move
                    for (var attempts = 0; attempts < 128; attempts++)
                    {
                        var res = await db.ScriptEvaluateAsync(luaFindPop, [listKey, inuseKey], []);
                        if (res.IsNull)
                        {
                            return null;
                        }

                        // Update gauge for the actual matched label
                        var listLenght = await db.ListLengthAsync(listKey);
                        poolAvailableGauge.WithLabels(candidate).Set(listLenght);

                        // Keep as JsonElement so we can both inspect fields and return it as the response object later.
                        var json = res.ToString();
                        if (string.IsNullOrEmpty(json))
                        {
                            return null;
                        }

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        // If the entry belongs to a quarantined node, reverse the pop and try the next one
                        try
                        {
                            if (root.TryGetProperty("nodeId", out var nodeEl) &&
                                nodeEl.ValueKind == JsonValueKind.String)
                            {
                                var nodeId = nodeEl.GetString();
                                if (!string.IsNullOrWhiteSpace(nodeId))
                                {
                                    var qTtl = await db.KeyTimeToLiveAsync(RedisKeys.NodeQuarantine(nodeId));
                                    if (qTtl is not null)
                                    {
                                        // Remove from in-use and push back to available tail to preserve the slot during cooldown
                                        await db.ListRemoveAsync(inuseKey, json);
                                        await db.ListRightPushAsync(listKey, json);
                                        // Continue scanning for a non-quarantined entry
                                        continue;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }

                        return root.Clone();
                    }

                    return null;
                }

                // 1) Exact match first (guarded by per-label concurrency cap)
                JsonElement? item = null;
                if (!EndpointCapacityQueue.ReachedCap(labelKey))
                {
                    item = await TryBorrowForAsync(labelKey);
                }

                if (item is not null)
                {
                    // Determine correlation id to use as runId
                    var runId = req.Headers["x-run-id"].FirstOrDefault()
                                ?? req.Query["runId"].FirstOrDefault()
                                ?? req.Headers["Correlation-Id"].FirstOrDefault()
                                ?? $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
                    using var op = chunkedLogger.BeginOperation("BorrowSession", new Dictionary<string, object>
                    {
                        ["runId"] = runId,
                        ["labelKey"] = labelKey,
                        ["runName"] = HubPrivacy.RedactRunName(runNameNormalized) ?? string.Empty
                    });

                    chunkedLogger.LogMilestone(EventCodes.BrowserPool.BorrowRequested,
                        "Borrow success for label {LabelKey}", labelKey);

                    // Borrow TTL: allow override via request body, else use default from config
                    var ttlSeconds = defaultBorrowTtlSeconds;
                    if (body.TryGetValue("ttlSeconds", out var ttlStr) && int.TryParse(ttlStr, out var ttlParsed))
                    {
                        ttlSeconds = Math.Clamp(ttlParsed, 60, 24 * 60 * 60);
                    }

                    var now = DateTime.UtcNow;
                    var ev = new CommandLogEventDto
                    {
                        RunId = runId,
                        TimestampUtc = now,
                        Kind = "Borrow",
                        Message = $"Borrowed for {labelKey}",
                        Props = new Dictionary<string, string> { ["labelKey"] = labelKey }
                    };

                    // Publish command event for ingestion service to process
                    try
                    {
                        var cmdEvt = new CommandEvent
                        {
                            EventType = "CommandAppended",
                            RunId = runId,
                            TimestampUtc = now,
                            DataJson = JsonSerializer.Serialize(new
                            {
                                kind = "Borrow",
                                message = $"Borrowed for {labelKey}",
                                propsJson = JsonSerializer.Serialize(
                                    new Dictionary<string, string> { ["labelKey"] = labelKey })
                            }),
                            CorrelationId = Guid.NewGuid().ToString()
                        };
                        await eventPublisher.PublishCommandEventAsync(cmdEvt);
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogWarning(ex, null, "[Borrow] Failed to publish command event for runId {RunId}", runId);
                    }

                    // Upsert and enrich run summary with metadata from borrowed item
                    var run = await resultsStore.GetRunAsync(runId) ?? new ResultRunSummaryDto
                    {
                        RunId = runId,
                        App = parsed!.App,
                        Browser = string.IsNullOrWhiteSpace(parsed!.Browser) ? "Chromium" : parsed!.Browser,
                        Env = parsed!.Env,
                        StartedAtUtc = now
                    };
                    if (!string.IsNullOrWhiteSpace(runNameNormalized))
                    {
                        run = run with { RunName = runNameNormalized };
                    }

                    run.SessionStatus = "Running"; // Browser session is active
                    run.Status = "Running"; // Keep legacy field in sync

                    try
                    {
                        string? nodeId = null, browserVer = null, region = null, os = null, pwVer = null;
                        if (item.Value.TryGetProperty("nodeId", out var nodeEl) &&
                            nodeEl.ValueKind == JsonValueKind.String)
                        {
                            nodeId = nodeEl.GetString();
                        }

                        if (item.Value.TryGetProperty("browserVersion", out var bvEl) &&
                            bvEl.ValueKind == JsonValueKind.String)
                        {
                            browserVer = bvEl.GetString();
                        }

                        if (item.Value.TryGetProperty("labels", out var labelsEl) &&
                            labelsEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in labelsEl.EnumerateObject())
                            {
                                if (string.Equals(p.Name, "region", StringComparison.OrdinalIgnoreCase) &&
                                    p.Value.ValueKind == JsonValueKind.String)
                                {
                                    region = p.Value.GetString();
                                }

                                if (string.Equals(p.Name, "os", StringComparison.OrdinalIgnoreCase) &&
                                    p.Value.ValueKind == JsonValueKind.String)
                                {
                                    os = p.Value.GetString();
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(nodeId))
                        {
                            var pwVal = await db.HashGetAsync($"node:{nodeId}", "PlaywrightVersion");
                            if (!pwVal.IsNullOrEmpty)
                            {
                                pwVer = pwVal.ToString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(region) || !string.IsNullOrWhiteSpace(os))
                        {
                            run = run with { Region = region ?? run.Region, OS = os ?? run.OS };
                        }

                        if (!string.IsNullOrWhiteSpace(nodeId))
                        {
                            run.WorkerNodeId = nodeId;
                        }

                        if (!string.IsNullOrWhiteSpace(pwVer))
                        {
                            run.PlaywrightVersion = pwVer;
                        }

                        if (!string.IsNullOrWhiteSpace(browserVer))
                        {
                            run.BrowserVersion = browserVer;
                        }
                    }
                    catch { }

                    await resultsStore.UpsertRunAsync(run);

                    CommandLogEventDto? serverEv = null;
                    try
                    {
                        string? ws = null, bt = null, args = null, bid = null;
                        if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl) &&
                            wsEl.ValueKind == JsonValueKind.String)
                        {
                            ws = wsEl.GetString();
                        }

                        if (item.Value.TryGetProperty("browserType", out var btEl) &&
                            btEl.ValueKind == JsonValueKind.String)
                        {
                            bt = btEl.GetString();
                        }

                        if (item.Value.TryGetProperty("args", out var argsEl) &&
                            argsEl.ValueKind == JsonValueKind.String)
                        {
                            args = argsEl.GetString();
                        }

                        if (item.Value.TryGetProperty("browserId", out var bidEl) &&
                            bidEl.ValueKind == JsonValueKind.String)
                        {
                            bid = bidEl.GetString();
                        }

                        // Map browserId to runId so worker-sourced command logs can be attributed
                        if (!string.IsNullOrWhiteSpace(bid))
                        {
                            await db.StringSetAsync(RedisKeys.BrowserRun(bid), runId, TimeSpan.FromHours(6));
                            chunkedLogger.LogMilestone(EventCodes.BrowserPool.BrowserAllocated,
                                "Borrow assigned browserId {BrowserId}", bid);
                        }

                        serverEv = new CommandLogEventDto
                        {
                            RunId = runId,
                            TimestampUtc = now.AddMilliseconds(-1),
                            Kind = "ServerLaunch",
                            Message = "Playwright server started",
                            Props = new Dictionary<string, string>()
                        };
                        if (!string.IsNullOrWhiteSpace(ws))
                        {
                            serverEv.Props["wsEndpoint"] = ws;
                        }

                        if (!string.IsNullOrWhiteSpace(bt))
                        {
                            serverEv.Props["browserType"] = bt;
                        }

                        if (!string.IsNullOrWhiteSpace(args))
                        {
                            serverEv.Props["args"] = args;
                        }

                        if (!string.IsNullOrWhiteSpace(bid))
                        {
                            serverEv.Props["browserId"] = bid;
                        }

                        // Persist session state and lease TTL to Redis so sessions survive Hub restarts
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(bid))
                            {
                                // Clamp ttlSeconds to configured maximum
                                ttlSeconds = Math.Min(ttlSeconds, maxSessionTtlSeconds);
                                await db.StringSetAsync(RedisKeys.BorrowTtl(bid), "1",
                                    TimeSpan.FromSeconds(ttlSeconds));
                                try
                                {
                                    await db.StringSetAsync($"borrow_idle:{bid}", "1",
                                        TimeSpan.FromSeconds(idleTimeoutSeconds));
                                }
                                catch { }

                                string? nodeIdPersist = null;
                                if (item.Value.TryGetProperty("nodeId", out var nodeIdEl) &&
                                    nodeIdEl.ValueKind == JsonValueKind.String)
                                {
                                    nodeIdPersist = nodeIdEl.GetString();
                                }

                                var sessionKey = $"session:{bid}";
                                var fields = new HashEntry[]
                                {
                                    new("browserId", bid), new("labelKey", labelKey), new("runId", runId),
                                    new("nodeId", nodeIdPersist ?? string.Empty),
                                    new("borrowedAtUtc", now.ToString("o")),
                                    new("ttlSeconds", ttlSeconds.ToString())
                                };
                                await db.HashSetAsync(sessionKey, fields);
                            }
                        }
                        catch { }

                        if (!string.IsNullOrWhiteSpace(run.PlaywrightVersion))
                        {
                            serverEv.Props["playwrightVersion"] = run.PlaywrightVersion;
                        }

                        if (!string.IsNullOrWhiteSpace(run.BrowserVersion))
                        {
                            serverEv.Props["browserVersion"] = run.BrowserVersion;
                        }

                        serverEv.Props["labelKey"] = labelKey;

                        // Publish ServerLaunch command event
                        try
                        {
                            var serverCmdEvt = new CommandEvent
                            {
                                EventType = "CommandAppended",
                                RunId = runId,
                                TimestampUtc = now.AddMilliseconds(-1),
                                DataJson = JsonSerializer.Serialize(new
                                {
                                    kind = "ServerLaunch",
                                    message = "Playwright server started",
                                    propsJson = JsonSerializer.Serialize(serverEv.Props)
                                }),
                                CorrelationId = Guid.NewGuid().ToString()
                            };
                            await eventPublisher.PublishCommandEventAsync(serverCmdEvt);
                        }
                        catch (Exception exPub)
                        {
                            chunkedLogger.LogWarning(exPub, null,
                                "[Borrow] Failed to publish ServerLaunch command event for runId {RunId}", runId);
                        }
                    }
                    catch { }

                    var toSend = serverEv is not null ? new[] { serverEv, ev } : new[] { ev };
                    await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(toSend);
                    await resultsHubCtx.Clients.Group($"run:{runId}").RunUpdate(run);

                    // Propagate correlation id back to caller
                    try { req.HttpContext.Response.Headers["Correlation-Id"] = runId; }
                    catch { }

                    // Metrics: outcome success for requested label; queue length currently 0
                    try
                    {
                        borrowOutcomes.WithLabels(labelKey, "success").Inc();
                        borrowQueueGauge.WithLabels(labelKey).Set(0);

                        var avail = await db.ListLengthAsync(RedisKeys.Available(labelKey));
                        var inuse = await db.ListLengthAsync(RedisKeys.InUse(labelKey));
                        var total = (double)(avail + inuse);
                        var ratio = total > 0 ? inuse / total : 0.0;
                        poolUtilizationGauge.WithLabels(labelKey).Set(ratio);
                    }
                    catch { }

                    // Track in-flight concurrency for requested label (for cap enforcement)
                    try { EndpointCapacityQueue.OnStarted(labelKey); }
                    catch { }

                    // Build response payload including runName
                    try
                    {
                        string? ws = null, bt = null, bid = null, nodeIdResp = null;
                        if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl3) &&
                            wsEl3.ValueKind == JsonValueKind.String)
                        {
                            ws = wsEl3.GetString();
                        }

                        if (item.Value.TryGetProperty("browserType", out var btEl3) &&
                            btEl3.ValueKind == JsonValueKind.String)
                        {
                            bt = btEl3.GetString();
                        }

                        if (item.Value.TryGetProperty("browserId", out var bidEl3) &&
                            bidEl3.ValueKind == JsonValueKind.String)
                        {
                            bid = bidEl3.GetString();
                        }

                        if (item.Value.TryGetProperty("nodeId", out var nodeIdEl3) &&
                            nodeIdEl3.ValueKind == JsonValueKind.String)
                        {
                            nodeIdResp = nodeIdEl3.GetString();
                        }

                        var expiresAt = now.AddSeconds(ttlSeconds);
                        var respObj = new
                        {
                            browserId = bid ?? string.Empty,
                            webSocketEndpoint = ws ?? string.Empty,
                            labelKey,
                            browserType = bt,
                            nodeId = nodeIdResp,
                            runName = runNameNormalized,
                            expiresAtUtc = expiresAt
                        };
                        if (!string.IsNullOrWhiteSpace(idempotencyKey))
                        {
                            try
                            {
                                var idemJson = JsonSerializer.Serialize(respObj);
                                await db.StringSetAsync(RedisKeys.BorrowIdempotency(idempotencyKey!), idemJson,
                                    TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                                await db.KeyDeleteAsync(RedisKeys.BorrowIdempotencyPending(idempotencyKey!));
                            }
                            catch { }
                        }

                        return Results.Ok(respObj);
                    }
                    catch
                    {
                        if (!string.IsNullOrWhiteSpace(idempotencyKey) && item is not null)
                        {
                            try
                            {
                                var idemJson = item.Value.GetRawText();
                                await db.StringSetAsync(RedisKeys.BorrowIdempotency(idempotencyKey), idemJson,
                                    TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                                await db.KeyDeleteAsync(RedisKeys.BorrowIdempotencyPending(idempotencyKey!));
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        return Results.Ok(item);
                    }
                }

                // Build candidate list according to configuration
                var candidates = new List<string>();

                // 2) Trailing-segment fallback: drop trailing segments from the requested label
                if (enableTrailingFallback)
                {
                    var s = labelKey;
                    while (true)
                    {
                        var idx = s.LastIndexOf(':');
                        if (idx <= 0)
                        {
                            break;
                        }

                        s = s[..idx];
                        if (!string.Equals(s, labelKey, StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add(s);
                        }
                    }
                }

                // 3) Prefix expansion: treat missing trailing segments as ANY by matching more specific pools
                if (enablePrefixExpand)
                {
                    var pattern = RedisKeys.AvailablePrefix + labelKey + ":*";
                    foreach (var ep in mux.GetEndPoints())
                    {
                        var server = mux.GetServer(ep);
                        if (server is null || !server.IsConnected)
                        {
                            continue;
                        }

                        foreach (var key in server.Keys(pattern: pattern))
                        {
                            var k = key.ToString();
                            if (k.StartsWith(RedisKeys.AvailablePrefix, StringComparison.Ordinal))
                            {
                                var label = k[RedisKeys.AvailablePrefix.Length..];
                                if (!candidates.Contains(label, StringComparer.OrdinalIgnoreCase))
                                {
                                    candidates.Add(label);
                                }
                            }
                        }
                    }
                }

                // 4) Wildcards: allow '*' in any segment if enabled
                if (enableWildcards && labelKey.Contains('*'))
                {
                    var pattern = RedisKeys.AvailablePrefix + labelKey;
                    foreach (var ep in mux.GetEndPoints())
                    {
                        var server = mux.GetServer(ep);
                        if (server is null || !server.IsConnected)
                        {
                            continue;
                        }

                        foreach (var key in server.Keys(pattern: pattern))
                        {
                            var k = key.ToString();
                            if (k.StartsWith(RedisKeys.AvailablePrefix, StringComparison.Ordinal))
                            {
                                var label = k[RedisKeys.AvailablePrefix.Length..];
                                if (!candidates.Contains(label, StringComparer.OrdinalIgnoreCase))
                                {
                                    candidates.Add(label);
                                }
                            }
                        }
                    }
                }

                // Order and select candidates using the central LabelMatcher (exact → trailing fallback → prefix expansion → wildcards)
                var matchingOptions = new LabelMatchingOptions
                {
                    TrailingFallbackEnabled = enableTrailingFallback,
                    PrefixExpansionEnabled = enablePrefixExpand,
                    WildcardsEnabled = enableWildcards,
                    MinSegmentsForFallback = 2
                };
                var matcher = new LabelMatcher(matchingOptions);

                var requestedKey = parsed!; // already parsed earlier

                // Parse candidate strings into LabelKey values (skip invalid)
                var availableKeys = new List<LabelKey>();
                foreach (var s in candidates)
                {
                    if (LabelKey.TryParse(s, out var lk))
                    {
                        availableKeys.Add(lk!);
                    }
                }

                // Iterate by matcher-priority; attempt to borrow for each chosen label until success
                var poolKeys = new List<LabelKey>(availableKeys);
                while (poolKeys.Count > 0)
                {
                    var chosen = matcher.TryMatch(requestedKey, poolKeys);
                    if (chosen is null)
                    {
                        break;
                    }

                    var matchedLabel = chosen.Normalized;
                    item = await TryBorrowForAsync(matchedLabel);

                    // Remove the attempted label and continue if not available
                    poolKeys.RemoveAll(x => string.Equals(x.Normalized, matchedLabel, StringComparison.Ordinal));

                    if (item is not null)
                    {
                        // Determine correlation id to use as runId
                        var runId = req.Headers["x-run-id"].FirstOrDefault()
                                    ?? req.Query["runId"].FirstOrDefault()
                                    ?? req.Headers["Correlation-Id"].FirstOrDefault()
                                    ?? $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";

                        // Borrow TTL: allow override via request body, else use default from config
                        var ttlSeconds = defaultBorrowTtlSeconds;
                        if (body.TryGetValue("ttlSeconds", out var ttlStr2) &&
                            int.TryParse(ttlStr2, out var ttlParsed2))
                        {
                            ttlSeconds = Math.Clamp(ttlParsed2, 60, 24 * 60 * 60);
                        }

                        var now = DateTime.UtcNow;
                        var ev = new CommandLogEventDto
                        {
                            RunId = runId,
                            TimestampUtc = now,
                            Kind = "Borrow",
                            Message = $"Borrowed for {labelKey} via fallback {matchedLabel}",
                            Props = new Dictionary<string, string>
                            {
                                ["labelKey"] = labelKey,
                                ["matchedLabel"] = matchedLabel
                            }
                        };

                        // Publish command event for ingestion service to process
                        try
                        {
                            var cmdEvt = new CommandEvent
                            {
                                EventType = "CommandAppended",
                                RunId = runId,
                                TimestampUtc = now,
                                DataJson = JsonSerializer.Serialize(new
                                {
                                    kind = "Borrow",
                                    message = $"Borrowed for {labelKey} via fallback {matchedLabel}",
                                    propsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                                    {
                                        ["labelKey"] = labelKey,
                                        ["matchedLabel"] = matchedLabel
                                    })
                                }),
                                CorrelationId = Guid.NewGuid().ToString()
                            };
                            await eventPublisher.PublishCommandEventAsync(cmdEvt);
                        }
                        catch (Exception ex)
                        {
                            chunkedLogger.LogWarning(ex, null, "[Borrow] Failed to publish fallback command event for runId {RunId}",
                                runId);
                        }

                        var run = await resultsStore.GetRunAsync(runId) ?? new ResultRunSummaryDto
                        {
                            RunId = runId!,
                            App = labelKey.Split(':').FirstOrDefault() ?? "",
                            Browser = labelKey.Split(':').Skip(1).FirstOrDefault() ?? "Chromium",
                            Env = labelKey.Split(':').Skip(2).FirstOrDefault() ?? "",
                            StartedAtUtc = now
                        };
                        if (!string.IsNullOrWhiteSpace(runNameNormalized))
                        {
                            run = run with { RunName = runNameNormalized };
                        }

                        run.SessionStatus = "Running"; // Browser session is active
                        run.Status = "Running"; // Keep legacy field in sync

                        try
                        {
                            string? nodeId = null, browserVer = null, region = null, os = null, pwVer = null;
                            if (item.Value.TryGetProperty("nodeId", out var nodeEl) &&
                                nodeEl.ValueKind == JsonValueKind.String)
                            {
                                nodeId = nodeEl.GetString();
                            }

                            if (item.Value.TryGetProperty("browserVersion", out var bvEl) &&
                                bvEl.ValueKind == JsonValueKind.String)
                            {
                                browserVer = bvEl.GetString();
                            }

                            if (item.Value.TryGetProperty("labels", out var labelsEl) &&
                                labelsEl.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var p in labelsEl.EnumerateObject())
                                {
                                    if (string.Equals(p.Name, "region", StringComparison.OrdinalIgnoreCase) &&
                                        p.Value.ValueKind == JsonValueKind.String)
                                    {
                                        region = p.Value.GetString();
                                    }

                                    if (string.Equals(p.Name, "os", StringComparison.OrdinalIgnoreCase) &&
                                        p.Value.ValueKind == JsonValueKind.String)
                                    {
                                        os = p.Value.GetString();
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(nodeId))
                            {
                                var pwVal = await db.HashGetAsync($"node:{nodeId}", "PlaywrightVersion");
                                if (!pwVal.IsNullOrEmpty)
                                {
                                    pwVer = pwVal.ToString();
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(region) || !string.IsNullOrWhiteSpace(os))
                            {
                                run = run with { Region = region ?? run.Region, OS = os ?? run.OS };
                            }

                            if (!string.IsNullOrWhiteSpace(nodeId))
                            {
                                run.WorkerNodeId = nodeId;
                            }

                            if (!string.IsNullOrWhiteSpace(pwVer))
                            {
                                run.PlaywrightVersion = pwVer;
                            }

                            if (!string.IsNullOrWhiteSpace(browserVer))
                            {
                                run.BrowserVersion = browserVer;
                            }
                        }
                        catch { }

                        await resultsStore.UpsertRunAsync(run);

                        CommandLogEventDto? serverEv = null;
                        try
                        {
                            string? ws = null, bt = null, args = null, bid = null;
                            if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl) &&
                                wsEl.ValueKind == JsonValueKind.String)
                            {
                                ws = wsEl.GetString();
                            }

                            if (item.Value.TryGetProperty("browserType", out var btEl) &&
                                btEl.ValueKind == JsonValueKind.String)
                            {
                                bt = btEl.GetString();
                            }

                            if (item.Value.TryGetProperty("args", out var argsEl) &&
                                argsEl.ValueKind == JsonValueKind.String)
                            {
                                args = argsEl.GetString();
                            }

                            if (item.Value.TryGetProperty("browserId", out var bidEl) &&
                                bidEl.ValueKind == JsonValueKind.String)
                            {
                                bid = bidEl.GetString();
                            }

                            // Map browserId to runId so worker-sourced command logs can be attributed
                            if (!string.IsNullOrWhiteSpace(bid))
                            {
                                await db.StringSetAsync(RedisKeys.BrowserRun(bid), runId!, TimeSpan.FromHours(6));
                            }

                            serverEv = new CommandLogEventDto
                            {
                                RunId = runId!,
                                TimestampUtc = now.AddMilliseconds(-1),
                                Kind = "ServerLaunch",
                                Message = "Playwright server started",
                                Props = new Dictionary<string, string>()
                            };
                            if (!string.IsNullOrWhiteSpace(ws))
                            {
                                serverEv.Props["wsEndpoint"] = ws;
                            }

                            if (!string.IsNullOrWhiteSpace(bt))
                            {
                                serverEv.Props["browserType"] = bt;
                            }

                            if (!string.IsNullOrWhiteSpace(args))
                            {
                                serverEv.Props["args"] = args!;
                            }

                            if (!string.IsNullOrWhiteSpace(bid))
                            {
                                serverEv.Props["browserId"] = bid!;
                            }

                            // Persist session state and lease TTL to Redis so sessions survive Hub restarts
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(bid))
                                {
                                    // Clamp ttlSeconds to configured maximum
                                    ttlSeconds = Math.Min(ttlSeconds, maxSessionTtlSeconds);
                                    await db.StringSetAsync(RedisKeys.BorrowTtl(bid), "1",
                                        TimeSpan.FromSeconds(ttlSeconds));
                                    try
                                    {
                                        await db.StringSetAsync($"borrow_idle:{bid}", "1",
                                            TimeSpan.FromSeconds(idleTimeoutSeconds));
                                    }
                                    catch { }

                                    string? nodeIdPersist = null;
                                    if (item.Value.TryGetProperty("nodeId", out var nodeIdEl2) &&
                                        nodeIdEl2.ValueKind == JsonValueKind.String)
                                    {
                                        nodeIdPersist = nodeIdEl2.GetString();
                                    }

                                    var sessionKey = $"session:{bid}";
                                    var fields = new HashEntry[]
                                    {
                                        new("browserId", bid), new("labelKey", labelKey), new("runId", runId!),
                                        new("nodeId", nodeIdPersist ?? string.Empty),
                                        new("borrowedAtUtc", now.ToString("o")),
                                        new("ttlSeconds", ttlSeconds.ToString())
                                    };
                                    await db.HashSetAsync(sessionKey, fields);
                                }
                            }
                            catch { }

                            if (!string.IsNullOrWhiteSpace(run.PlaywrightVersion))
                            {
                                serverEv.Props["playwrightVersion"] = run.PlaywrightVersion!;
                            }

                            if (!string.IsNullOrWhiteSpace(run.BrowserVersion))
                            {
                                serverEv.Props["browserVersion"] = run.BrowserVersion!;
                            }

                            serverEv.Props["labelKey"] = labelKey;
                            serverEv.Props["matchedLabel"] = matchedLabel;

                            // Publish ServerLaunch command event
                            try
                            {
                                var serverCmdEvt = new CommandEvent
                                {
                                    EventType = "CommandAppended",
                                    RunId = runId!,
                                    TimestampUtc = now.AddMilliseconds(-1),
                                    DataJson = JsonSerializer.Serialize(new
                                    {
                                        kind = "ServerLaunch",
                                        message = "Playwright server started",
                                        propsJson = JsonSerializer.Serialize(serverEv.Props)
                                    }),
                                    CorrelationId = Guid.NewGuid().ToString()
                                };
                                await eventPublisher.PublishCommandEventAsync(serverCmdEvt);
                            }
                            catch (Exception exPub)
                            {
                                logger.LogWarning(exPub,
                                    "[Borrow] Failed to publish fallback ServerLaunch command event for runId {RunId}",
                                    runId);
                            }
                        }
                        catch { }

                        var toSend = serverEv is not null ? new[] { serverEv, ev } : new[] { ev };
                        await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(toSend);
                        await resultsHubCtx.Clients.Group($"run:{runId}").RunUpdate(run);

                        // Propagate correlation id back to caller
                        try { req.HttpContext.Response.Headers["Correlation-Id"] = runId; }
                        catch { }

                        // Metrics: outcome success for requested label; queue length currently 0
                        try
                        {
                            borrowOutcomes.WithLabels(labelKey, "success").Inc();
                            borrowQueueGauge.WithLabels(labelKey).Set(0);

                            var avail = await db.ListLengthAsync(RedisKeys.Available(labelKey));
                            var inuse = await db.ListLengthAsync(RedisKeys.InUse(labelKey));
                            var total = (double)(avail + inuse);
                            var ratio = total > 0 ? inuse / total : 0.0;
                            poolUtilizationGauge.WithLabels(labelKey).Set(ratio);
                        }
                        catch { }

                        // Track in-flight concurrency for requested label (for cap enforcement)
                        try { EndpointCapacityQueue.OnStarted(labelKey); }
                        catch { }

                        // Build response payload including runName
                        try
                        {
                            string? ws = null, bt = null, bid = null, nodeIdResp = null;
                            if (item.Value.TryGetProperty("webSocketEndpoint", out var wsEl3) &&
                                wsEl3.ValueKind == JsonValueKind.String)
                            {
                                ws = wsEl3.GetString();
                            }

                            if (item.Value.TryGetProperty("browserType", out var btEl3) &&
                                btEl3.ValueKind == JsonValueKind.String)
                            {
                                bt = btEl3.GetString();
                            }

                            if (item.Value.TryGetProperty("browserId", out var bidEl3) &&
                                bidEl3.ValueKind == JsonValueKind.String)
                            {
                                bid = bidEl3.GetString();
                            }

                            if (item.Value.TryGetProperty("nodeId", out var nodeIdEl3) &&
                                nodeIdEl3.ValueKind == JsonValueKind.String)
                            {
                                nodeIdResp = nodeIdEl3.GetString();
                            }

                            var expiresAt = now.AddSeconds(ttlSeconds);
                            var respObj = new
                            {
                                browserId = bid ?? string.Empty,
                                webSocketEndpoint = ws ?? string.Empty,
                                labelKey,
                                browserType = bt,
                                nodeId = nodeIdResp,
                                runName = runNameNormalized,
                                expiresAtUtc = expiresAt
                            };
                            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                            {
                                try
                                {
                                    var idemJson = JsonSerializer.Serialize(respObj);
                                    await db.StringSetAsync(RedisKeys.BorrowIdempotency(idempotencyKey!), idemJson,
                                        TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                                    await db.KeyDeleteAsync(RedisKeys.BorrowIdempotencyPending(idempotencyKey!));
                                }
                                catch
                                {
                                    // ignored
                                }
                            }

                            return Results.Ok(respObj);
                        }
                        catch
                        {
                            if (!string.IsNullOrWhiteSpace(idempotencyKey) && item is not null)
                            {
                                try
                                {
                                    var idemJson = item.Value.GetRawText();
                                    await db.StringSetAsync(RedisKeys.BorrowIdempotency(idempotencyKey!), idemJson,
                                        TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                                    await db.KeyDeleteAsync(RedisKeys.BorrowIdempotencyPending(idempotencyKey!));
                                }
                                catch
                                {
                                    // ignored
                                }
                            }

                            return Results.Ok(item);
                        }
                    }
                }

                // Capacity queue: attempt to wait for capacity with timeout and fairness
                var runIdForQueue = req.Headers["x-run-id"].FirstOrDefault()
                                    ?? req.Query["runId"].FirstOrDefault()
                                    ?? req.Headers["Correlation-Id"].FirstOrDefault()
                                    ?? "anonymous";

                var enq = EndpointCapacityQueue.TryEnqueue(labelKey, runIdForQueue);
                if (enq.token is not null)
                {
                    try { borrowQueueGauge.WithLabels(labelKey).Set(EndpointCapacityQueue.GetQueueLength(labelKey)); }
                    catch
                    {
                        // ignored
                    }

                    var signaled = await EndpointCapacityQueue.WaitAsync(enq.token,
                        TimeSpan.FromSeconds(queueTimeoutSeconds), req.HttpContext.RequestAborted);
                    if (!signaled)
                    {
                        EndpointCapacityQueue.Remove(enq.token);
                        try { borrowOutcomes.WithLabels(labelKey, "timeout").Inc(); }
                        catch
                        {
                            // ignored
                        }

                        try
                        {
                            borrowQueueGauge.WithLabels(labelKey).Set(EndpointCapacityQueue.GetQueueLength(labelKey));
                        }
                        catch
                        {
                            // ignored
                        }

                        return ProblemDetailsHelpers.ServiceUnavailable(
                            dependency: "BrowserPool",
                            safeMessage: $"Borrow timed out after {queueTimeoutSeconds}s for {labelKey}",
                            eventCode: EventCodes.BrowserPool.BorrowTimeout,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    // After being signaled, try to borrow again (exact + fallback)
                    var item2 = await TryBorrowForAsync(labelKey);
                    if (item2 is null)
                    {
                        // Recompute candidates and attempt fallback again
                        var endpoints2 = mux.GetEndPoints();
                        var candidates2 = new List<string>();
                        foreach (var ep in endpoints2)
                        {
                            try
                            {
                                var srv = mux.GetServer(ep);
                                foreach (var key in srv.Keys(pattern: RedisKeys.AvailablePrefix + "*"))
                                {
                                    var sKey = key.ToString();
                                    var label = sKey[RedisKeys.AvailablePrefix.Length..];
                                    if (!string.IsNullOrWhiteSpace(label))
                                    {
                                        var len = await db.ListLengthAsync(RedisKeys.Available(label));
                                        if (len > 0)
                                        {
                                            if (!candidates2.Contains(label, StringComparer.OrdinalIgnoreCase))
                                            {
                                                candidates2.Add(label);
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        var matchingOptions2 = new LabelMatchingOptions
                        {
                            TrailingFallbackEnabled = enableTrailingFallback,
                            PrefixExpansionEnabled = enablePrefixExpand,
                            WildcardsEnabled = enableWildcards,
                            MinSegmentsForFallback = 2
                        };
                        var matcher2 = new LabelMatcher(matchingOptions2);
                        var requestedKey2 = LabelKey.TryParse(labelKey, out var lkTmp) ? lkTmp! : parsed!;
                        var availableKeys2 = new List<LabelKey>();
                        foreach (var s in candidates2)
                        {
                            if (LabelKey.TryParse(s, out var lk))
                            {
                                availableKeys2.Add(lk!);
                            }
                        }

                        var poolKeys2 = new List<LabelKey>(availableKeys2);
                        while (poolKeys2.Count > 0 && item2 is null)
                        {
                            var chosen2 = matcher2.TryMatch(requestedKey2, poolKeys2);
                            if (chosen2 is null)
                            {
                                break;
                            }

                            var matched2 = chosen2.Normalized;
                            item2 = await TryBorrowForAsync(matched2);
                            poolKeys2.RemoveAll(x => string.Equals(x.Normalized, matched2, StringComparison.Ordinal));
                        }
                    }

                    try { borrowQueueGauge.WithLabels(labelKey).Set(EndpointCapacityQueue.GetQueueLength(labelKey)); }
                    catch { }

                    if (item2 is not null)
                    {
                        try
                        {
                            borrowOutcomes.WithLabels(labelKey, "success").Inc();
                            var avail2 = await db.ListLengthAsync(RedisKeys.Available(labelKey));
                            var inuse2 = await db.ListLengthAsync(RedisKeys.InUse(labelKey));
                            var total2 = (double)(avail2 + inuse2);
                            var ratio2 = total2 > 0 ? inuse2 / total2 : 0.0;
                            poolUtilizationGauge.WithLabels(labelKey).Set(ratio2);
                        }
                        catch { }

                        // Track in-flight concurrency for requested label (for cap enforcement)
                        try { EndpointCapacityQueue.OnStarted(labelKey); }
                        catch { }

                        try
                        {
                            string? ws = null, bt = null, bid = null, nodeIdResp = null;
                            if (item2.Value.TryGetProperty("webSocketEndpoint", out var wsEl4) &&
                                wsEl4.ValueKind == JsonValueKind.String)
                            {
                                ws = wsEl4.GetString();
                            }

                            if (item2.Value.TryGetProperty("browserType", out var btEl4) &&
                                btEl4.ValueKind == JsonValueKind.String)
                            {
                                bt = btEl4.GetString();
                            }

                            if (item2.Value.TryGetProperty("browserId", out var bidEl4) &&
                                bidEl4.ValueKind == JsonValueKind.String)
                            {
                                bid = bidEl4.GetString();
                            }

                            if (item2.Value.TryGetProperty("nodeId", out var nodeIdEl4) &&
                                nodeIdEl4.ValueKind == JsonValueKind.String)
                            {
                                nodeIdResp = nodeIdEl4.GetString();
                            }

                            var respObj = new
                            {
                                browserId = bid ?? string.Empty,
                                webSocketEndpoint = ws ?? string.Empty,
                                labelKey,
                                browserType = bt,
                                nodeId = nodeIdResp,
                                runName = runNameNormalized
                            };
                            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                            {
                                try
                                {
                                    var idemJson = JsonSerializer.Serialize(respObj);
                                    await db.StringSetAsync(RedisKeys.BorrowIdempotency(idempotencyKey!), idemJson,
                                        TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                                    await db.KeyDeleteAsync(RedisKeys.BorrowIdempotencyPending(idempotencyKey!));
                                }
                                catch { }
                            }

                            return Results.Ok(respObj);
                        }
                        catch
                        {
                            if (!string.IsNullOrWhiteSpace(idempotencyKey) && item2 is not null)
                            {
                                try
                                {
                                    var idemJson = item2.Value.GetRawText();
                                    await db.StringSetAsync(RedisKeys.BorrowIdempotency(idempotencyKey!), idemJson,
                                        TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                                    await db.KeyDeleteAsync(RedisKeys.BorrowIdempotencyPending(idempotencyKey!));
                                }
                                catch { }
                            }

                            return Results.Ok(item2);
                        }
                    }
                }
                else
                {
                    // Queue rejected; treat as denied to avoid amplifying pressure
                    try { borrowOutcomes.WithLabels(labelKey, "denied").Inc(); }
                    catch { }
                }

                return ProblemDetailsHelpers.ServiceUnavailable(
                    dependency: "BrowserPool",
                    safeMessage: $"No browser available for {labelKey}",
                    eventCode: EventCodes.BrowserPool.BorrowFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            })
            .WithOpenApi(op =>
            {
                op.Summary = "Borrow a browser session";
                op.Description =
                    "Requests a browser session for the provided labelKey. Requires API key authentication via Bearer token.";
                op.Parameters ??= new List<OpenApiParameter>();
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "runId",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Optional run identifier to attribute logs and results",
                    Schema = new OpenApiSchema { Type = "string" }
                });
                op.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Example = new OpenApiObject
                            {
                                ["labelKey"] = new OpenApiString("AppB:Chromium:UAT"),
                                ["runName"] = new OpenApiString("Checkout – Smoke")
                            }
                        }
                    }
                };
                var ok = new OpenApiResponse
                {
                    Description = "Borrow successful",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Example = new OpenApiObject
                            {
                                ["browserId"] = new OpenApiString("b-123"),
                                ["webSocketEndpoint"] =
                                    new OpenApiString("ws://127.0.0.1:5200/ws/b-123"),
                                ["browserType"] = new OpenApiString("chromium"),
                                ["labelKey"] = new OpenApiString("AppB:Chromium:UAT"),
                                ["runName"] = new OpenApiString("Checkout – Smoke"),
                                ["expiresAtUtc"] = new OpenApiString("2025-01-01T12:00:00Z")
                            }
                        }
                    }
                };
                op.Responses["200"] = ok;
                op.Responses["401"] =
                    new OpenApiResponse { Description = "Unauthorized (missing or invalid x-hub-secret)" };
                op.Responses["400"] = new OpenApiResponse { Description = "Bad Request (invalid or missing labelKey)" };
                op.Responses["503"] =
                    new OpenApiResponse { Description = "No capacity available for the requested label" };
                op.Security = new List<OpenApiSecurityRequirement>
                {
                    new()
                    {
                        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "HubSecret" } }
                        ] = new List<string>()
                    }
                };
                return op;
            });

        app.MapPost("/session/return", async (HttpRequest req,
                [FromServices] IEventPublisher eventPublisher) =>
            {
                if (!CheckSecret(req, "x-hub-secret", hubNodeSecret))
                {
                    return ProblemDetailsHelpers.Unauthorized(
                        "Unauthorized",
                        eventCode: EventCodes.BrowserPool.ReturnFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var body = await req.ReadFromJsonAsync<Dictionary<string, string>>() ??
                           new Dictionary<string, string>();
                if (!body.TryGetValue("labelKey", out var labelKey) ||
                    !body.TryGetValue("browserId", out var browserId))
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["missing labelKey|browserId"] },
                        eventCode: EventCodes.BrowserPool.ReturnFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var op = chunkedLogger.BeginOperation("ReturnSession", new Dictionary<string, object>
                {
                    ["browserId"] = browserId,
                    ["labelKey"] = labelKey
                });

                chunkedLogger.LogMilestone(EventCodes.BrowserPool.ReturnRequested,
                    "Return requested for {BrowserId} on label {LabelKey}", browserId, labelKey);

                // Validate and normalize labelKey
                if (!LabelKey.TryParseDetailed(labelKey, out var parsedReturn, out var parseErr,
                        new LabelKeyParsingOptions { EnforceBrowserSecond = false }))
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["labelKey"] = [$"invalid labelKey: {parseErr}"] },
                        eventCode: EventCodes.BrowserPool.ReturnFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                labelKey = parsedReturn!.Normalized;

                // Idempotency handling for Return: short-circuit duplicate returns
                var returnIdempotencyKey = req.Headers["Idempotency-Key"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(returnIdempotencyKey))
                {
                    try
                    {
                        var saved = await db.StringGetAsync(RedisKeys.ReturnIdempotency(returnIdempotencyKey!));
                        if (!saved.IsNullOrEmpty)
                        {
                            return Results.Text(saved.ToString(), "application/json");
                        }

                        await db.StringSetAsync(
                            RedisKeys.ReturnIdempotencyPending(returnIdempotencyKey!),
                            $"{labelKey}:{browserId}",
                            TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                    }
                    catch { }
                }

                var inuseKey = RedisKeys.InUse(labelKey);
                var availKey = RedisKeys.Available(labelKey);

                var res = await db.ScriptEvaluateAsync(luaReturn, [inuseKey, availKey], [browserId]);
                // Update availability gauge regardless of idempotent outcomes
                var listLenght = await db.ListLengthAsync(availKey);
                poolAvailableGauge.WithLabels(labelKey).Set(listLenght);

                // Request sidecar recycle on the worker so the instance is torn down and replenished with a fresh one
                // This aligns with the policy of not reusing the same browser across multiple borrowers.
                try { await db.StringSetAsync(RedisKeys.Recycle(browserId), "1", TimeSpan.FromMinutes(2)); }
                catch { }

                if (!res.IsNull)
                {
                    // Capacity is now available: decrement in-flight for this label and wake a waiter fairly
                    try { EndpointCapacityQueue.OnFinished(labelKey); }
                    catch { }

                    try { EndpointCapacityQueue.Signal(labelKey); }
                    catch { }

                    try { borrowQueueGauge.WithLabels(labelKey).Set(EndpointCapacityQueue.GetQueueLength(labelKey)); }
                    catch { }
                }

                if (res.IsNull)
                {
                    // Treat return as idempotent: if browserId is not in the in-use list, consider it already returned
                    // Clean up any persisted session state/lease
                    try { await db.KeyDeleteAsync(RedisKeys.BorrowTtl(browserId)); }
                    catch { }

                    try { await db.KeyDeleteAsync($"borrow_idle:{browserId}"); }
                    catch { }

                    try { await db.KeyDeleteAsync($"session:{browserId}"); }
                    catch { }

                    var respAlready = new { returned = browserId, note = "already returned" };
                    if (!string.IsNullOrWhiteSpace(returnIdempotencyKey))
                    {
                        try
                        {
                            var idemJson = JsonSerializer.Serialize(respAlready);
                            await db.StringSetAsync(RedisKeys.ReturnIdempotency(returnIdempotencyKey!), idemJson,
                                TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                            await db.KeyDeleteAsync(RedisKeys.ReturnIdempotencyPending(returnIdempotencyKey!));
                        }
                        catch { }
                    }

                    return Results.Ok(respAlready);
                }

                // Optional results emission if runId provided or inferred via Correlation-Id or existing mapping
                var runId2 = req.Headers["x-run-id"].FirstOrDefault()
                             ?? req.Query["runId"].FirstOrDefault()
                             ?? req.Headers["Correlation-Id"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(runId2))
                {
                    try
                    {
                        var v = await db.StringGetAsync(RedisKeys.BrowserRun(browserId));
                        if (!v.IsNullOrEmpty)
                        {
                            runId2 = v.ToString();
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(runId2))
                {
                    var now = DateTime.UtcNow;
                    var ev = new CommandLogEventDto
                    {
                        RunId = runId2!,
                        TimestampUtc = now,
                        Kind = "Return",
                        Message = $"Returned browser {browserId} for {labelKey}",
                        Props = new Dictionary<string, string>
                        {
                            ["labelKey"] = labelKey,
                            ["browserId"] = browserId
                        }
                    };

                    // Publish command event for ingestion service to process
                    try
                    {
                        var cmdEvt = new CommandEvent
                        {
                            EventType = "CommandAppended",
                            RunId = runId2!,
                            TimestampUtc = now,
                            DataJson = JsonSerializer.Serialize(new
                            {
                                kind = "Return",
                                message = $"Returned browser {browserId} for {labelKey}",
                                propsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                                {
                                    ["labelKey"] = labelKey,
                                    ["browserId"] = browserId
                                })
                            }),
                            CorrelationId = Guid.NewGuid().ToString()
                        };
                        await eventPublisher.PublishCommandEventAsync(cmdEvt);
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogWarning(ex, null, "[Return] Failed to publish command event for runId {RunId}", runId2);
                    }

                    var run = await resultsStore.GetRunAsync(runId2) ?? new ResultRunSummaryDto
                    {
                        RunId = runId2,
                        App = labelKey.Split(':').FirstOrDefault() ?? "",
                        Browser = labelKey.Split(':').Skip(1).FirstOrDefault() ?? "Chromium",
                        Env = labelKey.Split(':').Skip(2).FirstOrDefault() ?? "",
                        StartedAtUtc = now
                    };
                    // Mark run complete on return when attributed by runId
                    run.CompletedAtUtc = now;
                    run.SessionStatus = "Completed"; // Browser session closed cleanly
                    run.ComputedStatus = run.Failed > 0 ? "Failed" : "Passed"; // Test outcome
                    run.Status = run.Failed > 0 ? "Failed" : "Passed"; // Keep legacy field in sync
                    await resultsStore.UpsertRunAsync(run);
                    await resultsHubCtx.Clients.Group($"run:{runId2}").CommandLogChunk(new[] { ev });
                    await resultsHubCtx.Clients.Group($"run:{runId2}").RunUpdate(run);

                    // Clear browserId->runId/testId mapping on return
                    try { await db.KeyDeleteAsync(RedisKeys.BrowserRun(browserId)); }
                    catch { }

                    try { await db.KeyDeleteAsync(RedisKeys.BrowserTest(browserId)); }
                    catch { }

                    // Propagate correlation id back to caller
                    try { req.HttpContext.Response.Headers["Correlation-Id"] = runId2!; }
                    catch { }
                }

                // Always cleanup persisted session state/lease keys on return
                try { await db.KeyDeleteAsync(RedisKeys.BorrowTtl(browserId)); }
                catch { }

                try { await db.KeyDeleteAsync($"borrow_idle:{browserId}"); }
                catch { }

                try { await db.KeyDeleteAsync($"session:{browserId}"); }
                catch { }

                var respOk = new { returned = browserId };
                if (!string.IsNullOrWhiteSpace(returnIdempotencyKey))
                {
                    try
                    {
                        var idemJson = JsonSerializer.Serialize(respOk);
                        await db.StringSetAsync(RedisKeys.ReturnIdempotency(returnIdempotencyKey!), idemJson,
                            TimeSpan.FromSeconds(borrowIdempTtlSeconds));
                        await db.KeyDeleteAsync(RedisKeys.ReturnIdempotencyPending(returnIdempotencyKey!));
                    }
                    catch { }
                }

                return Results.Ok(respOk);
            })
            .WithOpenApi(op =>
            {
                op.Summary = "Return a borrowed browser";
                op.Description =
                    "Returns a borrowed browser back to the pool. Requires API key authentication via Bearer token.";
                op.Parameters ??= new List<OpenApiParameter>();
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "runId",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Optional run identifier used for attributing logs",
                    Schema = new OpenApiSchema { Type = "string" }
                });
                op.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Example = new OpenApiObject
                            {
                                ["labelKey"] = new OpenApiString("AppB:Chromium:UAT"),
                                ["browserId"] = new OpenApiString("b-123")
                            }
                        }
                    }
                };
                op.Responses["200"] = new OpenApiResponse
                {
                    Description = "Return successful",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Example = new OpenApiObject { ["returned"] = new OpenApiString("b-123") }
                        }
                    }
                };
                op.Responses["401"] =
                    new OpenApiResponse { Description = "Unauthorized (missing or invalid x-hub-secret)" };
                op.Responses["400"] =
                    new OpenApiResponse { Description = "Bad Request (missing labelKey or browserId)" };
                op.Security = new List<OpenApiSecurityRequirement>
                {
                    new()
                    {
                        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "HubSecret" } }
                        ] = new List<string>()
                    }
                };
                return op;
            });

        // SignalR Hubs
        app.MapHub<PoolHub>("/ws");
        app.MapHub<ResultsHub>("/results-ws");
        app.MapHub<LaunchesHub>("/launches-ws");

        app.MapGet("/", () => Results.Redirect("/dashboard"));
        app.MapGet("/dashboard", () => Results.Redirect(dashboardUrl));

        // Results HTTP endpoints
        app.MapGet("/results", async (HttpRequest req) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 100 : take;
            var status = req.Query["status"].FirstOrDefault();
            var appQ = req.Query["app"].FirstOrDefault();
            var browser = req.Query["browser"].FirstOrDefault();
            var env = req.Query["env"].FirstOrDefault();

            // Optional server-driven sorting
            var sortBy = req.Query["sortBy"].FirstOrDefault(); // supported: runName | startedAt (default)
            var sortDir = req.Query["sortDir"].FirstOrDefault(); // asc | desc
            var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ||
                       string.IsNullOrWhiteSpace(sortDir);

            if (string.Equals(sortBy, "runName", StringComparison.OrdinalIgnoreCase))
            {
                // To ensure correct paging when sorting by RunName, we need to sort before paging.
                // We enumerate runs in storage order (by StartedAtUtc desc) and collect all, then sort by RunName.
                // This is acceptable for typical dashboard scale and keeps store interface unchanged.
                var all = new List<ResultRunSummaryDto>(take);
                var offset = 0;
                const int pageSize = 500;
                while (true)
                {
                    var page = await resultsStore.GetRunsAsync(offset, pageSize, status, appQ, browser, env)
                        .ConfigureAwait(false);
                    if (page.Count == 0)
                    {
                        break;
                    }

                    all.AddRange(page);
                    if (page.Count < pageSize)
                    {
                        break;
                    }

                    offset += page.Count;
                }

                // Sort by normalized RunName (null/empty last), fallback to RunId for ties
                static string? Key(ResultRunSummaryDto r)
                {
                    return string.IsNullOrWhiteSpace(r.RunName) ? null : r.RunName;
                }

                IEnumerable<ResultRunSummaryDto> ordered = all
                    .OrderBy(r => Key(r) is null) // false (has name) first
                    .ThenBy(r => Key(r), StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(r => r.StartedAtUtc); // stable fallback

                if (desc)
                {
                    // Reverse direction on the RunName key while keeping nulls last
                    ordered = all
                        .OrderBy(r => Key(r) is null)
                        .ThenByDescending(r => Key(r), StringComparer.OrdinalIgnoreCase)
                        .ThenByDescending(r => r.StartedAtUtc);
                }

                var pageOut = ordered.Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500)).ToList();
                return Results.Ok(pageOut);
            }

            // Default behavior: storage-driven order (StartedAtUtc desc)
            var runs = await resultsStore.GetRunsAsync(skip, take, status, appQ, browser, env);
            return Results.Ok(runs);
        });

        app.MapGet("/results/count", async (HttpRequest req) =>
        {
            var status = req.Query["status"].FirstOrDefault();
            var appQ = req.Query["app"].FirstOrDefault();
            var browser = req.Query["browser"].FirstOrDefault();
            var env = req.Query["env"].FirstOrDefault();
            var count = await resultsStore.GetRunsCountAsync(status, appQ, browser, env);
            return Results.Ok(new { count });
        });

        app.MapGet("/results/{runId}", async (string runId, HttpContext httpContext) =>
        {
            var run = await resultsStore.GetRunAsync(runId);
            if (run is null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Run {runId} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            return Results.Ok(run);
        });

        // Tests endpoint
        app.MapGet("/results/{runId}/tests", async (HttpRequest req, string runId) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 200 : take;
            var status = req.Query["status"].FirstOrDefault();
            var items = await resultsStore.GetTestsAsync(runId, skip, take, status);
            return Results.Ok(items);
        });

        app.MapGet("/results/{runId}/commands", async (HttpRequest req, string runId) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 200 : take;
            var items = await resultsStore.GetCommandsAsync(runId, skip, take);
            return Results.Ok(items);
        });

        app.MapGet("/results/{runId}/commands/count", async (string runId) =>
        {
            var count = await resultsStore.GetCommandCountAsync(runId);
            return Results.Ok(new { count });
        });

        // Export run details for external archiving (JSON/NDJSON)
        app.MapGet("/results/{runId}/export", async (HttpRequest req, string runId) =>
        {
            var format = req.Query["format"].FirstOrDefault()?.ToLowerInvariant() ?? "json";
            var run = await resultsStore.GetRunAsync(runId);
            if (run is null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Run {runId} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (format == "ndjson")
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
                var sb = new StringBuilder();
                sb.Append(JsonSerializer.Serialize(new { type = "run", run }, opts));
                sb.Append('\n');

                var skip = 0;
                const int pageSize = 500;
                while (true)
                {
                    var tests = await resultsStore.GetTestsAsync(runId, skip, pageSize).ConfigureAwait(false);
                    if (tests.Count == 0)
                    {
                        break;
                    }

                    foreach (var t in tests)
                    {
                        sb.Append(JsonSerializer.Serialize(new { type = "test", test = t }, opts));
                        sb.Append('\n');
                    }

                    skip += tests.Count;
                    if (tests.Count < pageSize)
                    {
                        break;
                    }
                }

                var cskip = 0;
                const int cpage = 1000;
                while (true)
                {
                    var commands = await resultsStore.GetCommandsAsync(runId, cskip, cpage).ConfigureAwait(false);
                    if (commands.Count == 0)
                    {
                        break;
                    }

                    foreach (var c in commands)
                    {
                        sb.Append(JsonSerializer.Serialize(new { type = "command", command = c }, opts));
                        sb.Append('\n');
                    }

                    cskip += commands.Count;
                    if (commands.Count < cpage)
                    {
                        break;
                    }
                }

                return Results.Text(sb.ToString(), "application/x-ndjson");
            }

            if (format == "json")
            {
                var allTests = new List<ResultTestCaseDto>(256);
                var skip = 0;
                const int pageSize = 500;
                while (true)
                {
                    var tests = await resultsStore.GetTestsAsync(runId, skip, pageSize);
                    if (tests.Count == 0)
                    {
                        break;
                    }

                    allTests.AddRange(tests);
                    skip += tests.Count;
                    if (tests.Count < pageSize)
                    {
                        break;
                    }
                }

                var allCommands = new List<CommandLogEventDto>(1024);
                var cskip = 0;
                const int cpage = 1000;
                while (true)
                {
                    var commands = await resultsStore.GetCommandsAsync(runId, cskip, cpage);
                    if (commands.Count == 0)
                    {
                        break;
                    }

                    allCommands.AddRange(commands);
                    cskip += commands.Count;
                    if (commands.Count < cpage)
                    {
                        break;
                    }
                }

                return Results.Ok(new { run, tests = allTests, commands = allCommands });
            }

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["format"] = ["Invalid format. Allowed: json, ndjson"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: req.Path,
                traceId: req.HttpContext.TraceIdentifier);
        });

        // Audit retrieval (admin)
        app.MapGet("/audit", async (HttpRequest req) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 100 : take;
            var category = req.Query["category"].FirstOrDefault();
            var action = req.Query["action"].FirstOrDefault();
            DateTime? sinceUtc = null;
            var sinceStr = req.Query["sinceUtc"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sinceStr) && DateTime.TryParse(sinceStr, null,
                    DateTimeStyles.RoundtripKind, out var dt))
            {
                sinceUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            var items = await auditQueryStore.QueryAsync(skip, take, category, action, sinceUtc);
            return Results.Ok(items);
        });

        // Audit retrieval NDJSON export (admin)
        app.MapGet("/audit.ndjson", async (HttpRequest req) =>
        {
            int.TryParse(req.Query["skip"], out var skip);
            int.TryParse(req.Query["take"], out var take);
            take = take == 0 ? 100 : take;
            var category = req.Query["category"].FirstOrDefault();
            var action = req.Query["action"].FirstOrDefault();
            DateTime? sinceUtc = null;
            var sinceStr = req.Query["sinceUtc"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sinceStr) && DateTime.TryParse(sinceStr, null,
                    DateTimeStyles.RoundtripKind, out var dt))
            {
                sinceUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            var sb = new StringBuilder(4096);
            var page = Math.Clamp(take, 1, 1000);
            var sk = Math.Max(0, skip);
            while (true)
            {
                var items = await auditQueryStore.QueryAsync(sk, page, category, action, sinceUtc);
                if (items.Count == 0)
                {
                    break;
                }

                foreach (var e in items)
                {
                    sb.Append(JsonSerializer.Serialize(e));
                    sb.Append('\n');
                }

                sk += items.Count;
                if (items.Count < page)
                {
                    break;
                }
            }

            return Results.Text(sb.ToString(), "application/x-ndjson");
        });

        // Admin: mark run stopped and attempt to release any borrowed browsers for this run
        app.MapPost("/results/{runId}/stop", async (HttpRequest req, string runId,
            [FromServices] IEventPublisher eventPublisher) =>
        {
            var now = DateTime.UtcNow;
            var released = 0;

            // Try to find borrowed browser(s) and their labelKey via stored commands
            var commands = await resultsStore.GetCommandsAsync(runId, 0, 5000);
            var candidates = new List<(string labelKey, string browserId)>();
            foreach (var cmd in commands)
            {
                if (!string.Equals(cmd.Kind, "ServerLaunch", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (cmd.Props is null)
                {
                    continue;
                }

                if (!cmd.Props.TryGetValue("browserId", out var bid) || string.IsNullOrWhiteSpace(bid))
                {
                    continue;
                }

                // Prefer matchedLabel (actual) then labelKey
                var labelKey = cmd.Props.TryGetValue("matchedLabel", out var ml) && !string.IsNullOrWhiteSpace(ml)
                    ? ml
                    : cmd.Props.TryGetValue("labelKey", out var lk)
                        ? lk ?? string.Empty
                        : string.Empty;
                if (string.IsNullOrWhiteSpace(labelKey))
                {
                    continue;
                }

                candidates.Add((labelKey, bid));
            }

            foreach (var (labelKey, browserId) in candidates.Distinct())
            {
                try
                {
                    var inuseKey = RedisKeys.InUse(labelKey);
                    var availKey = RedisKeys.Available(labelKey);
                    var res = await db.ScriptEvaluateAsync(luaReturn, new RedisKey[] { inuseKey, availKey },
                        new RedisValue[] { browserId });
                    if (!res.IsNull)
                    {
                        released++;
                        // Fair queue: decrement in-flight and signal waiters for this label
                        try { EndpointCapacityQueue.OnFinished(labelKey); }
                        catch { }

                        try { EndpointCapacityQueue.Signal(labelKey); }
                        catch { }

                        // clean mappings
                        try { await db.KeyDeleteAsync(RedisKeys.BrowserRun(browserId)); }
                        catch { }

                        try { await db.KeyDeleteAsync(RedisKeys.BrowserTest(browserId)); }
                        catch { }
                    }

                    // request sidecar recycle on the worker (idempotent, short TTL)
                    try { await db.StringSetAsync(RedisKeys.Recycle(browserId), "1", TimeSpan.FromMinutes(2)); }
                    catch { }
                }
                catch { }
            }

            // Update run status
            var run = await resultsStore.GetRunAsync(runId) ?? new ResultRunSummaryDto
            {
                RunId = runId,
                StartedAtUtc = now
            };
            run.SessionStatus = "Stopped"; // Browser session manually stopped
            run.ComputedStatus = "Cancelled"; // Test execution cancelled
            run.Status = "Stopped"; // Keep legacy field in sync
            run.CompletedAtUtc = now;
            await resultsStore.UpsertRunAsync(run);

            // Log and broadcast
            var ev = new CommandLogEventDto
            {
                RunId = runId,
                TimestampUtc = now,
                Kind = "Stop",
                Message = $"Run marked as Stopped. Released {released} borrow(s).",
                Props = new Dictionary<string, string>()
            };

            // Publish command event for ingestion service to process
            try
            {
                var cmdEvt = new CommandEvent
                {
                    EventType = "CommandAppended",
                    RunId = runId,
                    TimestampUtc = now,
                    DataJson = JsonSerializer.Serialize(new
                    {
                        kind = "Stop",
                        message = $"Run marked as Stopped. Released {released} borrow(s).",
                        propsJson = JsonSerializer.Serialize(new Dictionary<string, string>())
                    }),
                    CorrelationId = Guid.NewGuid().ToString()
                };
                await eventPublisher.PublishCommandEventAsync(cmdEvt);
            }
            catch (Exception ex)
            {
                chunkedLogger.LogWarning(ex, null, "[Stop] Failed to publish command event for runId {RunId}", runId);
            }

            await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(new[] { ev });
            await resultsHubCtx.Clients.Group($"run:{runId}").RunUpdate(run);

            // Audit admin stop action
            try
            {
                var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "run.stop",
                    RemoteIp = remoteIp,
                    Details = new Dictionary<string, string>
                    {
                        ["runId"] = runId,
                        ["released"] = released.ToString()
                    }
                });
            }
            catch { }

            return Results.Ok(new { stopped = runId, released });
        });

        // Admin: delete a run (only if not Running/InProgress)
        app.MapDelete("/results/{runId}", async (HttpRequest req, string runId) =>
        {
            var run = await resultsStore.GetRunAsync(runId);
            if (run is null)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Run {runId} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var status = run.Status ?? string.Empty;
            if (string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                return ProblemDetailsHelpers.Conflict(
                    "Run is Running/InProgress and cannot be deleted.",
                    eventCode: EventCodes.TestItem.TestItemOperationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var deleted = await resultsStore.DeleteRunAsync(runId);
            if (!deleted)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Run {runId} not found",
                    eventCode: EventCodes.TestItem.TestItemNotFound,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Audit admin delete action
            try
            {
                var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "run.delete",
                    Actor = "dashboard",
                    RemoteIp = remoteIp,
                    Details = new Dictionary<string, string> { ["runId"] = runId, ["status"] = status }
                });
            }
            catch { }

            return Results.NoContent();
        });

        // Admin: bulk delete non-Running runs matching filters
        app.MapPost("/results/delete", async (HttpRequest req) =>
        {
            var status = req.Query["status"].FirstOrDefault();
            var appQ = req.Query["app"].FirstOrDefault();
            var browser = req.Query["browser"].FirstOrDefault();
            var env = req.Query["env"].FirstOrDefault();
            var exclude = req.Query["exclude"].FirstOrDefault();
            var excludeRunning = string.Equals(exclude, "running", StringComparison.OrdinalIgnoreCase);

            // Collect matching run ids first to avoid paging shifts while deleting
            var take = 500;
            var skip = 0;
            var all = new List<ResultRunSummaryDto>(1024);
            while (true)
            {
                var page = await resultsStore.GetRunsAsync(skip, take, status, appQ, browser, env);
                if (page.Count == 0)
                {
                    break;
                }

                all.AddRange(page);
                if (page.Count < take)
                {
                    break;
                }

                skip += page.Count;
            }

            var considered = all.Count;
            var deleted = 0;
            var skipped = 0;
            foreach (var r in all)
            {
                var st = r.Status ?? string.Empty;
                if (string.Equals(st, "Running", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(st, "InProgress", StringComparison.OrdinalIgnoreCase))
                {
                    // Always skip running; this also covers exclude=running semantics
                    skipped++;
                    continue;
                }

                try
                {
                    var ok = await resultsStore.DeleteRunAsync(r.RunId);
                    if (ok)
                    {
                        deleted++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch { skipped++; }
            }

            // Audit bulk delete
            try
            {
                var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "runs.delete.bulk",
                    Actor = "dashboard",
                    RemoteIp = remoteIp,
                    Details = new Dictionary<string, string>
                    {
                        ["status"] = status ?? string.Empty,
                        ["app"] = appQ ?? string.Empty,
                        ["browser"] = browser ?? string.Empty,
                        ["env"] = env ?? string.Empty,
                        ["exclude"] = exclude ?? string.Empty,
                        ["considered"] = considered.ToString(),
                        ["deleted"] = deleted.ToString(),
                        ["skipped"] = skipped.ToString()
                    }
                });
            }
            catch { }

            return Results.Ok(new { considered, deleted, skipped });
        });

        // Admin: restart all pre-warmed browsers for a pool (available and in-use)
        app.MapPost("/admin/pools/{labelKey}/restart", async (HttpRequest req, string labelKey) =>
        {
            // Validate and normalize the route labelKey
            if (!LabelKey.TryParseDetailed(labelKey, out var parsedAdmin, out var adminErr,
                    new LabelKeyParsingOptions { EnforceBrowserSecond = false }))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["labelKey"] = [$"invalid labelKey: {adminErr}"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            labelKey = parsedAdmin!.Normalized;

            // Enter maintenance and snapshot current counts
            var availKey = RedisKeys.Available(labelKey);
            var inuseKey = RedisKeys.InUse(labelKey);
            var availLen = await db.ListLengthAsync(availKey);
            var inuseLen = await db.ListLengthAsync(inuseKey);
            var target = availLen + inuseLen;
            var ttl = TimeSpan.FromMinutes(30);
            try
            {
                await db.StringSetAsync(RedisKeys.MaintenanceFlag(labelKey), "1", ttl);
                await db.StringSetAsync(RedisKeys.MaintenanceTarget(labelKey),
                    target.ToString(CultureInfo.InvariantCulture),
                    ttl);
                await db.StringSetAsync(RedisKeys.MaintenanceSnapAvail(labelKey),
                    availLen.ToString(CultureInfo.InvariantCulture), ttl);
                await db.StringSetAsync(RedisKeys.MaintenanceSnapInuse(labelKey),
                    inuseLen.ToString(CultureInfo.InvariantCulture), ttl);
                await db.StringSetAsync(RedisKeys.MaintenanceSince(labelKey),
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ttl);
            }
            catch { }

            var scheduled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in new[] { availKey, inuseKey })
            {
                try
                {
                    var items = await db.ListRangeAsync(key);
                    foreach (var item in items)
                    {
                        var s = item.ToString();
                        var m = Regex.Match(s, "\"browserId\":\"(?<id>[^\"]+)\"");
                        if (m.Success)
                        {
                            var bid = m.Groups["id"].Value;
                            if (scheduled.Add(bid))
                            {
                                try { await db.StringSetAsync(RedisKeys.Recycle(bid), "1", TimeSpan.FromMinutes(2)); }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            try
            {
                var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "pool.restart",
                    Actor = "dashboard",
                    RemoteIp = remoteIp,
                    Details = new Dictionary<string, string>
                    {
                        ["labelKey"] = labelKey,
                        ["scheduled"] = scheduled.Count.ToString(),
                        ["avail"] = availLen.ToString(),
                        ["inuse"] = inuseLen.ToString()
                    }
                });
            }
            catch { }

            return Results.Ok(new
            {
                labelKey,
                scheduled = scheduled.Count,
                maintenance = new { active = true, target, avail = availLen, inuse = inuseLen }
            });
        });

        // Admin: orchestrate sidecar upgrade on one or all nodes via Redis trigger
        app.MapPost("/admin/nodes/{nodeId}/sidecar/upgrade", async (HttpRequest req, string nodeId) =>
        {
            var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var targets = new List<string>();
            try
            {
                if (string.Equals(nodeId, "all", StringComparison.OrdinalIgnoreCase))
                {
                    var all = await db.SetMembersAsync("nodes");
                    foreach (var v in all)
                    {
                        var s = v.ToString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            targets.Add(s);
                        }
                    }
                }
                else
                {
                    targets.Add(nodeId);
                }

                var ttl = TimeSpan.FromMinutes(5);
                var scheduled = 0;
                foreach (var nid in targets.Distinct(StringComparer.Ordinal))
                {
                    try
                    {
                        await db.StringSetAsync(RedisKeys.NodeUpgrade(nid), "1", ttl);
                        scheduled++;
                    }
                    catch { }
                }

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "node.sidecar.upgrade",
                        Actor = "dashboard",
                        RemoteIp = remoteIp,
                        Details = new Dictionary<string, string>
                        {
                            ["requested"] = nodeId,
                            ["scheduled"] = scheduled.ToString(),
                            ["targets"] = string.Join(',', targets)
                        }
                    });
                }
                catch { }

                return Results.Ok(new { scheduled = targets.Count, targets });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[admin] node sidecar upgrade trigger failed for {NodeId}", nodeId);
                return ProblemDetailsHelpers.InternalServerError(
                    "failed to schedule upgrade",
                    eventCode: EventCodes.Node.RegistrationFailed, // Or generic
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Test attribution from client: set current testId for a browserId
        app.MapPost("/results/browser/{browserId}/test", async (HttpRequest req, string browserId) =>
        {
            var body = await req.ReadFromJsonAsync<Dictionary<string, string?>>() ?? new Dictionary<string, string?>();
            if (!body.TryGetValue("testId", out var testId) || string.IsNullOrWhiteSpace(testId))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["testId"] = ["missing testId"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Ensure browser->run mapping if provided in body and missing in Redis
            var runMapKey = RedisKeys.BrowserRun(browserId);
            var runIdVal = await db.StringGetAsync(runMapKey);
            if (runIdVal.IsNullOrEmpty && body.TryGetValue("runId", out var providedRunId) &&
                !string.IsNullOrWhiteSpace(providedRunId))
            {
                await db.StringSetAsync(runMapKey, providedRunId!, TimeSpan.FromHours(6));
                runIdVal = providedRunId;
            }

            // Map current testId and TTL. Mapping is per-browser session; it's also cleared on return.
            await db.StringSetAsync(RedisKeys.BrowserTest(browserId), testId!, TimeSpan.FromHours(6));

            // If we can attribute to a run, persist and broadcast a TestUpdate for nicer UI
            if (!runIdVal.IsNullOrEmpty)
            {
                var runId = runIdVal.ToString();
                var title = body.GetValueOrDefault("title") ?? testId;
                var file = body.GetValueOrDefault("file") ?? string.Empty;
                var project = body.GetValueOrDefault("project");
                var status = body.GetValueOrDefault("status") ?? "Running";

                var dto = new ResultTestCaseDto
                {
                    RunId = runId!,
                    TestId = testId!,
                    Title = title ?? string.Empty,
                    File = file,
                    Project = project,
                    Status = status,
                    DurationMs = 0
                };
                await resultsStore.UpsertTestAsync(dto);
                await resultsHubCtx.Clients.Group($"run:{runId}").TestUpdate(dto);
            }

            return Results.Ok(new { mapped = browserId, testId });
        });

        // Worker-sourced Playwright command logs by browserId
        app.MapPost("/results/browser/{browserId}/commands", async (HttpRequest req, string browserId,
            [FromServices] IEventPublisher eventPublisher,
            [FromServices] IConfiguration configuration) =>
        {
            if (!CheckSecret(req, "x-hub-secret", hubNodeSecret))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Unauthorized",
                    eventCode: EventCodes.BrowserPool.ReturnFailed, // Generic for browser results
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var runIdVal = await db.StringGetAsync(RedisKeys.BrowserRun(browserId));
            if (runIdVal.IsNullOrEmpty)
            {
                return Results.Accepted(); // No run attribution yet
            }

            var runId = runIdVal.ToString();

            // Get current test attribution (if any)
            RedisValue currentTestVal;
            try { currentTestVal = await db.StringGetAsync(RedisKeys.BrowserTest(browserId)); }
            catch { currentTestVal = RedisValue.Null; }

            var currentTestId = currentTestVal.IsNullOrEmpty ? null : currentTestVal.ToString();

            JsonDocument? doc = null;
            try { doc = await JsonDocument.ParseAsync(req.Body); }
            catch
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["invalid JSON"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            using var _ = doc;

            var list = new List<CommandLogEventDto>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var (text, dir, ts) = ExtractLog(el);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    list.Add(ToEvent(runId, browserId, currentTestId, text, dir, ts, "worker"));
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("items", out var itemsEl) &&
                    itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsEl.EnumerateArray())
                    {
                        var (text, dir, ts) = ExtractLog(el);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        list.Add(ToEvent(runId, browserId, currentTestId, text, dir, ts, "worker"));
                    }
                }
                else
                {
                    var (text, dir, ts) = ExtractLog(doc.RootElement);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(ToEvent(runId, browserId, currentTestId, text, dir, ts, "worker"));
                    }
                }
            }

            if (list.Count == 0)
            {
                return Results.NoContent();
            }

            var enablePublisher = configuration.GetValue("ENABLE_EVENT_PUBLISHER", false);
            if (enablePublisher)
            {
                foreach (var ev in list)
                {
                    try
                    {
                        var cmdEvt = new CommandEvent
                        {
                            EventType = "CommandAppended",
                            RunId = ev.RunId,
                            DataJson = JsonSerializer.Serialize(ev),
                            TimestampUtc = ev.TimestampUtc,
                            CorrelationId = Guid.NewGuid().ToString()
                        };
                        await eventPublisher.PublishCommandEventAsync(cmdEvt);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to publish command event, falling back to direct DB write");
                        await resultsStore.AppendCommandAsync(ev);
                    }
                }
            }
            else
            {
                foreach (var ev in list)
                {
                    await resultsStore.AppendCommandAsync(ev);
                }
            }

            await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(list.ToArray());
            return Results.Ok(new { accepted = list.Count });

            static (string text, string? dir, DateTime? ts) ExtractLog(JsonElement el)
            {
                try
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        return (el.GetString() ?? string.Empty, null, null);
                    }

                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        string? text = null, dir = null;
                        DateTime? ts = null;
                        if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            text = t.GetString();
                        }

                        if (el.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String)
                        {
                            dir = d.GetString();
                        }

                        if (el.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(tsEl.GetString(), out var parsed))
                        {
                            ts = parsed;
                        }

                        return (text ?? string.Empty, dir, ts);
                    }
                }
                catch { }

                return (string.Empty, null, null);
            }

            static CommandLogEventDto ToEvent(string runId, string browserId, string? testId, string text, string? dir,
                DateTime? ts, string source)
            {
                if (text.Length > 4000)
                {
                    text = text.Substring(0, 4000);
                }

                return new CommandLogEventDto
                {
                    RunId = runId,
                    TimestampUtc = ts ?? DateTime.UtcNow,
                    Kind = "PwProtocol",
                    Message = text,
                    Props = new Dictionary<string, string>
                    {
                        ["browserId"] = browserId,
                        ["direction"] = string.IsNullOrWhiteSpace(dir) ? "" : dir,
                        ["source"] = source
                    },
                    TestId = string.IsNullOrWhiteSpace(testId) ? null : testId
                };
            }
        });

        // Runner-sourced Playwright API/protocol logs (pw:api) by browserId
        // Accepts API key authentication so test runners can forward their client-side logs
        app.MapPost("/results/browser/{browserId}/api-logs", async (HttpRequest req, string browserId,
            [FromServices] IEventPublisher eventPublisher,
            [FromServices] IConfiguration configuration) =>
        {
            var runIdVal = await db.StringGetAsync(RedisKeys.BrowserRun(browserId));
            if (runIdVal.IsNullOrEmpty)
            {
                return Results.Accepted();
            }

            var runId = runIdVal.ToString();

            RedisValue currentTestVal;
            try { currentTestVal = await db.StringGetAsync(RedisKeys.BrowserTest(browserId)); }
            catch { currentTestVal = RedisValue.Null; }

            var currentTestId = currentTestVal.IsNullOrEmpty ? null : currentTestVal.ToString();

            JsonDocument? doc;
            try { doc = await JsonDocument.ParseAsync(req.Body); }
            catch
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["invalid JSON"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            using var _2 = doc;

            var list = new List<CommandLogEventDto>();
            switch (doc.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var (text, dir, ts) = ExtractRunnerLog(el);
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                continue;
                            }

                            list.Add(ToRunnerEvent(runId, browserId, currentTestId, text, dir, ts));
                        }

                        break;
                    }
                case JsonValueKind.Object when doc.RootElement.TryGetProperty("items", out var itemsEl) &&
                                               itemsEl.ValueKind == JsonValueKind.Array:
                    {
                        foreach (var el in itemsEl.EnumerateArray())
                        {
                            var (text, dir, ts) = ExtractRunnerLog(el);
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                continue;
                            }

                            list.Add(ToRunnerEvent(runId, browserId, currentTestId, text, dir, ts));
                        }

                        break;
                    }
                case JsonValueKind.Object:
                    {
                        var (text, dir, ts) = ExtractRunnerLog(doc.RootElement);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            list.Add(ToRunnerEvent(runId, browserId, currentTestId, text, dir, ts));
                        }

                        break;
                    }
            }

            if (list.Count == 0)
            {
                return Results.NoContent();
            }

            var enablePublisher = configuration.GetValue("ENABLE_EVENT_PUBLISHER", false);
            if (enablePublisher)
            {
                foreach (var ev in list)
                {
                    try
                    {
                        var cmdEvt = new CommandEvent
                        {
                            EventType = "CommandAppended",
                            RunId = ev.RunId,
                            DataJson = JsonSerializer.Serialize(ev),
                            TimestampUtc = ev.TimestampUtc,
                            CorrelationId = Guid.NewGuid().ToString()
                        };
                        await eventPublisher.PublishCommandEventAsync(cmdEvt);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to publish command event, falling back to direct DB write");
                        await resultsStore.AppendCommandAsync(ev);
                    }
                }
            }
            else
            {
                foreach (var ev in list)
                {
                    await resultsStore.AppendCommandAsync(ev);
                }
            }

            await resultsHubCtx.Clients.Group($"run:{runId}").CommandLogChunk(list.ToArray());
            return Results.Ok(new { accepted = list.Count });

            static (string text, string? dir, DateTime? ts) ExtractRunnerLog(JsonElement el)
            {
                try
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        return (el.GetString() ?? string.Empty, null, null);
                    }

                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        string? text = null;
                        DateTime? ts = null;
                        string? dir = null;
                        if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            text = t.GetString();
                        }

                        if (el.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(tsEl.GetString(), out var parsed))
                        {
                            ts = parsed;
                        }

                        if (el.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String)
                        {
                            dir = d.GetString();
                        }

                        return (text ?? string.Empty, dir, ts);
                    }
                }
                catch { }

                return (string.Empty, null, null);
            }

            static CommandLogEventDto ToRunnerEvent(string runId, string browserId, string? testId, string text,
                string? dir, DateTime? ts)
            {
                if (text.Length > 4000)
                {
                    text = text.Substring(0, 4000);
                }

                return new CommandLogEventDto
                {
                    RunId = runId,
                    TimestampUtc = ts ?? DateTime.UtcNow,
                    Kind = "PwProtocol",
                    Message = text,
                    Props = new Dictionary<string, string>
                    {
                        ["browserId"] = browserId,
                        ["direction"] = string.IsNullOrWhiteSpace(dir) ? "" : dir,
                        ["source"] = "runner"
                    },
                    TestId = string.IsNullOrWhiteSpace(testId) ? null : testId
                };
            }
        });

        // Demo seeding endpoint to quickly generate a sample run
        app.MapGet("/results/demo", async () =>
        {
            var runId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var run = new ResultRunSummaryDto
            {
                RunId = runId,
                App = "demo-app",
                Browser = "Chromium",
                Env = "local",
                Region = "",
                OS = RuntimeInformation.OSDescription,
                Status = "Running",
                TotalTests = 3,
                Passed = 2,
                Failed = 1,
                StartedAtUtc = now,
                WorkerNodeId = "demo-node",
                PlaywrightVersion = "1.45.0",
                BrowserVersion = "117.0"
            };

            await resultsStore.UpsertRunAsync(run);

            var evs = new List<CommandLogEventDto>
            {
                new()
                {
                    RunId = runId,
                    TimestampUtc = now,
                    Kind = "ServerLaunch",
                    Message = "Playwright server started",
                    Props =
                        new Dictionary<string, string>
                        {
                            ["wsEndpoint"] = "ws://demo/ws", ["args"] = "--disable-gpu --no-sandbox"
                        }
                },
                new()
                {
                    RunId = runId,
                    TimestampUtc = now.AddSeconds(1),
                    Kind = "Borrow",
                    Message = "Borrowed Chromium endpoint",
                    Props =
                        new Dictionary<string, string>
                        {
                            ["labelKey"] = "demo:Chromium:local", ["browserId"] = "demo-browser-1"
                        }
                },
                new()
                {
                    RunId = runId,
                    TimestampUtc = now.AddSeconds(5),
                    Kind = "Return",
                    Message = "Returned Chromium endpoint",
                    Props = new Dictionary<string, string>
                    {
                        ["labelKey"] = "demo:Chromium:local", ["browserId"] = "demo-browser-1"
                    }
                }
            };

            foreach (var e in evs)
            {
                await resultsStore.AppendCommandAsync(e);
            }

            // Broadcast to any connected dashboard clients
            await resultsHubCtx.Clients.All.RunUpdate(run);
            await resultsHubCtx.Clients.All.CommandLogChunk(evs.ToArray());

            return Results.Ok(new { runId });
        });

        // Diagnostics endpoint (requires runner secret)
        app.MapGet("/diagnostics", async (HttpRequest req) =>
        {
            var reader = services.GetRequiredService<IPoolStateReader>();
            var state = await reader.GetStateAsync();

            static string GetInformationalVersion(Type t)
            {
                try
                {
                    var asm = t.Assembly;
                    var aiv = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    return aiv?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            var ver = GetInformationalVersion(typeof(EndpointMappingExtensions));

            static string TruncVer(string? v)
            {
                const int max = 15; // "1.0.1-preview.3".Length
                if (string.IsNullOrEmpty(v))
                {
                    return v ?? string.Empty;
                }

                return v!.Length <= max ? v : v.Substring(0, max);
            }

            var verShort = TruncVer(ver);

            var dto = new HubDiagnosticsDto
            {
                HubConfig = new HubEffectiveConfigDto
                {
                    RedisUrl = config["REDIS_URL"] ?? "redis:6379",
                    BorrowTrailingFallback = enableTrailingFallback,
                    BorrowPrefixExpand = enablePrefixExpand,
                    BorrowWildcards = enableWildcards,
                    NodeTimeoutSeconds = nodeTimeoutSeconds,
                    NodeQuarantineSeconds = nodeQuarantineSeconds,
                    DashboardUrl = dashboardUrl,
                    Version = verShort
                },
                Workers = state.Workers,
                Now = DateTime.UtcNow
            };

            return Results.Ok(dto);
        });

        // Hub environment variables (masked); requires runner secret
        app.MapGet("/diagnostics/env", (HttpRequest req) =>
        {
            static bool IsSecret(string key)
            {
                key = key.ToUpperInvariant();
                return key.Contains("SECRET") || key.Contains("PASSWORD") || key.Contains("TOKEN");
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
            {
                var k = de.Key?.ToString();
                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                var v = de.Value?.ToString() ?? string.Empty;
                dict[k] = IsSecret(k) && !string.IsNullOrEmpty(v) ? "***" : v;
            }

            return Results.Ok(new { env = dict, now = DateTime.UtcNow });
        });

        // Proxy to worker environment variables via hub (runner secret required). Hub forwards node secret to worker.
        app.MapGet("/diagnostics/worker-env/{nodeId}", async (HttpRequest req, string nodeId) =>
        {
            try
            {
                var key = $"node:{nodeId}";
                var baseUrl = (await db.HashGetAsync(key, "BaseUrl")).ToString();
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    baseUrl = $"http://{nodeId}:5000";
                }

                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Remove("x-hub-secret");
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", hubNodeSecret);

                var url = $"{baseUrl.TrimEnd('/')}/diagnostics/env";
                var resp = await client.GetAsync(url);
                var text = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    return ProblemDetailsHelpers.StatusCode(
                        (int)resp.StatusCode,
                        "Worker diagnostics failed",
                        eventCode: EventCodes.Node.RegistrationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Pass-through payload from worker
                return Results.Text(text, "application/json");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting worker environment for nodeId {NodeId}", nodeId);
                return ProblemDetailsHelpers.InternalServerError(
                    ex.Message,
                    eventCode: EventCodes.Node.RegistrationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        app.MapGet("/nodes", () => Results.Ok(db.SetMembers("nodes").Select(x => x.ToString())));

        // Liveness + critical dependencies health: ensure Redis is reachable and responsive.
        app.MapGet("/health", async (HttpContext httpContext) =>
        {
            try
            {
                var timeoutMs = int.TryParse(config["REDIS_HEALTH_TIMEOUT_MS"], out var ms) ? Math.Max(100, ms) : 1000;
                var breakerCooldownMs = int.TryParse(config["REDIS_BREAKER_COOLDOWN_MS"], out var cd)
                    ? Math.Max(500, cd)
                    : 5000;

                // Circuit breaker: if open, fail fast
                var nowTicks = DateTime.UtcNow.Ticks;
                if (Interlocked.Read(ref _redisBreakerUntilTicks) > nowTicks)
                {
                    return ProblemDetailsHelpers.ServiceUnavailable(
                        "Redis",
                        "Circuit breaker is open",
                        eventCode: EventCodes.Redis.ConnectionError,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                if (!mux.IsConnected)
                {
                    var until = nowTicks + breakerCooldownMs * TimeSpan.TicksPerMillisecond;
                    Interlocked.Exchange(ref _redisBreakerUntilTicks, until);
                    Interlocked.Exchange(ref _redisConsecutiveFailures, 0);
                    return ProblemDetailsHelpers.ServiceUnavailable(
                        "Redis",
                        "Redis is not connected",
                        eventCode: EventCodes.Redis.ConnectionError,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                var sw = Stopwatch.StartNew();
                var pingTask = db.PingAsync();
                var completed = await Task.WhenAny(pingTask, Task.Delay(timeoutMs));
                if (completed != pingTask)
                {
                    return ProblemDetailsHelpers.ServiceUnavailable(
                        "Redis",
                        "Ping timeout",
                        eventCode: EventCodes.Redis.OperationFailed,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                await pingTask; // propagate any errors
                sw.Stop();

                // Success: reset failure counters and breaker
                Interlocked.Exchange(ref _redisConsecutiveFailures, 0);
                Interlocked.Exchange(ref _redisBreakerUntilTicks, 0);
                return Results.Ok(new { status = "ok", redis = new { pingMs = sw.ElapsedMilliseconds } });
            }
            catch (Exception ex)
            {
                var threshold = Interlocked.Increment(ref _redisConsecutiveFailures);
                var breakerThreshold =
                    int.TryParse(config["REDIS_BREAKER_THRESHOLD"], out var th2) ? Math.Max(1, th2) : 3;
                var breakerCooldownMs = int.TryParse(config["REDIS_BREAKER_COOLDOWN_MS"], out var cd2)
                    ? Math.Max(500, cd2)
                    : 5000;
                if (threshold >= breakerThreshold)
                {
                    var until = DateTime.UtcNow.Ticks + breakerCooldownMs * TimeSpan.TicksPerMillisecond;
                    Interlocked.Exchange(ref _redisBreakerUntilTicks, until);
                    Interlocked.Exchange(ref _redisConsecutiveFailures, 0);
                }

                return ProblemDetailsHelpers.ServiceUnavailable(
                    "Redis",
                    $"Unexpected error: {ex.Message}",
                    eventCode: EventCodes.Redis.OperationFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        });

        // Readiness: reflect capacity to serve borrows (workers registered and available slots)
        app.MapGet("/ready", async (HttpContext httpContext) =>
        {
            // During graceful shutdown, report not ready
            if (!_acceptingBorrows)
            {
                return ProblemDetailsHelpers.ServiceUnavailable(
                    "Hub",
                    "Hub is shutting down",
                    eventCode: EventCodes.Generic,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            try
            {
                var reader = services.GetRequiredService<IPoolStateReader>();
                var state = await reader.GetStateAsync();
                var total = state.Pools.Sum(p => p.Total);
                var borrowed = state.Pools.Sum(p => p.Borrowed);
                var available = Math.Max(0, total - borrowed);
                var activeWorkers = state.Workers.Count;

                var hasCapacity = activeWorkers > 0 && available > 0;
                if (!hasCapacity)
                {
                    return ProblemDetailsHelpers.ServiceUnavailable(
                        "Pool",
                        "No available workers or capacity",
                        eventCode: EventCodes.BrowserPool.BorrowFailed,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                return Results.Ok(new
                {
                    status = "ready",
                    workers = activeWorkers,
                    total,
                    borrowed,
                    available
                });
            }
            catch (Exception ex)
            {
                // Conservatively report not ready on unexpected errors
                return ProblemDetailsHelpers.ServiceUnavailable(
                    "Pool",
                    $"Unexpected error: {ex.Message}",
                    eventCode: EventCodes.BrowserPool.BorrowFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        });
    }

    private static bool CheckSecret(HttpRequest req, string header, string expected)
    {
        return req.Headers.TryGetValue(header, out var h) && h.FirstOrDefault() == expected;
    }
}
