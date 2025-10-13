# Chunked Logging - Architecture Planning (SDD Stage 2)

**Feature Name**: Chunked Logging Phase 2 (Hub Integration)
**Created**: 2025-12-26
**Status**: Planned (awaiting approval)
**SDD Stage**: Stage 2 - Architecture Planning
**Previous Stage**: [SDD-STAGE1-SPECIFICATION.md](SDD-STAGE1-SPECIFICATION.md)

---

## Research: Existing Patterns

### Similar Features in Codebase

**Logging Infrastructure**:
- **Location**: `hub/Services/HubServiceRunner.cs` (lines 150-200)
- **Pattern**: Serilog configuration with enrichers (Machine Name, Thread ID)
- **Sinks**: Console, File (with rolling interval)
- **Filtering**: Background services logged to separate file

**Middleware Pattern**:
- **No existing middleware** - Hub uses Minimal APIs without custom middleware
- **Opportunity**: Add OperationLoggingMiddleware as first middleware in pipeline
- **Reference**: ASP.NET Core RequestLoggingMiddleware pattern (built-in but not used)

**Background Services**:
- **Location**: `hub/Infrastructure/Adapters/Background/BrowserAutoStopService.cs`
- **Pattern**: BackgroundService with ExecuteAsync tick loop
- **Logging**: ILogger<T> with simple log statements
- **Opportunity**: Wrap ticks in operation scopes

**Error Handling**:
- **Location**: Throughout endpoint files (TestItemsEndpoints.cs, LaunchesEndpoints.cs)
- **Pattern**: Try-catch with Results.Problem(statusCode, detail)
- **Logging**: Logger.LogError(ex, message)
- **Opportunity**: Add error classification with ErrorType + DependencyName

---

### Relevant Patterns (from CLAUDE.md)

**1. Middleware Pattern** (ASP.NET Core):
- Defined in `IApplicationBuilder.UseMiddleware<T>()`
- Request/response pipeline interception
- Used for: Authentication, CORS, compression, logging

**2. Dependency Injection** (Microsoft.Extensions.DI):
- Inject ILogger<T> via constructor
- Register services in HubServiceRunner.cs
- Scoped, Transient, Singleton lifetimes

**3. AsyncLocal<T> Context Propagation**:
- Thread-safe, async-aware context storage
- Flows through async/await boundaries
- Used by: HttpContext.Items, Activity.Current

**4. Serilog Enrichers**:
- Enrich log events with additional properties
- Registered in configuration: "Enrich": ["FromLogContext", "WithMachineName"]
- Custom enrichers: Implement ILogEventEnricher

---

## Approach 1: Middleware-Only (Minimal)

### Description

HTTP requests wrapped in operation scopes via middleware. Services use standard ILogger (no ChunkedLogger).

### Implementation

**Components**:
- OperationLoggingMiddleware - HTTP request wrapping only
- No changes to endpoints or services
- OperationContext enricher for Serilog
- ChunkedConsoleSink for visual output

**File Changes**:
- Create: `hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs` (120 lines)
- Modify: `hub/Services/HubServiceRunner.cs` (add middleware registration, 1 line)
- Modify: `hub/appsettings.json` (update Serilog configuration, 30 lines)

**No ChunkedLogger in Services**:
- Services continue using `ILogger<T>.LogInformation(...)`
- Logs automatically enriched with OperationId from middleware context
- No explicit BeginOperation() calls needed

### Pros

- ✅ **Minimal code changes** - Only middleware + configuration
- ✅ **Fast implementation** - 1 hour estimated
- ✅ **No learning curve** - Developers keep using ILogger
- ✅ **Automatic HTTP correlation** - All HTTP requests get OperationId
- ✅ **Low risk** - Small surface area for bugs

### Cons

- ❌ **No explicit milestones** - Can't use event codes (POOL01, ITEM02)
- ❌ **No operation boundaries** - Background services don't get operation scoping
- ❌ **No error classification** - ErrorType and DependencyName not populated
- ❌ **No nested operations** - Can't show browser borrow as child of StartTestItem
- ❌ **Limited value** - Only solves HTTP correlation, not full observability
- ❌ **Doesn't meet FR5, FR6** - No structured events or error clarity

### Complexity

**Low** - Single middleware + configuration

### Alignment with CLAUDE.md

- ✅ **DDD Layers**: Middleware in Interface layer (correct)
- ✅ **SOLID**: SRP (middleware does one thing)
- ⚠️ **Acceptance Criteria**: Only meets FR1, FR2 (partial), FR3 (partial)

---

## Approach 2: Middleware + ChunkedLogger (Recommended)

### Description

HTTP requests wrapped via middleware. Services use ChunkedLogger for explicit milestones and error classification.

### Implementation

**Components**:
- OperationLoggingMiddleware - HTTP request wrapping
- ChunkedLogger used in TestItemsEndpoints, LaunchesEndpoints
- ChunkedLogger used in BrowserPoolService, BrowserAutoStopService
- Event codes (POOL01, ITEM02, LCH10) for milestone tracking
- Error classification (ErrorType, DependencyName) in catch blocks
- Serilog enrichers + ChunkedConsoleSink

**File Changes**:
- Create: `hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs` (130 lines)
- Modify: `hub/Infrastructure/Web/TestItemsEndpoints.cs` (add ChunkedLogger, ~50 lines)
- Modify: `hub/Infrastructure/Web/LaunchesEndpoints.cs` (add ChunkedLogger, ~40 lines)
- Modify: `hub/Infrastructure/Services/BrowserPoolService.cs` (add ChunkedLogger, ~60 lines)
- Modify: `hub/Infrastructure/Adapters/Background/BrowserAutoStopService.cs` (add ChunkedLogger, ~50 lines)
- Modify: `hub/Services/HubServiceRunner.cs` (add middleware registration, 1 line)
- Modify: `hub/appsettings.json` (update Serilog configuration, 30 lines)

**ChunkedLogger Usage Pattern**:
```csharp
// In endpoints (existing OperationContext from middleware)
var chunkedLogger = new ChunkedLogger(_logger, nameof(TestItemsEndpoints));
chunkedLogger.LogMilestone(EventCodes.TestItem.ItemCreated, "name={Name}", name);

// In services (nested operation)
using var op = chunkedLogger.BeginOperation("BorrowBrowser", inputs);
chunkedLogger.LogMilestone(EventCodes.BrowserPool.BrowserReady, "browserId={Id}", id);
((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
```

### Pros

- ✅ **Meets all FRs** - Satisfies FR1-FR6 completely
- ✅ **Explicit milestones** - Event codes (POOL01, ITEM02) for key events
- ✅ **Error classification** - ErrorType + DependencyName populated
- ✅ **Nested operations** - Browser borrow shows as child of StartTestItem
- ✅ **Background services** - Ticks wrapped in operation scopes
- ✅ **Observability** - Complete operation context for debugging
- ✅ **Structured events** - Machine-readable event codes for alerting
- ✅ **Gradual adoption** - Can migrate endpoints incrementally

### Cons

- ❌ **More code changes** - ~200 lines across 5 files
- ❌ **Learning curve** - Developers must learn ChunkedLogger API
- ❌ **Implementation time** - 2-3 hours estimated
- ❌ **Memory overhead** - ~1KB per active operation (acceptable per NFR2)
- ❌ **Risk of forgot dispose** - Developers must use `using` statement

### Complexity

**Medium** - Middleware + service instrumentation + configuration

### Alignment with CLAUDE.md

- ✅ **DDD Layers**:
  - Middleware in Interface layer (HTTP boundary)
  - ChunkedLogger in Infrastructure layer (logging concern)
- ✅ **SOLID**:
  - SRP (ChunkedLogger does one thing)
  - DIP (depends on ILogger abstraction)
  - ISP (focused 3-method interface)
- ✅ **Acceptance Criteria**: Meets FR1-FR6, NFR1-NFR4
- ✅ **Prompt Engineering**: Follows Repository pattern (ChunkedLogger analogous to ResultsStore)

---

## Approach 3: AOP with Attributes (Advanced)

### Description

Automatic operation wrapping via `[Operation]` attribute on methods. Code generation or IL weaving inserts BeginOperation/EndOperation.

### Implementation

**Components**:
- [Operation("StartTestItem")] attribute on methods
- Roslyn source generator or PostSharp IL weaving
- Automatic BeginOperation at method entry
- Automatic SetOutputs/Dispose at method exit
- Same middleware, enrichers, sinks as Approach 2

**Example**:
```csharp
[Operation("StartTestItem")]
public async Task<IResult> StartTestItem(...)
{
    // Source generator inserts:
    // using var op = chunkedLogger.BeginOperation("StartTestItem", inputs);

    // User code here (unchanged)

    // Source generator inserts:
    // ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
}
```

**File Changes**:
- Create: `Agenix.PlaywrightGrid.SourceGenerators` project (new)
- Create: `OperationAttribute.cs` (attribute definition)
- Create: `OperationSourceGenerator.cs` (Roslyn source generator, ~500 lines)
- Modify: Endpoints and services (add [Operation] attribute, ~20 lines)
- Modify: `hub/appsettings.json` (Serilog configuration, 30 lines)

### Pros

- ✅ **Minimal manual code** - Just add [Operation] attribute
- ✅ **Consistent** - Hard to forget instrumentation
- ✅ **Clean code** - No using blocks cluttering methods
- ✅ **Automatic dispose** - No risk of forgot dispose
- ✅ **Meets all FRs** - Satisfies FR1-FR6 when combined with milestone logging

### Cons

- ❌ **High complexity** - Roslyn source generator ~500 lines
- ❌ **Build time overhead** - Code generation on every build
- ❌ **Debugging difficulty** - Generated code not in source files
- ❌ **Async complexity** - Hard to handle async/await boundaries correctly
- ❌ **Not flexible** - Can't control inputs/outputs dynamically
- ❌ **Learning curve** - Developers must understand attribute magic
- ❌ **Implementation time** - 1-2 days estimated

### Complexity

**High** - Source generator + attribute system + configuration

### Alignment with CLAUDE.md

- ⚠️ **Over-engineering** - YAGNI principle (see CLAUDE.md lines 11100-11120)
- ❌ **Complexity vs Value** - High complexity for marginal benefit over Approach 2
- ✅ **DDD Layers**: Correct, but overkill
- ❌ **Prompt Engineering**: Violates "Don't Over-Complicate" (CLAUDE.md line 9780)

---

## Recommendation: Approach 2 (Middleware + ChunkedLogger)

### Justification

**1. Meets All Requirements**:
- ✅ FR1-FR6: All functional requirements satisfied
- ✅ NFR1-NFR4: Performance, concurrency, security, compatibility
- ✅ User Stories: All 5 user stories addressed

**2. Aligns with CLAUDE.md Principles**:
- **DDD Layer Boundaries**: Middleware in Interface, ChunkedLogger in Infrastructure
- **SOLID Compliance**: SRP, DIP, ISP all satisfied
- **Code Quality**: Early return pattern, DRY principle followed
- **Prompt Engineering**: Follows established patterns (Repository, Middleware)

**3. Proven Pattern**:
- **Similar to OpenTelemetry**: Activity.Current pattern
- **Similar to Serilog**: LogContext.PushProperty pattern
- **Used in production**: ASP.NET Core diagnostics middleware

**4. Gradual Migration Path**:
- Can enable per-endpoint (TestItemsEndpoints first, then LaunchesEndpoints)
- Can enable per-service (Hub Phase 2, Worker Phase 3)
- Can disable instantly via feature flag

**5. Balance of Complexity vs Value**:
- Not too simple (Approach 1 doesn't meet requirements)
- Not too complex (Approach 3 is over-engineered)
- Just right for the value delivered

### Comparison Table

| Criterion | Approach 1 (Minimal) | Approach 2 (Recommended) | Approach 3 (AOP) |
|-----------|---------------------|--------------------------|------------------|
| **Implementation Time** | 1 hour | 2-3 hours | 1-2 days |
| **Code Changes** | ~150 lines | ~360 lines | ~700 lines |
| **Meets FRs** | FR1, FR2 (partial) | FR1-FR6 ✅ | FR1-FR6 ✅ |
| **Complexity** | Low | Medium | High |
| **Maintainability** | High | High | Low (generated code) |
| **Learning Curve** | None | Low (3 methods) | High (attributes magic) |
| **Risk** | Low | Low | Medium (build errors) |
| **YAGNI** | ✅ Simple enough | ✅ Right-sized | ❌ Over-engineered |

---

## Risks & Mitigations

### Risk 1: Developers Forget to Use `using` Statement

**Risk**: OperationScope not disposed, causing AsyncLocal<T> memory leaks

**Impact**: High - Memory growth, eventual OutOfMemoryException

**Probability**: Medium - Common mistake with IDisposable

**Mitigation**:
1. **Code Review Checklist** - Add "BeginOperation wrapped in using"
2. **Integration Tests** - Test for memory leaks (measure memory before/after)
3. **Documentation** - Prominent warning in CLAUDE.md
4. **Roslyn Analyzer** (Phase 3+) - Detect missing using statements
5. **Monitoring** - Prometheus metrics for active operations count

**Residual Risk**: Low (multiple layers of defense)

---

### Risk 2: Performance Regression on High-QPS Endpoints

**Risk**: Operation scope overhead causes latency increase

**Impact**: Medium - Slower response times, increased costs

**Probability**: Low - Benchmarks show <1ms overhead

**Mitigation**:
1. **Benchmarking** - Load test before/after (use BenchmarkDotNet)
2. **Feature Flag** - Can disable per-endpoint if needed
3. **Selective Instrumentation** - Don't instrument /health endpoint
4. **Async Fire-and-Forget** - Ensure log writing is non-blocking
5. **Buffer Tuning** - Adjust chunk buffer limits for performance

**Residual Risk**: Low (feature flag allows instant disable)

---

### Risk 3: Breaking Changes to Existing Logs

**Risk**: Chunked format breaks existing log parsers/dashboards

**Impact**: Medium - Disrupts monitoring, alerting

**Probability**: Low - Designed for backward compatibility

**Mitigation**:
1. **Backward Compatibility Mode** - Environment variable to disable
2. **Gradual Migration** - Enable per-service
3. **Documentation** - Migration guide with before/after examples
4. **Rollback Plan** - Can disable instantly via environment variable
5. **Testing** - Verify log format with existing parsers

**Residual Risk**: Low (feature flag + rollback plan)

---

## Contracts

### Database Schema Changes

**None** - Logs stored in Serilog sinks (Console, File, Seq), not PostgreSQL.

---

### DTOs

**No new DTOs** - Existing DTOs unchanged. ChunkedLogger is internal infrastructure.

---

### API Endpoints

**No new endpoints** - Existing endpoints enhanced with ChunkedLogger.

**Modified Behavior**:
- HTTP responses unchanged (same status codes, bodies)
- Console logs now show operation chunks
- Seq/Grafana queries can filter by OperationId

---

### Core Classes

#### OperationLoggingMiddleware

**Purpose**: Wrap HTTP requests in operation scopes

**File**: `hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs` (new)

**Dependencies**:
- `ILogger<OperationLoggingMiddleware>` - Injected via DI
- `IConfiguration` - For AGENIX_LOGGING_CHUNKED_ENABLED flag
- `ChunkedLogger` - From `Agenix.PlaywrightGrid.Shared.Logging`

**Key Methods**:
```csharp
public async Task InvokeAsync(HttpContext context)
{
    // Create operation for HTTP request
    // Extract inputs from context.Request
    // Call await _next(context)
    // Set outputs from context.Response
    // Classify errors based on status code
}
```

**Registration**:
```csharp
// In HubServiceRunner.cs
app.UseOperationLogging();
```

---

#### ChunkedLogger Usage in Endpoints

**Files Modified**:
- `hub/Infrastructure/Web/TestItemsEndpoints.cs`
- `hub/Infrastructure/Web/LaunchesEndpoints.cs`

**Pattern**:
```csharp
private static async Task<IResult> StartTestItem(
    [FromBody] StartTestItemRequest req,
    [FromServices] ILogger<TestItemsEndpoints> logger,
    ...)
{
    var chunkedLogger = new ChunkedLogger(logger, nameof(TestItemsEndpoints));

    // Log milestones (OperationContext already created by middleware)
    chunkedLogger.LogMilestone(
        EventCodes.TestItem.ItemCreated,
        "launchId={LaunchId} name={Name}",
        req.LaunchId, req.Name);

    // ... business logic ...

    // Errors classified in catch blocks
    try
    {
        // ...
    }
    catch (TimeoutException ex)
    {
        var context = OperationContext.Current;
        chunkedLogger.FailOperation(context, ex, ErrorType.Timeout, DependencyName.Worker);
        throw;
    }
}
```

---

#### ChunkedLogger Usage in Services

**Files Modified**:
- `hub/Infrastructure/Services/BrowserPoolService.cs`
- `hub/Infrastructure/Adapters/Background/BrowserAutoStopService.cs`

**Pattern (Nested Operation)**:
```csharp
public async Task<BorrowResult?> BorrowAsync(string labelKey, string runId, CancellationToken ct)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(BrowserPoolService));

    // Create nested operation (child of HTTP request operation)
    var parentOpId = OperationContext.Current?.OperationId;
    using var op = chunkedLogger.BeginOperation(
        "BorrowBrowser",
        inputs: new Dictionary<string, object> { ["labelKey"] = labelKey },
        parentOperationId: parentOpId);

    try
    {
        // Log milestones
        chunkedLogger.LogMilestone(EventCodes.BrowserPool.BorrowRequested, "labelKey={LabelKey}", labelKey);

        // ... business logic ...

        // Set outputs before dispose
        ((ChunkedLogger.OperationScope)op).SetOutputs(new Dictionary<string, object>
        {
            ["browserId"] = browser.BrowserId
        });

        return result;
    }
    catch (RedisException ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis);
        throw;
    }
}
```

**Pattern (Background Service Tick)**:
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(BrowserAutoStopService));

    while (!stoppingToken.IsCancellationRequested)
    {
        // Each tick is a discrete operation
        using var op = chunkedLogger.BeginOperation("BrowserAutoStop:Tick");

        int scanned = 0, processed = 0;

        try
        {
            chunkedLogger.LogMilestone(EventCodes.BrowserPool.ScanStarted, "batchSize={Size}", batchSize);

            // ... tick logic ...

            ((ChunkedLogger.OperationScope)op).SetOutputs(new Dictionary<string, object>
            {
                ["scanned"] = scanned,
                ["processed"] = processed
            });
        }
        catch (Exception ex)
        {
            ((ChunkedLogger.OperationScope)op).Fail(ex, ErrorType.Unexpected);
        }

        await Task.Delay(interval, stoppingToken);
    }
}
```

---

### Serilog Configuration Changes

#### File: `hub/appsettings.json` (modify)

**Changes**:
1. Add `Agenix.PlaywrightGrid.Shared` to `Using` array
2. Replace `Console` sink with `ChunkedConsole` sink
3. Add enrichers: `WithOperationContext`, `WithEventCode`, `WithCodeContext`

**Key Configuration**:
```json
{
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Sinks.File",
      "Agenix.PlaywrightGrid.Shared"
    ],
    "WriteTo": [
      {
        "Name": "ChunkedConsole",
        "Args": {
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

---

### Environment Variables

**File**: `.env` (add)

```bash
# Chunked Logging Configuration (Phase 2)
AGENIX_LOGGING_CHUNKED_ENABLED=true
AGENIX_LOGGING_CHUNK_MAX_EVENTS=1000
AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS=60
AGENIX_LOGGING_EVENT_CODE_PREFIX=true
AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION=false
```

**File**: `docker-compose.yml` (add to hub service)

```yaml
hub:
  environment:
    - AGENIX_LOGGING_CHUNKED_ENABLED=${AGENIX_LOGGING_CHUNKED_ENABLED:-true}
    - AGENIX_LOGGING_CHUNK_MAX_EVENTS=${AGENIX_LOGGING_CHUNK_MAX_EVENTS:-1000}
    - AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS=${AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS:-60}
```

**File**: `docs/ENVIRONMENT-VARIABLES.md` (add section)

```markdown
### Chunked Logging (All Services)

| Variable | Description | Default |
|----------|-------------|---------|
| `AGENIX_LOGGING_CHUNKED_ENABLED` | Enable operation-based chunked logging | `true` |
| `AGENIX_LOGGING_CHUNK_MAX_EVENTS` | Max events per chunk before auto-flush | `1000` |
| `AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS` | Max age of chunk buffer before auto-flush | `60` |
```

---

## Dependencies

### Library Dependencies

**Existing (No New Dependencies)**:
- Serilog (already in hub)
- Serilog.Sinks.Console (already in hub)
- Serilog.Sinks.File (already in hub)
- Microsoft.Extensions.Logging (already in hub)
- Agenix.PlaywrightGrid.Shared (already referenced, Phase 1 complete)

**No NuGet packages added** - Phase 1 already added ChunkedLogger to Shared library.

---

### Service Dependencies

**Internal Services**:
- BrowserPoolService - Modified to use ChunkedLogger
- BrowserAutoStopService - Modified to use ChunkedLogger

**External Services**:
- None - Chunked logging is self-contained

---

### Infrastructure Dependencies

**Serilog Sinks** (optional, for enhanced observability):
- Serilog.Sinks.Seq - For structured log aggregation (optional)
- Serilog.Sinks.Grafana.Loki - For Grafana dashboards (optional)

**Note**: Optional sinks not required for Phase 2. Console output sufficient for initial rollout.

---

## Next Stage: Task Breakdown

Once this architecture is approved, proceed to **Stage 3: Task Breakdown**:

1. Break down implementation into discrete tasks
2. Identify task dependencies
3. Estimate complexity (Low/Medium/High)
4. Define verification criteria for each task
5. Create task dependency graph
6. Plan execution strategy (4 phases)

**See**: PHASE2-HUB-INTEGRATION.md sections 2.1-2.7 will become individual tasks in Stage 3.

---

**Status**: ✅ Stage 2 Complete - Architecture Planning
**Next Stage**: Stage 3 - Task Breakdown
**Quality Gate**: Architecture reviewed and approved by team
**Recommendation**: Proceed with Approach 2 (Middleware + ChunkedLogger)
