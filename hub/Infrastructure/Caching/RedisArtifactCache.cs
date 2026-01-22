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

using System.IO.Compression;
using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Caching;

/// <summary>
///     Distributed Redis cache for frequently accessed artifacts.
///     Caches artifact content (bytes), metadata, and MinIO pre-signed URLs to reduce database queries,
///     disk I/O, and network bandwidth.
/// </summary>
public class RedisArtifactCache
{
    private readonly bool _compressionEnabled;
    private readonly int _contentTtlSeconds;
    private readonly ILogger<RedisArtifactCache> _logger;
    private readonly long _maxContentSizeBytes;
    private readonly int _metadataTtlSeconds;
    private readonly int _presignedUrlTtlSeconds;
    private readonly IDatabase _redis;

    public RedisArtifactCache(
        IDatabase redis,
        ILogger<RedisArtifactCache> logger,
        long maxContentSizeBytes,
        int contentTtlSeconds,
        int metadataTtlSeconds,
        int presignedUrlTtlSeconds,
        bool compressionEnabled = true)
    {
        _redis = redis;
        _logger = logger;
        _maxContentSizeBytes = maxContentSizeBytes;
        _contentTtlSeconds = contentTtlSeconds;
        _metadataTtlSeconds = metadataTtlSeconds;
        _presignedUrlTtlSeconds = presignedUrlTtlSeconds;
        _compressionEnabled = compressionEnabled;
    }

    // ==================== Content Caching ====================

    /// <summary>
    ///     Get cached artifact content bytes from Redis.
    ///     Automatically decompresses if compression is enabled.
    /// </summary>
    public async Task<byte[]?> GetContentAsync(Guid artifactId)
    {
        try
        {
            var key = RedisKeys.ArtifactContent(artifactId.ToString());
            var value = await _redis.StringGetAsync(key);

            if (value.HasValue)
            {
                var compressedData = (byte[]?)value;
                if (compressedData == null)
                {
                    return null;
                }

                _logger.LogDebug("Cache HIT for artifact content {ArtifactId}", artifactId);

                // Decompress if compression is enabled
                if (_compressionEnabled)
                {
                    try
                    {
                        var decompressed = await DecompressAsync(compressedData);
                        return decompressed;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Decompression failed for artifact {ArtifactId}, returning null",
                            artifactId);
                        return null;
                    }
                }

                return compressedData;
            }

            _logger.LogDebug("Cache MISS for artifact content {ArtifactId}", artifactId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached content for artifact {ArtifactId}", artifactId);
            return null;
        }
    }

    /// <summary>
    ///     Cache artifact content bytes in Redis.
    ///     Automatically compresses if compression is enabled.
    ///     Only caches if size is below max threshold.
    /// </summary>
    public async Task SetContentAsync(Guid artifactId, byte[] content, TimeSpan? ttl = null)
    {
        if (!ShouldCacheContent(content.Length))
        {
            _logger.LogDebug("Skipping cache for artifact {ArtifactId} (size {Size} exceeds limit)",
                artifactId, content.Length);
            return;
        }

        try
        {
            var key = RedisKeys.ArtifactContent(artifactId.ToString());
            var expiry = ttl ?? TimeSpan.FromSeconds(_contentTtlSeconds);

            // Compress if compression is enabled
            byte[] dataToStore;
            if (_compressionEnabled)
            {
                dataToStore = await CompressAsync(content);
                var compressionRatio = content.Length > 0 ? (double)content.Length / dataToStore.Length : 1.0;
                var reductionPercent = (1 - (double)dataToStore.Length / content.Length) * 100;

                _logger.LogDebug(
                    "Cached artifact content {ArtifactId} ({Original}→{Compressed} bytes, {Ratio:F2}x compression, {Reduction:F1}% reduction, TTL {TTL}s)",
                    artifactId, content.Length, dataToStore.Length, compressionRatio, reductionPercent,
                    expiry.TotalSeconds);

                // Track compression statistics
                await IncrementCompressedBytesAsync(content.Length, dataToStore.Length);
            }
            else
            {
                dataToStore = content;
                _logger.LogDebug("Cached artifact content {ArtifactId} ({Size} bytes, TTL {TTL}s)",
                    artifactId, content.Length, expiry.TotalSeconds);
            }

            await _redis.StringSetAsync(key, dataToStore, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching content for artifact {ArtifactId}", artifactId);
        }
    }

    // ==================== Metadata Caching ====================

    /// <summary>
    ///     Get cached artifact metadata from Redis.
    /// </summary>
    public async Task<ArtifactMetadata?> GetMetadataAsync(Guid artifactId)
    {
        try
        {
            var key = RedisKeys.ArtifactMetadata(artifactId.ToString());
            var value = await _redis.StringGetAsync(key);

            if (value.HasValue)
            {
                var metadata = JsonSerializer.Deserialize<ArtifactMetadata>(value.ToString());
                _logger.LogDebug("Cache HIT for artifact metadata {ArtifactId}", artifactId);
                return metadata;
            }

            _logger.LogDebug("Cache MISS for artifact metadata {ArtifactId}", artifactId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached metadata for artifact {ArtifactId}", artifactId);
            return null;
        }
    }

    /// <summary>
    ///     Cache artifact metadata in Redis.
    /// </summary>
    public async Task SetMetadataAsync(Guid artifactId, ArtifactMetadata metadata, TimeSpan? ttl = null)
    {
        try
        {
            var key = RedisKeys.ArtifactMetadata(artifactId.ToString());
            var json = JsonSerializer.Serialize(metadata);
            var expiry = ttl ?? TimeSpan.FromSeconds(_metadataTtlSeconds);

            await _redis.StringSetAsync(key, json, expiry);

            _logger.LogDebug("Cached artifact metadata {ArtifactId} (TTL {TTL}s)",
                artifactId, expiry.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching metadata for artifact {ArtifactId}", artifactId);
        }
    }

    // ==================== Pre-signed URL Caching ====================

    /// <summary>
    ///     Get cached MinIO pre-signed URL from Redis.
    ///     Returns null if expired or not found.
    /// </summary>
    public async Task<PresignedUrlCache?> GetPresignedUrlAsync(Guid artifactId)
    {
        try
        {
            var key = RedisKeys.ArtifactPresignedUrl(artifactId.ToString());
            var value = await _redis.StringGetAsync(key);

            if (value.HasValue)
            {
                var urlCache = JsonSerializer.Deserialize<PresignedUrlCache>(value.ToString());

                // Verify not expired
                if (urlCache != null && urlCache.ExpiresAt > DateTime.UtcNow)
                {
                    _logger.LogDebug("Cache HIT for pre-signed URL {ArtifactId}", artifactId);
                    return urlCache;
                }

                // Expired - remove from cache
                await _redis.KeyDeleteAsync(key);
                _logger.LogDebug("Cache EXPIRED for pre-signed URL {ArtifactId}", artifactId);
            }

            _logger.LogDebug("Cache MISS for pre-signed URL {ArtifactId}", artifactId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached pre-signed URL for artifact {ArtifactId}", artifactId);
            return null;
        }
    }

    /// <summary>
    ///     Cache MinIO pre-signed URL in Redis.
    /// </summary>
    public async Task SetPresignedUrlAsync(Guid artifactId, string url, DateTime expiresAt)
    {
        try
        {
            var key = RedisKeys.ArtifactPresignedUrl(artifactId.ToString());
            var urlCache = new PresignedUrlCache(url, expiresAt);
            var json = JsonSerializer.Serialize(urlCache);

            // Calculate TTL (cache expires slightly before MinIO URL expires)
            var ttl = TimeSpan.FromSeconds(_presignedUrlTtlSeconds);
            var timeUntilExpiry = expiresAt - DateTime.UtcNow;
            if (timeUntilExpiry < ttl)
            {
                ttl = timeUntilExpiry;
            }

            await _redis.StringSetAsync(key, json, ttl);

            _logger.LogDebug("Cached pre-signed URL {ArtifactId} (expires {ExpiresAt}, TTL {TTL}s)",
                artifactId, expiresAt, ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching pre-signed URL for artifact {ArtifactId}", artifactId);
        }
    }

    // ==================== Telemetry ====================

    /// <summary>
    ///     Increment cache hit counter.
    /// </summary>
    public async Task IncrementHitAsync()
    {
        try
        {
            var key = RedisKeys.ArtifactCacheStats();
            await _redis.HashIncrementAsync(key, "hits");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error incrementing cache hit counter");
        }
    }

    /// <summary>
    ///     Increment cache miss counter.
    /// </summary>
    public async Task IncrementMissAsync()
    {
        try
        {
            var key = RedisKeys.ArtifactCacheStats();
            await _redis.HashIncrementAsync(key, "misses");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error incrementing cache miss counter");
        }
    }

    /// <summary>
    ///     Add to bytes served counter.
    /// </summary>
    public async Task AddBytesServedAsync(long bytes)
    {
        try
        {
            var key = RedisKeys.ArtifactCacheStats();
            await _redis.HashIncrementAsync(key, "bytesServed", bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error incrementing bytes served counter");
        }
    }

    /// <summary>
    ///     Track compression statistics (original vs compressed size).
    /// </summary>
    public async Task IncrementCompressedBytesAsync(long originalSize, long compressedSize)
    {
        try
        {
            var key = RedisKeys.ArtifactCacheStats();
            await _redis.HashIncrementAsync(key, "originalBytes", originalSize);
            await _redis.HashIncrementAsync(key, "compressedBytes", compressedSize);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error tracking compression statistics");
        }
    }

    /// <summary>
    ///     Get cache statistics (hits, misses, hit rate, bytes served, compression metrics).
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        try
        {
            var key = RedisKeys.ArtifactCacheStats();
            var entries = await _redis.HashGetAllAsync(key);

            long hits = 0;
            long misses = 0;
            long bytesServed = 0;
            long originalBytes = 0;
            long compressedBytes = 0;

            foreach (var entry in entries)
            {
                if (entry.Name == "hits" && long.TryParse(entry.Value, out var h))
                {
                    hits = h;
                }
                else if (entry.Name == "misses" && long.TryParse(entry.Value, out var m))
                {
                    misses = m;
                }
                else if (entry.Name == "bytesServed" && long.TryParse(entry.Value, out var b))
                {
                    bytesServed = b;
                }
                else if (entry.Name == "originalBytes" && long.TryParse(entry.Value, out var ob))
                {
                    originalBytes = ob;
                }
                else if (entry.Name == "compressedBytes" && long.TryParse(entry.Value, out var cb))
                {
                    compressedBytes = cb;
                }
            }

            var total = hits + misses;
            var hitRate = total > 0 ? (double)hits / total : 0;
            var compressionRatio = compressedBytes > 0 ? (double)originalBytes / compressedBytes : 0;

            return new CacheStatistics(hits, misses, hitRate, bytesServed, 0, originalBytes, compressedBytes,
                compressionRatio);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            return new CacheStatistics(0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>
    ///     Clear all cached artifacts (content, metadata, URLs, stats).
    /// </summary>
    public async Task ClearAllAsync()
    {
        try
        {
            var server = _redis.Multiplexer.GetServer(_redis.Multiplexer.GetEndPoints()[0]);

            // Scan and delete artifact:* keys
            var keys = server.Keys(pattern: "artifact:*").ToArray();
            if (keys.Length > 0)
            {
                await _redis.KeyDeleteAsync(keys);
                _logger.LogInformation("Cleared {Count} artifact cache entries", keys.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing artifact cache");
        }
    }

    // ==================== Helpers ====================

    /// <summary>
    ///     Determine if artifact content should be cached based on size.
    /// </summary>
    public bool ShouldCacheContent(long fileSize)
    {
        return fileSize > 0 && fileSize <= _maxContentSizeBytes;
    }

    /// <summary>
    ///     Compress byte array using Gzip compression.
    /// </summary>
    private static async Task<byte[]> CompressAsync(byte[] data)
    {
        using var outputStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Fastest))
        {
            await gzipStream.WriteAsync(data, 0, data.Length);
        }

        return outputStream.ToArray();
    }

    /// <summary>
    ///     Decompress Gzip-compressed byte array.
    /// </summary>
    private static async Task<byte[]> DecompressAsync(byte[] compressedData)
    {
        using var inputStream = new MemoryStream(compressedData);
        await using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        await gzipStream.CopyToAsync(outputStream);
        return outputStream.ToArray();
    }
}

// ==================== DTOs ====================

/// <summary>
///     Artifact metadata cached in Redis.
/// </summary>
public record ArtifactMetadata(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime UploadedAt,
    string StoragePath
);

/// <summary>
///     Cached pre-signed URL with expiration.
/// </summary>
public record PresignedUrlCache(string Url, DateTime ExpiresAt);

/// <summary>
///     Cache statistics for telemetry and compression metrics.
/// </summary>
public record CacheStatistics(
    long Hits,
    long Misses,
    double HitRate,
    long BytesServed,
    long TotalCachedSize,
    long OriginalBytes,
    long CompressedBytes,
    double CompressionRatio
);
