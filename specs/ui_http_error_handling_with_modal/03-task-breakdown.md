# Task Breakdown: HTTP Error Handling with Modal & Retry Logic

## Tasks Overview

### Task Dependency Graph

```
[Task 1: Infrastructure - Polly Integration]
    ↓
[Task 2: Domain Models/DTOs] ← [Task 3: Unit Tests - DTOs]
    ↓
[Task 4: IHttpErrorHandler Interface] ← [Task 5: Unit Tests - Interface]
    ↓
[Task 6: HttpErrorHandler Implementation] ← [Task 7: Unit Tests - ErrorHandler]
    ↓
[Task 8: RequestDeduplicationService] ← [Task 9: Unit Tests - Deduplication]
    ↓
[Task 10: DashboardComponentBase] ← [Task 11: Unit Tests - Base Component]
    ↓
[Task 12: ErrorModal Component] ← [Task 13: Component Tests - Modal]
    ↓
[Task 14: Phase 1 Component Migration] ← [Task 15: Integration Tests - Phase 1]
    ↓
[Task 16: Phase 2 Component Migration] ← [Task 17: Integration Tests - Phase 2]
    ↓
[Task 18: Phase 3 Component Migration] ← [Task 19: Integration Tests - Phase 3]
    ↓
[Task 20: Phase 4 Component Migration] ← [Task 21: Integration Tests - Phase 4]
    ↓
[Task 22: Phase 5 Testing & Documentation]
```

---

## Task List

### Phase 1: Foundation & Infrastructure

#### Task 1: Polly Integration
- **Complexity**: Low
- **Estimated Time**: 1 hour
- **Files to Create/Modify**:
  - `dashboard/dashboard.csproj` (modify - add Polly NuGet reference)
- **Dependencies**: None
- **Implementation Steps**:
  1. Add Polly NuGet package reference v8.4.1
  2. Verify package installation with `dotnet restore`
  3. Confirm compatibility with existing dependencies
- **Verification**:
  - [ ] `dotnet restore` succeeds with no errors
  - [ ] Polly v8.4.1 appears in `dashboard.csproj`
  - [ ] Build succeeds with `dotnet build`

#### Task 2: Domain Models/DTOs
- **Complexity**: Low
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `dashboard/Models/ErrorDetails.cs` (create)
  - `dashboard/Models/ErrorCategory.cs` (create - enum)
  - `dashboard/Models/RetryContext.cs` (create)
- **Dependencies**: Task 1 must be complete
- **Implementation Steps**:
  1. Create `ErrorCategory` enum with values: Network, Server, Client, RateLimit, Validation, Unknown
  2. Create `ErrorDetails` record with properties: Message, Title, Details, StackTrace, StatusCode, RequestId, EventCode, HttpMethod, Endpoint, Timestamp, IsRetryable, Category
  3. Create `RetryContext` record with CurrentAttempt, MaxAttempts, NextDelay
  4. Add XML documentation comments
- **Verification**:
  - [ ] All DTOs compile without errors
  - [ ] Properties have appropriate data types
  - [ ] XML comments complete
  - [ ] Records are immutable (init-only properties)

#### Task 3: Unit Tests - DTOs
- **Complexity**: Low
- **Estimated Time**: 1 hour
- **Files to Create**:
  - `Dashboard.Tests/Models/ErrorDetailsTests.cs` (create)
  - `Dashboard.Tests/Models/RetryContextTests.cs` (create)
- **Dependencies**: Task 2 must be complete
- **Implementation Steps**:
  1. Test ErrorDetails construction with all properties
  2. Test ErrorDetails with defaults (Timestamp, Category)
  3. Test RetryContext construction
  4. Test immutability (cannot change properties after creation)
- **Verification**:
  - [ ] All tests pass
  - [ ] Code coverage >80% for DTOs
  - [ ] Tests verify immutability

#### Task 4: IHttpErrorHandler Interface
- **Complexity**: Low
- **Estimated Time**: 1 hour
- **Files to Create**:
  - `dashboard/Services/IHttpErrorHandler.cs` (create)
- **Dependencies**: Task 2 must be complete
- **Implementation Steps**:
  1. Define interface with 3 methods:
     - `Task<ErrorDetails> HandleExceptionAsync(Exception ex, HttpRequestMessage? request)`
     - `Task ShowErrorAsync(ErrorDetails error)`
     - `Task<bool> IsRetryableAsync(Exception ex)`
  2. Add XML documentation comments
  3. Add `using` statements for Exception, HttpRequestMessage
- **Verification**:
  - [ ] Interface compiles without errors
  - [ ] XML comments complete
  - [ ] Method signatures match Architecture doc

#### Task 5: Unit Tests - Interface
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Create**:
  - `Dashboard.Tests/Services/IHttpErrorHandlerTests.cs` (create)
- **Dependencies**: Task 4 must be complete
- **Implementation Steps**:
  1. Create mock implementation of IHttpErrorHandler
  2. Test that interface can be implemented
  3. Test method signatures are correct
- **Verification**:
  - [ ] Mock implementation compiles
  - [ ] All interface methods can be called
  - [ ] Tests pass

#### Task 6: HttpErrorHandler Implementation
- **Complexity**: High
- **Estimated Time**: 4 hours
- **Files to Create**:
  - `dashboard/Services/HttpErrorHandler.cs` (create)
- **Dependencies**: Tasks 2, 4 must be complete
- **Implementation Steps**:
  1. Create class implementing IHttpErrorHandler
  2. Implement `HandleExceptionAsync`:
     - Map HttpRequestException → ErrorCategory.Network
     - Map HttpStatusCode 5xx → ErrorCategory.Server
     - Map HttpStatusCode 4xx → ErrorCategory.Client
     - Map HttpStatusCode 429 → ErrorCategory.RateLimit
     - Map JsonException → ErrorCategory.Validation
     - Extract error details from ProblemDetails JSON (if present)
     - Extract `extensions["eventCode"]` as EventCode
     - Populate ErrorDetails DTO
  3. Implement `ShowErrorAsync`:
     - Invoke ErrorModal component via event/state
  4. Implement `IsRetryableAsync`:
     - Return true for Network, Server, RateLimit
     - Return false for Client, Validation
  5. Add logging for all error handling
  6. Inject ILogger<HttpErrorHandler>
- **Verification**:
  - [ ] Class compiles without errors
  - [ ] All interface methods implemented
  - [ ] Error classification logic correct
  - [ ] Logging statements present
  - [ ] Build succeeds

#### Task 7: Unit Tests - ErrorHandler
- **Complexity**: High
- **Estimated Time**: 3 hours
- **Files to Create**:
  - `Dashboard.Tests/Services/HttpErrorHandlerTests.cs` (create)
- **Dependencies**: Task 6 must be complete
- **Implementation Steps**:
  1. Test `HandleExceptionAsync` with HttpRequestException (Network)
  2. Test `HandleExceptionAsync` with HttpStatusCode 500 (Server)
  3. Test `HandleExceptionAsync` with HttpStatusCode 404 (Client)
  4. Test `HandleExceptionAsync` with HttpStatusCode 429 (RateLimit)
  5. Test `HandleExceptionAsync` with JsonException (Validation)
  6. Test `HandleExceptionAsync` with ProblemDetails JSON parsing
  7. Test `IsRetryableAsync` for all error categories
  8. Test `ShowErrorAsync` event invocation
  9. Use NSubstitute for ILogger mocking
- **Verification**:
  - [ ] All tests pass
  - [ ] Code coverage >80% for HttpErrorHandler
  - [ ] All error categories tested
  - [ ] ProblemDetails parsing tested

#### Task 8: RequestDeduplicationService
- **Complexity**: Medium
- **Estimated Time**: 3 hours
- **Files to Create**:
  - `dashboard/Services/IRequestDeduplicationService.cs` (create - interface)
  - `dashboard/Services/RequestDeduplicationService.cs` (create - implementation)
- **Dependencies**: Task 2 must be complete
- **Implementation Steps**:
  1. Define IRequestDeduplicationService interface:
     - `Task<T> ExecuteAsync<T>(string key, Func<Task<T>> operation)`
     - `void Clear(string key)`
  2. Implement RequestDeduplicationService:
     - Use `ConcurrentDictionary<string, Task>` for in-flight requests
     - Key format: `{httpMethod}:{endpoint}` (e.g., "GET:/api/launches/123")
     - If key exists, await existing task and return result
     - If key doesn't exist, create task, store in dictionary, execute, remove on completion
     - Handle exceptions: remove from dictionary on failure
  3. Inject ILogger<RequestDeduplicationService>
  4. Add logging for duplicate detection
- **Verification**:
  - [ ] Service compiles without errors
  - [ ] Thread-safe (uses ConcurrentDictionary)
  - [ ] Duplicate requests return same result
  - [ ] Failed requests don't block future requests

#### Task 9: Unit Tests - Deduplication
- **Complexity**: Medium
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `Dashboard.Tests/Services/RequestDeduplicationServiceTests.cs` (create)
- **Dependencies**: Task 8 must be complete
- **Implementation Steps**:
  1. Test concurrent requests with same key return same result
  2. Test different keys execute independently
  3. Test failed request doesn't block future requests
  4. Test cache clear removes key
  5. Test async operation completion removes key
  6. Use Task.Delay to simulate async operations
- **Verification**:
  - [ ] All tests pass
  - [ ] Code coverage >80% for RequestDeduplicationService
  - [ ] Concurrency tested (10+ concurrent requests)
  - [ ] Exception handling tested

#### Task 10: DashboardComponentBase
- **Complexity**: High
- **Estimated Time**: 4 hours
- **Files to Create**:
  - `dashboard/Components/DashboardComponentBase.cs` (create)
- **Dependencies**: Tasks 4, 8 must be complete
- **Implementation Steps**:
  1. Create abstract class inheriting from ComponentBase
  2. Inject IHttpClientFactory, IHttpErrorHandler, IRequestDeduplicationService
  3. Implement helper methods:
     - `Task<T?> GetJsonAsync<T>(string endpoint)`
     - `Task<HttpResponseMessage> PostJsonAsync<T>(string endpoint, T data)`
     - `Task<HttpResponseMessage> PutJsonAsync<T>(string endpoint, T data)`
     - `Task<HttpResponseMessage> DeleteAsync(string endpoint)`
  4. Integrate Polly retry policy:
     - 3 attempts with exponential backoff (2s, 4s, 8s)
     - Only retry on Network, Server, RateLimit errors
  5. Integrate request deduplication for GET requests
  6. Call IHttpErrorHandler on exceptions
  7. Add logging for retries and errors
- **Verification**:
  - [ ] Class compiles without errors
  - [ ] All HTTP methods implemented
  - [ ] Polly retry policy configured correctly
  - [ ] Deduplication applied to GET requests
  - [ ] Error handling integrated
  - [ ] Build succeeds

#### Task 11: Unit Tests - Base Component
- **Complexity**: High
- **Estimated Time**: 4 hours
- **Files to Create**:
  - `Dashboard.Tests/Components/DashboardComponentBaseTests.cs` (create)
- **Dependencies**: Task 10 must be complete
- **Implementation Steps**:
  1. Create test component inheriting from DashboardComponentBase
  2. Mock IHttpClientFactory, IHttpErrorHandler, IRequestDeduplicationService
  3. Test `GetJsonAsync` success scenario
  4. Test `GetJsonAsync` with retry (network error → success)
  5. Test `GetJsonAsync` with final failure (3 retries exhausted)
  6. Test `PostJsonAsync`, `PutJsonAsync`, `DeleteAsync` methods
  7. Test deduplication for concurrent GET requests
  8. Test error handler invocation on failure
  9. Use bUnit for Blazor component testing
  10. Use NSubstitute for dependency mocking
- **Verification**:
  - [ ] All tests pass
  - [ ] Code coverage >80% for DashboardComponentBase
  - [ ] Retry logic tested
  - [ ] Deduplication tested
  - [ ] Error handling tested

#### Task 12: ErrorModal Component
- **Complexity**: Medium
- **Estimated Time**: 3 hours
- **Files to Create**:
  - `dashboard/Components/ErrorModal.razor` (create)
  - `dashboard/wwwroot/css/error-modal.css` (create)
  - `dashboard/wwwroot/js/error-modal.js` (create - for copy-to-clipboard)
- **Dependencies**: Task 2 must be complete
- **Implementation Steps**:
  1. Create Blazor component with Bootstrap 5 modal structure
  2. Add purple gradient header (#667eea to #764ba2)
  3. Display ErrorDetails properties:
     - Title
     - Message
     - **Metadata Grid** (7 items):
       1. Status Code (with color coding)
       2. HTTP Method
       3. Endpoint
       4. Timestamp
       5. Request ID
       6. Event Code (e.g., "LCH03", "DB04", "ADM91")
       7. Error Category
     - Collapsible "Technical Details" section
     - Collapsible "Stack Trace" section with copy button
  4. Add retry button (conditional on IsRetryable)
  5. Add "Save as PNG" button in footer
  6. Add dismiss button
  7. Add JavaScript interop for:
     - Copy-to-clipboard
     - Modal capture to PNG (using html2canvas)
  8. Add event callback for retry action
  9. Add CSS animations (fade-in, slide-up)
- **Verification**:
  - [ ] Component compiles without errors
  - [ ] Modal displays correctly in browser
  - [ ] Collapsible sections work
  - [ ] Copy button works
  - [ ] Save as PNG button captures and downloads image
  - [ ] Retry button appears only for retryable errors
  - [ ] CSS animations smooth

#### Task 13: Component Tests - Modal
- **Complexity**: Medium
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `Dashboard.Tests/Components/ErrorModalTests.cs` (create)
- **Dependencies**: Task 12 must be complete
- **Implementation Steps**:
  1. Test modal renders with ErrorDetails
  2. Test collapsible sections toggle
  3. Test retry button appears for retryable errors
  4. Test retry button hidden for non-retryable errors
  5. Test dismiss button closes modal
  6. Test copy button invokes JavaScript
  7. Use bUnit for Blazor component testing
- **Verification**:
  - [ ] All tests pass
  - [ ] Code coverage >80% for ErrorModal
  - [ ] UI rendering tested
  - [ ] Event callbacks tested

---

### Phase 2: Critical Components (7 components)

#### Task 14: Phase 2 Component Migration
- **Complexity**: High
- **Estimated Time**: 14 hours (2 hours per component)
- **Files to Modify**:
  - `dashboard/Pages/LaunchDetails.razor` (modify - inherit from DashboardComponentBase)
  - `dashboard/Pages/TestItemDetails.razor` (modify)
  - `dashboard/Pages/Results.razor` (modify)
  - `dashboard/Pages/ResultsRun.razor` (modify)
  - `dashboard/Pages/ProjectLaunches.razor` (modify)
  - `dashboard/Pages/SuiteDetails.razor` (modify)
  - `dashboard/Pages/ItemHistoryMatrix.razor` (modify)
- **Dependencies**: Tasks 10, 12 must be complete
- **Implementation Steps (per component)**:
  1. Change base class from `ComponentBase` to `DashboardComponentBase`
  2. Remove direct HttpClient usage
  3. Replace `Http.GetFromJsonAsync` with `GetJsonAsync`
  4. Replace `Http.PostAsJsonAsync` with `PostJsonAsync`
  5. Replace `Http.PutAsJsonAsync` with `PutJsonAsync`
  6. Replace `Http.DeleteAsync` with `DeleteAsync`
  7. Remove manual try-catch blocks (now handled by base class)
  8. Remove loading state management (handled by base class)
  9. Remove error logging (handled by base class)
  10. Test component manually in browser
- **Verification**:
  - [ ] All components compile without errors
  - [ ] No manual try-catch blocks remain
  - [ ] All HTTP calls use base class methods
  - [ ] Components function correctly in browser
  - [ ] Error modal appears on failures
  - [ ] Retry logic works

#### Task 15: Integration Tests - Phase 2
- **Complexity**: Medium
- **Estimated Time**: 7 hours (1 hour per component)
- **Files to Create**:
  - `Dashboard.Tests/Integration/LaunchDetailsIntegrationTests.cs` (create)
  - `Dashboard.Tests/Integration/TestItemDetailsIntegrationTests.cs` (create)
  - `Dashboard.Tests/Integration/ResultsIntegrationTests.cs` (create)
  - `Dashboard.Tests/Integration/ResultsRunIntegrationTests.cs` (create)
  - `Dashboard.Tests/Integration/ProjectLaunchesIntegrationTests.cs` (create)
  - `Dashboard.Tests/Integration/SuiteDetailsIntegrationTests.cs` (create)
  - `Dashboard.Tests/Integration/ItemHistoryMatrixIntegrationTests.cs` (create)
- **Dependencies**: Task 14 must be complete
- **Implementation Steps (per component)**:
  1. Create integration test class
  2. Test component loads successfully
  3. Test error handling displays modal
  4. Test retry logic recovers from transient errors
  5. Test final failure displays error details
  6. Use bUnit for component rendering
  7. Mock HTTP responses with failures
- **Verification**:
  - [ ] All integration tests pass
  - [ ] Error modal tested
  - [ ] Retry logic tested
  - [ ] Test coverage >80% for integration paths

---

### Phase 3: Medium Priority Components (10 components)

#### Task 16: Phase 3 Component Migration
- **Complexity**: High
- **Estimated Time**: 20 hours (2 hours per component)
- **Files to Modify**:
  - `dashboard/Pages/TestRuns.razor` (modify)
  - `dashboard/Components/LaunchCard.razor` (modify)
  - `dashboard/Components/TestRunCard.razor` (modify)
  - `dashboard/Components/BrowserSessionDetails.razor` (modify)
  - `dashboard/Components/ArtifactViewer.razor` (modify)
  - `dashboard/Components/LogViewer.razor` (modify)
  - `dashboard/Components/TestStepTree.razor` (modify)
  - `dashboard/Components/StatusBadge.razor` (modify)
  - `dashboard/Components/FilterPanel.razor` (modify)
  - `dashboard/Components/PaginationControls.razor` (modify)
- **Dependencies**: Task 14 must be complete
- **Implementation Steps**: Same as Task 14 (per component refactoring)
- **Verification**:
  - [ ] All components compile without errors
  - [ ] Components function correctly in browser
  - [ ] Error handling works

#### Task 17: Integration Tests - Phase 3
- **Complexity**: Medium
- **Estimated Time**: 10 hours (1 hour per component)
- **Files to Create**: 10 integration test files (one per component)
- **Dependencies**: Task 16 must be complete
- **Implementation Steps**: Same as Task 15 (per component testing)
- **Verification**:
  - [ ] All integration tests pass
  - [ ] Test coverage >80%

---

### Phase 4: Remaining Components (8 components)

#### Task 18: Phase 4 Component Migration
- **Complexity**: Medium
- **Estimated Time**: 16 hours (2 hours per component)
- **Files to Modify**:
  - `dashboard/Components/LaunchFilters.razor` (modify)
  - `dashboard/Components/SuiteList.razor` (modify)
  - `dashboard/Components/TestItemBreadcrumb.razor` (modify)
  - `dashboard/Components/TimelineChart.razor` (modify)
  - `dashboard/Components/StatisticsPanel.razor` (modify)
  - `dashboard/Components/QuickActions.razor` (modify)
  - `dashboard/Components/NotificationBar.razor` (modify)
  - `dashboard/Components/UserProfile.razor` (modify)
- **Dependencies**: Task 16 must be complete
- **Implementation Steps**: Same as Task 14 (per component refactoring)
- **Verification**:
  - [ ] All components compile without errors
  - [ ] Components function correctly in browser
  - [ ] Error handling works

#### Task 19: Integration Tests - Phase 4
- **Complexity**: Medium
- **Estimated Time**: 8 hours (1 hour per component)
- **Files to Create**: 8 integration test files (one per component)
- **Dependencies**: Task 18 must be complete
- **Implementation Steps**: Same as Task 15 (per component testing)
- **Verification**:
  - [ ] All integration tests pass
  - [ ] Test coverage >80%

---

### Phase 5: Testing & Documentation

#### Task 20: End-to-End Testing
- **Complexity**: High
- **Estimated Time**: 8 hours
- **Files to Create**:
  - `Dashboard.Tests/E2E/ErrorHandlingE2ETests.cs` (create)
- **Dependencies**: Tasks 14, 16, 18 must be complete
- **Implementation Steps**:
  1. Test full user workflow: Navigate → HTTP error → Modal appears → Retry → Success
  2. Test full user workflow: Navigate → HTTP error → Modal appears → Retry 3 times → Final failure
  3. Test duplicate request prevention (concurrent requests)
  4. Test error modal dismissal
  5. Test copy-to-clipboard functionality
  6. Test network failure recovery
  7. Test server error recovery
  8. Test client error (non-retryable)
  9. Use Playwright for browser automation
- **Verification**:
  - [ ] All E2E tests pass
  - [ ] User workflows tested end-to-end
  - [ ] Error scenarios covered
  - [ ] Retry logic validated

#### Task 21: Performance Testing
- **Complexity**: Medium
- **Estimated Time**: 4 hours
- **Files to Create**:
  - `Dashboard.Tests/Performance/ErrorHandlingPerformanceTests.cs` (create)
- **Dependencies**: Task 20 must be complete
- **Implementation Steps**:
  1. Test retry overhead: Measure request time with 0 retries vs 3 retries
  2. Test deduplication overhead: Measure 100 concurrent requests vs 100 sequential requests
  3. Test modal rendering time: Modal appears within 200ms of error
  4. Test memory usage: No memory leaks after 1000 errors
  5. Use BenchmarkDotNet for performance benchmarks
- **Verification**:
  - [ ] Retry overhead <10% of total request time
  - [ ] Deduplication prevents >90% duplicate requests
  - [ ] Modal appears within 200ms
  - [ ] No memory leaks detected

#### Task 22: CLAUDE.md Documentation Update
- **Complexity**: Low
- **Estimated Time**: 2 hours
- **Files to Modify**:
  - `CLAUDE.md` (modify - add to Recent Changes section)
- **Dependencies**: All previous tasks must be complete
- **Implementation Steps**:
  1. Add feature to "Recent Changes" section using SDD template
  2. Document key components (DashboardComponentBase, ErrorModal, HttpErrorHandler)
  3. Document technical highlights (Polly integration, request deduplication)
  4. Add benefits achieved
  5. Add known limitations
  6. Add future enhancements roadmap
  7. Add testing recommendations
  8. Add migration notes for component authors
- **Verification**:
  - [ ] CLAUDE.md updated with complete feature documentation
  - [ ] All key components documented
  - [ ] Migration guide present
  - [ ] Examples included

#### Task 23: User Documentation
- **Complexity**: Low
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `docs/user-guide-error-handling.md` (create)
- **Dependencies**: Task 22 must be complete
- **Implementation Steps**:
  1. Document what users see when errors occur
  2. Document error modal UI elements
  3. Document retry behavior
  4. Document when to retry vs dismiss
  5. Document copy-to-clipboard feature
  6. Add screenshots of error modal
- **Verification**:
  - [ ] User documentation complete
  - [ ] Screenshots included
  - [ ] Clear explanations for non-technical users

---

## Execution Strategy

### Phase 1: Foundation (Tasks 1-13) - Estimated 30 hours
**Goal**: Build core infrastructure and base components
- Focus: Correctness, abstraction design, retry logic
- Deliverables:
  - Polly integration ✅
  - DTOs and interfaces ✅
  - HttpErrorHandler service ✅
  - RequestDeduplicationService ✅
  - DashboardComponentBase ✅
  - ErrorModal component ✅
  - 100% unit test coverage for foundation

**Milestone 1 Quality Gate**:
- [ ] All foundation tests pass (>80% code coverage)
- [ ] DashboardComponentBase can be inherited by test component
- [ ] ErrorModal displays correctly in browser
- [ ] Retry logic tested with mock HTTP failures
- [ ] Deduplication tested with concurrent requests

---

### Phase 2: Critical Components (Tasks 14-15) - Estimated 21 hours
**Goal**: Migrate highest-traffic components
- Focus: Real-world validation, user impact
- Components:
  1. LaunchDetails.razor (primary launch view)
  2. TestItemDetails.razor (primary test view)
  3. Results.razor (test results list)
  4. ResultsRun.razor (test run details)
  5. ProjectLaunches.razor (main dashboard)
  6. SuiteDetails.razor (suite overview)
  7. ItemHistoryMatrix.razor (history visualization)

**Milestone 2 Quality Gate**:
- [ ] All Phase 2 components migrated
- [ ] Integration tests pass for all 7 components
- [ ] Manual testing confirms error modal works
- [ ] Retry logic validated in production-like scenarios
- [ ] No regressions in component functionality

---

### Phase 3: Medium Priority (Tasks 16-17) - Estimated 30 hours
**Goal**: Migrate supporting components
- Focus: Breadth of coverage, consistency
- Components: TestRuns, LaunchCard, TestRunCard, BrowserSessionDetails, ArtifactViewer, LogViewer, TestStepTree, StatusBadge, FilterPanel, PaginationControls

**Milestone 3 Quality Gate**:
- [ ] All Phase 3 components migrated
- [ ] Integration tests pass for all 10 components
- [ ] Code quality maintained (no shortcuts)
- [ ] Test coverage >80% across all migrated components

---

### Phase 4: Remaining Components (Tasks 18-19) - Estimated 24 hours
**Goal**: Complete component migration
- Focus: Completeness, edge cases
- Components: LaunchFilters, SuiteList, TestItemBreadcrumb, TimelineChart, StatisticsPanel, QuickActions, NotificationBar, UserProfile

**Milestone 4 Quality Gate**:
- [ ] All Phase 4 components migrated
- [ ] Integration tests pass for all 8 components
- [ ] 100% of dashboard components using DashboardComponentBase
- [ ] Zero inline try-catch blocks remaining (except DashboardComponentBase itself)

---

### Phase 5: Testing & Documentation (Tasks 20-23) - Estimated 16 hours
**Goal**: Validate feature completeness and document
- Focus: End-to-end validation, performance, documentation
- Deliverables:
  - E2E tests with Playwright
  - Performance benchmarks
  - CLAUDE.md documentation
  - User guide

**Milestone 5 Quality Gate**:
- [ ] All E2E tests pass
- [ ] Performance meets targets (retry overhead <10%, modal appears <200ms)
- [ ] CLAUDE.md updated
- [ ] User documentation complete
- [ ] Feature ready for production

---

## Rollback Plan

**If issues arise during implementation:**

**After Phase 1 (Foundation)**:
- Rollback: Delete all new files (DashboardComponentBase, ErrorModal, services)
- Keep: Polly NuGet package (can be used for future features)
- Effort: 1 hour

**After Phase 2 (Critical Components)**:
- Rollback: Revert 7 component files to `main` branch version
- Keep: Foundation infrastructure (can be used by future components)
- Effort: 2 hours

**After Phase 3-4 (All Components)**:
- Rollback: Feature flag to disable error modal and use old error handling
- Keep: All code (for future debugging and reactivation)
- Implementation:
  ```csharp
  // In DashboardComponentBase
  if (!FeatureFlags.ErrorModalEnabled)
  {
      // Use old inline try-catch pattern
  }
  ```
- Effort: 3 hours

---

## Total Effort Estimate

| Phase | Tasks | Estimated Hours |
|-------|-------|-----------------|
| Phase 1: Foundation | Tasks 1-13 | 30 hours |
| Phase 2: Critical Components | Tasks 14-15 | 21 hours |
| Phase 3: Medium Priority | Tasks 16-17 | 30 hours |
| Phase 4: Remaining Components | Tasks 18-19 | 24 hours |
| Phase 5: Testing & Docs | Tasks 20-23 | 16 hours |
| **TOTAL** | **23 Tasks** | **121 hours (~3 weeks)** |

---

## Quality Gates Summary

Before advancing to the next phase, verify:

**Phase 1 → Phase 2**:
- [ ] All unit tests pass (>80% coverage)
- [ ] DashboardComponentBase compiles and can be inherited
- [ ] ErrorModal renders correctly in browser
- [ ] Polly retry logic tested
- [ ] Request deduplication tested

**Phase 2 → Phase 3**:
- [ ] 7 critical components migrated successfully
- [ ] Integration tests pass for all Phase 2 components
- [ ] Manual testing confirms error handling works
- [ ] No regressions detected

**Phase 3 → Phase 4**:
- [ ] 10 medium priority components migrated
- [ ] Integration tests pass for all Phase 3 components
- [ ] Code quality maintained

**Phase 4 → Phase 5**:
- [ ] All 25+ components migrated
- [ ] Integration tests pass for all components
- [ ] Zero inline try-catch blocks remaining

**Phase 5 → Production**:
- [ ] E2E tests pass
- [ ] Performance meets targets
- [ ] Documentation complete
- [ ] Feature approved for production deployment
