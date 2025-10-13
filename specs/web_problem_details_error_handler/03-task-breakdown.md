# ProblemDetails Error Handler - Stage 3: Task Breakdown

**Date**: 2026-01-12
**Author**: Claude Code
**Status**: Draft

---

## Task Dependency Graph

```
[Task 1: ProblemDetailsHelpers.cs]
    ↓
[Task 2: IEventCodeResolver Interface] ← [Task 3: Unit Tests for Helpers]
    ↓
[Task 4: EventCodeResolver Implementation] ← [Task 5: Unit Tests for Resolver]
    ↓
[Task 6: DI Registration in HubServiceRunner]
    ↓
[Task 7: Enhance 4xx/5xx Middleware] ← [Task 8: Integration Tests]
    ↓
[Task 9: Update OperationLoggingMiddleware]
    ↓
[Task 10: Refactor ProjectSettingsEndpoints] ← [Task 11: Integration Tests]
    ↓
[Task 12: Refactor LaunchesEndpoints] ← [Task 13: Integration Tests]
    ↓
[Task 14: Refactor TestItemsEndpoints] ← [Task 15: Integration Tests]
    ↓
[Task 16: Refactor AdminProjectsUsersEndpoints] ← [Task 17: Integration Tests]
    ↓
[Task 18: Refactor Remaining 9 Endpoint Files] ← [Task 19: Integration Tests]
    ↓
[Task 20: Update AGENTS.md Documentation]
```

---

## Task List

### Task 1: Create ProblemDetailsHelpers Static Class

**Complexity**: Low
**Estimated Time**: 2 hours
**Files to Create**:
- `hub/Infrastructure/Web/ProblemDetailsHelpers.cs` (create ~200 lines)

**Dependencies**: None

**Implementation Steps**:
1. Create static class `ProblemDetailsHelpers` in namespace `PlaywrightHub.Infrastructure.Web`
2. Implement 7 static methods:
   - `ValidationProblem(Dictionary<string, string[]>, string, string?, string?)`
   - `NotFound(string, string, string?, string?)`
   - `Conflict(string, string, string?, string?)`
   - `Unauthorized(string, string, string?, string?)`
   - `Forbidden(string, string, string?, string?)`
   - `InternalServerError(string, string, string?, string?)`
   - `ServiceUnavailable(string, string, string, string?, string?)`
3. Each method returns `IResult` from `Microsoft.AspNetCore.Http.HttpResults`
4. Use `Results.Problem()` or `Results.ValidationProblem()` internally
5. Add `eventCode` and `traceId` to `ProblemDetails.Extensions` dictionary
6. Add XML documentation for each method with examples

**Key Code Structure**:
```csharp
public static class ProblemDetailsHelpers
{
    public static IResult ValidationProblem(
        Dictionary<string, string[]> errors,
        string eventCode,
        string? instance = null,
        string? traceId = null)
    {
        var problemDetails = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Instance = instance
        };

        problemDetails.Extensions["eventCode"] = eventCode;
        if (!string.IsNullOrEmpty(traceId))
            problemDetails.Extensions["traceId"] = traceId;

        return Results.Problem(
            detail: null,
            statusCode: StatusCodes.Status400BadRequest,
            title: problemDetails.Title,
            type: problemDetails.Type,
            instance: problemDetails.Instance,
            extensions: problemDetails.Extensions);
    }

    // ... other methods follow similar pattern
}
```

**Verification**:
- [x] Class compiles with 0 errors
- [x] All 7 methods implemented
- [x] XML documentation complete
- [x] Returns correct HTTP status codes
- [x] EventCode added to extensions

---

### Task 2: Create IEventCodeResolver Interface

**Complexity**: Low
**Estimated Time**: 0.5 hours
**Files to Create**:
- `hub/Application/Ports/IEventCodeResolver.cs` (create ~40 lines)

**Dependencies**: None

**Implementation Steps**:
1. Create interface `IEventCodeResolver` in namespace `PlaywrightHub.Application.Ports`
2. Define two methods:
   - `string ResolveEventCode(Exception exception, HttpContext context)`
   - `string ResolveEventCodeFromStatus(int statusCode, HttpContext context)`
3. Add XML documentation explaining purpose and usage

**Key Code Structure**:
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
    string ResolveEventCode(Exception exception, HttpContext context);

    /// <summary>
    /// Resolves an HTTP status code to a generic EventCode (fallback).
    /// </summary>
    string ResolveEventCodeFromStatus(int statusCode, HttpContext context);
}
```

**Verification**:
- [x] Interface compiles with 0 errors
- [x] XML documentation complete
- [x] Method signatures match architecture spec

---

### Task 3: Unit Tests for ProblemDetailsHelpers

**Complexity**: Low
**Estimated Time**: 2 hours
**Files to Create**:
- `PlaywrightHub.Tests/Infrastructure/Web/ProblemDetailsHelpersTests.cs` (create ~300 lines)

**Dependencies**: Task 1 complete

**Implementation Steps**:
1. Create test class `ProblemDetailsHelpersTests`
2. Test each helper method (7 tests minimum):
   - `ValidationProblem_WithErrors_ReturnsProblemDetailsWithEventCode()`
   - `NotFound_WithMessage_ReturnsProblemDetailsWithEventCode()`
   - `Conflict_WithMessage_ReturnsProblemDetailsWithEventCode()`
   - `Unauthorized_WithMessage_ReturnsProblemDetailsWithEventCode()`
   - `Forbidden_WithMessage_ReturnsProblemDetailsWithEventCode()`
   - `InternalServerError_WithSafeMessage_ReturnsProblemDetailsWithEventCode()`
   - `ServiceUnavailable_WithDependency_ReturnsProblemDetailsWithEventCode()`
3. Verify:
   - Correct HTTP status codes
   - EventCode in extensions
   - TraceId in extensions (when provided)
   - Field-level errors for ValidationProblem
   - application/problem+json content type

**Verification**:
- [x] All tests pass
- [x] Code coverage >90%
- [x] Tests follow AAA pattern (Arrange, Act, Assert)

---

### Task 4: Create EventCodeResolver Implementation

**Complexity**: Medium
**Estimated Time**: 3 hours
**Files to Create**:
- `hub/Infrastructure/Web/EventCodeResolver.cs` (create ~150 lines)

**Dependencies**: Task 2 complete

**Implementation Steps**:
1. Create class `EventCodeResolver` implementing `IEventCodeResolver`
2. Inject `ILogger<EventCodeResolver>` in constructor
3. Implement `ResolveEventCode(Exception, HttpContext)`:
   - Pattern match on exception type
   - NpgsqlException → `MapDatabaseException(NpgsqlException)`
   - RedisException → `EventCodes.Redis.OperationFailed`
   - TimeoutException → `MapTimeoutException(HttpContext)`
   - InvalidOperationException → `EventCodes.WebServer.RequestFailed`
   - Default → `EventCodes.WebServer.RequestFailed`
4. Implement `ResolveEventCodeFromStatus(int, HttpContext)`:
   - Map status codes to generic event codes
   - 400 → `EventCodes.AdminProjectsUsers.Validation.ValidationFailed`
   - 401 → `EventCodes.AdminProjectsUsers.Authentication.LoginFailed`
   - 500/503 → `EventCodes.WebServer.RequestFailed`
5. Add private helper methods:
   - `MapDatabaseException(NpgsqlException)` - maps PostgreSQL error codes
   - `MapTimeoutException(HttpContext)` - context-aware timeout mapping
6. Add logging for resolved event codes

**Key Code Structure**:
```csharp
public class EventCodeResolver : IEventCodeResolver
{
    private readonly ILogger<EventCodeResolver> _logger;

    public EventCodeResolver(ILogger<EventCodeResolver> logger)
    {
        _logger = logger;
    }

    public string ResolveEventCode(Exception exception, HttpContext context)
    {
        var eventCode = exception switch
        {
            NpgsqlException ex => MapDatabaseException(ex),
            RedisException => EventCodes.Redis.OperationFailed,
            TimeoutException => MapTimeoutException(context),
            InvalidOperationException => EventCodes.WebServer.RequestFailed,
            _ => EventCodes.WebServer.RequestFailed
        };

        _logger.LogDebug("Resolved {ExceptionType} to event code {EventCode}",
            exception.GetType().Name, eventCode);

        return eventCode;
    }

    // ... other methods
}
```

**Verification**:
- [x] Class compiles with 0 errors
- [x] Implements IEventCodeResolver interface
- [x] Logging added for all resolutions
- [x] Context-aware mapping for timeouts

---

### Task 5: Unit Tests for EventCodeResolver

**Complexity**: Medium
**Estimated Time**: 2 hours
**Files to Create**:
- `PlaywrightHub.Tests/Infrastructure/Web/EventCodeResolverTests.cs` (create ~400 lines)

**Dependencies**: Task 4 complete

**Implementation Steps**:
1. Create test class `EventCodeResolverTests`
2. Test exception-to-event-code mappings (10+ tests):
   - `ResolveEventCode_NpgsqlDuplicateKey_ReturnsTransactionFailed()`
   - `ResolveEventCode_NpgsqlForeignKeyViolation_ReturnsTransactionFailed()`
   - `ResolveEventCode_RedisException_ReturnsRedisOperationFailed()`
   - `ResolveEventCode_TimeoutInLaunchEndpoint_ReturnsLaunchOperationFailed()`
   - `ResolveEventCode_TimeoutInTestItemEndpoint_ReturnsTestItemOperationFailed()`
   - `ResolveEventCode_GenericException_ReturnsRequestFailed()`
3. Test status-to-event-code mappings (5+ tests):
   - `ResolveEventCodeFromStatus_400_ReturnsValidationFailed()`
   - `ResolveEventCodeFromStatus_401_ReturnsLoginFailed()`
   - `ResolveEventCodeFromStatus_500_ReturnsRequestFailed()`
4. Mock HttpContext for path-based mapping tests
5. Mock ILogger to verify logging calls

**Verification**:
- [x] All tests pass
- [x] Code coverage >85%
- [x] Tests cover all exception types
- [x] Context-aware mapping tested

---

### Task 6: DI Registration in HubServiceRunner

**Complexity**: Low
**Estimated Time**: 0.5 hours
**Files to Modify**:
- `hub/Services/HubServiceRunner.cs` (add 2 lines around line 300)

**Dependencies**: Task 2, Task 4 complete

**Implementation Steps**:
1. Find DI registration section in `HubServiceRunner.cs` (around line 300)
2. Add registration for IEventCodeResolver:
   ```csharp
   builder.Services.AddSingleton<IEventCodeResolver, EventCodeResolver>();
   ```
3. Add comment explaining purpose

**Verification**:
- [x] Hub project compiles
- [x] IEventCodeResolver can be injected in middleware

---

### Task 7: Enhance 4xx/5xx Normalization Middleware

**Complexity**: Medium
**Estimated Time**: 3 hours
**Files to Modify**:
- `hub/Services/HubServiceRunner.cs` (lines 611-708 - modify)

**Dependencies**: Task 6 complete

**Implementation Steps**:
1. Inject `IEventCodeResolver` into middleware via `app.ApplicationServices.GetRequiredService<>`
2. In catch block (line 641):
   - Use `IEventCodeResolver.ResolveEventCode(ex, context)` to get event code
   - Add event code to `ProblemDetails.Extensions["eventCode"]`
   - Log full exception with event code using Serilog
3. In 4xx/5xx non-exception path (line 619):
   - Use `IEventCodeResolver.ResolveEventCodeFromStatus(status, context)` to get generic event code
   - Add event code to `ProblemDetails.Extensions["eventCode"]`
4. Remove response body buffering (FUTURE: separate task, not blocking)

**Key Code Changes**:
```csharp
app.Use(async (context, next) =>
{
    var eventCodeResolver = context.RequestServices.GetRequiredService<IEventCodeResolver>();
    var logger = context.RequestServices.GetRequiredService<ILogger<HubServiceRunner>>();

    try
    {
        await next();

        // ... existing normalization logic ...

        if (status >= 400 && !isProblem)
        {
            // Get event code from resolver
            var eventCode = eventCodeResolver.ResolveEventCodeFromStatus(status, context);

            // ... create ProblemDetails ...

            pd.Extensions["eventCode"] = eventCode;
            pd.Extensions["traceId"] = context.TraceIdentifier;
        }
    }
    catch (Exception ex)
    {
        var eventCode = eventCodeResolver.ResolveEventCode(ex, context);

        logger.LogError(ex, "Request failed with event code {EventCode}", eventCode);

        var pd = new ProblemDetails
        {
            Status = 500,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred. Please contact support with the trace ID.",
            Type = "https://httpstatuses.com/500",
            Instance = context.Request.Path,
            Extensions =
            {
                ["eventCode"] = eventCode,
                ["traceId"] = context.TraceIdentifier
            }
        };

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(pd);
    }
});
```

**Verification**:
- [x] Hub project compiles
- [x] EventCode added to ProblemDetails extensions
- [x] Exceptions logged with event codes (chunked logging)
- [x] Response buffering mitigated (AutoFlushingBufferStream)
- [x] No breaking changes to existing behavior

---

### Task 8: Integration Tests for Enhanced Middleware

**Complexity**: Medium
**Estimated Time**: 2 hours
**Files to Create**:
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/ErrorHandling/ProblemDetailsMiddlewareTests.cs` (create ~300 lines)

**Dependencies**: Task 7 complete

**Implementation Steps**:
1. Create test class inheriting from `ApiTestBase`
2. Test validation errors (3+ tests):
   - `ValidationError_ReturnsProblemDetailsWithEventCode()`
   - `ManualBadRequest_NormalizedToProblemDetailsWithEventCode()`
   - `ModelStateError_ReturnsProblemDetailsWithFieldErrors()`
3. Test exception handling (3+ tests):
   - `UnhandledException_Returns500WithEventCode()`
   - `DatabaseException_Returns500WithDatabaseEventCode()`
   - `TimeoutException_Returns500WithTimeoutEventCode()`
4. Verify:
   - `application/problem+json` content type
   - EventCode in response body
   - TraceId in response body
   - Safe error messages (no stack traces)

**Verification**:
- [x] All tests pass
- [x] Tests run against real hub API
- [x] EventCode present in all responses

---

### Task 9: Update OperationLoggingMiddleware

**Complexity**: Low
**Estimated Time**: 1 hour
**Files to Modify**:
- `hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs` (lines 32-57 - modify)

**Dependencies**: Task 7 complete

**Implementation Steps**:
1. After `await _next(context)` completes (line 34):
   - Check if response is ProblemDetails (content-type check)
   - If yes, extract `eventCode` from response body (need to buffer or use response feature)
2. In catch block (line 41):
   - Extract `eventCode` from `HttpContext.Features` (if middleware added it)
   - Include `eventCode` in operation outputs
3. Update `op.SetOutputs()` to include event code:
   ```csharp
   op.SetOutputs(new Dictionary<string, object>
   {
       ["statusCode"] = context.Response.StatusCode,
       ["eventCode"] = eventCode ?? "Unknown"
   });
   ```

**Verification**:
- [x] Middleware compiles
- [x] EventCode logged in operation context
- [x] No performance degradation

---

### Task 10: Refactor ProjectSettingsEndpoints (Proof of Concept)

**Complexity**: Low
**Estimated Time**: 2 hours
**Files to Modify**:
- `hub/Infrastructure/Web/ProjectSettingsEndpoints.cs` (lines 144-244 - modify 7 error responses)

**Dependencies**: Task 1, Task 7 complete

**Implementation Steps**:
1. Replace 7 error responses with ProblemDetailsHelpers calls:
   - Line 167: Invalid request body → `ValidationProblem`
   - Line 206-209: Invalid launchInactivityTimeout → `ValidationProblem`
   - Line 219-222: Invalid keepLaunches → `ValidationProblem`
   - Line 232-235: Invalid keepLogs → `ValidationProblem`
   - Line 245-248: Invalid keepAttachments → `ValidationProblem`
   - Line 260-263: Invalid keepAudit → `ValidationProblem`
   - Line 275: Redis error → `ServiceUnavailable`

2. **Example Refactoring** (lines 206-209):
   ```csharp
   // Before
   return Results.BadRequest(new
   {
       error = "Invalid launchInactivityTimeout. Allowed: 1h, 3h, 6h, 12h, 1d, 3d, 7d"
   });

   // After
   return ProblemDetailsHelpers.ValidationProblem(
       new Dictionary<string, string[]>
       {
           ["launchInactivityTimeout"] = ["Invalid value. Allowed: 1h, 3h, 6h, 12h, 1d, 3d, 7d"]
       },
       eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
       instance: context.Request.Path,
       traceId: context.TraceIdentifier);
   ```

3. Map errors to existing event codes:
   - All validation errors → `EventCodes.ProjectSettings.RetentionValueInvalid` (PRJ11)
   - Redis errors → `EventCodes.Redis.OperationFailed` (RDS99)

**Verification**:
- [x] All 7 errors refactored
- [x] Endpoint compiles
- [x] Manual testing: validation errors return field-level errors
- [x] Manual testing: eventCode present in responses

---

### Task 11: Integration Tests for ProjectSettingsEndpoints

**Complexity**: Low
**Estimated Time**: 1.5 hours
**Files to Create**:
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/ProjectSettings/ErrorHandlingTests.cs` (create ~200 lines) ✓
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/ProjectSettings/ProjectSettingsPositiveTests.cs` ✓

**Dependencies**: Task 10 complete

**Implementation Steps**:
1. Create test class inheriting from `ApiTestBase` ✓
2. Create positive tests for the project settings endpoints ✓
3. Test each validation scenario (7 tests): ErrorHandlingTests.cs ✓
   - `UpdateSettings_InvalidTimeout_Returns400WithEventCode()` ✓
   - `UpdateSettings_InvalidKeepLaunches_Returns400WithEventCode()` ✓
   - `UpdateSettings_InvalidKeepLogs_Returns400WithEventCode()` ✓
   - And so on... ✓
3.1 Verify each response: ✓
   - Status code 400 ✓
   - application/problem+json content type ✓
   - EventCode = "PRJ11" ✓
   - Field name in errors dictionary ✓
   - Error message matches expected ✓

**Verification**:
- [x] All tests pass (but before execution, start "bash scripts/run-local-dev-inline.sh  --frontail-enabled --skip-tests")
- [x] Tests verify eventCode presence
- [x] Tests verify field-level errors

---

### Task 12: Refactor LaunchesEndpoints

**Complexity**: High
**Estimated Time**: 8 hours (split across 2 days)
**Files to Modify**:
- `hub/Infrastructure/Web/LaunchesEndpoints.cs` (~100 error responses to refactor)

**Dependencies**: Task 10, Task 11 complete (proof of concept validated)

**Implementation Steps**:
1. Search for all error response patterns: ✓
   - `Results.BadRequest(new { error = ... })` ✓
   - `Results.NotFound(new { error = ... })` ✓
   - `Results.Conflict(new { error = ... })` ✓
   - `Results.Problem(...)` ✓
2. Group errors by type (validation, not found, conflict, server error) ✓
3. Replace with appropriate ProblemDetailsHelpers calls ✓
4. Map to EventCodes: ✓
   - Launch not found → `EventCodes.Launch.LaunchNotFound` (LCH03) ✓
   - Launch creation failed → `EventCodes.Launch.LaunchCreationFailed` (LCH04) ✓
   - Launch already finished → `EventCodes.Launch.LaunchAlreadyFinished` (LCH07) ✓
   - Validation errors → `EventCodes.AdminProjectsUsers.Validation.ValidationFailed` (ADM91) ✓
5. Add field names for validation errors (where applicable) ✓
6. Test each refactored endpoint manually ✓

**Verification**:
- [x] All ~100 errors refactored
- [x] Endpoint compiles with 0 errors & warnings
- [x] Execute smoke tests pass via "bash scripts/run-local-dev-inline.sh  --frontail-enabled"
- [x] EventCodes mapped correctly

---

### Task 13: Integration Tests for LaunchesEndpoints

**Complexity**: High
**Estimated Time**: 6 hours
**Files to Create/Modify**:
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/Launch/ErrorHandlingTests.cs` (create ~500 lines)
- 'Agenix.PlaywrightGrid.Integration.Tests/Tests/Launch/LaunchPositiveTests.cs` ✓

**Dependencies**: Task 12 complete

**Implementation Steps**:
1. Create comprehensive test suite for launch error scenarios
2. Create positive tests for the project settings endpoints ✓
3. Test categories:
   - Validation errors (bad request body, invalid fields)
   - Not found errors (launch doesn't exist)
   - Conflict errors (duplicate launch number, already finished)
   - Server errors (database failures)
4. Verify:
   - Correct HTTP status codes
   - EventCodes match expected values
   - Field-level errors for validations
   - TraceId present in all responses
   - Update the verification section below

**Verification**:
- [x] All tests pass (but before execution of the test, start "bash scripts/run-local-dev-inline.sh --frontail-enabled --skip-tests")
- [x] Coverage for major error scenarios
- [x] EventCodes verified in tests

---

### Task 14: Refactor TestItemsEndpoints

**Complexity**: High
**Estimated Time**: 7 hours
**Files to Modify**:
- `hub/Infrastructure/Web/TestItemsEndpoints.cs` (~80 error responses to refactor) ✓

**Dependencies**: Task 12 complete (pattern established)

**Implementation Steps**:
1. Follow same pattern as LaunchesEndpoints refactoring ✓
2. Map to EventCodes:
   - Test item not found → `EventCodes.TestItem.TestItemNotFound` (ITEM03) ✓
   - Test item creation failed → `EventCodes.TestItem.TestItemCreationFailed` (ITEM04) ✓
   - Invalid item type → `EventCodes.AdminProjectsUsers.Validation.ValidationFailed` (ADM91) ✓
3. Add field names for validation errors ✓

**Verification**:
- [x] All ~80 errors refactored
- [x] Endpoint compiles
- [x] Execute smoke tests pass via "bash scripts/run-local-dev-inline.sh  --frontail-enabled"

---

### Task 15: Integration Tests for TestItemsEndpoints

**Complexity**: High
**Estimated Time**: 5 hours
**Files to Create/Modify**:
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/TestItems/ErrorHandlingTests.cs` (create ~400 lines) ✓
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/TestItems/TestItemsPositiveTests.cs` ✓

**Dependencies**: Task 14 complete

**Implementation Steps**:
1. Create test suites similar to LaunchesEndpoints tests ✓
2. Test all major error scenarios ✓
3. Verify EventCodes ✓

**Verification**:
- [x] All tests pass (but before execution of the test, start "bash scripts/run-local-dev-inline.sh --frontail-enabled --skip-tests")
- [x] EventCodes verified
- [x] Fixed `UpdateTestItemStatusRequest` deserialization bug in `TestItemsEndpoints.cs`

---

### Task 16: Refactor AdminProjectsUsersEndpoints

**Complexity**: High
**Estimated Time**: 6 hours
**Files to Modify**:
- `hub/Infrastructure/Web/AdminProjectsUsersEndpoints.cs` (~60 error responses to refactor) ✓

**Dependencies**: Task 14 complete

**Implementation Steps**:
1. Follow established pattern ✓
2. Map to EventCodes (many already used in this file): ✓
   - Authentication failures → `EventCodes.AdminProjectsUsers.Authentication.*` (ADM11-ADM15) ✓
   - User management errors → `EventCodes.AdminProjectsUsers.UserManagement.*` (ADM21-ADM29) ✓
   - Project management errors → `EventCodes.AdminProjectsUsers.ProjectManagement.*` (ADM31-ADM39) ✓
   - Validation errors → `EventCodes.AdminProjectsUsers.Validation.*` (ADM91-ADM92) ✓
3. Add field names for validation errors ✓

**Verification**:
- [x] All ~60 errors refactored
- [x] Endpoint compiles
- [x] Execute smoke tests pass via "bash scripts/run-local-dev-inline.sh  --frontail-enabled"
- [x] Manual verification of ProblemDetails and EventCodes via curl

---

### Task 17: Integration Tests for AdminProjectsUsersEndpoints

**Complexity**: High
**Estimated Time**: 5 hours
**Files to Create/Modify**:
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/Admin/ErrorHandlingTests.cs` (create ~400 lines)

**Dependencies**: Task 16 complete

**Implementation Steps**:
1. Create test suite for admin endpoints
2. Focus on security-critical errors (authentication, authorization)
3. Verify EventCodes

**Verification**:
- [x] All tests pass
- [x] Security-critical errors tested

---

### Task 18: Refactor Remaining 9 Endpoint Files

**Complexity**: Medium-High
**Estimated Time**: 10 hours (split across 3-4 days)
**Files to Modify**:
- `hub/Infrastructure/Web/ArtifactsEndpoints.cs`
- `hub/Infrastructure/Web/LogItemsEndpoints.cs`
- `hub/Infrastructure/Web/SuitesEndpoints.cs`
- `hub/Infrastructure/Web/LaunchFiltersEndpoints.cs`
- `hub/Infrastructure/Web/PasswordResetEndpoints.cs`
- `hub/Infrastructure/Web/EnhancedLogItemsEndpoints.cs`
- `hub/Infrastructure/Web/ArtifactCacheStatsEndpoints.cs`

**Dependencies**: Task 16 complete (pattern well-established)

**Implementation Steps**:
1. Refactor each file one at a time
2. Follow established pattern
3. Map to appropriate EventCodes

**Verification**:
- [ ] All ~73 remaining errors refactored
- [ ] All endpoints compile
- [ ] Execute smoke tests pass via "bash scripts/run-local-dev-inline.sh  --frontail-enabled"

---

### Task 19: Integration Tests for Remaining Endpoints

**Complexity**: High
**Estimated Time**: 8 hours
**Files to Create/Modify**:
- Create test files for each remaining endpoint category
- Focus on major error scenarios

**Dependencies**: Task 18 complete

**Verification**:
- [ ] Tests cover major error scenarios
- [ ] All tests pass

---

### Task 20: Update AGENTS.md Documentation

**Complexity**: Low
**Estimated Time**: 3 hours
**Files to Modify**:
- `AGENTS.md` (add new "Error Handling Patterns" section)

**Dependencies**: All implementation tasks complete

**Implementation Steps**:
1. Add new section "Error Handling Patterns" to AGENTS.md
2. Document ProblemDetailsHelpers API with examples
3. Document EventCode mapping strategy
4. Document integration testing patterns
5. Add examples of common refactoring scenarios
6. Update "Recent Changes" section with feature summary

**Content to Add**:
```markdown
## Error Handling Patterns (2026-01-12)

### Overview
All Hub API endpoints return RFC 7807 ProblemDetails responses with EventCode integration.

### ProblemDetailsHelpers API

#### Validation Errors (400)
```csharp
return ProblemDetailsHelpers.ValidationProblem(
    new Dictionary<string, string[]>
    {
        ["fieldName"] = ["Error message"]
    },
    eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
    instance: context.Request.Path,
    traceId: context.TraceIdentifier);
```

#### Not Found (404)
```csharp
return ProblemDetailsHelpers.NotFound(
    "Launch not found",
    eventCode: EventCodes.Launch.LaunchNotFound,
    instance: context.Request.Path,
    traceId: context.TraceIdentifier);
```

#### Conflict (409)
```csharp
return ProblemDetailsHelpers.Conflict(
    "Launch already finished",
    eventCode: EventCodes.Launch.LaunchAlreadyFinished,
    instance: context.Request.Path,
    traceId: context.TraceIdentifier);
```

#### Internal Server Error (500)
```csharp
// Log full exception server-side
logger.LogError(ex, "Database operation failed");

// Return safe message to client
return ProblemDetailsHelpers.InternalServerError(
    "A database error occurred. Please contact support.",
    eventCode: EventCodes.Database.OperationFailed,
    instance: context.Request.Path,
    traceId: context.TraceIdentifier);
```

### EventCode Mapping Strategy

| Error Scenario | EventCode | Status |
|----------------|-----------|--------|
| Validation failure | ADM91 | 400 |
| Launch not found | LCH03 | 404 |
| Test item not found | ITEM03 | 404 |
| Launch already finished | LCH07 | 409 |
| Database error | DB04 | 500 |
| Redis error | RDS02 | 503 |
| Generic server error | WSH10 | 500 |

### Testing Patterns

Integration tests verify:
- Correct HTTP status code
- application/problem+json content type
- EventCode present in response
- TraceId present in response
- Field-level errors for validation failures
- Safe error messages (no stack traces)

Example:
```csharp
[Test]
public async Task UpdateSettings_InvalidValue_Returns400WithEventCode()
{
    // Arrange
    var invalidSettings = new { keepLaunches = "invalid" };

    // Act
    var response = await Client.PutAsJsonAsync("/api/project-settings", invalidSettings);

    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.That(problem.Extensions["eventCode"], Is.EqualTo("PRJ02"));
    Assert.That(problem.Extensions.ContainsKey("traceId"), Is.True);
}
```
```

**Verification**:
- [ ] Documentation complete
- [ ] Examples accurate
- [ ] Recent Changes section updated

---

## Execution Strategy

### Phase 1: Foundation (Week 1) - Tasks 1-9

**Goal**: Build infrastructure and prove concept with ProjectSettingsEndpoints

**Tasks**:
- Days 1-2: Tasks 1-6 (Helpers, Resolver, DI registration)
- Days 3-4: Tasks 7-9 (Middleware enhancement)
- Day 5: Task 10-11 (ProjectSettings proof of concept)

**Milestone**: ProjectSettingsEndpoints fully refactored with tests passing

---

### Phase 2: Major Endpoints (Weeks 2-3) - Tasks 12-17

**Goal**: Refactor high-traffic, high-error-count endpoints

**Tasks**:
- Week 2: Tasks 12-13 (LaunchesEndpoints - 100 errors)
- Week 3, Days 1-3: Tasks 14-15 (TestItemsEndpoints - 80 errors)
- Week 3, Days 4-5: Tasks 16-17 (AdminProjectsUsersEndpoints - 60 errors)

**Milestone**: Top 3 endpoint files refactored, ~240 errors converted

---

### Phase 3: Remaining Endpoints (Week 4) - Tasks 18-19

**Goal**: Complete refactoring of all remaining endpoints

**Tasks**:
- Week 4, Days 1-4: Task 18 (9 remaining endpoint files - 73 errors)
- Week 4, Day 5: Task 19 (Integration tests)

**Milestone**: All 320+ errors refactored

---

### Phase 4: Documentation (Week 4, Day 5) - Task 20

**Goal**: Complete documentation for error handling patterns

**Task**:
- Week 4, Day 5 afternoon: Task 20 (AGENTS.md update)

**Milestone**: Documentation complete, feature ready for production

---

## Rollback Plan

### If Issues Arise During Phase 1

**Action**: Delete new files, revert middleware changes
**Impact**: No production impact (changes are additive)

### If Issues Arise During Phase 2-3

**Action**: Revert specific endpoint files
**Mitigation**: Middleware still normalizes responses to ProblemDetails
**Impact**: Gradual rollback per endpoint file

### If Critical Bug Found After Full Rollout

**Action**:
1. Identify problematic endpoint
2. Revert endpoint to previous version
3. Fix bug in ProblemDetailsHelpers or EventCodeResolver
4. Re-deploy fixed version
5. Re-apply refactoring to endpoint

---

## Success Metrics

### Coverage

- **Target**: 100% of 320+ error responses use ProblemDetails
- **Measurement**: Code review + integration tests

### Consistency

- **Target**: 100% of error responses include EventCode
- **Measurement**: Integration tests verify eventCode presence

### Performance

- **Target**: <10ms overhead for error response generation
- **Measurement**: Load testing with intentional errors
- **Baseline**: ~2ms (existing middleware)

---

## Next Steps

1. **Review & Approve Task Breakdown** (this document)
2. **Begin Stage 4**: Implementation (TDD)
   - Start with Task 1 (ProblemDetailsHelpers)
   - Follow Red-Green-Refactor cycle
   - Track progress using Progress Tracking Template
3. **Daily Standups**: Review progress, adjust estimates
4. **Weekly Milestones**: Complete phases on schedule
5. **Stage 5**: Documentation (after all tasks complete)
