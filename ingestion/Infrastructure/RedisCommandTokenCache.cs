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
using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;

namespace IngestionService.Infrastructure;

/// <summary>
///     Redis-based command token cache with optional in-memory LRU cache.
///     Uses SHA256 hashing for command message deduplication with 90%+ storage reduction.
/// </summary>
public sealed class RedisCommandTokenCache : ICommandTokenCache
{
    // Pre-compiled SQL queries (performance optimization - avoid string allocation on every call)
    private const string BulkSelectSql = @"
        SELECT token_hash, message
        FROM command_tokens
        WHERE token_hash = ANY($1)";

    private const string InsertTokenSql = @"
        INSERT INTO command_tokens (token_hash, message, kind, metadata_json, first_seen_at, last_seen_at, occurrence_count)
        VALUES ($1, $2, $3, $4, $5, $6, 1)
        ON CONFLICT (token_hash) DO UPDATE SET
            last_seen_at = EXCLUDED.last_seen_at,
            occurrence_count = command_tokens.occurrence_count + 1";

    private const string UpdateOccurrenceSql = @"
        UPDATE command_tokens
        SET occurrence_count = occurrence_count + 1, last_seen_at = $2
        WHERE token_hash = $1";

    private const string SelectTokenSql = "SELECT message FROM command_tokens WHERE token_hash = $1";
    private readonly NpgsqlDataSource _dataSource;

    // Optional in-memory LRU cache (disabled by default)
    private readonly bool _enableInMemory;
    private readonly ILogger<RedisCommandTokenCache> _logger;
    private readonly ConcurrentQueue<string>? _lruQueue;
    private readonly int _maxInMemorySize;
    private readonly ConcurrentDictionary<string, TokenMetadata>? _memCache;
    private readonly IDatabase _redis;
    private readonly TimeSpan _ttl;

    public RedisCommandTokenCache(
        IDatabase redis,
        NpgsqlDataSource dataSource,
        TimeSpan ttl,
        bool enableInMemory = false,
        int maxInMemorySize = 10000,
        ILogger<RedisCommandTokenCache>? logger = null)
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
    ///     Get or create a command token. Returns token hash for storage.
    /// </summary>
    public async Task<string> GetOrCreateTokenAsync(string message, string? kind, string? metadataJson = null,
        CancellationToken ct = default)
    {
        var hash = ComputeHash(message, kind);

        // Tier 1: In-memory cache (if enabled)
        if (_enableInMemory && _memCache!.TryGetValue(hash, out _))
        {
            await IncrementOccurrenceAsync(hash, ct);
            return hash;
        }

        // Tier 2: Redis cache
        var redisKey = $"command_token:{hash}";
        var redisValue = await _redis.StringGetAsync(redisKey);

        if (redisValue.HasValue)
        {
            // Token exists in Redis
            await IncrementOccurrenceAsync(hash, ct);

            if (_enableInMemory)
            {
                CacheInMemory(hash, message);
            }

            return hash;
        }

        // Tier 3: PostgresSQL (check if token was created before Redis TTL expired)
        var existsInDb = await TokenExistsInDbAsync(hash, ct);
        if (existsInDb)
        {
            // Restore to Redis with new TTL
            await _redis.StringSetAsync(redisKey, message, _ttl);
            await IncrementOccurrenceAsync(hash, ct);

            if (_enableInMemory)
            {
                CacheInMemory(hash, message);
            }

            return hash;
        }

        // Token doesn't exist anywhere - create new
        await CreateNewTokenAsync(hash, message, kind, metadataJson, ct);

        if (_enableInMemory)
        {
            CacheInMemory(hash, message);
        }

        return hash;
    }

    /// <summary>
    ///     Batch resolves token hashes to messages (for reading commands).
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
            hash => batch.StringGetAsync($"command_token:{hash}")
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
                await _redis.StringSetAsync($"command_token:{hash}", message, _ttl);

                // Cache in memory if enabled
                if (_enableInMemory)
                {
                    CacheInMemory(hash, message);
                }
            }
        }

        return result;
    }

    private async Task CreateNewTokenAsync(string hash, string message, string? kind, string? metadataJson,
        CancellationToken ct)
    {
        // CRITICAL: Store in PostgreSQL FIRST before Redis
        // This ensures FK constraints are satisfied before any code can read the token from Redis
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Use explicit transaction to ensure commit is visible before caching in Redis
            await using var transaction = await conn.BeginTransactionAsync(ct);

            // Extract common metadata if present (remove instance-specific fields)
            var commonMetadataJson = ExtractCommonMetadata(metadataJson);

            await using var cmd = new NpgsqlCommand(InsertTokenSql, conn, transaction);
            cmd.Parameters.AddWithValue(hash);
            cmd.Parameters.AddWithValue(message);
            cmd.Parameters.AddWithValue((object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, (object?)commonMetadataJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue(DateTime.UtcNow);
            cmd.Parameters.AddWithValue(DateTime.UtcNow);

            var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Created/updated command token {Hash}, rows affected: {Rows}", hash, rowsAffected);

            // Commit transaction - ensures token is visible to other connections before caching
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist command token {Hash} to PostgreSQL - command_tokens table may not exist.", hash);
            throw; // Re-throw to surface the error
        }

        // Store in Redis with TTL AFTER successful PostgreSQL write and commit
        var redisKey = $"command_token:{hash}";
        await _redis.StringSetAsync(redisKey, message, _ttl);
    }

    private async Task IncrementOccurrenceAsync(string hash, CancellationToken ct)
    {
        // Refresh Redis TTL
        await _redis.KeyExpireAsync($"command_token:{hash}", _ttl);

        // Increment PostgreSQL counter asynchronously (fire-and-forget for performance)
        // This is analytics-only, so we don't want to block command processing
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);

                await using var cmd = new NpgsqlCommand(UpdateOccurrenceSql, conn);
                cmd.Parameters.AddWithValue(hash);
                cmd.Parameters.AddWithValue(DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                // Changed from silent swallow to warning log
                _logger.LogWarning(ex, "Failed to increment occurrence count for command token {Hash}", hash);
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

    private static string ComputeHash(string message, string? kind)
    {
        var combined = kind != null ? $"{kind}:{message}" : message;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    ///     Extracts common metadata from the full metadata JSON, removing instance-specific fields
    ///     that vary per command (to avoid duplication in command_tokens table)
    /// </summary>
    private string? ExtractCommonMetadata(string? metadataJson)
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

            // Remove instance-specific fields that vary per command
            metadata.Remove("timestamp"); // Timestamp varies per occurrence
            metadata.Remove("runId"); // Run ID varies per occurrence
            metadata.Remove("testId"); // Test ID varies per occurrence
            metadata.Remove("browserId"); // Browser ID varies per instance
            // Keep only truly common metadata fields:
            // - kind (command type - already stored separately)
            // - Props that are common across command instances
            // If nothing remains or only kind, return null - not worth storing
            return metadata.Count <= 1 ? null : JsonSerializer.Serialize(metadata);
        }
        catch (JsonException ex)
        {
            // If we can't parse the metadata, return null
            _logger.LogError(ex, "Failed to parse metadata JSON: {Json}", metadataJson);
            return null;
        }
    }

    private record TokenMetadata(string Hash, string Message, DateTime FirstSeen, DateTime LastSeen, long Count);
}
