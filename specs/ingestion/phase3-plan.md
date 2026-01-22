# Phase 3: Batching & Optimization - Implementation Plan

## Status
- ✅ COPY bulk inserts (Phase 1)
- ✅ Batch processing with timeout (Phase 1)
- ✅ Concurrent workers (Phase 1)
- ❌ **Log token optimization** ← Phase 3 focus

## Log Token Optimization

**Problem**: 90%+ storage waste from repeated log messages
**Solution**: Hash-based deduplication - store unique messages once, reference by token

### Files to Create (3)

1. **Migration: `ingestion/Infrastructure/Adapters/Results/Migrations/V24__log_tokens.sql`**
```sql
CREATE TABLE log_tokens (
    token_hash TEXT PRIMARY KEY,
    message TEXT NOT NULL,
    logger_name TEXT,
    first_seen_at TIMESTAMPTZ NOT NULL,
    last_seen_at TIMESTAMPTZ NOT NULL,
    occurrence_count BIGINT DEFAULT 1,
    metadata_json JSONB
);

ALTER TABLE log_items ADD COLUMN token_hash TEXT REFERENCES log_tokens(token_hash);
CREATE INDEX idx_log_items_token_hash ON log_items(token_hash);
CREATE INDEX idx_log_tokens_last_seen ON log_tokens(last_seen_at);
```

2. **`ingestion/Infrastructure/LogTokenCache.cs`** (120 lines)
```csharp
public sealed class LogTokenCache
{
    private readonly ConcurrentDictionary<string, bool> _cache = new();
    private readonly NpgsqlConnection _conn;

    public async Task<bool> TokenExistsAsync(string hash)
    {
        if (_cache.ContainsKey(hash)) return true;
        var exists = await CheckDbAsync(hash);
        if (exists) _cache.TryAdd(hash, true);
        return exists;
    }

    public async Task<string> UpsertTokenAsync(string message, string? logger)
    {
        var hash = ComputeHash(message);
        if (await TokenExistsAsync(hash))
        {
            await IncrementCountAsync(hash);
            return hash;
        }
        await InsertTokenAsync(hash, message, logger);
        _cache.TryAdd(hash, true);
        return hash;
    }

    private static string ComputeHash(string message)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)));
}
```

3. **Modify: `ingestion/Infrastructure/PostgresBatchWriter.cs`**
   - Add `LogTokenCache` dependency
   - Update `WriteLogItemsAsync`:
     - For each log item: `var token = await _tokenCache.UpsertTokenAsync(message, logger)`
     - Insert log_items with `token_hash` instead of full `message`

### Configuration

Add to `.env`:
```bash
LOG_TOKEN_CACHE_SIZE=100000  # In-memory cache size
LOG_TOKEN_TTL_DAYS=90        # Clean tokens not seen in 90 days
```

### Benefits

- **Storage**: 90%+ reduction (50 bytes vs 200+ bytes per log)
- **Indexing**: Faster queries on token_hash (8 bytes) vs full message
- **Deduplication**: Automatic across all test runs
- **Analytics**: `occurrence_count` shows most common messages

### Migration Strategy

1. Add schema (V24 migration)
2. Deploy ingestion service (writes to both columns)
3. Backfill existing logs (optional background job)
4. Drop old `message` column after verification

## Build Steps

1. Create migration file
2. Create LogTokenCache class
3. Modify PostgresBatchWriter
4. Build & test
5. Update documentation

**Time Estimate**: 90 minutes
