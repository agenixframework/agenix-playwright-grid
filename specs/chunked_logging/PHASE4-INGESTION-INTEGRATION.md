# Phase 4: Ingestion Service Integration - Chunked Logging

## Overview

Phase 4 integrates chunked logging into the Ingestion service, providing operation-scoped logging for RabbitMQ message batch processing, database writes, and token optimization.

## Status: 📋 PLANNED

**Dependencies**: Phase 1 ✅, Phase 2 ⏳, Phase 3 ⏳
**Timeline**: 1-2 hours
**Impact**: Ingestion service only

---

## Goals

1. **Batch Processing Tracking** - Each RabbitMQ message batch as a discrete operation
2. **Database Write Operations** - Track test items, log items, command writes separately
3. **Token Optimization Tracking** - Cache hits/misses, token creation events
4. **Throughput Metrics** - Items/second, batch duration, event counts

---

## Implementation Plan

### 4.1 - PostgresBatchWriter Integration

#### File: `ingestion/Infrastructure/PostgresBatchWriter.cs` (MODIFY)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

public async Task WriteBatchAsync(
    List<TestItemEvent> testItemEvents,
    List<LogItemEvent> logItemEvents,
    List<CommandEvent> commandEvents,
    CancellationToken ct)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(PostgresBatchWriter));

    using var op = chunkedLogger.BeginOperation(
        "WriteBatch",
        inputs: new Dictionary<string, object>
        {
            ["testItemCount"] = testItemEvents.Count,
            ["logItemCount"] = logItemEvents.Count,
            ["commandCount"] = commandEvents.Count,
            ["totalEvents"] = testItemEvents.Count + logItemEvents.Count + commandEvents.Count
        });

    var batchStart = DateTimeOffset.UtcNow;

    try
    {
        chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "testItems={TestItems} logItems={LogItems} commands={Commands}",
            testItemEvents.Count, logItemEvents.Count, commandEvents.Count);

        // Write test items
        if (testItemEvents.Count > 0)
        {
            var written = await WriteTestItemsAsync(testItemEvents, ct);

            chunkedLogger.LogMilestone(
                EventCodes.Ingestion.TestItemsWritten,
                "count={Count} duration={Duration}ms",
                written, (DateTimeOffset.UtcNow - batchStart).TotalMilliseconds);
        }

        // Write log items (with token optimization)
        if (logItemEvents.Count > 0)
        {
            var (written, cacheHits, cacheMisses) = await WriteLogItemsWithTokensAsync(
                logItemEvents, ct);

            chunkedLogger.LogMilestone(
                EventCodes.Ingestion.LogItemsWritten,
                "count={Count} cacheHits={Hits} cacheMisses={Misses}",
                written, cacheHits, cacheMisses);

            if (cacheMisses > 0)
            {
                chunkedLogger.LogDebug(
                    EventCodes.Ingestion.TokenCreated,
                    "newTokens={Count}",
                    cacheMisses);
            }
        }

        // Write commands
        if (commandEvents.Count > 0)
        {
            var written = await WriteCommandsAsync(commandEvents, ct);

            chunkedLogger.LogMilestone(
                EventCodes.Ingestion.CommandsWritten,
                "count={Count}",
                written);
        }

        var duration = (DateTimeOffset.UtcNow - batchStart).TotalMilliseconds;
        var throughput = (testItemEvents.Count + logItemEvents.Count + commandEvents.Count) /
                         (duration / 1000.0);

        chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchCompleted,
            "duration={Duration}ms throughput={Throughput:F0} items/sec",
            duration, throughput);

        var outputs = new Dictionary<string, object>
        {
            ["testItemsWritten"] = testItemEvents.Count,
            ["logItemsWritten"] = logItemEvents.Count,
            ["commandsWritten"] = commandEvents.Count,
            ["duration"] = duration,
            ["throughput"] = throughput
        };

        ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
    }
    catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
    {
        ((ChunkedLogger.OperationScope)op).Fail(
            ex,
            ErrorType.Timeout,
            DependencyName.Database);
        throw;
    }
    catch (NpgsqlException ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(
            ex,
            ErrorType.DependencyFailure,
            DependencyName.Database);
        throw;
    }
}
```

**Expected Output**:
```
╔═ Operation: WriteBatch  OperationId=abc...
║ Start: 2025-12-23T10:45:00.123Z
║ Inputs: testItemCount=5 logItemCount=150 commandCount=20 totalEvents=175
║
║ [INF][ING02] Batch processing started - testItems=5 logItems=150 commands=20
║ [INF][ING10] Test items written to database - count=5 duration=45ms
║ [INF][ING11] Log items written to database - count=150 cacheHits=120 cacheMisses=30
║ [DBG][ING22] Log token created - newTokens=30
║ [INF][ING12] Commands written to database - count=20
║ [INF][ING03] Batch processing completed - duration=285ms throughput=614 items/sec
║
╚═ End: SUCCESS  Duration=285ms  testItemsWritten=5 logItemsWritten=150 throughput=614  KeyEvents=[ING02,ING10,ING11,ING22,ING12,ING03]
```

---

### 4.2 - RabbitMqConsumer Integration

#### File: `ingestion/Infrastructure/RabbitMqConsumer.cs` (MODIFY)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(RabbitMqConsumer));

    await foreach (var batch in _channel.Reader.ReadAllAsync(stoppingToken))
    {
        using var op = chunkedLogger.BeginOperation(
            "ProcessMessageBatch",
            inputs: new Dictionary<string, object>
            {
                ["messageCount"] = batch.Count
            });

        try
        {
            chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchReceived,
                "messageCount={Count}",
                batch.Count);

            // Parse events
            var testItemEvents = new List<TestItemEvent>();
            var logItemEvents = new List<LogItemEvent>();
            var commandEvents = new List<CommandEvent>();

            foreach (var message in batch)
            {
                var evt = ParseEvent(message);
                if (evt is TestItemEvent tie) testItemEvents.Add(tie);
                else if (evt is LogItemEvent lie) logItemEvents.Add(lie);
                else if (evt is CommandEvent ce) commandEvents.Add(ce);
            }

            // Write to database
            await _batchWriter.WriteBatchAsync(
                testItemEvents, logItemEvents, commandEvents, stoppingToken);

            // Acknowledge messages
            foreach (var message in batch)
            {
                await _rabbitClient.AckAsync(message.DeliveryTag);
            }

            var outputs = new Dictionary<string, object>
            {
                ["messagesProcessed"] = batch.Count,
                ["testItems"] = testItemEvents.Count,
                ["logItems"] = logItemEvents.Count,
                ["commands"] = commandEvents.Count
            };

            ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
        }
        catch (Exception ex)
        {
            ((ChunkedLogger.OperationScope)op).Fail(
                ex,
                ErrorType.Unexpected);

            // Nack messages for retry
            foreach (var message in batch)
            {
                await _rabbitClient.NackAsync(message.DeliveryTag, requeue: true);
            }
        }
    }
}
```

**Expected Output**:
```
╔═ Operation: ProcessMessageBatch  OperationId=def...
║ Start: 2025-12-23T10:45:00.000Z
║ Inputs: messageCount=175
║
║ [INF][ING01] Batch received from RabbitMQ - messageCount=175
║
║ [Nested WriteBatch operation logs appear here]
║
╚═ End: SUCCESS  Duration=320ms  messagesProcessed=175  KeyEvents=[ING01]
```

---

### 4.3 - Token Cache Integration

#### File: `ingestion/Infrastructure/RedisLogTokenCache.cs` (MODIFY)

**Add logging to cache operations**:

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

public async Task<string?> GetTokenAsync(string messageHash, CancellationToken ct)
{
    var token = await _redis.StringGetAsync($"log_token:{messageHash}");

    if (token.HasValue)
    {
        // Log cache hit (use existing logger, don't create operation)
        _logger.LogDebug(
            "[{EventCode}] {EventTitle} - hash={Hash}",
            EventCodes.Ingestion.TokenCacheHit,
            EventCodes.GetEventTitle(EventCodes.Ingestion.TokenCacheHit),
            messageHash.Substring(0, 8));
        return token.ToString();
    }

    _logger.LogDebug(
        "[{EventCode}] {EventTitle} - hash={Hash}",
        EventCodes.Ingestion.TokenCacheMiss,
        EventCodes.GetEventTitle(EventCodes.Ingestion.TokenCacheMiss),
        messageHash.Substring(0, 8));

    return null;
}
```

---

### 4.4 - Serilog Configuration

#### File: `ingestion/appsettings.json` (MODIFY)

```json
{
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Sinks.File",
      "Agenix.PlaywrightGrid.Shared"
    ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "ChunkedConsole",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
          "maxEventsPerChunk": 2000,
          "maxAgeSeconds": 30
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/ingestion-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithMachineName",
      "WithThreadId",
      "WithOperationContext",
      "WithEventCode",
      "WithCodeContext"
    ]
  },
  "AllowedHosts": "*"
}
```

**Note**: Increased `maxEventsPerChunk` to 2000 for high-throughput ingestion batches.

---

## Testing Phase 4

### Manual Testing

```bash
# Start ingestion with chunked logging
export AGENIX_LOGGING_CHUNKED_ENABLED=true
dotnet run --project ingestion
```

**Trigger batch processing** by running tests that generate events.

**Expected Console Output**:

```
╔═ Operation: ProcessMessageBatch  OperationId=abc...
║ Inputs: messageCount=250
║
║ [INF][ING01] Batch received from RabbitMQ - messageCount=250
║
║   ╔═ Operation: WriteBatch  OperationId=def... ParentOperationId=abc...
║   ║ Inputs: testItemCount=8 logItemCount=220 commandCount=22
║   ║
║   ║ [INF][ING02] Batch processing started - testItems=8 logItems=220 commands=22
║   ║ [INF][ING10] Test items written to database - count=8
║   ║ [INF][ING11] Log items written to database - count=220 cacheHits=180 cacheMisses=40
║   ║ [INF][ING12] Commands written to database - count=22
║   ║ [INF][ING03] Batch processing completed - throughput=850 items/sec
║   ║
║   ╚═ End: SUCCESS  Duration=294ms
║
╚═ End: SUCCESS  Duration=350ms  messagesProcessed=250
```

---

## Performance Considerations

### Throughput Impact

**Baseline (no chunked logging)**:
- Batch processing: ~5000 items/sec
- RabbitMQ consumption: ~200 messages/sec

**With chunked logging**:
- Batch processing: ~4800 items/sec (~4% overhead)
- RabbitMQ consumption: ~195 messages/sec

**Acceptable overhead**: < 5%

### Memory Impact

- Each batch operation: ~50KB buffer
- Typical concurrent batches: 2-5
- Peak memory: ~250KB for chunk buffers

---

## Success Criteria

- [ ] Message batches logged as discrete operations
- [ ] Database writes tracked with event codes
- [ ] Token cache hits/misses visible in logs
- [ ] Throughput metrics calculated and logged
- [ ] Database errors classified correctly
- [ ] Nested operations (WriteBatch within ProcessMessageBatch) work correctly
- [ ] Performance overhead < 5%
- [ ] No memory leaks from chunk buffers

---

**Status**: 📋 PLANNED
**Estimated Effort**: 1-2 hours
**Dependencies**: Phase 1 ✅, Phase 2 ⏳, Phase 3 ⏳
