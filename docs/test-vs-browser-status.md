# Test Status vs Browser Session Status

## Overview

Agenix Playwright Grid separates **test execution outcomes** from **browser session lifecycle** using two distinct status fields:

1. **`ComputedStatus`** - Test outcome status (what happened in your tests)
2. **`SessionStatus`** - Browser session lifecycle (infrastructure state)

This separation elegantly handles scenarios where **tests pass but the browser fails to close cleanly**.

---

## The Problem

Previously, a single `Status` field conflated two different lifecycles:

**Scenario:**
```
1. TestRun borrows ONE browser
2. test-001 executes → ✅ Passed
3. test-002 executes → ✅ Passed
4. test-003 executes → ✅ Passed
5. ⚠️ Sidecar doesn't close properly
6. Run marked as "AutoStopped"
```

**Issue:** The run status `AutoStopped` hides the fact that all tests passed!

---

## The Solution

### Two Status Fields

```csharp
public sealed record ResultRunSummaryDto
{
    // LEGACY (deprecated)
    public string Status { get; set; } = "Queued";

    // BROWSER SESSION LIFECYCLE
    public string? SessionStatus { get; set; }

    // TEST EXECUTION OUTCOMES
    public string? ComputedStatus { get; set; }
}
```

### SessionStatus (Browser Session Lifecycle)

Tracks the **infrastructure state** of the browser session:

| Value | Meaning | When Used |
|-------|---------|-----------|
| `Queued` | Browser not borrowed yet | Initial state before borrowing |
| `Running` | Browser active, tests executing | After successful borrow |
| `Completed` | Browser returned successfully | Normal completion path |
| `Stopped` | User manually stopped | User clicked stop button |
| `AutoStopped` | Timeout/inactivity, force-returned | Cleanup service intervention |
| `Aborted` | Infrastructure error | Borrow/return failure |

### ComputedStatus (Test Outcomes)

Reflects **actual test execution results**, independent of browser state:

| Value | Meaning | When Used |
|-------|---------|-----------|
| `InProgress` | Tests still executing | Tests are running |
| `Passed` | All tests passed | No failures, no timeouts |
| `Failed` | One or more tests failed | Test assertions failed |
| `Skipped` | All tests skipped | Tests were not executed |
| `Timedout` | Tests exceeded time limit | Timeout occurred |
| `Cancelled` | User stopped tests | Intentional cancellation |
| `Errored` | Infrastructure prevented execution | Borrow failed, no tests ran |

---

## Example Scenarios

### Scenario 1: Tests Pass, Browser Fails to Close

```json
{
  "runId": "abc123",
  "sessionStatus": "AutoStopped",    // ⚠️ Sidecar cleanup issue
  "computedStatus": "Passed",         // ✅ All tests passed
  "totalTests": 3,
  "passed": 3,
  "failed": 0
}
```

**Dashboard Display:**
```
Tests: ✅ Passed (3/3)
Browser: ⚠️ AutoStopped (infrastructure issue)
```

**Suite/Launch Aggregation:** Uses `ComputedStatus = Passed` (not `SessionStatus`)

---

### Scenario 2: Tests Fail, Browser Closes Properly

```json
{
  "runId": "def456",
  "sessionStatus": "Completed",       // ✅ Browser returned normally
  "computedStatus": "Failed",         // ❌ Tests failed
  "totalTests": 5,
  "passed": 3,
  "failed": 2
}
```

**Dashboard Display:**
```
Tests: ❌ Failed (3/5 passed)
Browser: ✅ Completed
```

---

### Scenario 3: No Tests Ran, Infrastructure Error

```json
{
  "runId": "ghi789",
  "sessionStatus": "AutoStopped",     // ⚠️ Browser borrowed but never returned
  "computedStatus": "Errored",        // ❌ Infrastructure prevented tests
  "totalTests": 0,
  "passed": 0,
  "failed": 0
}
```

**Dashboard Display:**
```
Tests: ❌ Errored (no tests executed)
Browser: ⚠️ AutoStopped
```

---

### Scenario 4: User Stopped Tests Mid-Execution

```json
{
  "runId": "jkl012",
  "sessionStatus": "Stopped",         // 🛑 User clicked stop
  "computedStatus": "Cancelled",      // 🛑 Tests cancelled
  "totalTests": 10,
  "passed": 5,
  "failed": 0
}
```

**Dashboard Display:**
```
Tests: 🛑 Cancelled (5/10 completed)
Browser: 🛑 Stopped (user action)
```

---

## Implementation Details

### When SessionStatus is Set

**1. Test Run Start (TestRunsEndpoints.cs:158-170)**
```csharp
run = run with
{
    Status = "Running",
    SessionStatus = "Running",        // Browser borrowed successfully
    ComputedStatus = "InProgress",    // Tests starting
    BrowserId = borrowResult.BrowserId,
    // ...
};
```

**2. Test Run Finish (TestRunsEndpoints.cs:297-349)**
```csharp
// Try to return browser
try {
    await browserPoolService.ReturnBrowserAsync(...);
    sessionStatus = "Completed";  // ✅ Browser returned
}
catch {
    sessionStatus = "AutoStopped";  // ⚠️ Return failed
}

updatedRun = run with
{
    SessionStatus = sessionStatus,
    ComputedStatus = status,  // From test aggregations
};
```

**3. Auto-Stop Cleanup (RunCleanupService.cs:290-340)**
```csharp
// Browser didn't close after inactivity/timeout
updated.SessionStatus = "AutoStopped";

// Compute test status from actual test results
var testCases = await resultsStore.GetTestCasesForRunAsync(run.RunId);
updated.ComputedStatus = TestResultStatusCalculator.CalculateStatus(
    totalTests, passed, failed, skipped, timedout, ...
).ToString();
```

### When ComputedStatus is Calculated

**Test Result Status Calculator (TestResultStatusCalculator.cs)**

Logic:
```csharp
public static TestResultStatus CalculateStatus(
    int totalTests,
    int passedTests,
    int failedTests,
    int skippedTests,
    int timedoutTests,
    bool isInProgress,
    bool wasCancelled,
    bool hadInfrastructureError)
{
    // Priority order:
    if (hadInfrastructureError) return TestResultStatus.Errored;
    if (wasCancelled) return TestResultStatus.Cancelled;
    if (totalTests == 0 || isInProgress) return TestResultStatus.InProgress;

    // If not all tests completed, still in progress
    var completedTests = passedTests + failedTests + skippedTests + timedoutTests;
    if (completedTests < totalTests) return TestResultStatus.InProgress;

    // Determine outcome
    if (timedoutTests > 0) return TestResultStatus.Timedout;
    if (failedTests > 0) return TestResultStatus.Failed;
    if (skippedTests == totalTests) return TestResultStatus.Skipped;
    if (passedTests > 0) return TestResultStatus.Passed;

    return TestResultStatus.InProgress;
}
```

---

## Aggregation Rules

### Suite and Launch Status Calculation

**Use `ComputedStatus`, NOT `SessionStatus`!**

```csharp
// ✅ CORRECT: Use test outcomes for aggregation
var suiteStatus = CalculateSuiteStatus(
    testRuns.Select(r => r.ComputedStatus ?? r.Status)
);

// ❌ WRONG: Don't use browser session status
var suiteStatus = CalculateSuiteStatus(
    testRuns.Select(r => r.SessionStatus)  // BAD!
);
```

**Why?** Browser infrastructure issues should not affect pass/fail status at the suite/launch level. If all tests passed, the suite should be marked as passed, even if some browsers didn't close cleanly.

---

## Dashboard Display Guidelines

### Priority: Show Test Status First

**Primary Badge:** `ComputedStatus` (test outcomes)
**Secondary Badge:** `SessionStatus` (only show if problematic)

```razor
<!-- Test Outcome (primary) -->
<div class="badge badge-@GetTestStatusClass(run.ComputedStatus)">
    Tests: @(run.ComputedStatus ?? "InProgress")
</div>

<!-- Browser Session (secondary, conditional) -->
@if (IsBrowserSessionProblematic(run.SessionStatus))
{
    <div class="badge badge-warning">
        Browser: @run.SessionStatus
        <span class="tooltip">Browser didn't close cleanly</span>
    </div>
}

@code {
    bool IsBrowserSessionProblematic(string? status) =>
        status is "Stopped" or "AutoStopped" or "Aborted";
}
```

### Visual Hierarchy

```
┌──────────────────────────────────────┐
│ Tests: ✅ Passed (3/3)               │  ← Primary (large, prominent)
│ Browser: ⚠️ AutoStopped              │  ← Secondary (smaller, muted)
│                                      │
│ ✅ 3 passed  ❌ 0 failed  ⊘ 0 skipped│
└──────────────────────────────────────┘
```

---

## Migration Strategy

### Backward Compatibility

The legacy `Status` field is maintained for backward compatibility:

```csharp
/// <summary>
/// DEPRECATED: Use SessionStatus or ComputedStatus instead.
/// </summary>
public string Status { get; set; } = "Queued";
```

**Migration V21** backfills `session_status` from existing `status` values:

```sql
UPDATE runs
  SET session_status = CASE
    WHEN status IN ('Queued', 'Running') THEN status
    WHEN status IN ('Stopped', 'AutoStopped', 'Aborted') THEN status
    WHEN status IN ('Passed', 'Failed') THEN 'Completed'
    ELSE 'Completed'
  END;
```

### Deprecation Timeline

1. **v1.x** - Add `SessionStatus` and `ComputedStatus`, keep `Status`
2. **v2.0** - Mark `Status` as `[Obsolete]` in API
3. **v3.0** - Remove `Status` field (breaking change)

---

## API Examples

### Creating a Test Run

```http
POST /api/suites/{suiteId}/runs
X-Project-Key: myproject

Response:
{
  "runId": "abc123",
  "status": "Running",          // Legacy
  "sessionStatus": "Running",   // Browser borrowed
  "computedStatus": "InProgress" // Tests starting
}
```

### Finishing a Test Run

```http
PUT /api/runs/{runId}/finish
{
  "status": "Passed"
}

Response (if browser returned successfully):
{
  "runId": "abc123",
  "sessionStatus": "Completed",   // ✅ Browser returned
  "computedStatus": "Passed"      // ✅ Tests passed
}

Response (if browser return failed):
{
  "runId": "abc123",
  "sessionStatus": "AutoStopped", // ⚠️ Browser stuck
  "computedStatus": "Passed"      // ✅ Tests still passed!
}
```

---

## Best Practices

### DO ✅

- Use `ComputedStatus` for test outcome logic
- Use `SessionStatus` for infrastructure monitoring
- Show test status prominently in UI
- Aggregate using `ComputedStatus` at suite/launch level
- Alert on `SessionStatus = AutoStopped` for infrastructure issues

### DON'T ❌

- Don't use `SessionStatus` for pass/fail determination
- Don't hide test outcomes behind browser issues
- Don't conflate infrastructure problems with test failures
- Don't aggregate using `SessionStatus`

---

## Troubleshooting

### Q: Run shows "AutoStopped" but tests passed?

**A:** This is expected! Check both statuses:
- `SessionStatus = AutoStopped` → Sidecar cleanup issue
- `ComputedStatus = Passed` → Tests executed successfully

**Action:** Investigate sidecar health, check worker logs.

### Q: Should I fail the suite if SessionStatus is AutoStopped?

**A:** No! Use `ComputedStatus` for suite aggregation. Browser issues are infrastructure concerns, not test failures.

### Q: How do I filter runs with browser issues?

**A:** Filter by `SessionStatus IN ('Stopped', 'AutoStopped', 'Aborted')`.

### Q: Dashboard shows two different statuses, which is correct?

**A:** Both are correct! They represent different aspects:
- Test status = What happened in your tests
- Browser status = What happened to the infrastructure

---

## See Also

- [Test Result Status Calculator](/hub/Infrastructure/Services/TestResultStatusCalculator.cs)
- [Migration V21](/hub/Infrastructure/Adapters/Results/Migrations/V21__separate_session_and_test_status.sql)
- [Run Cleanup Service](/hub/Infrastructure/Adapters/Background/RunCleanupService.cs)
