# Event-Driven Architecture Proposal for Agenix Playwright Grid

**Status:** Proposal
**Date:** 2025-01-11
**Author:** Architecture Review

---

## Executive Summary

This document proposes migrating **HIGH-VOLUME write operations** (test items and command logs) 
from synchronous database writes to an event-driven architecture using RabbitMQ. 
This will significantly improve scalability, resilience, and performance under high load. **All other operations** 
(launches, admin) remain as direct database writes for simplicity and immediate consistency.

---

## Current Architecture Issues

### Current Data Flow
**Test Runners/Workers → Hub (Synchronous HTTP) → PostgreSQL**

### Components Flow
1. **Test runners** submit data via HTTP POST:
   - `POST /results/browser/{browserId}/commands` - Protocol logs from workers
   - Borrow/Return operations that call `UpsertRunAsync()` synchronously

2. **Hub endpoints** block on database writes:
   - `LaunchesEndpoints.cs` - Direct Postgres writes for launch CRUD (lines 206-312)
   - `EndpointMappingExtensions.cs` - Synchronous calls to `resultsStore.UpsertRunAsync()` and `resultsStore.AppendCommandAsync()`
   - Every HTTP request waits for Postgres INSERT/UPDATE to complete

3. **Scalability bottlenecks identified**:
   - Database connection pool exhaustion under high concurrency
   - Increased latency for test runners (they wait for DB writes)
   - Hub becomes write-heavy bottleneck for all projects
   - No horizontal scaling capability (stateful writes)
   - Database locks/contention on high insert rate tables (runs, commands, launches)
   - Single point of failure for write operations

---

## Scope & Exclusions

### Operations Included in Event-Driven Architecture ✅

**High-volume operations that benefit from async processing:**

1. **Test Item Create/Update/Finish** ✅
   - Volume: 100-1,000+ per launch
   - Frequency: Continuous during test execution
   - Can be eventually consistent (no immediate validation required)
   - Benefits: Batching, horizontal scaling, backpressure handling
   - **Primary optimization target**

2. **Command Log Appends** ✅
   - Volume: 10,000+ per test run
   - Frequency: Very high (protocol logs, CDP messages)
   - Current bottleneck: ~500 commands/sec
   - Benefits: 20x throughput improvement via batching
   - **Biggest performance bottleneck - critical to optimize**

3. **Log Item Appends** ✅
   - Volume: 1,000-10,000+ per test item
   - Frequency: Very high (test logs, framework logs, application logs)
   - Current issue: Storage bloat from repeated log messages
   - Benefits:
     - 20x throughput improvement via batching
     - **90%+ storage reduction via optimization token strategy**
     - Horizontal scaling for log ingestion
   - **High-volume operation with unique optimization opportunity**

### Operations Excluded from Event-Driven Architecture ❌

**Operations that remain as direct database writes:**

1. **Launch Create/Update/Finish** ❌
   - Volume: 1 per test suite (low volume)
   - **Validation Required:** Test items check if launch is in terminal state before creation:
   ```csharp
   // TestItemsEndpoints.cs line 233
   var (isTerminal, launchStatus) = await IsLaunchInTerminalStateAsync(launchId, db);
   if (isTerminal)
       return Results.Conflict(new { error = "Launch is in terminal state" });
   ```
   - **Reason:** Synchronous validation prevents test item creation on finished launches
   - **Trade-off:** Acceptable - launches are infrequent, not a bottleneck

2. **Suite Create/Update** ❌
   - Volume: 1-10 per launch (low volume)
   - Requires immediate consistency for test item association
   - Not a performance bottleneck

3. **Admin Operations** ❌
   - Volume: Manual operations (very low)
   - User expects immediate feedback
   - No performance concern

### Why Selective Event-Driven Architecture?

**Principle:** Use events for operations where:
- ✅ Volume justifies optimization (100+ ops/sec)
- ✅ Eventually consistent is acceptable
- ✅ Benefits from batching/buffering
- ✅ Horizontal scaling needed

**Don't use events when:**
- ❌ Volume is low (<10 ops/min)
- ❌ Immediate validation required (terminal state checks)
- ❌ Synchronous response needed for correctness
- ❌ Optimization complexity outweighs benefits

---

## Proposed Solution: Event-Driven Architecture with Message Broker

### New Architecture: Hub → Message Broker → Ingestion Service → PostgreSQL

```
┌─────────────────────────┐
│  Test Runners/Workers   │
└───────────┬─────────────┘
            │ HTTP POST (fast ACK)
            ↓
┌─────────────────────────┐
│   Hub (API Gateway)     │
│  - Validates requests   │
│  - Publishes to queue   │
│  - Returns 202 Accepted │
└───────────┬─────────────┘
            │ Async publish
            ↓
┌─────────────────────────┐
│   RabbitMQ/Kafka/NATS   │
│  - Durable queues       │
│  - Message persistence  │
│  - Dead letter queues   │
└───────────┬─────────────┘
            │ Consumer groups
            ↓
┌─────────────────────────┐
│  Ingestion Service      │
│  - Batch processing     │
│  - Error handling       │
│  - Retry logic          │
└───────────┬─────────────┘
            │ Batch writes
            ↓
┌─────────────────────────┐
│      PostgreSQL         │
│  - Optimized inserts    │
│  - Reduced connections  │
└─────────────────────────┘
```

### Technology Stack Options

#### Option A: RabbitMQ ⭐ **RECOMMENDED**

**Pros:**
- ✅ Simple setup, reliable, mature
- ✅ Built-in durability and acknowledgments
- ✅ Dead letter queues for error handling
- ✅ Lower operational complexity than Kafka
- ✅ Excellent .NET client (RabbitMQ.Client)
- ✅ Well-documented patterns for work queues
- ✅ Management UI included (port 15672)

**Cons:**
- ⚠️ Lower throughput than Kafka (but sufficient for test results)
- ⚠️ Not designed for event streaming/replay

**Use Case Fit:** Perfect for work queue patterns where we need reliable delivery and processing of individual messages.

---

#### Option B: Apache Kafka

**Pros:**
- ✅ Highest throughput and durability
- ✅ Built for event streaming
- ✅ Better for event replay and analytics
- ✅ Horizontal scaling via partitions
- ✅ Long-term event retention

**Cons:**
- ⚠️ Higher operational complexity (Zookeeper/KRaft, partitions)
- ⚠️ Heavier resource footprint
- ⚠️ Steeper learning curve
- ⚠️ Overkill for simple work queue patterns

**Use Case Fit:** Better if we need event sourcing, analytics, or event replay capabilities.

---

#### Option C: NATS/NATS JetStream

**Pros:**
- ✅ Extremely lightweight and fast
- ✅ Cloud-native, simple ops
- ✅ Low resource usage
- ✅ Good .NET client

**Cons:**
- ⚠️ Less mature ecosystem than RabbitMQ/Kafka
- ⚠️ Simpler persistence model
- ⚠️ Smaller community

**Use Case Fit:** Good for microservices communication, but less proven for durable work queues.

---

### **Recommendation: RabbitMQ**

Best balance of reliability, simplicity, and operational maturity for our use case.

---

## Implementation Plan

### Phase 1: Create Ingestion Service

**New Service: `ingestion/` (similar structure to hub/worker)**

#### Service Components

1. **Message Consumer** - Consumes from RabbitMQ queues
   - Multi-threaded consumption
   - Automatic reconnection
   - Prefetch control for backpressure

2. **Batch Writer** - Buffers and batch-writes to Postgres
   - Time-based batching (e.g., 1 second)
   - Size-based batching (e.g., 200 messages)
   - Transaction boundaries
   - COPY command for bulk inserts

3. **Error Handler** - Retries + Dead Letter Queue
   - Exponential backoff
   - Max retry count (e.g., 3 attempts)
   - Move to DLQ after max retries
   - Alert on DLQ depth threshold

4. **Health Checks** - Monitor queue depth, lag, errors
   - RabbitMQ connection health
   - Database connection health
   - Queue depth metrics
   - Consumer lag metrics
   - Error rate tracking

#### Queue Structure

```yaml
Queues:
  - playwright-grid.test-items
      Purpose: Test item create/update/finish events
      Consumer: Test item writer
      Batch size: 200
      Note: High volume - primary optimization target

  - playwright-grid.commands
      Purpose: Command/log append events
      Consumer: Command writer
      Batch size: 500 (very high volume)
      Note: Highest volume queue - biggest bottleneck

  - playwright-grid.log-items
      Purpose: Log item append events (test logs, framework logs)
      Consumer: Log item writer with token optimization
      Batch size: 300
      Note: High volume with 90%+ storage reduction via token strategy

  - playwright-grid.dlq
      Purpose: Dead letter queue for failed messages
      Consumer: Manual inspection/replay

Note: Only test items, commands, and log items are queued. All other operations
(launches, suites, API logs, test cases) use direct database writes.
```

#### Message Format

```csharp
public record TestItemEvent
{
    public string EventType { get; init; } = "TestItemCreated"; // or TestItemUpdated, TestItemFinished
    public Guid ItemId { get; init; }
    public Guid LaunchId { get; init; }
    public TestItemDto Data { get; init; } = new();
    public DateTime TimestampUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}

public record CommandEvent
{
    public string EventType { get; init; } = "CommandAppended";
    public string RunId { get; init; } = string.Empty;
    public CommandLogEventDto Data { get; init; } = new();
    public DateTime TimestampUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}

public record LogItemEvent
{
    public string EventType { get; init; } = "LogItemAppended";
    public Guid ItemId { get; init; }                    // Test item ID
    public Guid LaunchId { get; init; }                  // Launch ID for partitioning
    public string Level { get; init; } = "Info";         // Trace, Debug, Info, Warn, Error, Fatal
    public string Message { get; init; } = string.Empty; // Full log message
    public DateTime TimestampUtc { get; init; }          // Log timestamp
    public string? LoggerName { get; init; }             // Logger name (e.g., "Playwright.Browser")
    public Dictionary<string, string>? Metadata { get; init; } // Additional key-value metadata
    public string CorrelationId { get; init; } = string.Empty;

    // Optimization Token Strategy fields (computed by ingestion service)
    // NOTE: These are populated by the ingestion service, not by the publisher
    public string? TokenHash { get; set; }               // SHA256 hash of message (for deduplication)
    public bool IsFirstOccurrence { get; set; }          // True if this is first time seeing this message
}

// Note: Only 3 event types needed - test items, commands, and log items
// All other operations (launches, suites, API logs, test cases) use direct DB writes
```

---

### Phase 2: Modify Hub to Publish Events

**Changes to Hub:**

#### 1. Add RabbitMQ client

**Package:** `RabbitMQ.Client` (NuGet)

```bash
dotnet add hub/PlaywrightHub.csproj package RabbitMQ.Client
```

#### 2. Create Message Publisher abstraction

```csharp
// hub/Application/Ports/IEventPublisher.cs
namespace PlaywrightHub.Application.Ports;

public interface IEventPublisher
{
    Task PublishTestItemEventAsync(TestItemEvent evt, CancellationToken ct = default);
    Task PublishCommandEventAsync(CommandEvent evt, CancellationToken ct = default);
}

// hub/Infrastructure/Adapters/Messaging/RabbitMqEventPublisher.cs
public class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly JsonSerializerOptions _jsonOptions;

    public RabbitMqEventPublisher(string connectionString)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare exchanges and queues
        DeclareInfrastructure();
    }

    public Task PublishTestItemEventAsync(TestItemEvent evt, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOptions);
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel.CreateBasicProperties();
        props.Persistent = true; // Durable messages
        props.ContentType = "application/json";
        props.CorrelationId = evt.CorrelationId;

        _channel.BasicPublish(
            exchange: "playwright-grid",
            routingKey: "test-items",
            basicProperties: props,
            body: body
        );

        return Task.CompletedTask;
    }

    // Similar for other event types...
}

// Note: Launches continue using direct PostgreSQL writes (not published to queue)
```

#### 3. Modify endpoints to publish instead of write

**Test Items (Async via Events):**

**Before:**
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs
await resultsStore.UpsertTestItemAsync(testItem);
return Results.Ok(testItem);
```

**After:**
```csharp
// Publish event to queue
var testItemEvent = new TestItemEvent
{
    EventType = "TestItemCreated",
    ItemId = testItem.Id,
    LaunchId = testItem.LaunchId,
    Data = testItem,
    TimestampUtc = DateTime.UtcNow,
    CorrelationId = Guid.NewGuid().ToString()
};

await eventPublisher.PublishTestItemEventAsync(testItemEvent);

// Return 202 Accepted (async processing)
return Results.Accepted($"/api/test-items/{testItem.Id}", testItem);
```

**Status code change:** `200 OK` → `202 Accepted` (indicates async processing)

---

**Launches (Synchronous Direct Writes):**

**No Changes Required:**
```csharp
// hub/Infrastructure/Web/LaunchesEndpoints.cs (line 269)
// Continues using direct PostgreSQL writes
await cmd.ExecuteNonQueryAsync();
return Results.Created($"/api/launches/{id}", launch);
```

**Why No Changes:** Launches require immediate consistency for terminal state validation:
```csharp
// TestItemsEndpoints.cs checks launch state BEFORE accepting test item
var (isTerminal, launchStatus) = await IsLaunchInTerminalStateAsync(launchId, db);
if (isTerminal)
    return Results.Conflict(new { error = "Launch is in terminal state" });
```

**Status code:** Remains `201 Created` (synchronous processing)

#### 4. Add circuit breaker pattern

```csharp
public class ResilientEventPublisher : IEventPublisher
{
    private readonly IEventPublisher _primary;
    private readonly IResultsStore _fallback;
    private readonly CircuitBreakerPolicy _circuitBreaker;

    public async Task PublishTestItemEventAsync(TestItemEvent evt, CancellationToken ct = default)
    {
        try
        {
            await _circuitBreaker.ExecuteAsync(async () =>
            {
                await _primary.PublishTestItemEventAsync(evt, ct);
            });
        }
        catch (BrokenCircuitException)
        {
            // Circuit is open, use fallback (direct DB write)
            _logger.LogWarning("Circuit breaker open, falling back to direct DB write");
            await _fallback.UpsertTestItemAsync(evt.Data);
        }
    }

    // Similar for commands event type
}
```

---

### Phase 3: Implement Batching & Optimization

**Ingestion Service Optimizations:**

#### 1. Batch Processing

```csharp
public class BatchWriter<T>
{
    private readonly List<T> _buffer = new();
    private readonly SemaphoreSlim _lock = new(1);
    private readonly int _maxBatchSize;
    private readonly TimeSpan _maxBatchAge;
    private DateTime _batchStartTime;

    public async Task AddAsync(T item)
    {
        await _lock.WaitAsync();
        try
        {
            _buffer.Add(item);

            if (_buffer.Count == 1)
                _batchStartTime = DateTime.UtcNow;

            bool shouldFlush = _buffer.Count >= _maxBatchSize ||
                               DateTime.UtcNow - _batchStartTime >= _maxBatchAge;

            if (shouldFlush)
            {
                await FlushAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task FlushAsync()
    {
        if (_buffer.Count == 0) return;

        var batch = _buffer.ToList();
        _buffer.Clear();

        await WriteBatchToDbAsync(batch);
    }
}
```

#### 2. COPY command for bulk inserts

```csharp
// Use Npgsql's COPY for high-throughput inserts
public async Task BulkInsertCommandsAsync(List<CommandLogEventDto> commands)
{
    await using var conn = _dataSource.CreateConnection();
    await conn.OpenAsync();

    await using var writer = conn.BeginBinaryImport(
        "COPY commands (run_id, timestamp_utc, kind, message, props_json, test_id, expires_at) " +
        "FROM STDIN (FORMAT BINARY)"
    );

    foreach (var cmd in commands)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(cmd.RunId, NpgsqlDbType.Text);
        await writer.WriteAsync(cmd.TimestampUtc, NpgsqlDbType.TimestampTz);
        await writer.WriteAsync(cmd.Kind ?? "", NpgsqlDbType.Text);
        await writer.WriteAsync(cmd.Message ?? "", NpgsqlDbType.Text);

        var propsJson = cmd.Props == null ? null : JsonSerializer.Serialize(cmd.Props);
        await writer.WriteAsync(propsJson, NpgsqlDbType.Text);
        await writer.WriteAsync(cmd.TestId, NpgsqlDbType.Text);
        await writer.WriteAsync(DBNull.Value, NpgsqlDbType.TimestampTz);
    }

    await writer.CompleteAsync();
}
```

**Performance gain:** 10-100x faster than individual INSERTs

#### 3. Concurrent workers

```csharp
// Multiple consumer threads per queue
var consumerCount = config.GetValue<int>("CONSUMER_CONCURRENCY", 4);

for (int i = 0; i < consumerCount; i++)
{
    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.Received += async (sender, ea) =>
    {
        await ProcessMessageAsync(ea.Body.ToArray());
        channel.BasicAck(ea.DeliveryTag, multiple: false);
    };

    channel.BasicConsume(
        queue: "playwright-grid.commands",
        autoAck: false,
        consumer: consumer
    );
}
```

**Horizontal scaling:** Deploy multiple ingestion service instances

---

### Phase 4: Dashboard Real-time Updates

**SignalR Bridge in Ingestion Service:**

After writing to DB, publish to SignalR hub so dashboard gets real-time updates:

```csharp
public class SignalRNotifier
{
    private readonly IHubContext<ResultsHub> _hubContext;

    public async Task NotifyRunUpdateAsync(ResultRunSummaryDto run)
    {
        await _hubContext.Clients.Group($"run:{run.RunId}").SendAsync("RunUpdate", run);
    }

    public async Task NotifyCommandLogChunkAsync(string runId, CommandLogEventDto[] commands)
    {
        await _hubContext.Clients.Group($"run:{runId}").SendAsync("CommandLogChunk", commands);
    }
}

// In BatchWriter after DB write:
await WriteBatchToDbAsync(batch);
foreach (var run in batch)
{
    await _signalRNotifier.NotifyRunUpdateAsync(run);
}
```

This maintains current dashboard real-time UX while decoupling write path.

---

### Phase 5: Optimization Token Strategy for Log Items

**Problem:** Test execution generates massive volumes of repeated log messages:
- "Browser launched successfully" (1000s of times across tests)
- "Navigated to https://example.com" (repeated per test)
- "Click button[id='submit']" (common action)
- Framework startup messages (identical per test)

Storing each log message in full wastes 90%+ of storage space.

#### Solution: Token-Based Deduplication

**Core Concept:** Store each unique log message once, reference it by hash token for subsequent occurrences.

```
┌─────────────────────────────────────────────────────────────────┐
│  Log Message Flow with Token Strategy                           │
└─────────────────────────────────────────────────────────────────┘

Ingestion Service Receives LogItemEvent:
  │
  ├─→ Compute Message Hash (SHA256/xxHash)
  │    "Browser launched successfully" → Token: "a1b2c3d4..."
  │
  ├─→ Check Token Cache (in-memory + Redis)
  │    │
  │    ├─→ Token EXISTS (cache hit)
  │    │    └─→ Insert into log_items: (item_id, token_hash, timestamp, level)
  │    │         Storage: ~50 bytes (no message duplication)
  │    │
  │    └─→ Token MISSING (cache miss)
  │         ├─→ Insert into log_tokens: (token_hash, message, first_seen_at, occurrence_count)
  │         │    Storage: ~200 bytes (full message stored once)
  │         └─→ Insert into log_items: (item_id, token_hash, timestamp, level)
  │              Storage: ~50 bytes
  │         └─→ Add token to cache
```

#### Database Schema

```sql
-- Token dictionary: Unique log messages stored once
CREATE TABLE IF NOT EXISTS log_tokens (
    token_hash TEXT PRIMARY KEY,              -- SHA256 hash of message
    message TEXT NOT NULL,                    -- Full log message (stored once)
    logger_name TEXT,                         -- Logger that produced this message
    first_seen_at TIMESTAMPTZ NOT NULL,       -- When first encountered
    last_seen_at TIMESTAMPTZ NOT NULL,        -- Last occurrence (for TTL)
    occurrence_count BIGINT DEFAULT 1,        -- How many times seen across all tests
    metadata_json JSONB,                      -- Common metadata for this message
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_log_tokens_last_seen ON log_tokens(last_seen_at);
CREATE INDEX idx_log_tokens_occurrence ON log_tokens(occurrence_count DESC);

-- Log item references: One row per log occurrence
CREATE TABLE IF NOT EXISTS log_items (
    id BIGSERIAL PRIMARY KEY,
    item_id UUID NOT NULL,                    -- Test item this log belongs to
    launch_id UUID NOT NULL,                  -- Launch for partitioning/cleanup
    token_hash TEXT NOT NULL,                 -- Reference to log_tokens
    level TEXT NOT NULL,                      -- Trace, Debug, Info, Warn, Error, Fatal
    timestamp_utc TIMESTAMPTZ NOT NULL,       -- When log occurred
    metadata_json JSONB,                      -- Per-occurrence metadata (if different)
    created_at TIMESTAMPTZ DEFAULT NOW(),

    FOREIGN KEY (token_hash) REFERENCES log_tokens(token_hash)
);

CREATE INDEX idx_log_items_item_id ON log_items(item_id);
CREATE INDEX idx_log_items_launch_id ON log_items(launch_id);
CREATE INDEX idx_log_items_level ON log_items(level);
CREATE INDEX idx_log_items_timestamp ON log_items(timestamp_utc DESC);
CREATE INDEX idx_log_items_token_hash ON log_items(token_hash);
```

#### Token Cache Implementation

```csharp
// Ingestion service component
public class LogTokenCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly NpgsqlDataSource _postgres;
    private readonly SemaphoreSlim _lock = new(1);

    public async Task<bool> TokenExistsAsync(string tokenHash)
    {
        // Level 1: In-memory cache (fastest - microseconds)
        if (_memoryCache.TryGetValue($"token:{tokenHash}", out _))
            return true;

        // Level 2: Redis cache (fast - milliseconds)
        var redisDb = _redis.GetDatabase();
        if (await redisDb.KeyExistsAsync($"token:{tokenHash}"))
        {
            // Promote to memory cache
            _memoryCache.Set($"token:{tokenHash}", true, TimeSpan.FromMinutes(15));
            return true;
        }

        // Level 3: PostgreSQL (slower - tens of milliseconds)
        await using var conn = _postgres.CreateConnection();
        var exists = await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM log_tokens WHERE token_hash = @hash)",
            new { hash = tokenHash }
        );

        if (exists)
        {
            // Promote to Redis and memory cache
            await redisDb.StringSetAsync($"token:{tokenHash}", "1", TimeSpan.FromHours(24));
            _memoryCache.Set($"token:{tokenHash}", true, TimeSpan.FromMinutes(15));
        }

        return exists;
    }

    public async Task RegisterTokenAsync(string tokenHash, string message, string? loggerName)
    {
        await _lock.WaitAsync();
        try
        {
            // Insert into PostgreSQL (idempotent - ON CONFLICT DO UPDATE)
            await using var conn = _postgres.CreateConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO log_tokens (token_hash, message, logger_name, first_seen_at, last_seen_at)
                VALUES (@hash, @msg, @logger, NOW(), NOW())
                ON CONFLICT (token_hash) DO UPDATE
                SET last_seen_at = NOW(),
                    occurrence_count = log_tokens.occurrence_count + 1
            ", new { hash = tokenHash, msg = message, logger = loggerName });

            // Cache in Redis and Memory
            var redisDb = _redis.GetDatabase();
            await redisDb.StringSetAsync($"token:{tokenHash}", "1", TimeSpan.FromHours(24));
            _memoryCache.Set($"token:{tokenHash}", true, TimeSpan.FromMinutes(15));
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

#### Batch Processing with Token Strategy

```csharp
public class LogItemBatchWriter
{
    private readonly LogTokenCache _tokenCache;
    private readonly NpgsqlDataSource _postgres;

    public async Task WriteBatchAsync(List<LogItemEvent> logEvents)
    {
        // Step 1: Compute token hashes for all events
        foreach (var evt in logEvents)
        {
            evt.TokenHash = ComputeTokenHash(evt.Message);
            evt.IsFirstOccurrence = !await _tokenCache.TokenExistsAsync(evt.TokenHash);
        }

        // Step 2: Register new tokens (first occurrences only)
        var newTokens = logEvents.Where(e => e.IsFirstOccurrence).ToList();
        foreach (var evt in newTokens)
        {
            await _tokenCache.RegisterTokenAsync(evt.TokenHash, evt.Message, evt.LoggerName);
        }

        // Step 3: Bulk insert log item references (all events)
        await using var conn = _postgres.CreateConnection();
        await conn.OpenAsync();

        await using var writer = conn.BeginBinaryImport(
            "COPY log_items (item_id, launch_id, token_hash, level, timestamp_utc, metadata_json) " +
            "FROM STDIN (FORMAT BINARY)"
        );

        foreach (var evt in logEvents)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(evt.ItemId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(evt.LaunchId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(evt.TokenHash, NpgsqlDbType.Text);
            await writer.WriteAsync(evt.Level, NpgsqlDbType.Text);
            await writer.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz);

            var metadataJson = evt.Metadata == null ? null : JsonSerializer.Serialize(evt.Metadata);
            await writer.WriteAsync(metadataJson, NpgsqlDbType.Jsonb);
        }

        await writer.CompleteAsync();
    }

    private string ComputeTokenHash(string message)
    {
        // Use xxHash for speed (or SHA256 for cryptographic strength)
        using var hash = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(message);
        var hashBytes = hash.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }
}
```

#### Query Patterns

**Retrieve logs for a test item (with message resolution):**
```sql
SELECT
    li.id,
    li.item_id,
    li.level,
    li.timestamp_utc,
    lt.message,          -- Full message retrieved from token table
    lt.logger_name,
    li.metadata_json
FROM log_items li
JOIN log_tokens lt ON li.token_hash = lt.token_hash
WHERE li.item_id = '12345678-abcd-...'
ORDER BY li.timestamp_utc ASC;
```

**Find most common log messages (analytics):**
```sql
SELECT
    token_hash,
    message,
    occurrence_count,
    first_seen_at,
    last_seen_at
FROM log_tokens
ORDER BY occurrence_count DESC
LIMIT 100;
```

**Storage saved per token:**
```sql
SELECT
    token_hash,
    message,
    occurrence_count,
    -- Savings: (full message size) × (occurrence count - 1)
    LENGTH(message) * (occurrence_count - 1) AS bytes_saved
FROM log_tokens
ORDER BY bytes_saved DESC
LIMIT 20;
```

#### Performance Benefits

**Example Scenario:** 10,000 test items, each logging 500 messages, 80% repeated

**Without Token Strategy:**
- Total logs: 10,000 items × 500 logs = 5,000,000 rows
- Average message size: 200 bytes
- Storage: 5,000,000 × 200 bytes = 1 GB

**With Token Strategy:**
- Unique messages: 100 tokens (20% of messages are unique)
- Token storage: 100 × 200 bytes = 20 KB
- Log item references: 5,000,000 × 50 bytes = 250 MB
- **Total storage: 250.02 MB (75% reduction)**

**Real-world observations:**
- Browser startup logs: 95%+ deduplication
- Navigation logs: 80%+ deduplication
- Framework logs: 90%+ deduplication
- **Overall: 70-90% storage reduction**

#### Token Lifecycle Management

**TTL Policy:**
```csharp
// Background cleanup job (runs daily)
public async Task CleanupExpiredTokensAsync()
{
    await using var conn = _postgres.CreateConnection();

    // Delete tokens not seen in 30 days
    var deleted = await conn.ExecuteAsync(@"
        DELETE FROM log_tokens
        WHERE last_seen_at < NOW() - INTERVAL '30 days'
    ");

    _logger.LogInformation("Cleaned up {Count} expired log tokens", deleted);
}
```

**Cache Eviction:**
- Memory cache: 15 minutes (frequently accessed tokens)
- Redis cache: 24 hours (medium-term storage)
- PostgreSQL: 30 days TTL (long-term storage)

#### Benefits Summary

| Benefit | Impact |
|---------|--------|
| **Storage Reduction** | 70-90% less disk space |
| **Faster Writes** | 10-20x throughput via batching |
| **Analytics** | Easy to find most common logs |
| **Cost Savings** | Reduced database storage costs |
| **Cache Efficiency** | Hot tokens stay in memory |
| **Horizontal Scaling** | Multiple ingestion instances share token cache (Redis) |

---

## Configuration Changes

### Hub Environment Variables (New)

```bash
# Message Broker Selection
MESSAGE_BROKER=rabbitmq  # options: rabbitmq, kafka, nats, direct

# RabbitMQ Configuration
RABBITMQ_URL=amqp://rabbitmq:5672
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
RABBITMQ_VHOST=/
RABBITMQ_EXCHANGE=playwright-grid
RABBITMQ_HEARTBEAT_SECONDS=60

# Circuit Breaker Configuration
ENABLE_DIRECT_DB_FALLBACK=true
CIRCUIT_BREAKER_FAILURE_THRESHOLD=5
CIRCUIT_BREAKER_TIMEOUT_SECONDS=30

# Publishing Configuration
PUBLISH_TIMEOUT_MS=1000
PUBLISH_RETRY_COUNT=3
```

### Ingestion Service Environment Variables

```bash
# RabbitMQ Configuration
RABBITMQ_URL=amqp://rabbitmq:5672
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
RABBITMQ_PREFETCH_COUNT=100

# Database Configuration
POSTGRES_CONNECTION_STRING=Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid
POSTGRES_MAX_POOL_SIZE=50

# Batch Processing
BATCH_SIZE_LAUNCHES=50
BATCH_SIZE_RUNS=200
BATCH_SIZE_COMMANDS=500
BATCH_SIZE_TESTS=200
BATCH_SIZE_LOG_ITEMS=300
BATCH_TIMEOUT_MS=1000

# Log Token Cache Configuration
LOG_TOKEN_CACHE_SIZE_MB=128
LOG_TOKEN_CACHE_TTL_MINUTES=15
LOG_TOKEN_REDIS_TTL_HOURS=24
LOG_TOKEN_DB_TTL_DAYS=30

# Consumer Configuration
CONSUMER_CONCURRENCY=4
MAX_RETRY_ATTEMPTS=3
RETRY_DELAY_MS=1000

# SignalR Configuration
HUB_SIGNALR=http://hub:5000/ws
ENABLE_SIGNALR_NOTIFICATIONS=true

# Monitoring
ENABLE_METRICS=true
METRICS_PORT=9091
```

### docker-compose.yml Addition

```yaml
services:
  rabbitmq:
    image: rabbitmq:3.12-management
    container_name: rabbitmq
    ports:
      - "5672:5672"   # AMQP
      - "15672:15672" # Management UI
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
      RABBITMQ_DEFAULT_VHOST: /
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

  ingestion:
    build:
      context: .
      dockerfile: ingestion/Dockerfile
    environment:
      - RABBITMQ_URL=amqp://rabbitmq:5672
      - POSTGRES_CONNECTION_STRING=Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid
      - BATCH_SIZE_RUNS=200
      - BATCH_SIZE_COMMANDS=500
      - BATCH_TIMEOUT_MS=1000
      - CONSUMER_CONCURRENCY=4
      - HUB_SIGNALR=http://hub:5000/ws
    depends_on:
      rabbitmq:
        condition: service_healthy
      postgres:
        condition: service_healthy
      hub:
        condition: service_healthy
    deploy:
      replicas: 2  # Scale horizontally for high throughput
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
      interval: 10s
      timeout: 3s
      retries: 5

  hub:
    # ... existing hub config ...
    environment:
      # ... existing vars ...
      - MESSAGE_BROKER=rabbitmq
      - RABBITMQ_URL=amqp://rabbitmq:5672
      - ENABLE_DIRECT_DB_FALLBACK=true
    depends_on:
      # ... existing deps ...
      rabbitmq:
        condition: service_healthy

volumes:
  rabbitmq_data:
```

---

## Benefits

### Performance Benefits

1. **Hub becomes stateless** - No blocking DB writes, just message publishing (~1ms latency vs 10-50ms DB write)
2. **Reduced test runner latency** - Fast ACK from hub (202 Accepted), no DB wait
3. **Batch efficiency** - Reduce DB load by 10-100x via batching
4. **Higher throughput** - Process 10,000+ commands/sec vs ~500/sec currently

### Scalability Benefits

5. **Horizontal scalability** - Add more ingestion service instances for higher throughput
6. **Database connection pooling** - Ingestion service controls DB connections, preventing exhaustion
7. **Backpressure handling** - Queue absorbs spikes, ingestion service processes at sustainable rate

### Resilience Benefits

8. **Decoupled components** - Hub failure doesn't affect ingestion; ingestion failure doesn't affect hub
9. **Message durability** - Queue buffers writes if DB is slow/down; no data loss
10. **Graceful degradation** - Circuit breaker falls back to direct writes if broker down
11. **Retry logic** - Automatic retry on transient failures
12. **Dead letter queue** - Failed messages preserved for investigation

### Operational Benefits

13. **Observability** - Monitor queue depth, consumer lag, throughput metrics via RabbitMQ UI
14. **Debugging** - Replay messages from queue for testing
15. **Rate limiting** - Control ingestion rate to protect database

---

## Migration Strategy

### Stage 1: Add Ingestion Service (Dual-Write Mode) ✅ **NO DOWNTIME**

**Goal:** Deploy new components and validate event publishing WITHOUT changing existing behavior

**Configuration:**
```yaml
hub:
  environment:
    - ENABLE_EVENT_PUBLISHER=true     # Hub publishes to RabbitMQ
    - ENABLE_DUAL_WRITE=true          # Hub ALSO writes to DB (both paths active)

ingestion:
  environment:
    - ENABLE_CONSUMER=false           # Ingestion does NOT consume yet
```

**Important:** NO DUPLICATES because:
- ✅ Hub writes to DB directly (existing path)
- ✅ Hub publishes to RabbitMQ (validation only)
- ❌ Ingestion service does NOT consume (ENABLE_CONSUMER=false)
- Result: Only hub writes to database, events just flow to queue but aren't processed

**Steps:**
1. Deploy RabbitMQ container
2. Deploy Ingestion service with `ENABLE_CONSUMER=false`
3. Update hub configuration with dual-write enabled
4. Monitor RabbitMQ UI to verify events are published correctly
5. Monitor ingestion service logs (should NOT process messages)
6. Validate queue depth grows (messages accumulating, not consumed)
7. Run for 24-48 hours to validate stability

**Verification:**
```bash
# Check RabbitMQ queue depth (should grow over time)
rabbitmqctl list_queues name messages

# Check ingestion service NOT consuming
docker logs ingestion | grep "Consumer started"  # Should be disabled
```

**Rollback:** Set `ENABLE_EVENT_PUBLISHER=false`, restart hub (stops publishing, back to DB-only)

**Duration:** 1-2 days

---

### Stage 2: Switch Hub to Queue-Only ⚠️ **MINIMAL RISK**

**Goal:** Hub publishes events only, ingestion service writes to DB

**Steps:**
1. Enable `PUBLISH_ONLY_MODE=true` in hub
2. Hub stops direct DB writes
3. Set `ENABLE_DIRECT_DB_FALLBACK=true` (circuit breaker active)
4. Monitor for issues:
   - Queue depth (should stay low)
   - Ingestion service lag
   - Dashboard real-time updates
   - Error rates in logs
5. Load test with high volume
6. Run for 24-48 hours in production

**Rollback:** Set `PUBLISH_ONLY_MODE=false`, restart hub (falls back to direct writes)

**Duration:** 2-3 days (including monitoring period)

---

### Stage 3: Remove Direct DB Code 🧹 **CLEANUP**

**Goal:** Finalize migration, remove old code paths

**Steps:**
1. Remove `PostgresResultsStore.UpsertRunAsync()` calls from endpoints
2. Keep store interface for read operations only
3. Remove dual-write logic
4. Remove circuit breaker fallback (optional, can keep for safety)
5. Update documentation
6. Clean up environment variables

**Duration:** 1 day

---

## Dual-Write Mode Deep Dive

### Understanding Stage 1: No Duplicates Guarantee

**Common Misconception:** "Dual-write means both hub and ingestion write to DB, causing duplicates"

**Reality:** Stage 1 configuration ensures ONLY hub writes to database:

```yaml
# Stage 1 Configuration (Validation Phase)
hub:
  environment:
    - ENABLE_EVENT_PUBLISHER=true     # Publish events to RabbitMQ
    - ENABLE_DUAL_WRITE=true          # Continue direct DB writes (existing path)

ingestion:
  environment:
    - ENABLE_CONSUMER=false           # DO NOT consume messages yet
```

### Write Paths in Stage 1

**Hub Service:**
```csharp
// TestItemsEndpoints.cs - Stage 1 behavior
public async Task<IResult> CreateTestItem(TestItemDto item)
{
    // Path 1: Direct DB write (existing path) ✅
    if (config.GetValue<bool>("ENABLE_DUAL_WRITE"))
    {
        await resultsStore.UpsertTestItemAsync(item);
    }

    // Path 2: Publish event (validation only) ✅
    if (config.GetValue<bool>("ENABLE_EVENT_PUBLISHER"))
    {
        await eventPublisher.PublishTestItemEventAsync(new TestItemEvent { Data = item });
    }

    return Results.Ok(item);
}
```

**Ingestion Service:**
```csharp
// IngestionWorker.cs - Stage 1 behavior
public async Task StartAsync(CancellationToken ct)
{
    // Consumer is DISABLED - no messages processed ❌
    if (!config.GetValue<bool>("ENABLE_CONSUMER"))
    {
        _logger.LogInformation("Consumer disabled, not processing messages");
        return;
    }

    // This code never runs in Stage 1
    await StartConsumingAsync();
}
```

### Stage 1 Data Flow

```
Test Runner → Hub API
               ├─→ PostgreSQL (direct write) ✅ ONLY writer
               └─→ RabbitMQ (event published) ✅ Validation only

RabbitMQ Queue (growing)
    │
    └─→ Ingestion Service (consumer disabled) ❌ NOT consuming
```

**Result:** Database receives ONE write per operation (from hub only)

### Why This Approach?

**Stage 1 Goal:** Validate event publishing infrastructure without risk

1. **Validate Message Format:** Ensure events serialize/deserialize correctly
2. **Validate RabbitMQ Connection:** Hub can successfully publish
3. **Validate Queue Durability:** Messages persist in RabbitMQ
4. **Validate Event Schema:** Events contain all necessary data
5. **No Risk:** Database writes continue normally (existing path)

### Stage 2: Transition to Queue-Only

```yaml
# Stage 2 Configuration (Transition Phase)
hub:
  environment:
    - ENABLE_EVENT_PUBLISHER=true     # Continue publishing
    - ENABLE_DUAL_WRITE=false         # STOP direct DB writes ⚠️

ingestion:
  environment:
    - ENABLE_CONSUMER=true            # START consuming ✅
```

**Now:**
- ❌ Hub no longer writes to DB
- ✅ Ingestion service writes to DB
- **Still only ONE writer** (ingestion)

### Stage 2 Data Flow

```
Test Runner → Hub API
               └─→ RabbitMQ (event published) ✅ ONLY path

RabbitMQ Queue
    │
    └─→ Ingestion Service (consuming) ✅ ONLY writer
            └─→ PostgreSQL (batch writes)
```

**Result:** Database still receives ONE write per operation (now from ingestion)

### Verification Commands

**Stage 1 Verification (Dual-Write Mode):**
```bash
# Check hub is publishing
docker logs hub | grep "Published.*Event"

# Check ingestion NOT consuming
docker logs ingestion | grep "Consumer disabled"

# Check queue depth growing (messages accumulating)
rabbitmqctl list_queues name messages
# Output: playwright-grid.test-items 1234 (growing over time)

# Check database records match hub writes only
psql -c "SELECT COUNT(*) FROM test_items WHERE created_at > NOW() - INTERVAL '1 hour';"
```

**Stage 2 Verification (Queue-Only Mode):**
```bash
# Check hub NOT writing to DB
docker logs hub | grep -v "INSERT INTO test_items"  # Should see no direct inserts

# Check ingestion IS consuming
docker logs ingestion | grep "Consumer started"
docker logs ingestion | grep "Batch written"

# Check queue depth stable (messages consumed)
rabbitmqctl list_queues name messages
# Output: playwright-grid.test-items 0 (stable, near-zero)

# Check database records match event processing
psql -c "SELECT COUNT(*) FROM test_items WHERE created_at > NOW() - INTERVAL '1 hour';"
```

### Safety Mechanisms

1. **Circuit Breaker (Stage 2):**
   - If RabbitMQ down, hub falls back to direct DB writes
   - No data loss even if broker fails

2. **Dead Letter Queue:**
   - Failed events move to DLQ (not lost)
   - Can be replayed after fixing issues

3. **Idempotency:**
   - Events have correlation IDs
   - Ingestion service can deduplicate if needed

---

### Migration Timeline

| Stage | Duration | Risk | Rollback Strategy |
|-------|----------|------|-------------------|
| Stage 1: Parallel Run | 1-2 days | Low | Disable dual-write |
| Stage 2: Queue-Only | 2-3 days | Medium | Toggle env var |
| Stage 3: Cleanup | 1 day | Low | Revert commits |
| **Total** | **4-6 days** | | |

---

## Files to Create

### New Service Structure

```
ingestion/
├── Application/
│   ├── Ports/
│   │   └── IBatchWriter.cs
│   ├── MessageConsumer.cs
│   ├── BatchWriter.cs
│   └── SignalRNotifier.cs
├── Infrastructure/
│   ├── Adapters/
│   │   └── PostgresBatchWriter.cs
│   └── Services/
│       └── IngestionWorker.cs
├── Services/
│   └── IngestionServiceRunner.cs
├── Dockerfile
├── IngestionService.csproj
└── Program.cs

hub/Infrastructure/Adapters/Messaging/
├── IEventPublisher.cs
├── RabbitMqEventPublisher.cs
└── ResilientEventPublisher.cs

Agenix.PlaywrightGrid.Domain/Events/
├── TestItemEvent.cs
├── CommandEvent.cs
└── LogItemEvent.cs

ingestion/Application/Caching/
├── LogTokenCache.cs
└── ILogTokenCache.cs

ingestion/Infrastructure/Adapters/
└── PostgresLogItemBatchWriter.cs

Note: Only 3 event types - all other operations use direct DB writes
```

### Detailed File Descriptions

1. **`ingestion/Program.cs`** - Service entry point, host builder
2. **`ingestion/Services/IngestionServiceRunner.cs`** - Main service logic, DI setup
3. **`ingestion/Application/MessageConsumer.cs`** - RabbitMQ consumer, message routing
4. **`ingestion/Application/BatchWriter.cs`** - Generic batch accumulation logic
5. **`ingestion/Infrastructure/Adapters/PostgresBatchWriter.cs`** - Postgres-specific bulk inserts
6. **`ingestion/Application/SignalRNotifier.cs`** - SignalR client for dashboard updates
7. **`ingestion/Dockerfile`** - Container image definition
8. **`hub/Infrastructure/Adapters/Messaging/IEventPublisher.cs`** - Publisher interface
9. **`hub/Infrastructure/Adapters/Messaging/RabbitMqEventPublisher.cs`** - RabbitMQ implementation
10. **`hub/Infrastructure/Adapters/Messaging/ResilientEventPublisher.cs`** - Circuit breaker wrapper
11. **`Agenix.PlaywrightGrid.Domain/Events/TestItemEvent.cs`** - Test item event DTO
12. **`Agenix.PlaywrightGrid.Domain/Events/CommandEvent.cs`** - Command event DTO
13. **`Agenix.PlaywrightGrid.Domain/Events/LogItemEvent.cs`** - Log item event DTO
14. **`ingestion/Application/Caching/ILogTokenCache.cs`** - Token cache interface
15. **`ingestion/Application/Caching/LogTokenCache.cs`** - Multi-tier token cache (Memory → Redis → Postgres)
16. **`ingestion/Infrastructure/Adapters/PostgresLogItemBatchWriter.cs`** - Log item batch writer with token strategy

---

## Monitoring & Observability

### RabbitMQ Metrics (Management UI - port 15672)

- Queue depth (messages ready)
- Message rates (publish, deliver, ack)
- Consumer count and utilization
- Message latency
- Dead letter queue depth

### Ingestion Service Metrics (Prometheus)

```csharp
// Custom metrics to expose
private static readonly Counter MessagesProcessed = Metrics
    .CreateCounter("ingestion_messages_processed_total", "Total messages processed",
        new CounterConfiguration { LabelNames = new[] { "queue", "status" } });

private static readonly Histogram BatchSize = Metrics
    .CreateHistogram("ingestion_batch_size", "Batch size distribution",
        new HistogramConfiguration { LabelNames = new[] { "queue" } });

private static readonly Gauge QueueDepth = Metrics
    .CreateGauge("ingestion_queue_depth", "Current queue depth",
        new GaugeConfiguration { LabelNames = new[] { "queue" } });

private static readonly Histogram ProcessingDuration = Metrics
    .CreateHistogram("ingestion_processing_duration_seconds", "Message processing duration",
        new HistogramConfiguration { LabelNames = new[] { "queue" } });
```

### Alerts to Configure

1. **Queue depth > 10,000** - Ingestion falling behind
2. **DLQ depth > 100** - High error rate
3. **Consumer lag > 60 seconds** - Processing too slow
4. **Circuit breaker open > 5 minutes** - RabbitMQ down
5. **Database connection errors** - DB connectivity issues

---

## Performance Benchmarks (Expected)

### Current Architecture (Direct DB Writes)

| Operation | Throughput | P99 Latency | DB Connections |
|-----------|------------|-------------|----------------|
| **Command logs** | ~500/sec | 50-100ms | 50-100 |
| **Test items** | ~50/sec | 20-50ms | 10-20 |
| **Launches** | ~5/min | 10-20ms | 1-2 |
| **Hub CPU usage** | High (I/O wait) | - | - |

### Proposed Architecture (Selective Event-Driven)

| Operation | Throughput | P99 Latency | Improvement | Architecture | Storage Benefit |
|-----------|------------|-------------|-------------|--------------|-----------------|
| **Command logs** | 10,000+/sec | 1-5ms | **20x faster** | ✅ Events (RabbitMQ) | - |
| **Test items** | 1,000+/sec | 1-5ms | **20x faster** | ✅ Events (RabbitMQ) | - |
| **Log items** | 5,000+/sec | 1-5ms | **20x faster** | ✅ Events (RabbitMQ) | **70-90% reduction** |
| **Launches** | ~5/min | 10-20ms | No change | ❌ Direct DB | - |
| **Suites** | ~10/min | 10-20ms | No change | ❌ Direct DB | - |
| **Hub CPU usage** | Low (async publish) | - | **Significant reduction** | - | - |
| **DB connections** | 5-10 (ingestion only) | - | **10x reduction** | - | - |

**Key Insight:** Focus optimization on the 3 highest-volume operations (commands, test items, and log items). Log items additionally benefit from 70-90% storage reduction via optimization token strategy. All other operations remain as direct database writes for simplicity.

---

## Effort Estimate

### Development Phases

| Phase | Description | Effort | Developer |
|-------|-------------|--------|-----------|
| **Phase 1** | Ingestion Service | 3-5 days | Backend Dev |
| | - RabbitMQ consumer | 1 day | |
| | - Batch writer | 1 day | |
| | - Error handling | 1 day | |
| | - Health checks | 1 day | |
| **Phase 2** | Hub modifications | 2-3 days | Backend Dev |
| | - Event publisher | 1 day | |
| | - Update endpoints | 1 day | |
| | - Circuit breaker | 0.5 day | |
| **Phase 3** | Optimization | 2 days | Backend Dev |
| | - COPY bulk inserts | 1 day | |
| | - Concurrent consumers | 0.5 day | |
| | - Performance tuning | 0.5 day | |
| **Phase 4** | SignalR bridge | 1 day | Backend Dev |
| **Testing** | Testing & validation | 2-3 days | QA + Dev |
| | - Unit tests | 1 day | |
| | - Integration tests | 1 day | |
| | - Load testing | 1 day | |
| **Documentation** | Docs + runbooks | 1 day | Dev |
| **Total** | | **11-15 days** | **1 developer** |

**With 2 developers working in parallel: 6-8 days**

---

## Risks & Mitigation

### Risk 1: Message Loss (High Impact, Low Probability)

**Scenario:** RabbitMQ crashes before message is persisted

**Mitigation:**
- Enable persistent messages (`props.Persistent = true`)
- Use durable queues
- Use publisher confirms for critical operations
- Circuit breaker falls back to direct DB write

---

### Risk 2: Ingestion Service Failure (Medium Impact, Low Probability)

**Scenario:** Ingestion service crashes or bugs cause processing to stop

**Mitigation:**
- Deploy multiple replicas (2+)
- Health checks and automatic restart
- Alert on queue depth growth
- Manual intervention: restart service or process queue manually

---

### Risk 3: Queue Buildup (Medium Impact, Medium Probability)

**Scenario:** Ingestion can't keep up with publish rate

**Mitigation:**
- Horizontal scaling (add more ingestion instances)
- Increase batch size for higher throughput
- Optimize database write performance
- Backpressure: Hub returns 503 if queue too deep (optional)

---

### Risk 4: Dashboard Delay (Low Impact, High Probability)

**Scenario:** Dashboard shows stale data due to async processing

**Mitigation:**
- Optimize batch timeout (e.g., 1 second max)
- SignalR notifications maintain real-time feel
- Show "processing" indicator in UI (optional)
- Acceptable trade-off for better overall system performance

---

### Risk 5: Migration Issues (Medium Impact, Medium Probability)

**Scenario:** Bugs in new code cause data corruption or loss

**Mitigation:**
- Stage 1: Dual-write mode validates correctness
- Comprehensive testing before Stage 2
- Rollback plan (toggle env var)
- Gradual rollout (canary deployment)

---

## Future Enhancements

### Phase 5: Analytics & Reporting (Future)

- **Event streaming** to data warehouse (Kafka → S3 → Redshift/BigQuery)
- **Real-time dashboards** (Grafana + TimescaleDB)
- **Anomaly detection** (ML models on test result patterns)

### Phase 6: Multi-Tenancy Optimization (Future)

- **Per-project queues** for isolation
- **Priority queues** for paid vs free tiers
- **Rate limiting** per project/user

### Phase 7: Event Sourcing (Future)

- **Event store** (full history of all events)
- **Event replay** for debugging and recovery
- **Temporal queries** ("show me state at timestamp X")

---

## References

- [RabbitMQ Patterns - Work Queues](https://www.rabbitmq.com/tutorials/tutorial-two-dotnet.html)
- [Npgsql COPY Performance](https://www.npgsql.org/doc/copy.html)
- [Circuit Breaker Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [Event-Driven Architecture (Martin Fowler)](https://martinfowler.com/articles/201701-event-driven.html)

---

## Approval & Sign-off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Architect | | | |
| Tech Lead | | | |
| Product Owner | | | |
| DevOps Lead | | | |

---

**Document Version:** 1.0
**Last Updated:** 2025-01-11
