# HttpContext TraceIdentifier in Chunked Logging - Architecture

**Date**: 2025-01-15
**Status**: Draft

---

## Research: Existing Patterns

### Similar Features in Codebase

**1. OperationContext (Shared/Logging/OperationContext.cs)**
- Stores operation-scoped data: OperationId, TraceId, SpanId, Properties
- Uses AsyncLocal<OperationContext> for async-safe thread-local storage
- Properties dictionary allows arbitrary key-value storage

**2. OperationContextEnricher (Shared/Logging/SerilogEnrichers.cs)**
- Serilog enricher that reads OperationContext.Current
- Adds OperationId, TraceId, SpanId to log events
- Pattern: Read context → Add properties to log event

**3. OperationLoggingMiddleware (hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs)**
- ASP.NET Core middleware that creates OperationContext at request start
- Captures Activity.Current.TraceId and SpanId
- Disposes OperationContext at request end
- Registered in Program.cs: `app.UseMiddleware<OperationLoggingMiddleware>()`

**Relevant Patterns** (from CLAUDE.md):
- **Middleware Pattern**: HttpContext interception for cross-cutting concerns
- **Enricher Pattern**: Serilog enrichers add contextual data to logs
- **AsyncLocal Storage**: Thread-safe context storage across async calls

---

## Approach 1: Extend OperationLoggingMiddleware (Recommended)

### Description
Enhance the existing `OperationLoggingMiddleware` to capture `HttpContext.TraceIdentifier` and store it in `OperationContext.Properties["HttpTraceId"]`. No new middleware needed.

### Implementation

**Layer**: Infrastructure (hub/Infrastructure/Web/Middleware/)

**Key Changes**:
1. Modify `OperationLoggingMiddleware.InvokeAsync()` to capture `httpContext.TraceIdentifier`
2. Store in `opContext.Properties["HttpTraceId"]` after creating OperationContext
3. No changes to OperationContextEnricher (already reads Properties dictionary)

**Code**:
```csharp
// hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs (lines ~40-50)
public async Task InvokeAsync(HttpContext httpContext)
{
    var activity = Activity.Current;
    var traceId = activity?.TraceId.ToString();
    var spanId = activity?.SpanId.ToString();

    using (var opContext = new OperationContext(operationId, traceId, spanId))
    {
        // ✅ NEW: Capture HttpContext.TraceIdentifier
        opContext.Properties["HttpTraceId"] = httpContext.TraceIdentifier;

        await _next(httpContext);
    }
}
```

### Pros
- ✅ **Minimal changes**: One line addition to existing middleware
- ✅ **Zero new files**: No new middleware class needed
- ✅ **Consistent location**: All context setup in one place
- ✅ **Already registered**: Middleware already in pipeline
- ✅ **Automatic enrichment**: OperationContextEnricher reads Properties automatically

### Cons
- ❌ **Couples HTTP concerns to general OperationContext**: Properties dictionary contains HTTP-specific data
- ❌ **Hub-only initially**: Would need to duplicate change in ingestion/housekeeping services

**Complexity**: Low (1 line change)

---

## Approach 2: New HttpTraceContextMiddleware (Original Plan)

### Description
Create a dedicated `HttpTraceContextMiddleware` that ONLY captures `HttpContext.TraceIdentifier` and stores it in OperationContext. Register after `OperationLoggingMiddleware`.

### Implementation

**Layer**: Infrastructure (Shared/Logging/)

**Key Classes**:
- Create: `Agenix.PlaywrightGrid.Shared/Logging/HttpTraceContextMiddleware.cs`
- Modify: `hub/Program.cs`, `ingestion/Program.cs`, `housekeeping-service/Program.cs`

**Code**:
```csharp
// Agenix.PlaywrightGrid.Shared/Logging/HttpTraceContextMiddleware.cs
public class HttpTraceContextMiddleware
{
    private readonly RequestDelegate _next;

    public HttpTraceContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var currentContext = OperationContext.Current;
        if (currentContext != null)
        {
            currentContext.Properties["HttpTraceId"] = context.TraceIdentifier;
        }

        await _next(context);
    }
}

// hub/Program.cs (after UseRouting, before MapEndpoints)
app.UseMiddleware<OperationLoggingMiddleware>();
app.UseMiddleware<HttpTraceContextMiddleware>();  // ✅ NEW
```

### Pros
- ✅ **Single Responsibility**: Middleware only handles HttpTraceId (not mixed with operation context setup)
- ✅ **Shared across services**: Single class in Shared project, used by all services
- ✅ **Explicit intent**: Clear that this middleware is specifically for HTTP tracing
- ✅ **Easy to test**: Isolated logic, easy to unit test

### Cons
- ❌ **New file**: Adds one more middleware class (minor)
- ❌ **Registration overhead**: All services must remember to register it
- ❌ **Pipeline complexity**: One more middleware in the chain

**Complexity**: Low-Medium (new file, 3 registration points)

---

## Approach 3: Serilog Enricher Only (No Middleware)

### Description
Create a new Serilog enricher `HttpContextEnricher` that directly reads `IHttpContextAccessor` to get `TraceIdentifier` at log-time (not request-time).

### Implementation

**Layer**: Infrastructure (Shared/Logging/)

**Key Classes**:
- Create: `Agenix.PlaywrightGrid.Shared/Logging/HttpContextEnricher.cs`
- Modify: `hub/Program.cs` (Serilog configuration)

**Code**:
```csharp
// Agenix.PlaywrightGrid.Shared/Logging/HttpContextEnricher.cs
public class HttpContextEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("HttpTraceId", httpContext.TraceIdentifier));
        }
    }
}

// hub/Program.cs (Serilog configuration)
Log.Logger = new LoggerConfiguration()
    .Enrich.With<OperationContextEnricher>()
    .Enrich.With<HttpContextEnricher>()  // ✅ NEW
    .WriteTo.Console()
    .CreateLogger();
```

### Pros
- ✅ **No middleware**: Zero pipeline changes
- ✅ **Lazy evaluation**: TraceIdentifier read at log time, not request time
- ✅ **DI integration**: Uses IHttpContextAccessor (standard ASP.NET Core pattern)

### Cons
- ❌ **HttpContext dependency**: Requires IHttpContextAccessor registration
- ❌ **Log-time overhead**: Reads HttpContext on every log event (small cost)
- ❌ **Not available in OperationContext**: Can't be used by non-logging code
- ❌ **Async boundary issues**: IHttpContextAccessor may fail in background tasks

**Complexity**: Medium (new enricher, DI registration, IHttpContextAccessor setup)

---

## Recommendation: Approach 1 (Extend OperationLoggingMiddleware)

### Justification

1. **Minimal Changes** - One line addition to existing middleware
2. **Consistency** - All operation context setup in one place (OperationLoggingMiddleware)
3. **Zero New Files** - No additional classes to maintain
4. **Already Works** - OperationContextEnricher automatically reads Properties dictionary
5. **DDD Alignment** - Middleware is already in Infrastructure layer (correct boundary)
6. **SOLID Compliance**:
   - **SRP**: Still single responsibility (initialize operation context)
   - **OCP**: Open for extension via Properties dictionary
   - **DIP**: No new dependencies

### Why Not Approach 2?
- Approach 2 adds a separate middleware for a single property assignment (overkill)
- Two middlewares for operation context (split brain)
- More registration points = more places to forget

### Why Not Approach 3?
- IHttpContextAccessor adds complexity (DI registration, async issues)
- Log-time evaluation adds overhead on every log write
- HttpContext may not be available in background/async contexts

### Risks & Mitigations

**Risk 1**: Properties dictionary becomes dumping ground for HTTP concerns
- **Mitigation**: Document that Properties is for cross-cutting contextual data (including HTTP)
- **Precedent**: Already storing TraceId/SpanId which are request-scoped

**Risk 2**: Other services (ingestion, housekeeping) need same change
- **Mitigation**: Since they already have OperationLoggingMiddleware equivalent, apply same one-line change
- **Alternative**: Extract shared middleware to Shared project in future if duplication becomes problem

---

## Contracts

### Modified Interface

**OperationLoggingMiddleware** (no interface, direct modification):
```csharp
// hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs
public async Task InvokeAsync(HttpContext httpContext)
{
    var activity = Activity.Current;
    var traceId = activity?.TraceId.ToString();
    var spanId = activity?.SpanId.ToString();

    using (var opContext = new OperationContext(operationId, traceId, spanId))
    {
        // ✅ NEW LINE
        opContext.Properties["HttpTraceId"] = httpContext.TraceIdentifier;

        await _next(httpContext);
    }
}
```

### OperationContext Properties Contract

**New Property**:
- **Key**: `"HttpTraceId"` (string)
- **Value**: `HttpContext.TraceIdentifier` (string)
- **Format**: ASP.NET Core trace identifier format (e.g., `"00-abc123-def456-789"`)
- **Availability**: Only present in HTTP request contexts (null in background jobs)

### Serilog Output Contract

**New Log Property**:
```json
{
  "HttpTraceId": "00-abc123-def456-789"
}
```

**Full Example**:
```json
{
  "Timestamp": "2025-01-15T10:30:45.123Z",
  "Level": "Error",
  "MessageTemplate": "Log item creation failed",
  "Properties": {
    "EventCode": "LOG01",
    "OperationId": "CreateLogItem",
    "TraceId": "00-otel-trace-123",    // OpenTelemetry
    "SpanId": "span-456",
    "HttpTraceId": "00-http-trace-789", // ✅ NEW
    "TestItemUuid": "abc-def"
  }
}
```

---

## Dependencies

### External Libraries
- None (uses existing Microsoft.AspNetCore.Http)

### Infrastructure
- None (middleware already registered)

### Other Features
- **OperationContext** - Modified to store HttpTraceId
- **OperationContextEnricher** - No changes (already reads Properties)
- **OperationLoggingMiddleware** - One line addition

---

## Rollback Plan

If issues arise:
1. **Remove the one line** from OperationLoggingMiddleware
2. **Redeploy services** with original code
3. **No data migration needed** (log properties are ephemeral)

**Risk Level**: Very Low (single line change, non-breaking)

---

## Alternative Considered: Both TraceIds in ProblemDetails

**Question**: Should ProblemDetails include BOTH OpenTelemetry TraceId AND HttpContext.TraceIdentifier?

**Answer**: No, out of scope for this spec.

**Reasons**:
1. ProblemDetails already uses `HttpContext.TraceIdentifier` consistently
2. This spec is about making LOGS match API responses (one-way: API → Logs)
3. Including both would confuse users ("which traceId do I search for?")
4. If needed in future, add as separate property: `{ "traceId": "...", "otelTraceId": "..." }`

**Current behavior** (keep as-is):
```csharp
return ProblemDetailsHelpers.NotFound(
    message,
    traceId: httpContext.TraceIdentifier);  // Single traceId in response
```

**Logs will now have both** (for correlation):
```json
{
  "TraceId": "00-otel-trace-123",      // OpenTelemetry Activity.TraceId
  "HttpTraceId": "00-http-trace-789"   // HttpContext.TraceIdentifier (matches API)
}
```

Support can search by either:
- Search by `HttpTraceId` from API response (primary use case)
- Search by `TraceId` for distributed tracing across microservices (advanced use case)

---

## Next Steps

1. ✅ Specification complete (01-specification.md)
2. ✅ Architecture complete (this document)
3. 📋 Task Breakdown - Break into actionable implementation tasks
4. 💻 Implementation - TDD cycle (tests → code → refactor)
5. 📚 Documentation - Update CLAUDE.md with HttpTraceId pattern
