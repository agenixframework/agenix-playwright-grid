# SDD Stage 3: Task Breakdown - Chunked Logging Feature

**Date**: 2025-01-01
**Feature**: Chunked Logging with Automatic Flush
**Approach**: Middleware + ChunkedLogger (Recommended)
**Status**: Ready for Implementation

---

## Task Dependency Graph

```
[Task 1: ChunkedLogger Class]
    ↓
[Task 2: Log Chunk Middleware] ← [Task 3: Unit Tests - ChunkedLogger]
    ↓
[Task 4: DI Registration & Configuration] ← [Task 5: Unit Tests - Middleware]
    ↓
[Task 6: Integration Tests]
    ↓
[Task 7: Update CLAUDE.md]
```

---

## Task List

### Task 1: Implement ChunkedLogger Class

- **Complexity**: Medium
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `Agenix.PlaywrightGrid.Client/Infrastructure/ChunkedLogger.cs` (150-200 lines)
- **Dependencies**: None
- **Implementation Steps**:
  1. Create `ChunkedLogger` class implementing `IChunkedLogger` interface
  2. Add private fields:
     - `List<CreateLogItemRequest> _buffer`
     - `SemaphoreSlim _lock` for thread safety
     - `ILogItemResource _logItemResource`
     - `int _maxChunkSize`
  3. Implement `AddLogAsync()` method:
     - Acquire lock
     - Add log to buffer
     - Check if buffer size >= maxChunkSize
     - If yes, call `FlushAsync()`
     - Release lock
  4. Implement `FlushAsync()` method:
     - Acquire lock
     - If buffer empty, return immediately
     - Copy buffer to local variable
     - Clear buffer
     - Release lock
     - Call `_logItemResource.CreateBulkAsync(logs)`
     - Log success/failure
  5. Implement `IAsyncDisposable.DisposeAsync()`:
     - Call `FlushAsync()` to ensure no logs lost
     - Dispose semaphore
  6. Add XML documentation for all public methods
- **Verification**:
  - [ ] Class implements `IChunkedLogger` interface
  - [ ] Thread-safe buffer operations (SemaphoreSlim used correctly)
  - [ ] Buffer cleared after flush
  - [ ] Logs sent to API in bulk
  - [ ] DisposeAsync flushes remaining logs
  - [ ] Build succeeds with 0 errors

---

### Task 2: Implement Log Chunk Middleware

- **Complexity**: Medium
- **Estimated Time**: 1.5 hours
- **Files to Create**:
  - `Agenix.PlaywrightGrid.Client/Infrastructure/LogChunkMiddleware.cs` (100-120 lines)
- **Dependencies**: Task 1 must be complete
- **Implementation Steps**:
  1. Create `LogChunkMiddleware` class
  2. Add constructor accepting:
     - `RequestDelegate next`
     - `IChunkedLogger chunkedLogger`
     - `ILogger<LogChunkMiddleware>` (optional for diagnostics)
  3. Implement `InvokeAsync(HttpContext context)` method:
     - Call `await next(context)` (process request)
     - After request completes, call `await chunkedLogger.FlushAsync()`
     - Wrap in try-catch to prevent middleware failures from breaking requests
  4. Add XML documentation
- **Verification**:
  - [ ] Middleware executes after request completes
  - [ ] Calls `FlushAsync()` on every request completion
  - [ ] Exceptions in flush don't break HTTP responses
  - [ ] Build succeeds with 0 errors

---

### Task 3: Unit Tests for ChunkedLogger

- **Complexity**: Medium
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `Agenix.PlaywrightGrid.Client.Tests/Infrastructure/ChunkedLoggerTests.cs` (200-250 lines)
- **Dependencies**: Task 1 must be complete
- **Implementation Steps**:
  1. Create test fixture with mock `ILogItemResource`
  2. Test: `AddLogAsync_BelowChunkSize_DoesNotFlush`
     - Add 4 logs (chunk size = 5)
     - Verify `CreateBulkAsync` NOT called
  3. Test: `AddLogAsync_ReachesChunkSize_AutoFlushes`
     - Add 5 logs (chunk size = 5)
     - Verify `CreateBulkAsync` called once with 5 logs
  4. Test: `FlushAsync_EmptyBuffer_DoesNotCallApi`
     - Call `FlushAsync()` without adding logs
     - Verify `CreateBulkAsync` NOT called
  5. Test: `FlushAsync_WithLogs_SendsAllAndClearsBuffer`
     - Add 3 logs
     - Call `FlushAsync()`
     - Verify `CreateBulkAsync` called with 3 logs
     - Add 2 more logs, verify buffer has 2 logs
  6. Test: `DisposeAsync_WithRemainingLogs_FlushesBeforeDispose`
     - Add 2 logs
     - Call `DisposeAsync()`
     - Verify `CreateBulkAsync` called with 2 logs
  7. Test: `AddLogAsync_ConcurrentCalls_ThreadSafe`
     - Create 10 tasks adding logs concurrently
     - Verify all logs received (no race condition)
  8. Test: `FlushAsync_ApiFailure_LogsError`
     - Mock `CreateBulkAsync` to throw exception
     - Call `FlushAsync()`
     - Verify exception logged but not rethrown
- **Verification**:
  - [ ] All 8 tests pass
  - [ ] Code coverage >90% for ChunkedLogger
  - [ ] Thread safety verified with concurrent test
  - [ ] Build succeeds with 0 errors

---

### Task 4: DI Registration & Configuration

- **Complexity**: Low
- **Estimated Time**: 1 hour
- **Files to Modify**:
  - `Agenix.PlaywrightGrid.Client/Service.cs` (lines ~50-80, add DI registration)
  - `Agenix.PlaywrightGrid.Client/ClientServiceExtensions.cs` (create if doesn't exist, ~50 lines)
- **Dependencies**: Tasks 1 and 2 must be complete
- **Implementation Steps**:
  1. Add `IChunkedLogger` interface definition:
     ```csharp
     public interface IChunkedLogger : IAsyncDisposable
     {
         Task AddLogAsync(CreateLogItemRequest log);
         Task FlushAsync();
     }
     ```
  2. Create `ClientServiceExtensions.cs` with extension method:
     ```csharp
     public static class ClientServiceExtensions
     {
         public static IServiceCollection AddChunkedLogging(
             this IServiceCollection services,
             int maxChunkSize = 10)
         {
             services.AddSingleton<IChunkedLogger>(sp =>
                 new ChunkedLogger(
                     sp.GetRequiredService<ILogItemResource>(),
                     maxChunkSize
                 ));
             return services;
         }
     }
     ```
  3. Update `Service.cs` to register middleware:
     - Add middleware registration in constructor or startup
     - Make chunk size configurable via environment variable or parameter
  4. Add XML documentation for configuration options
- **Verification**:
  - [ ] `IChunkedLogger` registered as singleton
  - [ ] Middleware registered in pipeline
  - [ ] Chunk size configurable
  - [ ] Build succeeds with 0 errors
  - [ ] No NuGet package warnings

---

### Task 5: Unit Tests for Middleware

- **Complexity**: Low
- **Estimated Time**: 1 hour
- **Files to Create**:
  - `Agenix.PlaywrightGrid.Client.Tests/Infrastructure/LogChunkMiddlewareTests.cs` (100-120 lines)
- **Dependencies**: Task 2 must be complete
- **Implementation Steps**:
  1. Create test fixture with mock middleware pipeline
  2. Test: `InvokeAsync_AfterRequestCompletes_CallsFlush`
     - Simulate HTTP request
     - Verify `FlushAsync()` called after `next(context)`
  3. Test: `InvokeAsync_FlushFails_DoesNotBreakRequest`
     - Mock `FlushAsync()` to throw exception
     - Verify request completes successfully (200 OK)
     - Verify exception logged
  4. Test: `InvokeAsync_ConcurrentRequests_AllFlush`
     - Simulate 5 concurrent requests
     - Verify `FlushAsync()` called 5 times
- **Verification**:
  - [ ] All 4 tests pass
  - [ ] Code coverage >85% for middleware
  - [ ] Build succeeds with 0 errors

---

### Task 6: Integration Tests

- **Complexity**: High
- **Estimated Time**: 3 hours
- **Files to Create**:
  - `Agenix.PlaywrightGrid.Integration.Tests/Tests/Logging/ChunkedLoggingIntegrationTests.cs` (300-350 lines)
- **Dependencies**: Tasks 1-5 must be complete
- **Implementation Steps**:
  1. Create test fixture inheriting from `ApiTestBase`
  2. Test: `ChunkedLogger_BulkLogs_SavedToDatabase`
     - Create test item
     - Add 10 logs via `ChunkedLogger`
     - Call `FlushAsync()`
     - Query database for logs
     - Verify 10 logs exist with correct content
  3. Test: `ChunkedLogger_AutoFlush_TriggersAtChunkSize`
     - Set chunk size = 5
     - Add 5 logs (should auto-flush)
     - Query database immediately
     - Verify 5 logs exist
     - Add 3 more logs (no flush yet)
     - Query database, verify still only 5 logs
     - Manual flush, verify 8 logs total
  4. Test: `Middleware_EndOfRequest_FlushesLogs`
     - Configure middleware in test pipeline
     - Make HTTP request that adds 3 logs
     - After request completes, query database
     - Verify 3 logs exist (flushed by middleware)
  5. Test: `ChunkedLogger_LargeVolume_HandlesCorrectly`
     - Add 100 logs rapidly
     - Verify all 100 logs saved (no data loss)
     - Verify logs batched correctly (10 per chunk)
  6. Test: `ChunkedLogger_DisposeWithoutFlush_SavesRemainingLogs`
     - Add 3 logs
     - Dispose ChunkedLogger without manual flush
     - Query database, verify 3 logs exist
  7. Test: `ChunkedLogger_ConcurrentAccess_ThreadSafe`
     - Create 10 tasks adding logs concurrently
     - Verify all logs saved (no race condition)
- **Verification**:
  - [ ] All 7 integration tests pass
  - [ ] Tests run against real database (PostgreSQL)
  - [ ] No data loss under any scenario
  - [ ] Build succeeds with 0 errors

---

### Task 7: Update CLAUDE.md Documentation

- **Complexity**: Low
- **Estimated Time**: 1 hour
- **Files to Modify**:
  - `CLAUDE.md` (add new section under "Recent Changes")
- **Dependencies**: Tasks 1-6 must be complete
- **Implementation Steps**:
  1. Add new section: "Chunked Logging Feature (2025-01-01)"
  2. Document:
     - Overview of feature (1-2 paragraphs)
     - Problem it solves (N+1 API calls)
     - Solution approach (Middleware + ChunkedLogger)
     - Key components (ChunkedLogger, LogChunkMiddleware)
     - Configuration options (chunk size)
     - Benefits achieved (50-90% reduction in API calls)
  3. Add code examples:
     - Usage example with ChunkedLogger
     - DI registration example
     - Configuration example
  4. Add technical highlights:
     - Thread-safe buffer with SemaphoreSlim
     - Auto-flush on chunk size
     - Middleware-based flush on request completion
     - IAsyncDisposable for cleanup
  5. Add testing notes:
     - Unit test coverage
     - Integration test coverage
     - Performance benchmarks (if available)
  6. Add migration notes:
     - How existing code is affected
     - Backward compatibility
     - Opt-in vs opt-out behavior
- **Verification**:
  - [ ] Documentation complete and clear
  - [ ] Code examples compile and run
  - [ ] Links to relevant files correct
  - [ ] Markdown formatting valid
  - [ ] Build succeeds with 0 errors

---

## Execution Strategy

### Phase 1: Core Implementation (Tasks 1-2)
**Duration**: 3.5 hours
**Goal**: Build ChunkedLogger and Middleware foundation
**Focus**: Correctness, thread safety, buffer management

**Deliverables**:
- ✅ ChunkedLogger with thread-safe buffer
- ✅ Middleware that flushes after requests
- ✅ Basic error handling
- ✅ XML documentation

**Success Criteria**:
- Build succeeds with 0 errors
- Code compiles and basic manual testing works

---

### Phase 2: Unit Testing (Tasks 3-5)
**Duration**: 4 hours
**Goal**: Comprehensive unit test coverage
**Focus**: Edge cases, thread safety, error handling

**Deliverables**:
- ✅ 8 tests for ChunkedLogger
- ✅ 4 tests for Middleware
- ✅ >85% code coverage
- ✅ Thread safety verification

**Success Criteria**:
- All unit tests pass
- No race conditions detected
- Error paths tested

---

### Phase 3: Integration & Configuration (Tasks 4-6)
**Duration**: 5 hours
**Goal**: End-to-end functionality with real database
**Focus**: Data integrity, performance, configuration

**Deliverables**:
- ✅ DI registration and configuration
- ✅ 7 integration tests against PostgreSQL
- ✅ Performance benchmarks
- ✅ No data loss under any scenario

**Success Criteria**:
- Integration tests pass against real database
- Chunk size configurable
- 50-90% reduction in API calls measured
- No data loss in concurrent scenarios

---

### Phase 4: Documentation (Task 7)
**Duration**: 1 hour
**Goal**: Comprehensive feature documentation
**Focus**: Completeness, clarity, examples

**Deliverables**:
- ✅ CLAUDE.md updated with feature section
- ✅ Code examples provided
- ✅ Configuration options documented
- ✅ Migration notes for existing users

**Success Criteria**:
- Documentation complete and accurate
- Code examples compile
- Migration path clear

---

## Rollback Plan

### After Phase 1-2 (Core + Unit Tests)
**If issues arise**:
- Delete `ChunkedLogger.cs` and `LogChunkMiddleware.cs`
- Remove test files
- Revert DI registration changes
- **Impact**: Feature never shipped, no production impact

### After Phase 3 (Integration Tests)
**If issues arise**:
- Keep ChunkedLogger class (may be useful for future)
- Disable middleware registration (comment out)
- Add feature flag: `ENABLE_CHUNKED_LOGGING=false`
- **Impact**: Feature disabled but code remains for future use

### After Phase 4 (Documentation)
**If issues arise in production**:
- Add environment variable: `ENABLE_CHUNKED_LOGGING=false`
- Document rollback procedure in CLAUDE.md
- Monitor metrics to verify rollback success
- **Impact**: Minimal - fall back to original logging behavior

---

## Risk Assessment

### Risk 1: Thread Safety Issues
**Probability**: Medium
**Impact**: High (data loss or corruption)
**Mitigation**:
- Use SemaphoreSlim for all buffer operations
- Add dedicated thread safety unit test
- Review code with focus on concurrent access

### Risk 2: Memory Consumption
**Probability**: Low
**Impact**: Medium (high memory usage if buffer grows too large)
**Mitigation**:
- Set reasonable default chunk size (10 logs)
- Add max buffer size limit (e.g., 100 logs)
- Monitor memory usage in integration tests

### Risk 3: API Batch Endpoint Failures
**Probability**: Medium
**Impact**: Medium (logs lost if bulk API fails)
**Mitigation**:
- Add retry logic in `FlushAsync()`
- Log failures to ILogger for monitoring
- Add fallback to individual API calls if bulk fails

### Risk 4: Middleware Execution Order
**Probability**: Low
**Impact**: Medium (logs not flushed if middleware not executed)
**Mitigation**:
- Document middleware registration order
- Add integration test verifying middleware execution
- Ensure middleware registered early in pipeline

---

## Performance Targets

### API Call Reduction
- **Before**: N+1 API calls (1 per log)
- **After**: N/chunk_size + 1 API calls
- **Target**: 50-90% reduction in API calls

### Latency Impact
- **Buffer overhead**: <5ms per log add
- **Flush overhead**: <100ms per chunk
- **Total impact**: <10% increase in end-to-end test execution time

### Memory Usage
- **Buffer size**: ~10 logs × 1KB = 10KB per buffer
- **Max memory**: ~100KB (assuming 10 concurrent test items)
- **Target**: <1MB total memory overhead

---

## Success Metrics

### Code Quality
- [ ] Unit test coverage >85%
- [ ] Integration test coverage >80%
- [ ] 0 compiler warnings
- [ ] 0 static analysis warnings

### Functional Requirements
- [ ] Logs batched correctly (10 per chunk by default)
- [ ] Auto-flush at chunk size
- [ ] Middleware flushes after request
- [ ] DisposeAsync flushes remaining logs
- [ ] No data loss under any scenario

### Performance Requirements
- [ ] 50-90% reduction in API calls
- [ ] <10% increase in test execution time
- [ ] <1MB memory overhead
- [ ] Thread-safe under concurrent access

### Documentation
- [ ] CLAUDE.md updated with feature section
- [ ] Code examples compile and run
- [ ] Configuration options documented
- [ ] Migration notes complete

---

## Next Steps

1. ✅ Review this task breakdown with user
2. ✅ Confirm Approach 2 (Middleware + ChunkedLogger) is approved
3. ✅ Proceed to Stage 4 (Implementation) starting with Task 1
4. Track progress using Task List checkboxes above
5. Update CLAUDE.md with implementation notes as tasks complete

---

**Status**: Ready for Implementation
**Estimated Total Duration**: 13.5 hours
**Tasks**: 7 (sequenced by dependency graph)
**Risk Level**: Low-Medium (mitigations in place)
