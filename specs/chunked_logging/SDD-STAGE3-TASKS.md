# SDD Stage 3: Task Breakdown - Chunked Logging Feature

**Feature**: Operation-Based Chunked Logging for Hub Service
**Previous Stage**: [SDD-STAGE2-ARCHITECTURE.md](./SDD-STAGE2-ARCHITECTURE.md)
**Next Stage**: [SDD-STAGE4-IMPLEMENTATION.md](./SDD-STAGE4-IMPLEMENTATION.md) (to be created)

---

## Task Dependency Graph

```
Foundation Phase (Tasks 1-4)
├── [Task 1: Middleware Infrastructure] ← No dependencies
│       ↓
├── [Task 2: Middleware Registration] ← Depends on Task 1
│       ↓
├── [Task 3: Serilog Configuration] ← Depends on Task 1
│       ↓
└── [Task 4: Environment Variables] ← Depends on Task 3

Endpoints Phase (Tasks 5-6)
├── [Task 5: TestItemsEndpoints Integration] ← Depends on Task 2, Task 3
│       ↓
└── [Task 6: LaunchesEndpoints Integration] ← Depends on Task 2, Task 3

Services Phase (Tasks 7-8)
├── [Task 7: BrowserPoolService Integration] ← Depends on Task 2, Task 3
│       ↓
└── [Task 8: BrowserAutoStopService Integration] ← Depends on Task 2, Task 3

Verification Phase (Tasks 9-11)
├── [Task 9: Unit Tests] ← Depends on Tasks 1-8
│       ↓
├── [Task 10: Integration Tests] ← Depends on Task 9
│       ↓
└── [Task 11: Manual Smoke Tests] ← Depends on Task 10

Documentation Phase (Task 12)
└── [Task 12: CLAUDE.md Update] ← Depends on Tasks 1-11
```

---

## Task List

### Task 1: Create OperationLoggingMiddleware

**Complexity**: Medium
**Estimated Time**: 1.0 hour
**Layer**: Interface Layer (HTTP boundary)

#### Description
Create ASP.NET Core middleware that automatically wraps every HTTP request in an OperationContext. This provides automatic correlation IDs and operation boundaries for all incoming requests.

#### Files to Create/Modify
- **CREATE**: `hub/Infrastructure/Web/OperationLoggingMiddleware.cs` (~130 lines)
  - Middleware class with InvokeAsync method
  - Extension method UseOperationLogging()
  - Configuration-based enable/disable flag

#### Dependencies
- None (foundation task)

#### Implementation Steps
1. Create new file `hub/Infrastructure/Web/OperationLoggingMiddleware.cs`
2. Add copyright header and namespace
3. Implement middleware class with:
   - Constructor: RequestDelegate, ILogger, IConfiguration
   - InvokeAsync method:
     - Check AGENIX_LOGGING_CHUNKED_ENABLED flag
     - Create ChunkedLogger instance
     - Extract inputs: method, path, queryString, userAgent, userId, projectKey
     - Call BeginOperation() with inputs
     - Call _next(context) within using scope
     - Set outputs: statusCode
     - Handle exceptions with error classification:
       - 400 → ErrorType.Validation
       - 404 → ErrorType.NotFound
       - 409 → ErrorType.Conflict
       - 401/403 → ErrorType.Unauthorized
       - Other → ErrorType.Unexpected
4. Implement extension method UseOperationLogging()

#### Verification Checklist
- [ ] File created with ~130 lines
- [ ] Middleware implements proper using/dispose pattern
- [ ] Error classification covers all HTTP status codes
- [ ] Configuration flag checked before wrapping
- [ ] Inputs extracted from HttpContext
- [ ] Outputs set before scope disposal
- [ ] Extension method added for clean registration
- [ ] Build succeeds with 0 errors
- [ ] No warnings related to new code

---

### Task 2: Register Middleware in Hub Pipeline

**Complexity**: Low
**Estimated Time**: 0.25 hour
**Layer**: Infrastructure Layer (service configuration)

#### Description
Register OperationLoggingMiddleware in the ASP.NET Core pipeline, positioned after authentication but before endpoint routing.

#### Files to Create/Modify
- **MODIFY**: `hub/Services/HubServiceRunner.cs` (~1 line addition)
  - Add `app.UseOperationLogging();` after line ~200

#### Dependencies
- Task 1 (OperationLoggingMiddleware must exist)

#### Implementation Steps
1. Open `hub/Services/HubServiceRunner.cs`
2. Locate middleware registration section (after UseAuthentication, before endpoint mapping)
3. Add comment: `// Add operation logging middleware (Phase 2 - Chunked Logging)`
4. Add line: `app.UseOperationLogging();`

#### Verification Checklist
- [ ] Middleware registered in correct pipeline position
- [ ] Comment added explaining the addition
- [ ] Build succeeds
- [ ] Hub starts without errors
- [ ] Middleware executes for test HTTP requests

---

### Task 3: Configure Serilog with ChunkedConsoleSink

**Complexity**: Medium
**Estimated Time**: 0.5 hour
**Layer**: Infrastructure Layer (logging configuration)

#### Description
Update Serilog configuration in appsettings.json to use ChunkedConsoleSink with enrichers. Add separate log sinks for background services vs HTTP requests for readability.

#### Files to Create/Modify
- **MODIFY**: `hub/appsettings.json` (replace Serilog section)
  - Add "Agenix.PlaywrightGrid.Shared" to Using array
  - Configure ChunkedConsole sink with maxEventsPerChunk, maxAgeSeconds
  - Configure File sinks with separate paths for background vs HTTP logs
  - Add enrichers: WithOperationContext, WithEventCode, WithCodeContext
- **CREATE**: `hub/appsettings.Development.json` (~10 lines)
  - Override MinimumLevel to Debug

#### Dependencies
- Task 1 (needs OperationContext to enrich)

#### Implementation Steps
1. Open `hub/appsettings.json`
2. Replace entire Serilog section with new configuration:
   - Using array: add "Agenix.PlaywrightGrid.Shared"
   - MinimumLevel: Info for Default, Warning for Microsoft/ASP.NET
   - WriteTo array with 3 logger configurations:
     - Logger 1: ChunkedConsole for non-background logs
     - Logger 2: File for non-background logs
     - Logger 3: File for background logs only
   - Enrich array: add WithOperationContext, WithEventCode, WithCodeContext
3. Create `hub/appsettings.Development.json` with Debug level override

#### Verification Checklist
- [ ] Serilog configuration valid JSON
- [ ] ChunkedConsoleSink registered with correct parameters
- [ ] File sinks configured with rolling intervals
- [ ] Background services filtered to separate log file
- [ ] Enrichers registered in correct order
- [ ] Development config overrides log level
- [ ] Build succeeds
- [ ] Hub starts with new logging configuration
- [ ] Console output shows chunked format

---

### Task 4: Add Environment Variables

**Complexity**: Low
**Estimated Time**: 0.25 hour
**Layer**: Configuration

#### Description
Add environment variables for chunked logging configuration to .env file and document them in environment-variables.md.

#### Files to Create/Modify
- **MODIFY**: `.env` (~6 lines addition)
  - Add AGENIX_LOGGING_CHUNKED_ENABLED
  - Add AGENIX_LOGGING_CHUNK_MAX_EVENTS
  - Add AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS
  - Add AGENIX_LOGGING_EVENT_CODE_PREFIX
  - Add AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION
- **MODIFY**: `docs/environment-variables.md` (~15 lines addition)
  - Add "Chunked Logging (All Services)" section
  - Document all 5 variables with descriptions, defaults, examples

#### Dependencies
- Task 3 (Serilog configuration references these variables)

#### Implementation Steps
1. Open `.env` file
2. Add new section at end with comment header: `# Chunked Logging Configuration (Phase 2)`
3. Add 5 variables with default values
4. Open `docs/environment-variables.md`
5. Add new section "Chunked Logging (All Services)" in alphabetical order
6. Create markdown table with columns: Variable, Description, Default, Example
7. Document all 5 variables with clear descriptions

#### Verification Checklist
- [ ] All 5 variables added to .env with defaults
- [ ] Variables follow AGENIX_* naming convention
- [ ] Documentation table complete with all columns
- [ ] Default values match .env file
- [ ] Examples show boolean/numeric variations
- [ ] Build succeeds
- [ ] Variables accessible in Hub configuration

---

### Task 5: Integrate ChunkedLogger into TestItemsEndpoints

**Complexity**: High
**Estimated Time**: 1.5 hours
**Layer**: Interface Layer (API endpoints)

#### Description
Update StartTestItem and FinishTestItem endpoint methods to use ChunkedLogger for milestone tracking and error classification. Ensure browser borrow/return operations emit event codes.

#### Files to Create/Modify
- **MODIFY**: `hub/Infrastructure/Web/TestItemsEndpoints.cs` (~50 lines modification)
  - StartTestItem method:
    - Create ChunkedLogger instance
    - Add milestone: EventCodes.TestItem.ItemCreated
    - Add milestone: EventCodes.BrowserPool.BorrowRequested (if Test/Scenario)
    - Add warning: EventCodes.BrowserPool.BorrowFailed (if no capacity)
    - Add milestone: EventCodes.BrowserPool.BrowserReady (on success)
    - Add milestone: EventCodes.TestItem.ItemPersisted
    - Add error handling with TimeoutException → ErrorType.Timeout + DependencyName.Worker
    - Add error handling with NpgsqlException → ErrorType.Timeout + DependencyName.Database
  - FinishTestItem method:
    - Add milestone: EventCodes.TestItem.ItemFinished
    - Add milestone: EventCodes.BrowserPool.ReturnRequested (if browser borrowed)

#### Dependencies
- Task 2 (middleware must be registered)
- Task 3 (Serilog must be configured)

#### Implementation Steps
1. Open `hub/Infrastructure/Web/TestItemsEndpoints.cs`
2. Locate StartTestItem method signature
3. Add ChunkedLogger instance creation at method start
4. Replace existing log calls with chunkedLogger.LogMilestone() calls:
   - Map log messages to event codes (ITEM01, POOL01, POOL03, ITEM02)
5. Update error handling:
   - Wrap TimeoutException with Fail(ex, ErrorType.Timeout, DependencyName.Worker)
   - Wrap NpgsqlException with Fail(ex, ErrorType.Timeout, DependencyName.Database)
6. Locate FinishTestItem method
7. Add milestone logs for item finish and browser return
8. Test with curl requests

#### Verification Checklist
- [ ] ChunkedLogger instantiated at method start
- [ ] All critical operations have milestone logs
- [ ] Event codes used (ITEM01, POOL01, POOL03, ITEM02)
- [ ] Error classification with ErrorType + DependencyName
- [ ] Browser borrow/return operations tracked
- [ ] No capacity warning uses correct event code
- [ ] Build succeeds
- [ ] Manual curl test shows chunked output with event codes

---

### Task 6: Integrate ChunkedLogger into LaunchesEndpoints

**Complexity**: Medium
**Estimated Time**: 1.0 hour
**Layer**: Interface Layer (API endpoints)

#### Description
Update CreateLaunch and FinishLaunch endpoint methods to use ChunkedLogger for milestone tracking. Track launch lifecycle events.

#### Files to Create/Modify
- **MODIFY**: `hub/Infrastructure/Web/LaunchesEndpoints.cs` (~40 lines modification)
  - CreateLaunch method:
    - Create ChunkedLogger instance
    - Add milestone: EventCodes.Launch.LaunchCreated
    - Add milestone: EventCodes.Launch.LaunchStarted
  - FinishLaunch method:
    - Add milestone: EventCodes.Launch.StatusCalculated
    - Add milestone: EventCodes.Launch.AggregationsUpdated
    - Add milestone: EventCodes.Launch.LaunchFinished

#### Dependencies
- Task 2 (middleware must be registered)
- Task 3 (Serilog must be configured)

#### Implementation Steps
1. Open `hub/Infrastructure/Web/LaunchesEndpoints.cs`
2. Locate CreateLaunch method
3. Add ChunkedLogger instance creation
4. Replace existing logs with milestone logs (LCH01, LCH02)
5. Locate FinishLaunch method
6. Add milestone logs for status calculation, aggregation, and finish (LCH10, LCH11, LCH03)
7. Test with curl requests

#### Verification Checklist
- [ ] ChunkedLogger instantiated in both methods
- [ ] Launch lifecycle events tracked (create, start, finish)
- [ ] Event codes used (LCH01, LCH02, LCH03, LCH10, LCH11)
- [ ] Build succeeds
- [ ] Manual curl test shows chunked output

---

### Task 7: Integrate ChunkedLogger into BrowserPoolService

**Complexity**: High
**Estimated Time**: 1.5 hours
**Layer**: Infrastructure Layer (business service)

#### Description
Update BorrowAsync and ReturnAsync methods in BrowserPoolService to use ChunkedLogger. Create nested operations that link to parent HTTP request operations.

#### Files to Create/Modify
- **MODIFY**: `hub/Infrastructure/Services/BrowserPoolService.cs` (~60 lines modification)
  - BorrowAsync method:
    - Create ChunkedLogger instance
    - Get parent operation ID from OperationContext.Current
    - Begin nested operation with parentOperationId
    - Add milestone: EventCodes.BrowserPool.BorrowRequested
    - Add debug log: EventCodes.BrowserPool.BrowserAllocated
    - Add warning: EventCodes.BrowserPool.BorrowFailed (if no capacity)
    - Add milestone: EventCodes.BrowserPool.BrowserReady
    - Set outputs: browserId, workerNode
    - Handle RedisException → Fail(ex, ErrorType.DependencyFailure, DependencyName.Redis)
  - ReturnAsync method:
    - Begin nested operation
    - Add milestone: EventCodes.BrowserPool.ReturnRequested
    - Add milestone: EventCodes.BrowserPool.BrowserReturned

#### Dependencies
- Task 2 (middleware must be registered)
- Task 3 (Serilog must be configured)

#### Implementation Steps
1. Open `hub/Infrastructure/Services/BrowserPoolService.cs`
2. Add using statement: `using Agenix.PlaywrightGrid.Shared.Logging;`
3. Locate BorrowAsync method
4. Add ChunkedLogger instance at method start
5. Get parent operation ID: `var parentOpId = OperationContext.Current?.OperationId;`
6. Wrap method body with: `using var op = chunkedLogger.BeginOperation("BorrowBrowser", inputs, parentOperationId: parentOpId);`
7. Replace existing logs with milestone logs (POOL01, POOL02, POOL03, POOL04)
8. Add error handling for RedisException
9. Set outputs dictionary with browserId and workerNode
10. Repeat steps 3-8 for ReturnAsync method (POOL11, POOL12)

#### Verification Checklist
- [ ] ChunkedLogger instantiated in both methods
- [ ] Nested operations created with parent operation ID
- [ ] Event codes used (POOL01, POOL02, POOL03, POOL11, POOL12)
- [ ] Outputs set with browser metadata
- [ ] Redis error handling with proper classification
- [ ] Build succeeds
- [ ] Nested operation IDs visible in logs

---

### Task 8: Integrate ChunkedLogger into BrowserAutoStopService

**Complexity**: Medium
**Estimated Time**: 1.0 hour
**Layer**: Infrastructure Layer (background service)

#### Description
Update BrowserAutoStopService ExecuteAsync method to create discrete operations for each tick. Track scan operations, auto-stop events, and browser releases.

#### Files to Create/Modify
- **MODIFY**: `hub/Infrastructure/Adapters/Background/BrowserAutoStopService.cs` (~50 lines modification)
  - ExecuteAsync method:
    - Create ChunkedLogger instance (before while loop)
    - Wrap each tick iteration with BeginOperation("BrowserAutoStop:Tick")
    - Add milestone: EventCodes.BrowserPool.ScanStarted
    - Add milestone: EventCodes.BrowserPool.ItemAutoStopped (per stopped item)
    - Add milestone: EventCodes.BrowserPool.BrowserReleased (per released browser)
    - Set outputs: scanned, processed, released, duration
    - Handle exceptions with Fail(ex, ErrorType.Unexpected)

#### Dependencies
- Task 2 (middleware must be registered)
- Task 3 (Serilog must be configured)

#### Implementation Steps
1. Open `hub/Infrastructure/Adapters/Background/BrowserAutoStopService.cs`
2. Locate ExecuteAsync method
3. Add ChunkedLogger instance before while loop
4. Inside while loop, wrap tick logic with BeginOperation()
5. Replace existing logs with milestone logs (POOL20, POOL21, POOL22)
6. Add outputs dictionary with counters (scanned, processed, released, duration)
7. Wrap tick body in try-catch with Fail() on exceptions

#### Verification Checklist
- [ ] ChunkedLogger instantiated once before loop
- [ ] Each tick creates discrete operation
- [ ] Event codes used (POOL20, POOL21, POOL22)
- [ ] Outputs include counters and duration
- [ ] Exception handling with Fail()
- [ ] Build succeeds
- [ ] Manual test shows tick operations in logs

---

### Task 9: Write Unit Tests for OperationContext

**Complexity**: Low
**Estimated Time**: 0.5 hour
**Layer**: Test Layer

#### Description
Create unit tests for OperationContext to verify AsyncLocal propagation, key event tracking, and parent-child relationships.

#### Files to Create/Modify
- **CREATE**: `hub.Tests/Infrastructure/ChunkedLoggingTests.cs` (~80 lines)
  - Test: OperationContext_FlowsAcrossAsyncBoundaries
  - Test: ChunkedLogger_RecordsKeyEvents
  - Test: OperationContext_PropagatesInNestedOperations

#### Dependencies
- Tasks 1-8 (implementation must be complete)

#### Implementation Steps
1. Create new file `hub.Tests/Infrastructure/ChunkedLoggingTests.cs`
2. Add NUnit [TestFixture] attribute
3. Write test 1: Verify OperationContext.Current flows across async/await
4. Write test 2: Verify KeyEvents list populated by LogMilestone()
5. Write test 3: Verify ParentOperationId set correctly in nested operations
6. Run tests with `dotnet test`

#### Verification Checklist
- [ ] File created with 3 tests
- [ ] All tests pass
- [ ] Tests cover key OperationContext behaviors
- [ ] No test warnings or errors
- [ ] Tests run in <1 second

---

### Task 10: Write Integration Tests for Middleware

**Complexity**: Medium
**Estimated Time**: 1.0 hour
**Layer**: Test Layer

#### Description
Create integration tests that verify OperationLoggingMiddleware creates OperationContext for HTTP requests and captures inputs/outputs correctly.

#### Files to Create/Modify
- **CREATE**: `Agenix.PlaywrightGrid.Integration.Tests/Tests/Logging/MiddlewareTests.cs` (~120 lines)
  - Test: Middleware_CreatesOperationContext_ForHttpRequests
  - Test: Middleware_CapturesInputs_FromHttpContext
  - Test: Middleware_CapturesOutputs_AfterRequest
  - Test: Middleware_ClassifiesErrors_ByStatusCode

#### Dependencies
- Task 9 (unit tests must pass)

#### Implementation Steps
1. Create new file in integration tests project
2. Inherit from ApiTestBase (for HttpClient)
3. Write test 1: Send HTTP request, verify OperationContext created
4. Write test 2: Verify inputs (method, path, userId) captured
5. Write test 3: Verify outputs (statusCode) captured
6. Write test 4: Trigger 404/400 errors, verify ErrorType classification
7. Run tests with `dotnet test --filter "FullyQualifiedName~MiddlewareTests"`

#### Verification Checklist
- [ ] File created with 4 tests
- [ ] All tests pass
- [ ] Tests verify middleware behavior end-to-end
- [ ] Tests use ApiTestBase for test user/auth
- [ ] Tests run in <5 seconds

---

### Task 11: Perform Manual Smoke Tests

**Complexity**: Low
**Estimated Time**: 0.5 hour
**Layer**: Manual QA

#### Description
Execute manual smoke tests to verify chunked logging works end-to-end with real HTTP requests and background service operations.

#### Test Scenarios
1. **HTTP Request Chunking**:
   - Start Hub with AGENIX_LOGGING_CHUNKED_ENABLED=true
   - Send POST /api/test-items request
   - Verify console output shows operation chunk with boundaries
   - Verify event codes appear (ITEM01, POOL01, POOL03, ITEM02)
   - Verify OperationId present in all logs
   - Verify KeyEvents summary in footer

2. **Background Service Chunking**:
   - Wait for BrowserAutoStopService tick (5 minutes)
   - Verify console output shows discrete tick operation
   - Verify event codes (POOL20, POOL21, POOL22)
   - Verify outputs (scanned, processed, released, duration)

3. **Error Classification**:
   - Send GET request for non-existent test item (404)
   - Verify ErrorType=NotFound in chunk footer
   - Send POST request with invalid data (400)
   - Verify ErrorType=Validation in chunk footer

4. **Nested Operations**:
   - Send POST /api/test-items request
   - Verify BorrowBrowser operation nested under HTTP operation
   - Verify ParentOperationId visible in logs

5. **Performance**:
   - Measure request latency before/after chunked logging
   - Verify overhead < 5ms per request
   - Verify no memory leaks after 100 requests

#### Dependencies
- Task 10 (integration tests must pass)

#### Verification Checklist
- [ ] HTTP request chunking works correctly
- [ ] Background service chunking works correctly
- [ ] Error classification correct for 404/400 errors
- [ ] Nested operations show parent-child relationship
- [ ] Performance overhead < 5ms per operation
- [ ] No memory leaks or exceptions after sustained load
- [ ] All 5 test scenarios pass

---

### Task 12: Update CLAUDE.md with Feature Documentation

**Complexity**: Low
**Estimated Time**: 0.5 hour
**Layer**: Documentation

#### Description
Add Phase 2 implementation to the "Recent Changes" section of CLAUDE.md, documenting the Hub service integration of chunked logging.

#### Files to Create/Modify
- **MODIFY**: `CLAUDE.md` (~200 lines addition to "Recent Changes" section)
  - Add new heading: "### Phase 2: Hub Service Integration (2025-12-26)"
  - Document overview, goals, and approach
  - List files created (OperationLoggingMiddleware.cs, ChunkedLoggingTests.cs, MiddlewareTests.cs)
  - List files modified (HubServiceRunner.cs, TestItemsEndpoints.cs, LaunchesEndpoints.cs, BrowserPoolService.cs, BrowserAutoStopService.cs, appsettings.json, .env, docs/environment-variables.md)
  - Document technical highlights (middleware pattern, nested operations, event codes)
  - Document build verification results
  - Document benefits achieved (HTTP correlation, endpoint instrumentation, background tracking)
  - Document testing recommendations
  - Add references to Phase 1 and future Phase 3+

#### Dependencies
- Task 11 (smoke tests must pass)

#### Implementation Steps
1. Open `CLAUDE.md`
2. Locate "Recent Changes" section near top of file
3. Add new Phase 2 entry with date
4. Document implementation details using template from existing entries
5. Include code examples for middleware and endpoint usage
6. Add verification checklist results
7. Link to specs/chunked_logging folder for detailed specs

#### Verification Checklist
- [ ] Entry added to "Recent Changes" section
- [ ] All files created/modified listed
- [ ] Technical highlights documented
- [ ] Code examples included
- [ ] Benefits and testing documented
- [ ] Links to related documentation
- [ ] Markdown formatting correct
- [ ] No broken internal links

---

## Execution Strategy

### Phase 1: Foundation (Tasks 1-4)
**Duration**: ~2 hours
**Goal**: Build middleware infrastructure and configuration

**Tasks**:
1. Task 1: Create OperationLoggingMiddleware (1.0h)
2. Task 2: Register middleware (0.25h)
3. Task 3: Configure Serilog (0.5h)
4. Task 4: Add environment variables (0.25h)

**Milestone**: Hub starts with chunked logging enabled, HTTP requests create OperationContext automatically.

**Verification**: Start Hub, send test request, verify chunked console output with operation boundaries.

---

### Phase 2: Endpoints (Tasks 5-6)
**Duration**: ~2.5 hours
**Goal**: Integrate ChunkedLogger into critical API endpoints

**Tasks**:
1. Task 5: TestItemsEndpoints integration (1.5h)
2. Task 6: LaunchesEndpoints integration (1.0h)

**Milestone**: Test item and launch operations emit milestone logs with event codes.

**Verification**: Create test item via API, verify event codes (ITEM01, POOL01, POOL03, ITEM02) in logs.

---

### Phase 3: Services (Tasks 7-8)
**Duration**: ~2.5 hours
**Goal**: Add chunked logging to business services and background workers

**Tasks**:
1. Task 7: BrowserPoolService integration (1.5h)
2. Task 8: BrowserAutoStopService integration (1.0h)

**Milestone**: Browser operations and background ticks logged as discrete operations.

**Verification**: Borrow/return browser, verify nested operations. Wait for tick, verify discrete tick operation.

---

### Phase 4: Verification (Tasks 9-11)
**Duration**: ~2 hours
**Goal**: Comprehensive testing across unit, integration, and manual levels

**Tasks**:
1. Task 9: Unit tests (0.5h)
2. Task 10: Integration tests (1.0h)
3. Task 11: Manual smoke tests (0.5h)

**Milestone**: All tests pass, manual verification confirms feature works end-to-end.

**Verification**: Run full test suite, execute smoke test scenarios, verify all success criteria met.

---

### Phase 5: Documentation (Task 12)
**Duration**: ~0.5 hour
**Goal**: Document completed feature in CLAUDE.md

**Tasks**:
1. Task 12: Update CLAUDE.md (0.5h)

**Milestone**: Feature fully documented with examples and verification results.

**Verification**: Review CLAUDE.md entry for completeness and accuracy.

---

## Total Effort Estimate

| Phase | Duration | Tasks | Critical Path |
|-------|----------|-------|---------------|
| Phase 1: Foundation | 2.0 hours | 1-4 | Yes |
| Phase 2: Endpoints | 2.5 hours | 5-6 | Yes |
| Phase 3: Services | 2.5 hours | 7-8 | Yes |
| Phase 4: Verification | 2.0 hours | 9-11 | Yes |
| Phase 5: Documentation | 0.5 hours | 12 | Yes |
| **Total** | **9.5 hours** | **12 tasks** | **All tasks** |

**Risk Buffer**: +1.5 hours (15% contingency)
**Total with Buffer**: **11 hours** (~1.5 working days)

---

## Rollback Plan

### After Phase 1 (Foundation)
**Issue**: Middleware causes errors or performance issues

**Rollback**:
1. Comment out `app.UseOperationLogging();` in HubServiceRunner.cs
2. Set `AGENIX_LOGGING_CHUNKED_ENABLED=false` in .env
3. Restart Hub

**Impact**: Minimal - middleware can be disabled without code changes

---

### After Phase 2 (Endpoints)
**Issue**: Endpoint integration causes errors

**Rollback**:
1. Set `AGENIX_LOGGING_CHUNKED_ENABLED=false` (disables middleware)
2. Endpoints revert to standard ILogger calls
3. Restart Hub

**Impact**: Low - feature flag disables new code paths

---

### After Phase 3 (Services)
**Issue**: Service integration causes errors or memory leaks

**Rollback**:
1. Set `AGENIX_LOGGING_CHUNKED_ENABLED=false`
2. Revert Serilog configuration: `git checkout hub/appsettings.json`
3. Restart Hub

**Impact**: Medium - requires Serilog config revert, but no data loss

---

### After Phase 4 (Verification)
**Issue**: Tests fail, blocking deployment

**Rollback**:
1. Fix failing tests or temporarily disable them
2. If unfixable, revert all code changes via git
3. Re-run test suite to verify stability

**Impact**: High - may require full feature revert if tests unrecoverable

---

### Complete Rollback (Nuclear Option)
**Issue**: Feature causes production issues

**Steps**:
1. `git revert <commit-range>` to remove all Phase 2 changes
2. Restore original appsettings.json
3. Remove environment variables from .env
4. Remove documentation from CLAUDE.md
5. Rebuild and redeploy

**Impact**: Feature completely removed, system reverts to Phase 1 state

---

## Success Criteria

Before considering Phase 2 complete, verify:

### Functional Requirements
- [ ] HTTP requests logged as visual chunks with boundaries (`╔═` / `╚═`)
- [ ] Event codes appear in all milestone logs (e.g., `[ITEM01]`, `[POOL03]`)
- [ ] OperationId present in all log events within operation
- [ ] Background service ticks logged as discrete operations
- [ ] Error classification shows ErrorType + Dependency (e.g., `ErrorType=Timeout Dependency=Database`)
- [ ] KeyEvents summary appears in chunk footer (e.g., `KeyEvents=[ITEM01,POOL01,POOL03,ITEM02]`)
- [ ] Duration calculated and formatted correctly (e.g., `Duration=1.82s`)

### Performance Requirements
- [ ] Middleware overhead < 2ms per request (p95)
- [ ] ChunkedLogger overhead < 1ms per milestone (p95)
- [ ] Total operation overhead < 5ms per operation (p95)
- [ ] No memory leaks after 1000 operations
- [ ] Chunk buffer memory < 10MB under normal load

### Testing Requirements
- [ ] All unit tests pass (3/3 in ChunkedLoggingTests.cs)
- [ ] All integration tests pass (4/4 in MiddlewareTests.cs)
- [ ] All manual smoke tests pass (5/5 scenarios)
- [ ] No regressions in existing test suite

### Documentation Requirements
- [ ] CLAUDE.md updated with Phase 2 entry
- [ ] All files created/modified listed
- [ ] Code examples provided
- [ ] Verification results documented

---

## Risk Assessment

### Risk 1: Performance Overhead Exceeds Budget
**Probability**: Medium
**Impact**: High (users experience latency)
**Mitigation**:
- Benchmark middleware overhead before deployment
- Use feature flag to disable if needed
- Optimize chunk buffer implementation if necessary

### Risk 2: Memory Leaks from Unclosed Operations
**Probability**: Low
**Impact**: High (service OOM crash)
**Mitigation**:
- Enforce using/dispose pattern in all code
- Add integration tests for memory usage
- Monitor memory metrics in production

### Risk 3: Breaking Changes to Existing Logs
**Probability**: Low
**Impact**: Medium (log parsing scripts break)
**Mitigation**:
- Use separate log sinks for background vs HTTP
- Keep File sink format unchanged
- Document ChunkedConsoleSink format differences

### Risk 4: Incorrect Event Code Usage
**Probability**: Medium
**Impact**: Low (confusing logs, not functional issue)
**Mitigation**:
- Document event code catalog in specs
- Code review to verify correct event codes
- Add unit tests for event code presence

---

## Open Questions

1. **Should middleware be enabled by default?**
   - Recommendation: Yes, with feature flag for easy disable
   - Rationale: Provides immediate value with minimal risk

2. **Should we add event codes to ALL log statements?**
   - Recommendation: Only milestone logs, not all logs
   - Rationale: Keeps event code catalog focused and meaningful

3. **Should we track performance metrics (overhead)?**
   - Recommendation: Yes, add Prometheus metrics for overhead
   - Rationale: Provides visibility into performance impact
   - Implementation: Defer to Phase 4 (not blocking)

---

**Next Stage**: [SDD-STAGE4-IMPLEMENTATION.md](./SDD-STAGE4-IMPLEMENTATION.md) - TDD implementation examples showing Red-Green-Refactor cycle for key tasks.

**Estimated Total Effort**: 11 hours (~1.5 working days)
**Risk Level**: Low (feature-flagged, non-breaking)
**Dependencies**: ✅ Phase 1 Complete (Core Infrastructure)
