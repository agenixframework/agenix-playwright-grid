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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;

namespace IngestionService.Infrastructure;

/// <summary>
///     Redis-based log token cache with optional in-memory LRU cache.
///     Uses SHA256 hashing for message deduplication with 90%+ storage reduction.
/// </summary>
public sealed partial class RedisLogTokenCache : ILogTokenCache
{
    // Pre-compiled SQL queries (performance optimization - avoid string allocation on every call)
    private const string BulkSelectSql = @"
        SELECT token_hash, message
        FROM log_tokens
        WHERE token_hash = ANY($1)";

    private const string InsertTokenSql = @"
        INSERT INTO log_tokens (token_hash, message, metadata_json, error_fingerprint, first_seen_at, last_seen_at, occurrence_count)
        VALUES ($1, $2, $3, $4, $5, $6, 1)
        ON CONFLICT (token_hash) DO UPDATE SET
            last_seen_at = EXCLUDED.last_seen_at,
            occurrence_count = log_tokens.occurrence_count + 1";

    private const string UpdateOccurrenceSql = @"
        UPDATE log_tokens
        SET occurrence_count = occurrence_count + 1,
            last_seen_at = $2,
            error_fingerprint = COALESCE(error_fingerprint, $3)
        WHERE token_hash = $1";

    private const string SelectTokenSql = "SELECT message FROM log_tokens WHERE token_hash = $1";
    private readonly NpgsqlDataSource _dataSource;

    // Optional in-memory LRU cache (disabled by default)
    private readonly bool _enableInMemory;
    private readonly ILogger<RedisLogTokenCache> _logger;
    private readonly ConcurrentQueue<string>? _lruQueue;
    private readonly int _maxInMemorySize;
    private readonly ConcurrentDictionary<string, TokenMetadata>? _memCache;
    private readonly IDatabase _redis;
    private readonly TimeSpan _ttl;

    public RedisLogTokenCache(
        IDatabase redis,
        NpgsqlDataSource dataSource,
        TimeSpan ttl,
        bool enableInMemory = false,
        int maxInMemorySize = 10000,
        ILogger<RedisLogTokenCache>? logger = null)
    {
        _redis = redis;
        _dataSource = dataSource;
        _ttl = ttl;
        _enableInMemory = enableInMemory;
        _maxInMemorySize = maxInMemorySize;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_enableInMemory)
        {
            return;
        }

        _memCache = new ConcurrentDictionary<string, TokenMetadata>();
        _lruQueue = new ConcurrentQueue<string>();
    }

    /// <summary>
    ///     Get or create a log token. Returns token hash for storage.
    /// </summary>
    public async Task<string> GetOrCreateTokenAsync(string message, string level, string? metadataJson = null,
        CancellationToken ct = default)
    {
        var hash = ComputeHash(message);

        // Tier 1: In-memory cache (if enabled)
        if (_enableInMemory && _memCache!.TryGetValue(hash, out var cached))
        {
            await IncrementOccurrenceAsync(hash, message, level, ct);
            return hash;
        }

        // Tier 2: Redis cache
        var redisKey = $"log_token:{hash}";
        var redisValue = await _redis.StringGetAsync(redisKey);

        if (redisValue.HasValue)
        {
            // Token exists in Redis
            await IncrementOccurrenceAsync(hash, message, level, ct);

            if (_enableInMemory)
            {
                CacheInMemory(hash, message);
            }

            return hash;
        }

        // Tier 3: PostgresSQL (check if the token was created before Redis TTL expired)
        var existsInDb = await TokenExistsInDbAsync(hash, ct);
        if (existsInDb)
        {
            // Restore to Redis with new TTL
            await _redis.StringSetAsync(redisKey, message, _ttl);
            await IncrementOccurrenceAsync(hash, message, level, ct);

            if (_enableInMemory)
            {
                CacheInMemory(hash, message);
            }

            return hash;
        }

        // Token doesn't exist anywhere - create new
        await CreateNewTokenAsync(hash, message, level, metadataJson, ct);

        if (_enableInMemory)
        {
            CacheInMemory(hash, message);
        }

        return hash;
    }

    /// <summary>
    ///     Batch resolves token hashes to messages (for reading logs).
    /// </summary>
    public async Task<Dictionary<string, string>> ResolveTokensAsync(IEnumerable<string> hashes,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        var toFetchFromRedis = new List<string>();

        foreach (var hash in hashes.Distinct())
        {
            // Tier 1: Check in-memory first
            if (_enableInMemory && _memCache!.TryGetValue(hash, out var cached))
            {
                result[hash] = cached.Message;
            }
            else
            {
                toFetchFromRedis.Add(hash);
            }
        }

        if (toFetchFromRedis.Count == 0)
        {
            return result;
        }

        // Tier 2: Batch fetch from Redis using a pipeline
        var batch = _redis.CreateBatch();
        var redisTasks = toFetchFromRedis.ToDictionary(
            hash => hash,
            hash => batch.StringGetAsync($"log_token:{hash}")
        );
        batch.Execute();

        await Task.WhenAll(redisTasks.Values);

        var toFetchFromDb = new List<string>();

        foreach (var (hash, task) in redisTasks)
        {
            var value = await task;
            if (!value.HasValue)
            {
                // Token not in Redis, will need to fetch from PostgreSQL
                toFetchFromDb.Add(hash);
                continue;
            }

            result[hash] = value.ToString();

            if (_enableInMemory)
            {
                CacheInMemory(hash, value.ToString());
            }
        }

        // Tier 3: Bulk fetch from PostgreSQL for Redis cache misses (prevents N+1 queries)
        if (toFetchFromDb.Count > 0)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(BulkSelectSql, conn);
            cmd.Parameters.AddWithValue(toFetchFromDb.ToArray());

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var hash = reader.GetString(0);
                var message = reader.GetString(1);

                result[hash] = message;

                // Restore to Redis with TTL
                await _redis.StringSetAsync($"log_token:{hash}", message, _ttl);

                // Cache in memory if enabled
                if (_enableInMemory)
                {
                    CacheInMemory(hash, message);
                }
            }
        }

        return result;
    }

    private async Task CreateNewTokenAsync(string hash, string message, string level, string? metadataJson,
        CancellationToken ct)
    {
        // CRITICAL: Store in PostgreSQL FIRST before Redis
        // This ensures FK constraints are satisfied before any code can read the token from Redis
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Use explicit transaction to ensure commit is visible before caching in Redis
            await using var transaction = await conn.BeginTransactionAsync(ct);

            // Extract common metadata if present (remove attachment-specific fields to avoid duplication)
            var commonMetadataJson = ExtractCommonMetadata(metadataJson);

            // Generate error fingerprint for ERROR/FATAL logs (for unique errors grouping)
            var errorFingerprint = GenerateErrorFingerprint(message, level);

            await using var cmd = new NpgsqlCommand(InsertTokenSql, conn, transaction);
            cmd.Parameters.AddWithValue(hash);
            cmd.Parameters.AddWithValue(message);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, (object?)commonMetadataJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)errorFingerprint ?? DBNull.Value);
            cmd.Parameters.AddWithValue(DateTime.UtcNow);
            cmd.Parameters.AddWithValue(DateTime.UtcNow);

            var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Created/updated log token {Hash} (fingerprint: {Fingerprint}), rows affected: {Rows}",
                hash, errorFingerprint ?? "(none)", rowsAffected);

            // Commit transaction - ensures token is visible to other connections before caching
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist log token {Hash} to PostgreSQL - log_tokens table may not exist or migration V4 not applied",
                hash);
            throw; // Re-throw to surface the error
        }

        // Store in Redis with TTL AFTER successful PostgreSQL write and commit
        var redisKey = $"log_token:{hash}";
        await _redis.StringSetAsync(redisKey, message, _ttl);
    }

    private async Task IncrementOccurrenceAsync(string hash, string message, string level, CancellationToken ct)
    {
        // Refresh Redis TTL
        await _redis.KeyExpireAsync($"log_token:{hash}", _ttl);

        // Calculate error fingerprint (for backfilling NULL fingerprints)
        var errorFingerprint = GenerateErrorFingerprint(message, level);

        // Increment PostgresSQL counter (fire-and-forget but log errors)
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);

                // CRITICAL: Use COALESCE to backfill missing error_fingerprint
                // - Keeps existing fingerprint if not NULL (don't recalculate)
                // - Sets new fingerprint if NULL (backfills old tokens)
                await using var cmd = new NpgsqlCommand(UpdateOccurrenceSql, conn);
                cmd.Parameters.AddWithValue(hash);
                cmd.Parameters.AddWithValue(DateTime.UtcNow);
                cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)errorFingerprint ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail - this is best-effort analytics
                _logger.LogWarning(ex, "Failed to increment occurrence count for token {Hash}", hash);
            }
        }, ct);
    }

    private async Task<bool> TokenExistsInDbAsync(string hash, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            await using var cmd = new NpgsqlCommand(SelectTokenSql, conn);
            cmd.Parameters.AddWithValue(hash);

            var result = await cmd.ExecuteScalarAsync(ct);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    private void CacheInMemory(string hash, string message)
    {
        if (!_enableInMemory)
        {
            return;
        }

        var metadata = new TokenMetadata(hash, message, DateTime.UtcNow, DateTime.UtcNow, 1);
        _memCache!.TryAdd(hash, metadata);
        _lruQueue!.Enqueue(hash);

        // Evict old entries if the cache is full
        while (_memCache.Count > _maxInMemorySize && _lruQueue.TryDequeue(out var oldHash))
        {
            _memCache.TryRemove(oldHash, out _);
        }
    }

    private static string ComputeHash(string message)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    ///     Extracts common metadata from the full metadata JSON, removing attachment-specific fields
    ///     that vary per log item (to avoid duplication in the log_tokens table)
    /// </summary>
    private static string? ExtractCommonMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadataJson);
            if (metadata == null || metadata.Count == 0)
            {
                return null;
            }

            // Remove attachment-specific fields that vary per log item
            // These should not be stored in log_tokens as they're not "common" across all occurrences
            metadata.Remove("attachmentDataBase64"); // Binary data - varies per occurrence
            metadata.Remove("attachmentSize"); // Size varies if different attachments
            metadata.Remove("capturedAt"); // Timestamp varies per occurrence
            // Keep only truly common metadata fields:
            // - level (common across the same message type)
            // - attachmentName (if the same attachment name is always used)
            // - attachmentMimeType (if the same type is always used)
            // If only level remains (or nothing), return null - not worth storing
            return metadata.Count <= 1 ? null : JsonSerializer.Serialize(metadata);
        }
        catch (JsonException)
        {
            // If we can't parse the metadata, return null
            return null;
        }
    }

    /// <summary>
    ///     Generates a normalized error fingerprint for grouping similar errors.
    ///     Uses ReportPortal-inspired normalization techniques.
    ///     Returns null for non-ERROR/FATAL logs.
    /// </summary>
    private static string? GenerateErrorFingerprint(string message, string level)
    {
        // Only fingerprint ERROR and FATAL level logs
        var normalizedLevel = level.Trim().ToUpperInvariant();
        if (normalizedLevel != "ERROR" && normalizedLevel != "FATAL")
        {
            return null;
        }

        // 1. Extract first line only (ignore stack trace)
        var firstLine = message.Split('\n')[0].Trim();
        if (string.IsNullOrEmpty(firstLine))
        {
            return null;
        }

        // 2. Remove datetime stamps (ISO 8601 and common formats)
        firstLine = MyRegex().Replace(firstLine, "");

        // 3. Remove log level prefix (case-insensitive)
        firstLine = Regex.Replace(firstLine,
            @"^(TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|CRITICAL)\s*:?\s*",
            "", RegexOptions.IgnoreCase);

        // 4. Split camelCase and PascalCase words (ReportPortal technique)
        // NullPointerException → Null Pointer Exception
        firstLine = Regex.Replace(firstLine, @"([a-z])([A-Z])", "$1 $2");

        // 5. Lowercase for case-insensitive grouping
        firstLine = firstLine.ToLower();

        // 6. Normalize dynamic values to placeholders
        // Numbers: 12345 → {num}
        firstLine = Regex.Replace(firstLine, @"\b\d+\b", "{num}");

        // UUIDs: a3b2c1d4-... → {uuid}
        firstLine = Regex.Replace(firstLine,
            @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}",
            "{uuid}");

        // Hex addresses: 0x7fff5c3d2a10 → {hex}
        firstLine = Regex.Replace(firstLine, @"0x[a-f0-9]+", "{hex}");

        // File paths with line numbers: File.java:123 → {file}:{num}
        firstLine = Regex.Replace(firstLine,
            @"/[\w/]+\.(java|py|cs|js|ts|go|rb|cpp|c|h):\d+",
            "/{file}:{num}");

        // 7. Remove special characters (keep alphanumeric, spaces, basic punctuation)
        firstLine = Regex.Replace(firstLine, @"[^\w\s\{\}:/-]", " ");

        // 8. Normalize whitespace
        firstLine = Regex.Replace(firstLine, @"\s+", " ").Trim();

        // 9. Limit length to prevent index bloat
        if (firstLine.Length > 200)
        {
            firstLine = firstLine[..200];
        }

        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    private record TokenMetadata(string Hash, string Message, DateTime FirstSeen, DateTime LastSeen, long Count);

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex MyRegex();
}
