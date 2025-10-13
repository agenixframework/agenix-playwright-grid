# Phase 2: Hub Service Integration - Chunked Logging

## Overview

Phase 2 integrates the chunked logging infrastructure (from Phase 1) into the Hub service, providing operation-scoped logging for HTTP requests, background services, and business operations.

## Status: 📋 PLANNED

**Dependencies**: ✅ Phase 1 Complete (Core Infrastructure in Shared library)
**Timeline**: 2-3 hours
**Impact**: Hub service only (Worker, Ingestion, Housekeeping in Phase 3+)

---

## Goals

1. **HTTP Request Correlation** - Every HTTP request becomes an operation with automatic start/end logging
2. **Endpoint Instrumentation** - Key endpoints (TestItems, Launches, BrowserPool) use ChunkedLogger
3. **Background Service Tracking** - Background workers log as discrete operations (ticks, scans)
4. **Error Classification** - All error handling uses ErrorType + DependencyName
5. **Serilog Configuration** - Enable ChunkedConsoleSink and enrichers

---

## Implementation Plan

### 2.1 - ASP.NET Core Middleware

#### File: `hub/Infrastructure/Web/OperationLoggingMiddleware.cs` (NEW)

**Purpose**: Automatically create OperationContext for every HTTP request.

**Implementation**:

```csharp
#region License
// Copyright (c) 2025 Agenix
// SPDX-License-Identifier: Apache-2.0
#endregion

using Agenix.PlaywrightGrid.Shared.Logging;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
/// Middleware that creates an OperationContext for every HTTP request,
/// providing automatic operation boundaries and correlation IDs.
/// </summary>
public sealed class OperationLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OperationLoggingMiddleware> _logger;
    private readonly bool _enabled;

    public OperationLoggingMiddleware(
        RequestDelegate next,
        ILogger<OperationLoggingMiddleware> logger,
        IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _enabled = config.GetValue("AGENIX_LOGGING_CHUNKED_ENABLED", true);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var chunkedLogger = new ChunkedLogger(_logger, "HttpRequest");
        var operationName = $"{context.Request.Method} {context.Request.Path}";

        var inputs = new Dictionary<string, object>
        {
            ["method"] = context.Request.Method,
            ["path"] = context.Request.Path.Value ?? "",
            ["queryString"] = context.Request.QueryString.Value ?? "",
            ["userAgent"] = context.Request.Headers.UserAgent.ToString()
        };

        // Add authenticated user info if available
        if (context.User.Identity?.IsAuthenticated == true)
        {
            inputs["userId"] = context.User.Identity.Name ?? "unknown";
        }

        // Add project key from route if available
        if (context.Request.RouteValues.TryGetValue("projectKey", out var projectKey))
        {
            inputs["projectKey"] = projectKey?.ToString() ?? "";
        }

        using var op = chunkedLogger.BeginOperation(operationName, inputs);

        try
        {
            await _next(context);

            // Set outputs
            var outputs = new Dictionary<string, object>
            {
                ["statusCode"] = context.Response.StatusCode
            };

            ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
        }
        catch (Exception ex)
        {
            // Classify error based on status code
            var errorType = context.Response.StatusCode switch
            {
                400 => ErrorType.Validation,
                404 => ErrorType.NotFound,
                409 => ErrorType.Conflict,
                401 or 403 => ErrorType.Unauthorized,
                _ => ErrorType.Unexpected
            };

            ((ChunkedLogger.OperationScope)op).Fail(ex, errorType);
            throw;
        }
    }
}

/// <summary>
/// Extension method for registering OperationLoggingMiddleware.
/// </summary>
public static class OperationLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseOperationLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<OperationLoggingMiddleware>();
    }
}
```

#### File: `hub/Services/HubServiceRunner.cs` (MODIFY)

**Add middleware registration** (after line ~200, before endpoint mapping):

```csharp
// Add operation logging middleware (Phase 2 - Chunked Logging)
app.UseOperationLogging();
```

**Expected Output**:
```
╔═ Operation: GET /api/test-items/123e4567...  OperationId=9b3c...
║ Start: 2025-12-23T10:15:02.123Z
║ Inputs: method=GET path=/api/test-items/123e4567... userId=admin
║
║ [INF][ITEM01] Test item retrieved from database
║
╚═ End: SUCCESS  Duration=45ms  statusCode=200
```

---

### 2.2 - TestItemsEndpoints Integration

#### File: `hub/Infrastructure/Web/TestItemsEndpoints.cs` (MODIFY)

**Update `StartTestItem` method** to use ChunkedLogger:

```csharp
private static async Task<IResult> StartTestItem(
    [FromBody] StartTestItemRequest req,
    [FromServices] IResultsStore store,
    [FromServices] IBrowserPoolService browserPool,
    [FromServices] IEventPublisher eventPublisher,
    [FromServices] ILogger<TestItemsEndpoints> logger,
    [FromServices] IConfiguration config,
    HttpContext httpContext)
{
    var chunkedLogger = new ChunkedLogger(logger, nameof(TestItemsEndpoints));

    // OperationContext already created by middleware
    // Add milestone events within the existing operation

    try
    {
        // Milestone: Item creation initiated
        chunkedLogger.LogMilestone(
            EventCodes.TestItem.ItemCreated,
            "launchId={LaunchId} name={Name} itemType={ItemType}",
            req.LaunchId, req.Name, req.Type);

        var itemId = Guid.NewGuid();

        // If Test or Scenario, borrow browser
        if (req.Type is "Test" or "Scenario" && !string.IsNullOrWhiteSpace(req.LabelKey))
        {
            chunkedLogger.LogMilestone(
                EventCodes.BrowserPool.BorrowRequested,
                "labelKey={LabelKey}",
                req.LabelKey);

            var borrowResult = await browserPool.BorrowAsync(req.LabelKey, itemId.ToString());

            if (borrowResult == null)
            {
                chunkedLogger.LogWarning(
                    EventCodes.BrowserPool.BorrowFailed,
                    "No browser capacity available for {LabelKey}",
                    req.LabelKey);

                return Results.Problem(
                    statusCode: 503,
                    detail: $"No browser capacity available for {req.LabelKey}");
            }

            chunkedLogger.LogMilestone(
                EventCodes.BrowserPool.BrowserReady,
                "browserId={BrowserId} endpoint={Endpoint}",
                borrowResult.BrowserId, borrowResult.WebSocketEndpoint);
        }

        // Persist to database
        await store.UpsertRunAsync(/* ... */);

        chunkedLogger.LogMilestone(
            EventCodes.TestItem.ItemPersisted,
            "itemId={ItemId}",
            itemId);

        // Publish event if enabled
        var enablePublisher = config.GetValue("ENABLE_EVENT_PUBLISHER", false);
        if (enablePublisher)
        {
            await eventPublisher.PublishTestItemEventAsync(/* ... */);
        }

        return Results.Created($"/api/test-items/{itemId}", new TestItemCreatedResponse { Id = itemId });
    }
    catch (TimeoutException ex)
    {
        // Classify timeout errors
        var context = OperationContext.Current;
        if (context != null)
        {
            chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
        }
        throw;
    }
    catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
    {
        var context = OperationContext.Current;
        if (context != null)
        {
            chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Database);
        }
        throw;
    }
}
```

**Expected Output**:
```
╔═ Operation: POST /api/test-items  OperationId=abc...
║ Inputs: method=POST path=/api/test-items userId=testuser
║
║ [INF][ITEM01] Test item created - launchId=123... name="Login test" itemType=Test
║ [INF][POOL01] Browser borrow requested - labelKey=myapp:chromium:prod
║ [DBG][POOL02] Browser allocated - browserId=br_456
║ [INF][POOL03] Browser ready - endpoint=ws://worker-3:9222/...
║ [INF][ITEM02] Test item persisted to database - itemId=789...
║
╚═ End: SUCCESS  Duration=1.82s  statusCode=201  KeyEvents=[ITEM01,POOL01,POOL03,ITEM02]
```

---

### 2.3 - BrowserPoolService Integration

#### File: `hub/Infrastructure/Services/BrowserPoolService.cs` (MODIFY)

**Update `BorrowAsync` method**:

```csharp
public async Task<BorrowResult?> BorrowAsync(string labelKey, string runId, CancellationToken ct = default)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(BrowserPoolService));

    // Create nested operation (child of HTTP request operation)
    var parentOpId = OperationContext.Current?.OperationId;
    using var op = chunkedLogger.BeginOperation(
        "BorrowBrowser",
        inputs: new Dictionary<string, object> { ["labelKey"] = labelKey, ["runId"] = runId },
        parentOperationId: parentOpId);

    try
    {
        chunkedLogger.LogMilestone(
            EventCodes.BrowserPool.BorrowRequested,
            "labelKey={LabelKey}",
            labelKey);

        // Find available browser
        var browser = await FindAvailableBrowserAsync(labelKey, ct);

        if (browser == null)
        {
            chunkedLogger.LogWarning(
                EventCodes.BrowserPool.BorrowFailed,
                "No capacity for {LabelKey}",
                labelKey);
            return null;
        }

        chunkedLogger.LogDebug(
            EventCodes.BrowserPool.BrowserAllocated,
            "browserId={BrowserId} workerNode={WorkerNode}",
            browser.BrowserId, browser.WorkerNodeId);

        // Mark as borrowed
        await MarkBorrowedAsync(browser.BrowserId, runId, ct);

        chunkedLogger.LogMilestone(
            EventCodes.BrowserPool.BrowserReady,
            "browserId={BrowserId} endpoint={Endpoint}",
            browser.BrowserId, browser.WebSocketEndpoint);

        var outputs = new Dictionary<string, object>
        {
            ["browserId"] = browser.BrowserId,
            ["workerNode"] = browser.WorkerNodeId
        };

        ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);

        return new BorrowResult { /* ... */ };
    }
    catch (RedisException ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
        throw;
    }
}
```

**Update `ReturnAsync` method**:

```csharp
public async Task ReturnAsync(string browserId, CancellationToken ct = default)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(BrowserPoolService));

    var parentOpId = OperationContext.Current?.OperationId;
    using var op = chunkedLogger.BeginOperation(
        "ReturnBrowser",
        inputs: new Dictionary<string, object> { ["browserId"] = browserId },
        parentOperationId: parentOpId);

    chunkedLogger.LogMilestone(
        EventCodes.BrowserPool.ReturnRequested,
        "browserId={BrowserId}",
        browserId);

    await MarkAvailableAsync(browserId, ct);

    chunkedLogger.LogMilestone(
        EventCodes.BrowserPool.BrowserReturned,
        "browserId={BrowserId}",
        browserId);
}
```

---

### 2.4 - BrowserAutoStopService Integration

#### File: `hub/Infrastructure/Adapters/Background/BrowserAutoStopService.cs` (MODIFY)

**Update `ExecuteAsync` tick loop**:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // ... configuration loading ...

    var chunkedLogger = new ChunkedLogger(logger, nameof(BrowserAutoStopService));

    while (!stoppingToken.IsCancellationRequested)
    {
        var tickStart = DateTime.UtcNow;

        // Each tick is a discrete operation
        using var op = chunkedLogger.BeginOperation("BrowserAutoStop:Tick");

        int scanned = 0, processed = 0, releasedTotal = 0, errors = 0;

        try
        {
            chunkedLogger.LogMilestone(
                EventCodes.BrowserPool.ScanStarted,
                "batchSize={BatchSize}",
                batchSize);

            var candidates = await resultsStore.GetActiveTestItemsAsync(
                new[] { "Queued", "Running" },
                new[] { "Test", "Scenario" },
                batchSize,
                offset: 0);

            scanned = candidates.Count;

            foreach (var testItem in candidates)
            {
                // ... inactivity checks ...

                if (shouldStop)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.BrowserPool.ItemAutoStopped,
                        "itemId={ItemId} reason={Reason}",
                        testItem.Id, stopReason);

                    // Release browser
                    await browserPool.ReturnAsync(testItem.BrowserId);

                    chunkedLogger.LogMilestone(
                        EventCodes.BrowserPool.BrowserReleased,
                        "browserId={BrowserId}",
                        testItem.BrowserId);

                    processed++;
                    releasedTotal++;
                }
            }

            var outputs = new Dictionary<string, object>
            {
                ["scanned"] = scanned,
                ["processed"] = processed,
                ["released"] = releasedTotal,
                ["duration"] = (DateTime.UtcNow - tickStart).TotalMilliseconds
            };

            ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
        }
        catch (Exception ex)
        {
            errors++;
            ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.Unexpected);
        }

        await Task.Delay(interval, stoppingToken);
    }
}
```

**Expected Output**:
```
╔═ Operation: BrowserAutoStop:Tick  OperationId=def...
║ Start: 2025-12-23T10:20:00.000Z
║
║ [INF][POOL20] Cleanup scan started - batchSize=50
║ [INF][POOL22] Test item auto-stopped - itemId=123... reason=Inactivity
║ [INF][POOL21] Browser released - browserId=br_456
║ [INF][POOL22] Test item auto-stopped - itemId=789... reason=MaxDuration
║ [INF][POOL21] Browser released - browserId=br_789
║
╚═ End: SUCCESS  Duration=342ms  scanned=50 processed=2 released=2  KeyEvents=[POOL20,POOL22,POOL21]
```

---

### 2.5 - LaunchesEndpoints Integration

#### File: `hub/Infrastructure/Web/LaunchesEndpoints.cs` (MODIFY)

**Update key endpoints** with ChunkedLogger:

```csharp
// POST /api/launches
private static async Task<IResult> CreateLaunch(
    [FromBody] CreateLaunchRequest req,
    [FromServices] IResultsStore store,
    [FromServices] ILogger<LaunchesEndpoints> logger)
{
    var chunkedLogger = new ChunkedLogger(logger, nameof(LaunchesEndpoints));

    chunkedLogger.LogMilestone(
        EventCodes.Launch.LaunchCreated,
        "name={Name} projectKey={ProjectKey}",
        req.Name, req.ProjectKey);

    var launchId = Guid.NewGuid();
    await store.CreateLaunchAsync(/* ... */);

    chunkedLogger.LogMilestone(
        EventCodes.Launch.LaunchStarted,
        "launchId={LaunchId}",
        launchId);

    return Results.Created($"/api/launches/{launchId}", new { id = launchId });
}

// PUT /api/launches/{id}/finish
private static async Task<IResult> FinishLaunch(
    Guid id,
    [FromServices] IResultsStore store,
    [FromServices] ILogger<LaunchesEndpoints> logger)
{
    var chunkedLogger = new ChunkedLogger(logger, nameof(LaunchesEndpoints));

    chunkedLogger.LogMilestone(
        EventCodes.Launch.StatusCalculated,
        "launchId={LaunchId}",
        id);

    await store.UpdateLaunchStatusAsync(id, /* ... */);

    chunkedLogger.LogMilestone(
        EventCodes.Launch.AggregationsUpdated,
        "launchId={LaunchId}",
        id);

    chunkedLogger.LogMilestone(
        EventCodes.Launch.LaunchFinished,
        "launchId={LaunchId}",
        id);

    return Results.Ok();
}
```

---

### 2.6 - Serilog Configuration

#### File: `hub/appsettings.json` (MODIFY)

**Replace existing Serilog configuration**:

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
        "Microsoft.AspNetCore": "Warning",
        "Microsoft": "Warning",
        "System.Net.Http": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [{
              "Name": "ByExcluding",
              "Args": {
                "expression": "StartsWith(SourceContext, 'PlaywrightHub.Infrastructure.Adapters.Background')"
              }
            }],
            "WriteTo": [{
              "Name": "ChunkedConsole",
              "Args": {
                "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                "maxEventsPerChunk": 1000,
                "maxAgeSeconds": 60
              }
            }]
          }
        }
      },
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [{
              "Name": "ByExcluding",
              "Args": {
                "expression": "StartsWith(SourceContext, 'PlaywrightHub.Infrastructure.Adapters.Background')"
              }
            }],
            "WriteTo": [{
              "Name": "File",
              "Args": {
                "path": "/tmp/pg-hub-.log",
                "rollingInterval": "Day",
                "retainedFileCountLimit": 3,
                "fileSizeLimitBytes": 52428800,
                "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
              }
            }]
          }
        }
      },
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [{
              "Name": "ByIncludingOnly",
              "Args": {
                "expression": "StartsWith(SourceContext, 'PlaywrightHub.Infrastructure.Adapters.Background')"
              }
            }],
            "WriteTo": [{
              "Name": "File",
              "Args": {
                "path": "/tmp/pg-hub-background-.log",
                "rollingInterval": "Day",
                "retainedFileCountLimit": 3,
                "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
              }
            }]
          }
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

#### File: `hub/appsettings.Development.json` (CREATE if not exists)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

---

### 2.7 - Environment Variables

#### File: `.env` (ADD)

**Add at end of file**:

```bash
# Chunked Logging Configuration (Phase 2)
AGENIX_LOGGING_CHUNKED_ENABLED=true
AGENIX_LOGGING_CHUNK_MAX_EVENTS=1000
AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS=60
AGENIX_LOGGING_EVENT_CODE_PREFIX=true
AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION=false
```

#### File: `docs/environment-variables.md` (ADD)

**Add new section**:

```markdown
### Chunked Logging (All Services)

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_LOGGING_CHUNKED_ENABLED` | Enable operation-based chunked logging | `true` | `true`, `false` |
| `AGENIX_LOGGING_CHUNK_MAX_EVENTS` | Max events per chunk before auto-flush | `1000` | `500`, `2000` |
| `AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS` | Max age of chunk buffer before auto-flush | `60` | `30`, `120` |
| `AGENIX_LOGGING_EVENT_CODE_PREFIX` | Show [CODE] prefix in log output | `true` | `true`, `false` |
| `AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION` | Include FilePath:LineNumber in logs | `false` | `true`, `false` |
```

---

## Testing Phase 2

### Manual Testing

#### 1. Start Hub with Chunked Logging

```bash
cd /Users/asuruceanu/RiderProjects/agenix-playwright-grid
export AGENIX_LOGGING_CHUNKED_ENABLED=true
dotnet run --project hub
```

#### 2. Test HTTP Request Chunking

```bash
# Create a test item
curl -X POST http://localhost:5100/api/test-items \
  -H "Content-Type: application/json" \
  -d '{
    "launchId": "123e4567-e89b-12d3-a456-426614174000",
    "name": "Login test",
    "type": "Test",
    "labelKey": "myapp:chromium:prod"
  }'
```

**Expected Console Output**:
```
╔═ Operation: POST /api/test-items  OperationId=9b3c4a21...
║ Start: 2025-12-23T10:15:02.123Z
║ Inputs: method=POST path=/api/test-items
║
║ [INF][ITEM01] Test item created - launchId=123... name="Login test" itemType=Test
║ [INF][POOL01] Browser borrow requested - labelKey=myapp:chromium:prod
║ [INF][POOL03] Browser ready - browserId=br_456 endpoint=ws://...
║ [INF][ITEM02] Test item persisted to database - itemId=789...
║
╚═ End: SUCCESS  Duration=1.82s  statusCode=201  KeyEvents=[ITEM01,POOL01,POOL03,ITEM02]
```

#### 3. Test Background Service Chunking

**Wait for BrowserAutoStopService tick** (check logs every 5 minutes):

```
╔═ Operation: BrowserAutoStop:Tick  OperationId=def456...
║ Start: 2025-12-23T10:20:00.000Z
║
║ [INF][POOL20] Cleanup scan started - batchSize=50
║ [INF][POOL22] Test item auto-stopped - itemId=123... reason=Inactivity
║ [INF][POOL21] Browser released - browserId=br_456
║
╚═ End: SUCCESS  Duration=342ms  scanned=50 processed=1 released=1  KeyEvents=[POOL20,POOL22,POOL21]
```

#### 4. Test Error Classification

```bash
# Trigger a 404 error
curl http://localhost:5100/api/test-items/00000000-0000-0000-0000-000000000000
```

**Expected Output**:
```
╔═ Operation: GET /api/test-items/00000000...  OperationId=abc...
║ Start: 2025-12-23T10:25:00.000Z
║
╚═ End: FAILED  ErrorType=NotFound  Duration=15ms  statusCode=404
```

### Integration Tests

#### File: `hub.Tests/Infrastructure/ChunkedLoggingTests.cs` (NEW)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace PlaywrightHub.Tests.Infrastructure;

[TestFixture]
public class ChunkedLoggingTests
{
    [Test]
    public void OperationContext_FlowsAcrossAsyncBoundaries()
    {
        using var ctx = OperationContext.Begin("TestOp");
        var ctxInside = OperationContext.Current;

        Assert.That(ctxInside, Is.Not.Null);
        Assert.That(ctxInside!.OperationName, Is.EqualTo("TestOp"));
    }

    [Test]
    public void ChunkedLogger_RecordsKeyEvents()
    {
        var logger = NullLogger.Instance;
        var chunkedLogger = new ChunkedLogger(logger, "Test");

        using var op = chunkedLogger.BeginOperation("TestOp");
        chunkedLogger.LogMilestone(EventCodes.TestItem.ItemCreated, "test");

        var context = OperationContext.Current;
        Assert.That(context!.KeyEvents, Contains.Item(EventCodes.TestItem.ItemCreated));
    }

    [Test]
    public async Task OperationContext_PropagatesInNestedOperations()
    {
        using var parent = OperationContext.Begin("ParentOp");
        var parentId = OperationContext.Current!.OperationId;

        using var child = OperationContext.Begin("ChildOp", parentOperationId: parentId);
        var childContext = OperationContext.Current!;

        Assert.That(childContext.ParentOperationId, Is.EqualTo(parentId));
    }
}
```

---

## Rollback Plan

If Phase 2 causes issues:

### 1. Disable Chunked Logging

```bash
# .env
AGENIX_LOGGING_CHUNKED_ENABLED=false
```

Restart hub - logging reverts to line-by-line.

### 2. Remove Middleware

Comment out in `hub/Services/HubServiceRunner.cs`:

```csharp
// app.UseOperationLogging();
```

### 3. Revert Serilog Configuration

Restore original `hub/appsettings.json` from git:

```bash
git checkout hub/appsettings.json
```

---

## Success Criteria

- [ ] HTTP requests logged as visual chunks with boundaries
- [ ] Event codes appear in all milestone logs
- [ ] OperationId present in all log events
- [ ] Background service ticks logged as discrete operations
- [ ] Error classification shows ErrorType + Dependency
- [ ] KeyEvents summary appears in chunk footer
- [ ] Duration calculated and formatted correctly
- [ ] No performance degradation (< 5ms overhead per operation)
- [ ] Integration tests pass
- [ ] Manual smoke tests pass

---

## Performance Metrics

**Expected Overhead**:
- Middleware: ~2ms per request (OperationContext creation)
- ChunkedLogger: ~1ms per milestone log
- Chunk buffering: ~100KB memory per active operation
- Total: < 5ms per operation

**Memory Budget**:
- Max 1000 events per chunk
- Max 60 seconds buffer age
- ~100 concurrent operations typical
- Peak memory: ~10MB for chunk buffers

---

## Next Steps After Phase 2

### Phase 3 - Worker Service Integration

- Worker browser lifecycle operations
- Worker registration with Hub
- Health check operations

### Phase 4 - Ingestion Service Integration

- Batch processing operations
- Token optimization operations
- Database write operations

### Phase 5 - Housekeeping Service Integration

- Retention cleanup operations
- Artifact deletion operations
- Audit cleanup operations

---

**Status**: 📋 PLANNED (awaiting Phase 1 completion)
**Estimated Effort**: 2-3 hours
**Risk Level**: Low (non-breaking, feature-flagged)
**Dependencies**: ✅ Phase 1 Complete
