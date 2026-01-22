# Chunked Logging Infrastructure

## Overview

This directory contains the core infrastructure for **operation-based chunked logging** with strong "what does this refer to?" context. The implementation follows the requirements specified in the chunked logging specification, providing clear visual grouping of logs, operation correlation, milestone tracking, and structured error classification.

## Architecture

### Core Components

1. **OperationContext** - Ambient operation context using AsyncLocal storage
2. **ChunkedLogger** - High-level API for operation-scoped logging
3. **EventCodes** - Catalog of stable event codes for milestone tracking
4. **ErrorTypes & DependencyName** - Error classification enums
5. **Serilog Enrichers** - Automatic enrichment of log events with context
6. **ChunkedConsoleSink** - Visual chunk rendering with box-drawing characters

### Design Goals Achieved

✅ **FR1 - Correlation and grouping key**: Every log has `OperationId`, `ParentOperationId`, `TraceId`, `SpanId`
✅ **FR2 - Chunk boundaries**: Explicit `OperationStart` and `OperationEnd` events
✅ **FR3 - Chunked display format**: Visual chunks with `╔═` / `╚═` boundaries
✅ **FR4 - Code context**: `SourceContext`, `EventCode` in every log
✅ **FR5 - Structured events**: All logs use structured properties
✅ **FR6 - Error clarity**: `ErrorType` and `DependencyName` classification

## Usage Examples

### Basic Operation with Milestones

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;

public class TestItemService
{
    private readonly ILogger<TestItemService> _logger;
    private readonly ChunkedLogger _chunkedLogger;

    public TestItemService(ILogger<TestItemService> logger)
    {
        _logger = logger;
        _chunkedLogger = new ChunkedLogger(logger, nameof(TestItemService));
    }

    public async Task<Guid> StartTestItemAsync(Guid launchId, string name, string labelKey)
    {
        // Begin operation scope - creates OperationContext
        using var op = _chunkedLogger.BeginOperation(
            "StartTestItem",
            inputs: new Dictionary<string, object>
            {
                ["launchId"] = launchId,
                ["name"] = name,
                ["labelKey"] = labelKey
            });

        try
        {
            // Log milestone events
            _chunkedLogger.LogMilestone(
                EventCodes.TestItem.ItemCreated,
                "name={Name} itemType={ItemType}",
                name, "Test");

            var itemId = Guid.NewGuid();

            // Borrow browser
            _chunkedLogger.LogMilestone(
                EventCodes.BrowserPool.BorrowRequested,
                "labelKey={LabelKey}",
                labelKey);

            var browser = await BorrowBrowserAsync(labelKey);

            _chunkedLogger.LogMilestone(
                EventCodes.BrowserPool.BrowserReady,
                "browserId={BrowserId} endpoint={Endpoint}",
                browser.Id, browser.WebSocketEndpoint);

            // Persist to database
            await SaveToDatabase(itemId, launchId, name, browser.Id);

            _chunkedLogger.LogMilestone(
                EventCodes.TestItem.ItemPersisted,
                "itemId={ItemId}",
                itemId);

            // Set outputs for completion log
            ((ChunkedLogger.OperationScope)op).SetOutputs(new Dictionary<string, object>
            {
                ["itemId"] = itemId,
                ["browserId"] = browser.Id
            });

            return itemId;
        }
        catch (TimeoutException ex)
        {
            // Classify error with dependency
            ((ChunkedLogger.OperationScope)op).Fail(
                ex,
                ErrorType.Timeout,
                DependencyName.Worker);
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected error
            ((ChunkedLogger.OperationScope)op).Fail(
                ex,
                ErrorType.Unexpected);
            throw;
        }
    }
}
```

### Expected Console Output

```
╔═ Operation: StartTestItem  OperationId=9b3c4a21-...  UserId=42  Env=prod
║ Start: 2025-12-23T10:15:02.123Z
║ Inputs: launchId=abc... name="Login test" labelKey=myapp:chromium:prod
║
║ [INF][ITEM01] Test item created (TestItemService) - name="Login test" itemType=Test
║ [INF][POOL01] Browser borrow requested (TestItemService) - labelKey=myapp:chromium:prod
║ [DBG][POOL02] Browser allocated (BrowserPoolService) - browserId=br_456 workerNode=worker-3
║ [INF][POOL03] Browser ready (BrowserPoolService) - endpoint=ws://worker-3:9222/...
║ [INF][ITEM02] Test item persisted to database (TestItemService) - itemId=789...
║
╚═ End: SUCCESS  Duration=1.82s  itemId=789...  browserId=br_456  KeyEvents=[ITEM01,POOL01,POOL03,ITEM02]
```

### Error Handling with Classification

```csharp
try
{
    await database.ExecuteQueryAsync(...);
}
catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
{
    _chunkedLogger.FailOperation(
        OperationContext.Current!,
        ex,
        ErrorType.Timeout,
        DependencyName.Database);
    throw;
}
```

**Error Output:**
```
╚═ End: FAILED  ErrorType=Timeout Dependency=Database  Duration=30.5s  KeyEvents=[ITEM01,POOL01]
```

## Event Codes Catalog

### Browser Pool (POOL01-POOL99)

| Code | Title | When to Use |
|------|-------|-------------|
| POOL01 | Browser borrow requested | Start of browser allocation |
| POOL02 | Browser allocated | Browser selected from pool |
| POOL03 | Browser ready | Browser connection established |
| POOL04 | Browser borrow failed | Allocation failed |
| POOL11 | Browser return requested | Start of browser return |
| POOL12 | Browser returned to pool | Browser successfully returned |
| POOL20 | Cleanup scan started | Auto-stop service tick |
| POOL21 | Browser released | Browser forcibly released |
| POOL22 | Test item auto-stopped | Item stopped due to inactivity |

### Test Item (ITEM01-ITEM99)

| Code | Title | When to Use |
|------|-------|-------------|
| ITEM01 | Test item created | Item instance created |
| ITEM02 | Test item persisted to database | Database write completed |
| ITEM03 | Test item started | Test execution started |
| ITEM04 | Test item finished | Test execution completed |
| ITEM20 | Log item added | Log attached to test |
| ITEM21 | Artifact uploaded | File artifact uploaded |

### Launch (LCH01-LCH99)

| Code | Title | When to Use |
|------|-------|-------------|
| LCH01 | Launch created | New launch initialized |
| LCH02 | Launch started | First test started |
| LCH03 | Launch finished | All tests completed |
| LCH10 | Launch status calculated | Status aggregation computed |

*See `EventCodes.cs` for complete catalog of 100+ codes*

## Error Classification

### ErrorType Enum

- **Validation** - Bad input (400)
- **NotFound** - Resource not found (404)
- **Conflict** - Duplicate/state conflict (409)
- **Timeout** - Operation timeout
- **DependencyFailure** - External service failure
- **Unauthorized** - Auth failure (401/403)
- **ResourceExhaustion** - Capacity limits
- **Unexpected** - Programming errors

### DependencyName Enum

- Database, Redis, RabbitMQ, MinIO
- Worker, Hub, Ingestion
- Playwright, ExternalApi, FileSystem

## Configuration

### Serilog Integration (appsettings.json)

```json
{
  "Serilog": {
    "Using": ["Agenix.PlaywrightGrid.Shared"],
    "MinimumLevel": {
      "Default": "Information"
    },
    "WriteTo": [
      {
        "Name": "ChunkedConsole",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
          "maxEventsPerChunk": 1000,
          "maxAgeSeconds": 60
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithOperationContext",
      "WithEventCode",
      "WithCodeContext"
    ]
  }
}
```

### Environment Variables

```bash
# Enable/disable chunked logging
AGENIX_LOGGING_CHUNKED_ENABLED=true

# Chunk buffer limits
AGENIX_LOGGING_CHUNK_MAX_EVENTS=1000
AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS=60
AGENIX_LOGGING_CHUNK_MAX_MEMORY_MB=10

# Output format
AGENIX_LOGGING_EVENT_CODE_PREFIX=true
AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION=true
```

## Integration with Services

### ASP.NET Core Middleware

```csharp
public class OperationLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OperationLoggingMiddleware> _logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var chunkedLogger = new ChunkedLogger(_logger, nameof(OperationLoggingMiddleware));

        using var op = chunkedLogger.BeginOperation(
            $"{context.Request.Method} {context.Request.Path}",
            inputs: new Dictionary<string, object>
            {
                ["method"] = context.Request.Method,
                ["path"] = context.Request.Path.Value ?? "",
                ["userId"] = context.User.Identity?.Name ?? "anonymous"
            });

        try
        {
            await _next(context);

            ((ChunkedLogger.OperationScope)op).SetOutputs(new Dictionary<string, object>
            {
                ["statusCode"] = context.Response.StatusCode
            });
        }
        catch (Exception ex)
        {
            var errorType = context.Response.StatusCode switch
            {
                400 => ErrorType.Validation,
                404 => ErrorType.NotFound,
                409 => ErrorType.Conflict,
                _ => ErrorType.Unexpected
            };

            ((ChunkedLogger.OperationScope)op).Fail(ex, errorType);
            throw;
        }
    }
}
```

## Performance Considerations

### NFR1 - Performance and Memory Safety

- **Buffer Limits**: Max 1000 events per chunk
- **Age Limit**: Chunks auto-flush after 60 seconds
- **Memory Limit**: Configurable max 10MB per buffer
- **Degradation**: Falls back to line-by-line on pressure

### NFR2 - Ordering and Concurrency

- Events within chunk ordered by timestamp
- Thread-safe buffering with ConcurrentDictionary
- Lock-per-operation for flush operations
- Interleaved chunks from concurrent operations rendered separately

### NFR3 - Security and Privacy

- Sensitive fields MUST be masked (implement per-service)
- Use allowlist for chunk summary properties
- Log sanitization hooks available

## Testing

### Unit Tests

```csharp
[Test]
public void OperationContext_PropagatesAcrossAsyncBoundaries()
{
    using var ctx = OperationContext.Begin("TestOp");
    var ctxInside = OperationContext.Current;

    Assert.That(ctxInside, Is.Not.Null);
    Assert.That(ctxInside.OperationName, Is.EqualTo("TestOp"));
}

[Test]
public void ChunkedLogger_RecordsKeyEvents()
{
    var logger = CreateTestLogger();
    var chunkedLogger = new ChunkedLogger(logger, "Test");

    using var op = chunkedLogger.BeginOperation("TestOp");
    chunkedLogger.LogMilestone(EventCodes.TestItem.ItemCreated, "test");

    var context = OperationContext.Current;
    Assert.That(context.KeyEvents, Contains.Item(EventCodes.TestItem.ItemCreated));
}
```

### Integration Tests

- Verify chunk format in captured logs
- Verify OperationId correlation across services
- Verify error classification appears in output
- Load test with 1000+ concurrent operations

## Migration Path

1. **Phase 1 (Current)**: Core infrastructure in Shared library ✅
2. **Phase 2**: Hub service integration with middleware
3. **Phase 3**: Worker, Ingestion, Housekeeping integration
4. **Phase 4**: Error classification across all services
5. **Phase 5**: Monitoring & alerting based on EventCodes

## Next Steps

### Phase 2 - Hub Integration

1. Create `OperationLoggingMiddleware` in `hub/Infrastructure/Web/`
2. Update `TestItemsEndpoints` to use ChunkedLogger
3. Update `BrowserPoolService` to use ChunkedLogger
4. Update `BrowserAutoStopService` background worker
5. Configure Serilog in `hub/appsettings.json`

### Phase 3 - Other Services

Apply same pattern to Worker, Ingestion, Housekeeping, Dashboard services.

## References

- Event codes inspired by HTTP status codes (stable, searchable, meaningful)
- Chunk rendering inspired by ReportPortal's execution flow visualization
- OperationContext pattern from Activity/OpenTelemetry tracing
- Error classification from standard HTTP status categories

---

**Status**: ✅ Phase 1 Complete - Core infrastructure ready for service integration
**Build**: ✅ Compiles successfully with 0 errors
**Tests**: ⏳ Pending (Phase 2)
**Documentation**: ✅ Complete
