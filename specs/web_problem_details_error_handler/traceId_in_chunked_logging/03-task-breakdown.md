# HttpContext TraceIdentifier in Chunked Logging - Task Breakdown

**Date**: 2025-01-15
**Status**: Draft

---

## Task Dependency Graph

```
[Task 1: Unit Tests for OperationLoggingMiddleware]
    ↓
[Task 2: Modify OperationLoggingMiddleware (Hub)]
    ↓
[Task 3: Integration Test (Hub)] ← [Task 4: Verify Logs Locally]
    ↓
[Task 5: Apply to Ingestion Service]
    ↓
[Task 6: Apply to Housekeeping Service]
    ↓
[Task 7: Update CLAUDE.md Documentation]
```

---

## Task List

### Task 1: Unit Tests for OperationLoggingMiddleware Enhancement

**Complexity**: Medium
**Estimated Time**: 1 hour

**Files to Create**:
- `PlaywrightHub.Tests/Infrastructure/Web/Middleware/OperationLoggingMiddlewareTests.cs` (create)

**Dependencies**: None (can start immediately)

**Implementation Steps**:
1. Create test file in `PlaywrightHub.Tests/Infrastructure/Web/Middleware/`
2. Write test: `InvokeAsync_SetsHttpTraceIdInOperationContext()`
   - Mock HttpContext with TraceIdentifier = "test-trace-123"
   - Mock RequestDelegate as next middleware
   - Call `middleware.InvokeAsync(httpContext)`
   - Assert: `OperationContext.Current.Properties["HttpTraceId"]` equals "test-trace-123"
3. Write test: `InvokeAsync_HandlesNullTraceIdentifier()`
   - HttpContext.TraceIdentifier returns null
   - Verify middleware doesn't throw
4. Run tests → Should FAIL (method doesn't set HttpTraceId yet)

**Verification**:
- [ ] Test file created with 2 test methods
- [ ] Tests compile successfully
- [ ] Tests fail as expected (Red phase of TDD)
- [ ] Test names clearly describe expected behavior

---

### Task 2: Modify OperationLoggingMiddleware (Hub)

**Complexity**: Low
**Estimated Time**: 0.25 hours (15 minutes)

**Files to Modify**:
- `hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs` (modify ~line 50)

**Dependencies**: Task 1 complete (tests written)

**Implementation Steps**:
1. Open `OperationLoggingMiddleware.cs`
2. Locate the `using (var opContext = new OperationContext(...))` block
3. Add ONE line after OperationContext creation:
   ```csharp
   opContext.Properties["HttpTraceId"] = httpContext.TraceIdentifier;
   ```
4. Run Task 1 tests → Should PASS (Green phase)
5. Verify no other code changes needed (OperationContextEnricher already reads Properties)

**Code Location**:
```csharp
// hub/Infrastructure/Web/Middleware/OperationLoggingMiddleware.cs (~line 50)
public async Task InvokeAsync(HttpContext httpContext)
{
    var activity = Activity.Current;
    var traceId = activity?.TraceId.ToString();
    var spanId = activity?.SpanId.ToString();

    using (var opContext = new OperationContext(operationId, traceId, spanId))
    {
        // ✅ ADD THIS LINE
        opContext.Properties["HttpTraceId"] = httpContext.TraceIdentifier;

        await _next(httpContext);
    }
}
```

**Verification**:
- [ ] One line added to OperationLoggingMiddleware
- [ ] Build succeeds (0 errors, 0 warnings)
- [ ] Task 1 unit tests now pass
- [ ] No other files modified

---

### Task 3: Integration Test (Hub)

**Complexity**: Medium
**Estimated Time**: 1 hour

**Files to Create**:
- `Agenix.PlaywrightGrid.Integration.Tests/Tests/Middleware/OperationLoggingIntegrationTests.cs` (create)

**Dependencies**: Task 2 complete (middleware modified)

**Implementation Steps**:
1. Create integration test file
2. Write test: `HttpTraceId_IncludedInLogs_WhenEndpointCalled()`
   - Start hub with test server
   - Make HTTP request to any endpoint (e.g., GET /health)
   - Capture Serilog output to in-memory sink
   - Parse log event properties
   - Assert: Log event contains `HttpTraceId` property
   - Assert: `HttpTraceId` value matches request's TraceIdentifier
3. Write test: `HttpTraceId_MatchesProblemDetailsTraceId_OnError()`
   - Make request that triggers 404 error
   - Capture ProblemDetails response traceId
   - Capture log entry for the error
   - Assert: log.HttpTraceId equals response.traceId
4. Run integration tests

**Verification**:
- [ ] Integration test file created
- [ ] 2 integration tests pass
- [ ] Tests verify HttpTraceId appears in logs
- [ ] Tests verify HttpTraceId matches API response traceId

---

### Task 4: Verify Logs Locally

**Complexity**: Low
**Estimated Time**: 0.5 hours

**Files to Modify**: None (manual verification)

**Dependencies**: Task 3 complete (integration tests pass)

**Implementation Steps**:
1. Start hub locally: `dotnet run --project hub`
2. Make HTTP request that triggers error (e.g., POST invalid log item)
   ```bash
   curl -X POST http://localhost:5100/api/log-items \
     -H "Content-Type: application/json" \
     -d '{"testItemUuid":"invalid","text":"test"}'
   ```
3. Check console logs for:
   ```json
   {
     "EventCode": "LOG01",
     "TraceId": "00-otel-...",
     "HttpTraceId": "00-http-..."  // ✅ Should be present
   }
   ```
4. Check ProblemDetails response contains same HttpTraceId:
   ```json
   {
     "type": "https://httpstatuses.com/404",
     "title": "Not Found",
     "traceId": "00-http-..."  // ✅ Should match log
   }
   ```
5. Verify you can grep logs by traceId from API response:
   ```bash
   grep "HttpTraceId.*00-http-..." hub/logs/*.log
   ```

**Verification**:
- [ ] HttpTraceId appears in console logs
- [ ] HttpTraceId matches ProblemDetails traceId
- [ ] Can search logs by traceId from API response
- [ ] Both OpenTelemetry TraceId and HttpTraceId present

---

### Task 5: Apply to Ingestion Service

**Complexity**: Low
**Estimated Time**: 0.5 hours

**Files to Modify**:
- `ingestion/Services/IngestionServiceRunner.cs` (modify middleware setup)

**Dependencies**: Task 4 complete (verified in Hub)

**Implementation Steps**:
1. Check if Ingestion has OperationLoggingMiddleware equivalent
   - Search for `OperationContext` usage in ingestion service
   - If not found, check Program.cs/Startup for context setup
2. If middleware exists:
   - Add same one line: `opContext.Properties["HttpTraceId"] = httpContext.TraceIdentifier;`
3. If no middleware:
   - Create minimal middleware following hub pattern
   - Register in Program.cs: `app.UseMiddleware<OperationLoggingMiddleware>()`
4. Build and test ingestion service
5. Verify logs include HttpTraceId

**Verification**:
- [ ] Ingestion service builds successfully
- [ ] HttpTraceId appears in ingestion service logs
- [ ] No breaking changes to existing functionality

---

### Task 6: Apply to Housekeeping Service

**Complexity**: Low
**Estimated Time**: 0.5 hours

**Files to Modify**:
- `housekeeping-service/Services/HousekeepingServiceRunner.cs` (modify middleware setup)

**Dependencies**: Task 5 complete (Ingestion updated)

**Implementation Steps**:
1. Check if Housekeeping has OperationLoggingMiddleware equivalent
2. Apply same change as Task 5 (one line addition)
3. Build and test housekeeping service
4. Verify logs include HttpTraceId

**Note**: Housekeeping service may have fewer HTTP endpoints (mostly background workers). HttpTraceId will only appear in logs for HTTP requests to health/metrics endpoints.

**Verification**:
- [ ] Housekeeping service builds successfully
- [ ] HttpTraceId appears in health endpoint logs
- [ ] No breaking changes to existing functionality

---

### Task 7: Update CLAUDE.md Documentation

**Complexity**: Low
**Estimated Time**: 0.5 hours

**Files to Modify**:
- `CLAUDE.md` (add to Recent Changes section)

**Dependencies**: Tasks 2-6 complete (all services updated)

**Implementation Steps**:
1. Add new section to "Recent Changes" in CLAUDE.md
2. Document:
   - What was changed (HttpTraceId support)
   - Why (correlation between API responses and logs)
   - How it works (OperationContext.Properties)
   - Example log output (before/after)
   - How to search logs using traceId from API response
3. Add to "Common Patterns" section:
   - Pattern name: "HttpTraceId Correlation"
   - Usage: "Middleware captures HttpContext.TraceIdentifier into OperationContext"
   - Example code snippet
4. Update "Troubleshooting" section:
   - Add tip: "Search logs by traceId from API error response using grep"

**Verification**:
- [ ] CLAUDE.md updated with comprehensive documentation
- [ ] Example log output included (before/after)
- [ ] grep command example provided for support engineers
- [ ] Pattern added to "Common Patterns" section

---

## Execution Strategy

### Phase 1: Hub Service (Tasks 1-4)
**Goal**: Implement and verify in Hub service first
**Timeline**: ~3 hours
**Focus**: Correctness, TDD approach, integration testing

**Milestones**:
- ✅ Unit tests written (Task 1)
- ✅ Implementation complete (Task 2)
- ✅ Integration tests pass (Task 3)
- ✅ Manual verification successful (Task 4)

### Phase 2: Other Services (Tasks 5-6)
**Goal**: Replicate to Ingestion and Housekeeping services
**Timeline**: ~1 hour
**Focus**: Consistency across services

**Milestones**:
- ✅ Ingestion service updated (Task 5)
- ✅ Housekeeping service updated (Task 6)

### Phase 3: Documentation (Task 7)
**Goal**: Document the new pattern for future reference
**Timeline**: ~0.5 hours
**Focus**: Completeness, clarity, examples

**Milestones**:
- ✅ CLAUDE.md updated with pattern and examples

---

## Rollback Plan

### If Issues in Phase 1 (Hub)
**Symptoms**: Logs missing HttpTraceId, tests failing, exceptions thrown
**Action**:
1. Revert Task 2 changes (remove one line from OperationLoggingMiddleware)
2. Commit reverted code
3. Investigate root cause before re-attempting

**Risk**: Very Low (single line change, non-breaking)

### If Issues in Phase 2 (Other Services)
**Symptoms**: Ingestion/Housekeeping logs missing HttpTraceId
**Action**:
1. Check if services have OperationLoggingMiddleware (may not exist yet)
2. If missing, defer to future phase (Hub-only implementation is still valuable)
3. Document limitation: "HttpTraceId only available in Hub service logs"

**Risk**: Low (services may not have HTTP endpoints)

### If Issues in Phase 3 (Documentation)
**Symptoms**: Documentation unclear or incorrect
**Action**:
1. Fix documentation based on feedback
2. Add more examples or clarification
3. No code rollback needed

**Risk**: None (documentation only)

---

## Success Criteria

### Technical Success
- [ ] All 3 services (Hub, Ingestion, Housekeeping) include HttpTraceId in logs
- [ ] Unit tests pass for middleware changes
- [ ] Integration tests pass for HTTP request flow
- [ ] Build succeeds with 0 errors, 0 warnings

### User Success
- [ ] Support engineers can search logs by traceId from API response
- [ ] Average log search time reduced from ~2 minutes to <10 seconds
- [ ] 100% of API error responses have matching log entries

### Documentation Success
- [ ] CLAUDE.md includes comprehensive documentation
- [ ] Example log output provided (before/after)
- [ ] grep command example provided for troubleshooting

---

## Estimated Total Time

| Phase | Tasks | Estimated Time | Actual Time |
|-------|-------|---------------|-------------|
| Phase 1: Hub | 1-4 | 3.0 hours | TBD |
| Phase 2: Services | 5-6 | 1.0 hours | TBD |
| Phase 3: Docs | 7 | 0.5 hours | TBD |
| **Total** | **7** | **4.5 hours** | **TBD** |

---

## Notes for Implementation

1. **TDD Approach**: Write tests BEFORE modifying middleware (Task 1 → Task 2)
2. **One Service at a Time**: Complete Hub first, then replicate to other services
3. **Verify Locally**: Always test manually in addition to automated tests
4. **Small Commits**: Commit after each task completion
5. **Documentation Last**: Wait until implementation is complete before updating CLAUDE.md

---

## Open Questions

1. **Should we add HttpTraceId to startup/shutdown logs?**
   - Recommendation: No, those logs happen outside HTTP request context
   - Reason: HttpContext is null during startup/shutdown

2. **Should we add HttpTraceId to background worker logs?**
   - Recommendation: No, workers don't have HttpContext
   - Reason: Only HTTP requests have TraceIdentifier

3. **Should we add a warning if HttpContext.TraceIdentifier is null?**
   - Recommendation: No, gracefully handle null (store null in Properties)
   - Reason: Some edge cases (tests, background tasks) may not have TraceIdentifier

---

## Next Steps

1. ✅ Specification complete (01-specification.md)
2. ✅ Architecture complete (02-architecture.md)
3. ✅ Task Breakdown complete (this document)
4. 💻 **READY FOR IMPLEMENTATION** - Proceed to Task 1 (Unit Tests)
5. 📚 Documentation (Task 7) after implementation complete
