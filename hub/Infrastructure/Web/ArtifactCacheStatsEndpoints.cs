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

using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Infrastructure.Caching;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     Admin endpoints for artifact cache statistics and management.
///     Provides telemetry about cache performance (hits, misses, bytes served)
///     and cache management operations (clear cache).
/// </summary>
public static class ArtifactCacheStatsEndpoints
{
    public static void MapArtifactCacheStatsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/cache/artifacts")
            .WithTags("Admin", "Cache")
            .WithOpenApi();

        // GET /api/admin/cache/artifacts/stats - Get cache statistics
        group.MapGet("/stats", GetCacheStatistics)
            .WithName("GetArtifactCacheStats")
            .WithSummary("Get artifact cache statistics (hits, misses, hit rate, bytes served)")
            .Produces<CacheStatsResponse>();

        // POST /api/admin/cache/artifacts/clear - Clear all cached artifacts
        group.MapPost("/clear", ClearArtifactCache)
            .WithName("ClearArtifactCache")
            .WithSummary("Clear all cached artifacts (metadata, content, pre-signed URLs)")
            .Produces<ClearCacheResponse>();

        // GET /api/admin/cache/artifacts/health - Get cache health status with warnings
        group.MapGet("/health", GetCacheHealth)
            .WithName("GetArtifactCacheHealth")
            .WithSummary("Get cache health status with memory warnings and recommendations")
            .Produces<CacheHealthResponse>();
    }

    /// <summary>
    ///     Get artifact cache statistics including hits, misses, hit rate, bytes served, and Redis memory metrics.
    /// </summary>
    private static async Task<IResult> GetCacheStatistics(
        [FromServices] RedisArtifactCache? cache,
        [FromServices] IConnectionMultiplexer? redis,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ArtifactCacheStats");
        var chunkedLogger = new ChunkedLogger(logger, "ArtifactCacheStats.GetStatistics");

        if (cache == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Artifacts.ArtifactStorageError,
                "error=CacheDisabled");

            return Results.Ok(new CacheStatsResponse
            {
                Enabled = false,
                Hits = 0,
                Misses = 0,
                HitRate = 0,
                HitRatePercentage = "0.00%",
                BytesServed = 0,
                TotalCachedSize = 0,
                Message = "Artifact caching is disabled (ARTIFACT_CACHE_ENABLED=false)"
            });
        }

        try
        {
            var stats = await cache.GetStatisticsAsync();

            // Get Redis memory metrics via INFO command
            RedisMemoryMetrics? memoryMetrics = null;
            if (redis != null)
            {
                try
                {
                    var server = redis.GetServer(redis.GetEndPoints().First());
                    var info = await server.InfoAsync("memory");
                    memoryMetrics = ParseRedisMemoryInfo(info);
                }
                catch (Exception ex)
                {
                    // Redis INFO command failed - continue without memory metrics
                    chunkedLogger.LogMilestone(
                        EventCodes.Database.OperationFailed, ex,
                        "error=RedisInfoFailed");
                }
            }

            return Results.Ok(new CacheStatsResponse
            {
                Enabled = true,
                Hits = stats.Hits,
                Misses = stats.Misses,
                HitRate = stats.HitRate,
                HitRatePercentage = $"{stats.HitRate * 100:F2}%",
                BytesServed = stats.BytesServed,
                TotalCachedSize = stats.TotalCachedSize,
                OriginalBytes = stats.OriginalBytes,
                CompressedBytes = stats.CompressedBytes,
                CompressionRatio = stats.CompressionRatio,
                CompressionRatioFormatted = stats.CompressionRatio > 0 ? $"{stats.CompressionRatio:F2}x" : "N/A",
                MemorySaved = stats.OriginalBytes - stats.CompressedBytes,
                MemorySavedFormatted = FormatBytes(stats.OriginalBytes - stats.CompressedBytes),
                RedisMemory = memoryMetrics
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "error=GetStatsFailed");

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to retrieve cache statistics",
                eventCode: EventCodes.Database.OperationFailed,
                instance: "/api/admin/cache/artifacts/stats",
                traceId: null); // HttpContext not available here unless I add it
        }
    }

    /// <summary>
    ///     Parse Redis INFO memory output into structured metrics.
    /// </summary>
    private static RedisMemoryMetrics ParseRedisMemoryInfo(IGrouping<string, KeyValuePair<string, string>>[] info)
    {
        var memoryInfo = info.FirstOrDefault(g => g.Key == "Memory");
        if (memoryInfo == null)
        {
            return new RedisMemoryMetrics();
        }

        var dict = memoryInfo.ToDictionary(kv => kv.Key, kv => kv.Value);

        var usedMemory = ParseLongOrZero(dict.GetValueOrDefault("used_memory"));
        var usedMemoryRss = ParseLongOrZero(dict.GetValueOrDefault("used_memory_rss"));
        var usedMemoryPeak = ParseLongOrZero(dict.GetValueOrDefault("used_memory_peak"));
        var maxMemory = ParseLongOrZero(dict.GetValueOrDefault("maxmemory"));

        // Calculate memory usage percentage
        var memoryUsagePercent = maxMemory > 0 ? usedMemory / (double)maxMemory * 100 : 0;

        // Determine warning level based on usage
        var warningLevel = memoryUsagePercent switch
        {
            >= 90 => "critical",
            >= 75 => "warning",
            >= 50 => "info",
            _ => "ok"
        };

        return new RedisMemoryMetrics
        {
            UsedMemoryBytes = usedMemory,
            UsedMemoryFormatted = FormatBytes(usedMemory),
            UsedMemoryRssBytes = usedMemoryRss,
            UsedMemoryRssFormatted = FormatBytes(usedMemoryRss),
            UsedMemoryPeakBytes = usedMemoryPeak,
            UsedMemoryPeakFormatted = FormatBytes(usedMemoryPeak),
            MaxMemoryBytes = maxMemory,
            MaxMemoryFormatted = maxMemory > 0 ? FormatBytes(maxMemory) : "unlimited",
            MemoryUsagePercent = memoryUsagePercent,
            MemoryUsagePercentFormatted = $"{memoryUsagePercent:F2}%",
            WarningLevel = warningLevel,
            EvictionPolicy = dict.GetValueOrDefault("maxmemory_policy") ?? "noeviction",
            MemoryFragmentationRatio = ParseDoubleOrZero(dict.GetValueOrDefault("mem_fragmentation_ratio"))
        };
    }

    /// <summary>
    ///     Parse long value or return 0 if invalid.
    /// </summary>
    private static long ParseLongOrZero(string? value)
    {
        return long.TryParse(value, out var result) ? result : 0;
    }

    /// <summary>
    ///     Parse double value or return 0 if invalid.
    /// </summary>
    private static double ParseDoubleOrZero(string? value)
    {
        return double.TryParse(value, out var result) ? result : 0;
    }

    /// <summary>
    ///     Format bytes into human-readable format (KB, MB, GB).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F2} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    /// <summary>
    ///     Clear all cached artifacts from Redis (metadata, content, pre-signed URLs).
    ///     Useful for troubleshooting or forcing cache refresh after artifact updates.
    /// </summary>
    private static async Task<IResult> ClearArtifactCache(
        [FromServices] RedisArtifactCache? cache,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ArtifactCacheStats");
        var chunkedLogger = new ChunkedLogger(logger, "ArtifactCacheStats.Clear");

        if (cache == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Artifacts.ArtifactStorageError,
                "error=CacheDisabled");

            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["cache"] = ["Artifact caching is disabled"] },
                eventCode: EventCodes.Artifacts.ArtifactStorageError,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            await cache.ClearAllAsync();

            chunkedLogger.LogMilestone(
                EventCodes.Artifacts.ArtifactStorageError, // Or a more specific code if exists
                "action=ClearCache success=true");

            return Results.Ok(new ClearCacheResponse
            {
                Success = true,
                Message = "Artifact cache cleared successfully",
                ItemsCleared = 0 // TODO: Return actual count if needed
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "error=ClearCacheFailed");

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to clear artifact cache",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    /// <summary>
    ///     Get cache health status with memory warnings and tuning recommendations.
    /// </summary>
    private static async Task<IResult> GetCacheHealth(
        [FromServices] RedisArtifactCache? cache,
        [FromServices] IConnectionMultiplexer? redis,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ArtifactCacheStats");
        var chunkedLogger = new ChunkedLogger(logger, "ArtifactCacheStats.GetHealth");

        if (cache == null)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Artifacts.ArtifactStorageError,
                "error=CacheDisabled");

            return Results.Ok(new CacheHealthResponse
            {
                Enabled = false,
                HealthStatus = "disabled",
                Warnings = ["Artifact caching is disabled (ARTIFACT_CACHE_ENABLED=false)"],
                Recommendations =
                    ["Enable artifact caching by setting ARTIFACT_CACHE_ENABLED=true"]
            });
        }

        try
        {
            var warnings = new List<string>();
            var recommendations = new List<string>();
            var healthStatus = "healthy";

            // Get Redis memory metrics
            RedisMemoryMetrics? memoryMetrics = null;
            if (redis != null)
            {
                try
                {
                    var server = redis.GetServer(redis.GetEndPoints().First());
                    var info = await server.InfoAsync("memory");
                    memoryMetrics = ParseRedisMemoryInfo(info);

                    // Analyze memory health
                    if (memoryMetrics.MemoryUsagePercent >= 90)
                    {
                        healthStatus = "critical";
                        warnings.Add(
                            $"Redis memory usage is critical: {memoryMetrics.MemoryUsagePercentFormatted} of {memoryMetrics.MaxMemoryFormatted}");
                        recommendations.Add("Immediate action required: Increase maxmemory limit or clear cache");
                        recommendations.Add("Consider reducing ARTIFACT_CACHE_MAX_SIZE_MB or ARTIFACT_CACHE_TTL_HOURS");
                    }
                    else if (memoryMetrics.MemoryUsagePercent >= 75)
                    {
                        healthStatus = "warning";
                        warnings.Add(
                            $"Redis memory usage is high: {memoryMetrics.MemoryUsagePercentFormatted} of {memoryMetrics.MaxMemoryFormatted}");
                        recommendations.Add("Consider increasing maxmemory limit or reducing cache TTL");
                    }
                    else if (memoryMetrics.MemoryUsagePercent >= 50)
                    {
                        healthStatus = "info";
                        warnings.Add(
                            $"Redis memory usage is moderate: {memoryMetrics.MemoryUsagePercentFormatted} of {memoryMetrics.MaxMemoryFormatted}");
                    }

                    // Check fragmentation ratio
                    if (memoryMetrics.MemoryFragmentationRatio > 1.5)
                    {
                        warnings.Add($"High memory fragmentation detected: {memoryMetrics.MemoryFragmentationRatio:F2}");
                        recommendations.Add("Consider restarting Redis to defragment memory (or enable activedefrag)");
                    }
                    else if (memoryMetrics.MemoryFragmentationRatio < 1.0)
                    {
                        healthStatus = healthStatus == "healthy" ? "warning" : healthStatus;
                        warnings.Add(
                            $"Redis is swapping to disk (fragmentation ratio: {memoryMetrics.MemoryFragmentationRatio:F2})");
                        recommendations.Add("Critical: Increase system RAM or reduce Redis memory usage immediately");
                    }

                    // Check eviction policy
                    if (memoryMetrics is { EvictionPolicy: "noeviction", MaxMemoryBytes: > 0 })
                    {
                        warnings.Add("Eviction policy is 'noeviction' - writes will fail when memory is full");
                        recommendations.Add("Consider using 'allkeys-lru' or 'volatile-lru' eviction policy");
                    }
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Database.OperationFailed, ex,
                        "error=RedisInfoFailed");
                    warnings.Add("Failed to retrieve Redis memory metrics");
                    recommendations.Add("Check Redis connection and INFO command permissions");
                }
            }
            else
            {
                warnings.Add("Redis connection not available - memory monitoring disabled");
            }

            // Get cache statistics
            var stats = await cache.GetStatisticsAsync();

            // Check cache hit rate
            if (stats.HitRate < 0.5 && stats.Hits + stats.Misses > 100)
            {
                warnings.Add($"Low cache hit rate: {stats.HitRate * 100:F2}% (should be > 50%)");
                recommendations.Add("Consider increasing ARTIFACT_CACHE_TTL_HOURS to improve hit rate");
            }

            return Results.Ok(new CacheHealthResponse
            {
                Enabled = true,
                HealthStatus = healthStatus,
                MemoryMetrics = memoryMetrics,
                CacheStats = new CacheHealthStats
                {
                    HitRate = stats.HitRate,
                    HitRatePercentage = $"{stats.HitRate * 100:F2}%",
                    TotalRequests = stats.Hits + stats.Misses,
                    CompressionRatio = stats.CompressionRatio,
                    MemorySaved = FormatBytes(stats.OriginalBytes - stats.CompressedBytes)
                },
                Warnings = warnings,
                Recommendations = recommendations
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.Database.OperationFailed, ex,
                "error=GetHealthFailed");

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to retrieve cache health",
                eventCode: EventCodes.Database.OperationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }
}

/// <summary>
///     Response model for artifact cache statistics endpoint.
/// </summary>
public record CacheStatsResponse
{
    /// <summary>
    ///     Whether artifact caching is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    ///     Total number of cache hits (artifacts served from Redis).
    /// </summary>
    public long Hits { get; init; }

    /// <summary>
    ///     Total number of cache misses (artifacts loaded from disk/MinIO).
    /// </summary>
    public long Misses { get; init; }

    /// <summary>
    ///     Cache hit rate (0.0 to 1.0).
    /// </summary>
    public double HitRate { get; init; }

    /// <summary>
    ///     Cache hit rate as formatted percentage (e.g., "95.23%").
    /// </summary>
    public string HitRatePercentage { get; init; } = string.Empty;

    /// <summary>
    ///     Total bytes served from cache.
    /// </summary>
    public long BytesServed { get; init; }

    /// <summary>
    ///     Total size of cached content in bytes (not implemented yet).
    /// </summary>
    public long TotalCachedSize { get; init; }

    /// <summary>
    ///     Total original uncompressed bytes cached.
    /// </summary>
    public long OriginalBytes { get; init; }

    /// <summary>
    ///     Total compressed bytes stored in Redis.
    /// </summary>
    public long CompressedBytes { get; init; }

    /// <summary>
    ///     Compression ratio (original / compressed).
    /// </summary>
    public double CompressionRatio { get; init; }

    /// <summary>
    ///     Compression ratio formatted (e.g., "3.45x").
    /// </summary>
    public string CompressionRatioFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Memory saved due to compression (bytes).
    /// </summary>
    public long MemorySaved { get; init; }

    /// <summary>
    ///     Memory saved formatted (e.g., "15.23 MB").
    /// </summary>
    public string MemorySavedFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Optional message (e.g., "Caching disabled").
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    ///     Redis memory metrics (null if Redis unavailable).
    /// </summary>
    public RedisMemoryMetrics? RedisMemory { get; init; }
}

/// <summary>
///     Response model for clear cache endpoint.
/// </summary>
public record ClearCacheResponse
{
    /// <summary>
    ///     Whether the cache was cleared successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    ///     Message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    ///     Number of items cleared from the cache.
    /// </summary>
    public int ItemsCleared { get; init; }
}

/// <summary>
///     Response model for cache health endpoint with warnings and recommendations.
/// </summary>
public record CacheHealthResponse
{
    /// <summary>
    ///     Whether artifact caching is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    ///     Overall health status: "healthy", "info", "warning", "critical", "disabled".
    /// </summary>
    public string HealthStatus { get; init; } = string.Empty;

    /// <summary>
    ///     Redis memory metrics (null if unavailable).
    /// </summary>
    public RedisMemoryMetrics? MemoryMetrics { get; init; }

    /// <summary>
    ///     Cache performance statistics.
    /// </summary>
    public CacheHealthStats? CacheStats { get; init; }

    /// <summary>
    ///     List of warnings about cache health issues.
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    ///     List of recommendations for improving cache health.
    /// </summary>
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
///     Cache health statistics subset.
/// </summary>
public record CacheHealthStats
{
    /// <summary>
    ///     Cache hit rate (0.0 to 1.0).
    /// </summary>
    public double HitRate { get; init; }

    /// <summary>
    ///     Cache hit rate as a formatted percentage.
    /// </summary>
    public string HitRatePercentage { get; init; } = string.Empty;

    /// <summary>
    ///     Total cache requests (hits + misses).
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    ///     Compression ratio (original / compressed).
    /// </summary>
    public double CompressionRatio { get; init; }

    /// <summary>
    ///     Memory saved due to compression (formatted).
    /// </summary>
    public string MemorySaved { get; init; } = string.Empty;
}

/// <summary>
///     Redis memory usage metrics from INFO memory command.
///     Provides visibility into Redis memory consumption and health.
/// </summary>
public record RedisMemoryMetrics
{
    /// <summary>
    ///     Total memory used by Redis (in bytes).
    ///     This is the total number of bytes allocated by Redis using its allocator.
    /// </summary>
    public long UsedMemoryBytes { get; init; }

    /// <summary>
    ///     Total memory used formatted (e.g., "512.5 MB").
    /// </summary>
    public string UsedMemoryFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Resident Set Size memory used by Redis (in bytes).
    ///     Memory allocated by the operating system to Redis (includes fragmentation).
    /// </summary>
    public long UsedMemoryRssBytes { get; init; }

    /// <summary>
    ///     RSS memory formatted (e.g., "678.2 MB").
    /// </summary>
    public string UsedMemoryRssFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Peak memory used by Redis (in bytes).
    ///     Highest memory usage since Redis started.
    /// </summary>
    public long UsedMemoryPeakBytes { get; init; }

    /// <summary>
    ///     Peak memory formatted (e.g., "1.2 GB").
    /// </summary>
    public string UsedMemoryPeakFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Maximum memory limit configured for Redis (in bytes).
    ///     0 = unlimited (no maxmemory directive set).
    /// </summary>
    public long MaxMemoryBytes { get; init; }

    /// <summary>
    ///     Maximum memory formatted (e.g., "2.0 GB" or "unlimited").
    /// </summary>
    public string MaxMemoryFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Memory usage as percentage of max memory (0-100).
    ///     0 if maxmemory is unlimited.
    /// </summary>
    public double MemoryUsagePercent { get; init; }

    /// <summary>
    ///     Memory usage percentage formatted (e.g., "75.23%").
    /// </summary>
    public string MemoryUsagePercentFormatted { get; init; } = string.Empty;

    /// <summary>
    ///     Warning level based on memory usage: "ok", "info", "warning", "critical".
    ///     - ok: less than 50% usage
    ///     - info: 50-74% usage
    ///     - warning: 75-89% usage
    ///     - critical: 90% or higher usage
    /// </summary>
    public string WarningLevel { get; init; } = "ok";

    /// <summary>
    ///     Redis eviction policy (e.g., "noeviction", "allkeys-lru", "volatile-lru").
    /// </summary>
    public string EvictionPolicy { get; init; } = string.Empty;

    /// <summary>
    ///     Memory fragmentation ratio (used_memory_rss / used_memory).
    ///     Values greater than 1 indicate memory fragmentation.
    ///     Values less than 1 indicate Redis is swapping to disk (performance degradation).
    ///     Ideal range: 1.0 - 1.5
    /// </summary>
    public double MemoryFragmentationRatio { get; init; }
}
