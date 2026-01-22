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

using System.Diagnostics;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Caching;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Proactively prefetches artifact bytes into Redis cache when test items are loaded.
///     Improves user experience by eliminating first-access cache misses.
/// </summary>
public class ArtifactPrefetchService : IArtifactPrefetchService
{
    private readonly RedisArtifactCache? _cache;
    private readonly IConfiguration _config;
    private readonly bool _enabled;
    private readonly ILogger<ArtifactPrefetchService> _logger;
    private readonly int _maxConcurrency;
    private readonly int _maxPerItem;
    private readonly MinioStorageService? _minioStorage;
    private readonly IDatabase? _redis;
    private readonly IResultsStore _store;
    private readonly SemaphoreSlim _throttle;

    public ArtifactPrefetchService(
        IResultsStore store,
        RedisArtifactCache? cache,
        MinioStorageService? minioStorage,
        IConfiguration config,
        ILogger<ArtifactPrefetchService> logger,
        IDatabase? redis,
        bool enabled,
        int maxConcurrency,
        int maxPerItem)
    {
        _store = store;
        _cache = cache;
        _minioStorage = minioStorage;
        _config = config;
        _logger = logger;
        _redis = redis;
        _enabled = enabled;
        _maxConcurrency = maxConcurrency;
        _maxPerItem = maxPerItem;
        _throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>
    ///     Prefetch artifacts for a single test item.
    ///     Runs as a fire-and-forget background task.
    /// </summary>
    public async Task PrefetchArtifactsAsync(Guid testItemId, CancellationToken ct = default)
    {
        if (!_enabled || _cache == null)
        {
            _logger.LogDebug("Prefetch skipped: enabled={Enabled}, cache={HasCache}", _enabled, _cache != null);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await IncrementStatAsync("totalRequests");

            // Get artifact metadata for the test item
            var artifacts = await _store.GetArtifactsForTestAsync(testItemId.ToString(), "*");

            if (artifacts == null || artifacts.Count == 0)
            {
                _logger.LogDebug("No artifacts to prefetch for test item {ItemId}", testItemId);
                return;
            }

            // Filter cacheable artifacts (size < max threshold)
            var cacheable = artifacts
                .Where(a => _cache.ShouldCacheContent(a.Size))
                .Take(_maxPerItem)
                .ToList();

            if (cacheable.Count == 0)
            {
                _logger.LogDebug("No cacheable artifacts for test item {ItemId} (all exceed size limit)", testItemId);
                return;
            }

            _logger.LogDebug("Prefetching {Count}/{Total} artifacts for test item {ItemId}",
                cacheable.Count, artifacts.Count, testItemId);

            // Prefetch in parallel with throttling
            var tasks = cacheable.Select(artifact => PrefetchSingleArtifactAsync(artifact, ct));
            await Task.WhenAll(tasks);

            sw.Stop();
            await RecordPrefetchTimeAsync(sw.ElapsedMilliseconds);

            _logger.LogInformation("Prefetched {Count} artifacts for test item {ItemId} in {ElapsedMs}ms",
                cacheable.Count, testItemId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            await IncrementStatAsync("failed");
            _logger.LogWarning(ex, "Prefetch failed for test item {ItemId}", testItemId);
            // Swallow errors - prefetch is the best-effort
        }
    }

    /// <summary>
    ///     Prefetch artifacts for multiple test items (batch operation).
    /// </summary>
    public async Task PrefetchArtifactsForItemsAsync(List<Guid> testItemIds, CancellationToken ct = default)
    {
        if (!_enabled || _cache == null || testItemIds.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Batch prefetch for {Count} test items", testItemIds.Count);

        // Process items in parallel (with throttling via PrefetchArtifactsAsync)
        var tasks = testItemIds.Select(itemId => PrefetchArtifactsAsync(itemId, ct));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    ///     Get prefetch statistics from Redis.
    /// </summary>
    public async Task<PrefetchStatistics> GetStatisticsAsync()
    {
        if (_redis == null)
        {
            return new PrefetchStatistics(0, 0, 0, 0, 0, 0, 0);
        }

        try
        {
            var key = "prefetch:stats";
            var entries = await _redis.HashGetAllAsync(key);

            long totalRequests = 0, successful = 0, failed = 0, skipped = 0, bytes = 0;
            double avgTime = 0;

            foreach (var entry in entries)
            {
                if (entry.Name == "totalRequests" && long.TryParse(entry.Value, out var tr))
                {
                    totalRequests = tr;
                }
                else if (entry.Name == "successful" && long.TryParse(entry.Value, out var s))
                {
                    successful = s;
                }
                else if (entry.Name == "failed" && long.TryParse(entry.Value, out var f))
                {
                    failed = f;
                }
                else if (entry.Name == "skippedCached" && long.TryParse(entry.Value, out var sk))
                {
                    skipped = sk;
                }
                else if (entry.Name == "bytesPrefetched" && long.TryParse(entry.Value, out var b))
                {
                    bytes = b;
                }
                else if (entry.Name == "avgTimeMs" && double.TryParse(entry.Value, out var at))
                {
                    avgTime = at;
                }
            }

            var successRate = successful + failed > 0 ? (double)successful / (successful + failed) : 0;

            return new PrefetchStatistics(totalRequests, successful, failed, skipped, bytes, avgTime, successRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting prefetch statistics");
            return new PrefetchStatistics(0, 0, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>
    ///     Prefetch a single artifact.
    ///     Checks cache, loads from storage, stores in cache.
    /// </summary>
    private async Task PrefetchSingleArtifactAsync(TestAttachmentDto artifact, CancellationToken ct)
    {
        if (_cache == null)
        {
            return;
        }

        await _throttle.WaitAsync(ct); // Rate limiting
        try
        {
            // Check if already cached (avoid redundant work)
            var cached = await _cache.GetContentAsync(artifact.Id);
            if (cached != null)
            {
                await IncrementStatAsync("skippedCached");
                _logger.LogTrace("Artifact {Id} already cached, skipping prefetch", artifact.Id);
                return;
            }

            // Load from storage
            byte[] bytes;
            var storageBackend = _config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");

            if (storageBackend == "minio" && _minioStorage != null)
            {
                bytes = await _minioStorage.DownloadArtifactAsync(artifact.Path);
            }
            else
            {
                var baseStoragePath = _config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts") ??
                                      "./data/artifacts";
                var fullPath = Path.Combine(baseStoragePath, artifact.Path);

                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("Artifact file not found: {Path}", fullPath);
                    await IncrementStatAsync("failed");
                    return;
                }

                bytes = await File.ReadAllBytesAsync(fullPath, ct);
            }

            // Store in cache (will compress if enabled)
            await _cache.SetContentAsync(artifact.Id, bytes);
            await IncrementStatAsync("successful");
            await IncrementStatAsync("bytesPrefetched", bytes.Length);

            _logger.LogTrace("Prefetched artifact {Id} ({Size} bytes)", artifact.Id, bytes.Length);
        }
        catch (Exception ex)
        {
            await IncrementStatAsync("failed");
            _logger.LogWarning(ex, "Failed to prefetch artifact {Id}", artifact.Id);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    ///     Increment a counter-statistic in Redis.
    /// </summary>
    private async Task IncrementStatAsync(string field, long value = 1)
    {
        if (_redis == null)
        {
            return;
        }

        try
        {
            const string key = "prefetch:stats";
            await _redis.HashIncrementAsync(key, field, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error incrementing prefetch stat {Field}", field);
        }
    }

    /// <summary>
    ///     Record prefetch time (moving average).
    /// </summary>
    private async Task RecordPrefetchTimeAsync(long elapsedMs)
    {
        if (_redis == null)
        {
            return;
        }

        try
        {
            const string key = "prefetch:stats";
            var current = await _redis.HashGetAsync(key, "avgTimeMs");
            var count = await _redis.HashGetAsync(key, "totalRequests");

            var currentAvg = current.HasValue && double.TryParse(current, out var c) ? c : 0;
            var totalCount = count.HasValue && long.TryParse(count, out var tc) ? tc : 1;

            // Moving average: new_avg = ((old_avg * count) + new_value) / (count + 1)
            var newAvg = (currentAvg * (totalCount - 1) + elapsedMs) / totalCount;

            await _redis.HashSetAsync(key, "avgTimeMs", newAvg.ToString("F2"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error recording prefetch time");
        }
    }
}
