# ProblemDetails Error Handler - Stage 2: Architecture Planning

**Date**: 2026-01-12
**Author**: Claude Code
**Status**: Draft

---

## Research: Existing Patterns

### Similar Features in Codebase

**1. Existing ProblemDetails Infrastructure**:
- **Location**: `hub/Services/HubServiceRunner.cs` (lines 611-708)
- **Pattern**: 4xx/5xx response normalization middleware
- **Current Behavior**:
  - Buffers response body
  - Detects non-ProblemDetails 4xx/5xx responses
  - Converts to ProblemDetails format
  - Adds `traceId` to extensions
  - Handles validation errors (extracts `errors` dictionary from response body)
- **Issues**:
  - No EventCode integration
  - Buffers entire response (performance concern)
  - Exposes raw error details in `detail` field

**2. Error Type Classification**:
- **Location**: `hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs` (lines 43-52)
- **Pattern**: Maps HTTP status codes to ErrorType enum
- **Current Usage**: Logging only (not in error responses)

**3. EventCodes System**:
- **Location**: `Agenix.PlaywrightGrid.Shared/Logging/EventCodes.cs` (1304 lines)
- **Pattern**: Static class with nested categories
- **Categories**: 15 categories (ADM, LCH, PRJ, DB, RDS, WSH, etc.)
- **Total Codes**: 460+ event codes
- **Key Method**: `GetEventTitle(string eventCode)` - maps codes to human-readable titles

**4. Existing Error Response Patterns**:
- **Location**: 13 endpoint files
- **Count**: 320+ error responses
- **Common Patterns**:
  - `Results.BadRequest(new { error = "message" })` (most common)
  - `Results.NotFound(new { error = "message" })`
  - `Results.Conflict(new { error = "message" })`
  - `Results.Problem(...)` (rare, inconsistent)

### Relevant Patterns from CLAUDE.md

**1. Repository Pattern**: Not directly applicable (no data access)

**2. Early Return Pattern**:
- Use for validation checks
- Guard clauses at top of helper methods

**3. DRY Principle**:
- Extract common ProblemDetails creation to helper methods
- Avoid duplicating error handling logic

**4. Dependency Inversion**:
- Define `IProblemDetailsFactory` interface
- Inject into endpoints (if needed)

---

## Approach 1: Enhance Existing Middleware + Helper Library

### Description

Enhance the existing 4xx/5xx normalization middleware to integrate EventCodes, and provide a helper library for endpoints to create standardized ProblemDetails responses.

### Implementation

**Components**:

1. **ProblemDetailsHelpers** (NEW):
   - Static helper class in `hub/Infrastructure/Web/ProblemDetailsHelpers.cs`
   - Methods for common error scenarios with EventCode integration
   - Methods:
     - `ValidationProblem(Dictionary<string, string[]> errors, string eventCode)`
     - `NotFound(string message, string eventCode)`
     - `Conflict(string message, string eventCode)`
     - `Unauthorized(string message, string eventCode)`
     - `InternalServerError(string safeMessage, string eventCode)`
     - `DependencyError(string dependency, string safeMessage, string eventCode)`

2. **Enhanced Middleware** (MODIFY):
   - Update `HubServiceRunner.cs` (lines 611-708)
   - Add EventCode extraction from response
   - Map exception types to event codes
   - Remove response body buffering (use streaming)

3. **EventCode Mapper** (NEW):
   - Static class `EventCodeMapper` in `hub/Infrastructure/Web/EventCodeMapper.cs`
   - Maps exception types to event codes
   - Maps HTTP status codes to event codes (fallback)

**Data Flow**:
```
Endpoint returns ProblemDetailsHelpers.ValidationProblem(...)
  ↓
ProblemDetails with eventCode created
  ↓
Response sent to client (application/problem+json)
  ↓
OperationLoggingMiddleware logs eventCode
```

**For Unhandled Exceptions**:
```
Exception thrown in endpoint
  ↓
4xx/5xx Normalization Middleware catches exception
  ↓
EventCodeMapper maps exception type to event code
  ↓
ProblemDetails created with safe message + event code
  ↓
Response sent to client
  ↓
Full exception logged server-side with event code
```

### Pros

- ✅ **Simple**: Minimal changes to existing architecture
- ✅ **Backward Compatible**: Existing middleware handles non-ProblemDetails responses
- ✅ **Incremental Rollout**: Can refactor endpoints one at a time
- ✅ **Low Risk**: Helper library is stateless, easy to test
- ✅ **No DI Changes**: Static helpers, no service registration needed

### Cons

- ❌ **Response Buffering**: Existing middleware buffers entire response (performance concern)
- ❌ **Middleware Complexity**: Middleware does two things (normalize + add event codes)
- ❌ **Limited Extensibility**: Static helpers harder to mock in tests

### Complexity

**Low-Medium**: Primarily refactoring existing code

---

## Approach 2: Global Exception Handler + ProblemDetailsFactory Service

### Description

Replace existing middleware with ASP.NET Core's built-in exception handling middleware, and create a ProblemDetailsFactory service for generating standardized error responses.

### Implementation

**Components**:

1. **IProblemDetailsFactory** (NEW):
   - Interface in `hub/Application/Ports/IProblemDetailsFactory.cs`
   - Methods:
     - `CreateValidationProblem(HttpContext, Dictionary<string, string[]>, string eventCode)`
     - `CreateNotFoundProblem(HttpContext, string message, string eventCode)`
     - `CreateExceptionProblem(HttpContext, Exception, string eventCode)`

2. **ProblemDetailsFactory** (NEW):
   - Implementation in `hub/Infrastructure/Web/ProblemDetailsFactory.cs`
   - Implements IProblemDetailsFactory
   - Registered as singleton in DI container

3. **Global Exception Handler** (NEW):
   - Custom exception handler in `hub/Infrastructure/Web/GlobalExceptionHandler.cs`
   - Implements `IExceptionHandler` interface (ASP.NET Core 8)
   - Uses ProblemDetailsFactory to generate responses
   - Maps exceptions to event codes

4. **Endpoint Helpers** (NEW):
   - Extension methods on `IResult`
   - `Results.ValidationProblem(errors, eventCode, problemDetailsFactory)`
   - Injects factory via method parameter

**Data Flow**:
```
Endpoint returns Results.ValidationProblem(errors, eventCode, factory)
  ↓
Factory creates ProblemDetails with eventCode
  ↓
Response sent to client
```

**For Unhandled Exceptions**:
```
Exception thrown in endpoint
  ↓
Global Exception Handler catches exception
  ↓
Factory maps exception to event code
  ↓
Factory creates ProblemDetails with safe message
  ↓
Response sent to client
  ↓
Full exception logged server-side
```

### Pros

- ✅ **Built-in Framework Support**: Uses ASP.NET Core 8 IExceptionHandler
- ✅ **Testable**: Factory is injectable, easy to mock
- ✅ **Extensible**: Can add new problem types by extending factory
- ✅ **Separation of Concerns**: Exception handling separate from response normalization
- ✅ **No Response Buffering**: Framework handles streaming

### Cons

- ❌ **Higher Complexity**: More interfaces, DI registration, more files
- ❌ **Breaking Change**: Must remove existing middleware (risk of regression)
- ❌ **Requires DI**: Endpoints must inject IProblemDetailsFactory
- ❌ **More Boilerplate**: Every endpoint must pass factory to Results helpers

### Complexity

**High**: Significant architectural change

---

## Approach 3: Hybrid - Middleware + Static Helpers + Exception Mapping

### Description

Keep existing middleware for backward compatibility, add static helper library for endpoints, and create exception-to-event-code mapping service.

### Implementation

**Components**:

1. **ProblemDetailsHelpers** (NEW):
   - Static helper class (same as Approach 1)
   - No DI dependencies

2. **IEventCodeResolver** (NEW):
   - Interface in `hub/Application/Ports/IEventCodeResolver.cs`
   - Single method: `string ResolveEventCode(Exception exception, HttpContext context)`

3. **EventCodeResolver** (NEW):
   - Implementation in `hub/Infrastructure/Web/EventCodeResolver.cs`
   - Maps exception types to event codes
   - Provides context-aware mapping (e.g., NpgsqlException in /api/launches → LCH05)

4. **Enhanced Middleware** (MODIFY):
   - Update existing middleware to use IEventCodeResolver
   - Remove response buffering
   - Stream ProblemDetails responses

**Data Flow**:
```
Endpoint returns ProblemDetailsHelpers.ValidationProblem(errors, eventCode)
  ↓
ProblemDetails with eventCode created
  ↓
Middleware passes through (already ProblemDetails)
  ↓
Response sent to client
```

**For Unhandled Exceptions**:
```
Exception thrown in endpoint
  ↓
Middleware catches exception
  ↓
EventCodeResolver maps exception to event code
  ↓
Middleware creates ProblemDetails with safe message + event code
  ↓
Response sent to client
  ↓
Full exception logged server-side
```

### Pros

- ✅ **Best of Both Worlds**: Static helpers + DI for extensibility
- ✅ **Incremental Migration**: Keep existing middleware, add helpers
- ✅ **Testable Exception Mapping**: IEventCodeResolver is mockable
- ✅ **Context-Aware**: Can map same exception type to different event codes based on endpoint
- ✅ **Low Risk**: Changes are additive, not destructive

### Cons

- ❌ **More Components**: Three new components (helpers, interface, resolver)
- ❌ **Mixed Patterns**: Static helpers + DI service (inconsistent)
- ❌ **Response Buffering**: Still present in existing middleware (must be fixed separately)

### Complexity

**Medium**: Moderate number of new components, but well-separated concerns

---

## Recommendation: Approach 3 (Hybrid)

### Justification

**Alignment with Project Requirements**:
1. **Incremental Migration**: Can refactor 320+ error responses gradually without breaking changes
2. **Low Risk**: Additive changes, existing middleware remains as fallback
3. **Testability**: Exception mapping is mockable via DI
4. **Context-Awareness**: Same exception can map to different event codes based on endpoint

**Alignment with DDD Principles** (from CLAUDE.md):
- **Layer Boundaries**:
  - Helpers in Interface layer (Web/ProblemDetailsHelpers.cs)
  - IEventCodeResolver in Use Case layer (Application/Ports/)
  - EventCodeResolver in Infrastructure layer (Infrastructure/Web/)
- **Dependency Inversion**: Middleware depends on IEventCodeResolver interface, not concrete implementation

**Alignment with SOLID Principles**:
- **SRP**: ProblemDetailsHelpers (create responses) vs EventCodeResolver (map exceptions)
- **OCP**: New exception mappings added without modifying existing code
- **DIP**: Middleware depends on abstraction (IEventCodeResolver)

**Performance Considerations**:
- Static helpers have zero DI overhead
- EventCodeResolver called only for unhandled exceptions (rare)
- Must fix response buffering separately (not blocking)

### Risks and Mitigations

**Risk 1: Response Buffering Performance**
- **Mitigation**: Address in separate task (not blocking this feature)
- **Workaround**: Middleware only activates for non-ProblemDetails responses (most endpoints will use helpers)

**Risk 2: Mixed Patterns (Static + DI)**
- **Mitigation**: Document pattern clearly in AGENTS.md
- **Justification**: Static helpers for 95% case (endpoints), DI for 5% case (exception mapping)

---

## Contracts

### Database Schema Changes

**None required** - No database changes needed.

---

### DTOs

#### ProblemDetailsResponse (RFC 7807)

```csharp
// Standard ProblemDetails from ASP.NET Core
public class ProblemDetails
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }
    public IDictionary<string, object?> Extensions { get; set; }
}

// Specialized for validation errors
public class HttpValidationProblemDetails : ProblemDetails
{
    public IDictionary<string, string[]> Errors { get; set; }
}
```

#### EventCode Extension

```csharp
// Added to ProblemDetails.Extensions
{
  "eventCode": "PRJ02",
  "traceId": "00-abc123-def456-01"
}
```

---

### API Endpoints

**No new endpoints** - This is infrastructure for existing endpoints.

**Modified Behavior**:
- All error responses return `application/problem+json` content type
- All error responses include `eventCode` in extensions
- Validation errors include `errors` dictionary

---

### Interfaces

#### IEventCodeResolver

```csharp
namespace PlaywrightHub.Application.Ports;

/// <summary>
/// Resolves exception and error scenarios to EventCodes for consistent error identification.
/// </summary>
public interface IEventCodeResolver
{
    /// <summary>
    /// Resolves an exception to an appropriate EventCode based on exception type and HTTP context.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="context">The HTTP context (for endpoint-specific mapping).</param>
    /// <returns>Event code (e.g., "DB04", "LCH05", "WSH10").</returns>
    string ResolveEventCode(Exception exception, HttpContext context);

    /// <summary>
    /// Resolves an HTTP status code to a generic EventCode (fallback when no exception available).
    /// </summary>
    /// <param name="statusCode">HTTP status code (400, 404, 500, etc.).</param>
    /// <param name="context">The HTTP context.</param>
    /// <returns>Generic event code (e.g., "ADM91" for 400, "WSH10" for 500).</returns>
    string ResolveEventCodeFromStatus(int statusCode, HttpContext context);
}
```

---

### ProblemDetailsHelpers API

```csharp
namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
/// Static helper methods for creating standardized RFC 7807 ProblemDetails responses
/// with EventCode integration for consistent error identification.
/// </summary>
public static class ProblemDetailsHelpers
{
    /// <summary>
    /// Creates a validation problem (400 Bad Request) with field-level errors and event code.
    /// </summary>
    /// <param name="errors">Dictionary of field names to error messages.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "ADM91", "PRJ02").</param>
    /// <param name="instance">Request path (optional, defaults to null).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 400 Bad Request with ProblemDetails.</returns>
    public static IResult ValidationProblem(
        Dictionary<string, string[]> errors,
        string eventCode,
        string? instance = null,
        string? traceId = null);

    /// <summary>
    /// Creates a not found problem (404 Not Found) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "LCH03").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 404 Not Found with ProblemDetails.</returns>
    public static IResult NotFound(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null);

    /// <summary>
    /// Creates a conflict problem (409 Conflict) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "LCH06").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 409 Conflict with ProblemDetails.</returns>
    public static IResult Conflict(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null);

    /// <summary>
    /// Creates an unauthorized problem (401 Unauthorized) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "ADM11").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 401 Unauthorized with ProblemDetails.</returns>
    public static IResult Unauthorized(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null);

    /// <summary>
    /// Creates a forbidden problem (403 Forbidden) with event code.
    /// </summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "ADM12").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 403 Forbidden with ProblemDetails.</returns>
    public static IResult Forbidden(
        string message,
        string eventCode,
        string? instance = null,
        string? traceId = null);

    /// <summary>
    /// Creates an internal server error problem (500) with safe message and event code.
    /// IMPORTANT: Full exception details must be logged server-side separately.
    /// </summary>
    /// <param name="safeMessage">Safe, generic error message for client.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "DB04", "WSH10").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 500 Internal Server Error with ProblemDetails.</returns>
    public static IResult InternalServerError(
        string safeMessage,
        string eventCode,
        string? instance = null,
        string? traceId = null);

    /// <summary>
    /// Creates a service unavailable problem (503) with safe message and event code.
    /// Used for dependency failures (database down, Redis unavailable, etc.).
    /// </summary>
    /// <param name="dependency">Dependency name (e.g., "Database", "Redis", "Worker").</param>
    /// <param name="safeMessage">Safe, generic error message for client.</param>
    /// <param name="eventCode">Event code from EventCodes system (e.g., "DB01", "RDS01").</param>
    /// <param name="instance">Request path (optional).</param>
    /// <param name="traceId">Trace identifier (optional).</param>
    /// <returns>IResult representing a 503 Service Unavailable with ProblemDetails.</returns>
    public static IResult ServiceUnavailable(
        string dependency,
        string safeMessage,
        string eventCode,
        string? instance = null,
        string? traceId = null);
}
```

---

### EventCodeResolver Implementation

```csharp
namespace PlaywrightHub.Infrastructure.Web;

public class EventCodeResolver : IEventCodeResolver
{
    private readonly ILogger<EventCodeResolver> _logger;

    public EventCodeResolver(ILogger<EventCodeResolver> logger)
    {
        _logger = logger;
    }

    public string ResolveEventCode(Exception exception, HttpContext context)
    {
        // Map exception type to event code
        var eventCode = exception switch
        {
            NpgsqlException => MapDatabaseException((NpgsqlException)exception),
            RedisException => EventCodes.Redis.OperationFailed,
            TimeoutException => MapTimeoutException(context),
            InvalidOperationException => EventCodes.WebServer.RequestFailed,
            _ => EventCodes.WebServer.RequestFailed // Generic fallback
        };

        _logger.LogDebug("Resolved exception {ExceptionType} to event code {EventCode}",
            exception.GetType().Name, eventCode);

        return eventCode;
    }

    public string ResolveEventCodeFromStatus(int statusCode, HttpContext context)
    {
        // Map HTTP status code to generic event code
        return statusCode switch
        {
            400 => EventCodes.AdminProjectsUsers.Validation.ValidationFailed, // ADM91
            401 => EventCodes.AdminProjectsUsers.Authentication.LoginFailed, // ADM11
            404 => EventCodes.WebServer.RequestFailed, // WSH10 (generic, endpoint should use specific code)
            409 => EventCodes.WebServer.RequestFailed, // WSH10 (generic)
            500 => EventCodes.WebServer.RequestFailed, // WSH10
            503 => EventCodes.WebServer.RequestFailed, // WSH10
            _ => EventCodes.WebServer.RequestFailed
        };
    }

    private string MapDatabaseException(NpgsqlException ex)
    {
        // Map PostgreSQL error codes to event codes
        return ex.SqlState switch
        {
            "23505" => EventCodes.Database.TransactionFailed, // Duplicate key
            "23503" => EventCodes.Database.TransactionFailed, // Foreign key violation
            "40001" => EventCodes.Database.TransactionFailed, // Serialization failure
            _ => EventCodes.Database.OperationFailed
        };
    }

    private string MapTimeoutException(HttpContext context)
    {
        // Context-aware timeout mapping
        var path = context.Request.Path.Value ?? "";

        if (path.StartsWith("/api/launches"))
            return EventCodes.Launch.LaunchOperationFailed;
        if (path.StartsWith("/api/test-items"))
            return EventCodes.TestItem.TestItemOperationFailed;

        return EventCodes.WebServer.RequestFailed; // Generic fallback
    }
}
```

---

## Dependencies

### External Libraries

- **ASP.NET Core 8**: Built-in ProblemDetails support
- **Microsoft.AspNetCore.Http.HttpResults**: Results.Problem() helper
- **System.Text.Json**: JSON serialization (already in use)

### Internal Dependencies

- **EventCodes.cs**: 460+ event codes (already exists)
- **ErrorTypes.cs**: Error classification enum (already exists)
- **Existing Middleware**: 4xx/5xx normalization (will be enhanced)
- **OperationLoggingMiddleware**: Will be updated to log event codes

### Service Registration

```csharp
// In HubServiceRunner.cs ConfigureServices
builder.Services.AddSingleton<IEventCodeResolver, EventCodeResolver>();
```

---

## Migration Strategy

### Phase 1: Infrastructure Setup (Week 1)

1. Create ProblemDetailsHelpers.cs
2. Create IEventCodeResolver interface
3. Create EventCodeResolver implementation
4. Register EventCodeResolver in DI
5. Unit tests for all components

### Phase 2: Middleware Enhancement (Week 1)

1. Update 4xx/5xx middleware to use IEventCodeResolver
2. Add eventCode to ProblemDetails extensions
3. Integration tests for middleware

### Phase 3: Endpoint Refactoring (Weeks 2-3)

**Prioritized Order** (based on error count and criticality):

1. **ProjectSettingsEndpoints.cs** (7 errors) - Proof of concept
2. **LaunchesEndpoints.cs** (~100 errors) - High traffic
3. **TestItemsEndpoints.cs** (~80 errors) - High traffic
4. **AdminProjectsUsersEndpoints.cs** (~60 errors) - Security-critical
5. Remaining 9 files (~73 errors)

**Refactoring Pattern**:
```csharp
// Before
return Results.BadRequest(new { error = "Invalid value" });

// After
return ProblemDetailsHelpers.ValidationProblem(
    new Dictionary<string, string[]>
    {
        ["fieldName"] = ["Invalid value. Allowed: ..."]
    },
    eventCode: EventCodes.ProjectSettings.RetentionValueInvalid);
```

### Phase 4: Documentation (Week 4)

1. Update AGENTS.md with error handling patterns
2. Document EventCode mappings
3. API documentation updates
4. Migration guide for future endpoints

---

## Rollback Plan

### If Issues Arise

**After Phase 1 (Infrastructure)**:
- Delete new files (ProblemDetailsHelpers.cs, EventCodeResolver.cs)
- Remove DI registration
- No impact on production

**After Phase 2 (Middleware)**:
- Revert middleware changes
- Existing middleware still works as fallback
- Minimal production impact

**After Phase 3 (Endpoint Refactoring)**:
- Revert specific endpoint files
- Middleware converts responses back to ProblemDetails
- Gradual rollback per endpoint file

---

## Next Steps

1. **Review & Approve Architecture** (this document)
2. **Proceed to Stage 3**: Task Breakdown
   - Break down implementation into specific tasks
   - Define dependencies between tasks
   - Estimate complexity and timeline
3. **Stage 4**: Implementation (TDD)
4. **Stage 5**: Documentation
