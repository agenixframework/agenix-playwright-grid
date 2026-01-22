# Agenix.PlaywrightGrid.Integration.Tests

Comprehensive integration tests for the Playwright Grid browser pool system using the new `Agenix.PlaywrightGrid.Client`
library.

## Test Files

### TestItemIntegrationTests.cs

**NEW (Phase 8)**: Integration tests for the TestItem API (ReportPortal-aligned hierarchical test structure), replacing
deprecated TestRun API.

**Test Coverage (6 Tests)**:

1. **Start test item of type Test → verify browser borrowed** - Validates browser session is created when Test type item
   starts
2. **Create hierarchical test items (Test → Step → Sub-step)** - Verifies nested test item structure with parent-child
   relationships
3. **Get test item by ID → verify complete data** - Tests GET endpoint returns all test item properties
4. **Get test item children → verify direct children** - Tests GetChildrenAsync returns immediate children only
5. **Finish test item → verify browser returned** - Validates browser cleanup and Redis pool state after test completion
6. **BDD Scenario with Given/When/Then steps** - Tests Scenario item type with BDD-style step hierarchy

**Key Features Tested**:

- ✅ Browser borrowing for Test/Scenario types
- ✅ Hierarchical test item structure (Test → Step nesting)
- ✅ BDD scenario support (Given/When/Then steps)
- ✅ Browser session cleanup and return to pool
- ✅ Redis pool state verification
- ✅ PostgreSQL persistence
- ✅ API key authentication
- ✅ TestItem API endpoints (Start, Finish, Get, GetChildren)

### BrowserPoolIntegrationTests.cs

Core integration tests for browser pool operations, verifying the complete flow from test run start through browser
management to test completion.

**Note**: Uses deprecated TestRun API (warnings expected). Will be migrated to TestItem API in future phase.

**Test Coverage (9 Tests)**:

#### Basic Operations (Tests 1-4)

- **Test 1**: Start test run → verify browser borrowed and session details stored
- **Test 2**: Finish test run → verify browser returned and session cleaned up
- **Test 3**: No capacity → verify 503 error and rollback
- **Test 4**: Maintenance mode → verify proper rejection with 503

#### Leak Detection (Tests 5-7)

- **Test 5**: Start N runs, finish N runs → verify pool size unchanged
- **Test 6**: Start run, crash before finish → verify timeout cleanup
- **Test 7**: Monitor Redis counters during operations → verify counters always balance

#### Load Testing (Tests 8-9)

- **Test 8**: 20 concurrent test runs → verify no deadlocks or race conditions
- **Test 9**: 50 iterations sustained load → verify counter accuracy maintained

### BrowserPoolSmokeTests.cs

End-to-end smoke tests using Playwright browser automation to verify complete browser workflows.

**Test Coverage**:

- **SmokeTest_BorrowNavigateReturn_AllLabels**: Borrows browser, navigates to Google, verifies navigation, returns
  browser
- **ConcurrentTest_ParallelBorrowsWithNavigation**: Multiple parallel browser borrows with concurrent navigation

---

## Running the Tests

### Prerequisites

- Hub running and accessible
- PostgreSQL database accessible
- Redis accessible
- At least one browser pool configured

### Environment Variables

```bash
HUB_URL=http://localhost:5100
PROJECT_KEY=TestProject
TEST_LABEL=AppB:Chromium:UAT
REDIS_CONNECTION=localhost:6379
POSTGRES_CONNECTION=Host=localhost;Port=5432;Database=playwright_grid;Username=postgres;Password=postgres
```

### Run Tests

```bash
dotnet test Agenix.PlaywrightGrid.Integration.Tests/Agenix.PlaywrightGrid.Integration.Tests.csproj
```

---

## API Reference

### Correct Property Names

**StartLaunchRequest**:

- `Name` (required) - not "LaunchName"
- `Description` (optional)
- `Attributes` (required array)

**StartSuiteRequest**:

- `Name` (required) - not "SuiteName"
- `LaunchId` or `ParentSuiteId` (optional, for nesting)
- `Description` (optional)
- `Attributes` (optional)

**StartTestRunRequest**:

- `SuiteId` (required in object initializer)
- `LabelKey` (required)
- `RunName` (optional)
- `Description` (optional)
- The StartAsync method takes (Guid suiteId, StartTestRunRequest request) but SuiteId MUST also be in the request

**LaunchCreatedResponse**:

- `Id` (Guid) - not "LaunchId"
- `LaunchNumber` (int)

**SuiteCreatedResponse**:

- `SuiteId` (Guid)

**TestRunCreatedResponse**:

- `RunId` (string)
- `SuiteId` (Guid)
- `Status` (TestRunStatus enum)
- `BrowserId`, `WebSocketEndpoint`, `BrowserType`, `NodeId`, `ExpiresAt`

### Client API

```csharp
var client = new Service(new Uri("http://localhost:5100"), "ProjectKey");

// Launch operations
var launchResponse = await client.Launch.StartAsync(new StartLaunchRequest { ... });

// Suite operations
var suiteResponse = await client.Suite.StartAsync(launchId, new StartSuiteRequest { ... });

// Test run operations
var runResponse = await client.TestRun.StartAsync(suiteId, new StartTestRunRequest {
    SuiteId = suiteId,  // REQUIRED in object
    LabelKey = "AppB:Chromium:UAT",
    ...
});

await client.TestRun.FinishAsync(runId, new FinishTestRunRequest { ... });
```

---

## Migration Status

### ✅ Completed (Phase 8)

- Created comprehensive TestItemIntegrationTests.cs with 6 tests
- All tests compile successfully
- Tests cover hierarchical test item structure
- Tests verify browser pool integration
- Tests validate API key authentication
- README updated with test documentation

### ⏳ Pending

- **BrowserPoolIntegrationTests.cs migration**: Existing tests use deprecated TestRun API (23 deprecation warnings)
- **BrowserPoolSmokeTests.cs migration**: Smoke tests use deprecated TestRun API (8 deprecation warnings)
- **Run integration tests**: Tests not yet executed against live hub
- **API Documentation**: Swagger/OpenAPI docs for TestItem endpoints

---

## Test Coverage

✅ Browser borrowing on test run start
✅ Browser return on test run finish
✅ No capacity error handling (503)
✅ Maintenance mode rejection (503)
✅ Rollback on borrow failure
✅ Redis pool state verification
✅ Database persistence verification
✅ Session metadata cleanup

---

## Future Enhancements

- Add test for browser timeout/TTL expiration
- Add test for quarantined node handling
- Add test for wildcard label matching
- Add test for concurrent borrow/return operations
- Add performance/stress tests for pool exhaustion scenarios

