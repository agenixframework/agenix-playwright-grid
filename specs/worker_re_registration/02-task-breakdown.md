# Worker Heartbeat Resilience - Task Breakdown

**Date**: 2025-12-30
**Status**: Stage 3 - Task Breakdown

---

## Task Dependency Graph

```
[Task 1: WorkerOptions Configuration]
    ↓
[Task 2: EnsureRegisteredAsync Method] ← [Task 3: Unit Tests for EnsureRegisteredAsync]
    ↓
[Task 4: Timer Gap Detection in Heartbeat]
    ↓
[Task 5: WorkerRegistrationVerifier Service] ← [Task 6: Unit Tests for Verifier]
    ↓
[Task 7: DI Registration & Startup]
    ↓
[Task 8: Metrics Implementation] ← [Task 9: Integration Tests]
    ↓
[Task 10: Environment Variables Documentation]
    ↓
[Task 11: Update CLAUDE.md]
```

---

## Task List

### Task 1: Add Configuration to WorkerOptions
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Modify**:
  - `worker/Services/WorkerOptions.cs` (add property lines 50-55)
- **Dependencies**: None
- **Implementation Steps**:
  1. Add `RegistrationVerificationIntervalSeconds` property with default value 60
  2. Add XML documentation explaining the purpose
  3. Read from environment variable `AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS`
  4. Add validation (must be >= 10 seconds)
- **Verification**:
  - [ ] Build succeeds with 0 errors
  - [ ] Property has default value of 60
  - [ ] Environment variable override works
  - [ ] Validation rejects values < 10

**Code**:
```csharp
/// <summary>
/// Interval (in seconds) for verifying worker registration with hub.
/// If worker is not found in hub diagnostics, it will re-register.
/// </summary>
public int RegistrationVerificationIntervalSeconds { get; init; } = 60;

// In FromEnvironment() method:
RegistrationVerificationIntervalSeconds = Math.Max(10,
    int.TryParse(Environment.GetEnvironmentVariable("AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS"), out var verif)
    ? verif : 60),
```

---

### Task 2: Implement EnsureRegisteredAsync Method
- **Complexity**: Medium
- **Estimated Time**: 1 hour
- **Files to Modify**:
  - `worker/Services/WorkerServiceRunner.cs` (lines 380-450, add new method)
- **Dependencies**: Task 1 complete
- **Implementation Steps**:
  1. Add `SemaphoreSlim _registrationLock = new(1, 1)` field
  2. Create `public async Task EnsureRegisteredAsync()` method
  3. Acquire lock using `await _registrationLock.WaitAsync()`
  4. Call existing `RegisterWithHubAsync()` method
  5. Call `_poolManager.InitializeAsync()` to re-warm pools
  6. Log success/failure with context
  7. Release lock in finally block
  8. Make method idempotent (safe to call multiple times)
- **Verification**:
  - [ ] Method is idempotent (calling twice doesn't cause issues)
  - [ ] Lock prevents concurrent registrations
  - [ ] Existing registration logic reused (no duplication)
  - [ ] Logs clear messages for debugging
  - [ ] Build succeeds with 0 errors

**Code**:
```csharp
private readonly SemaphoreSlim _registrationLock = new(1, 1);

/// <summary>
/// Ensures worker is registered with hub. Idempotent - safe to call multiple times.
/// </summary>
public async Task EnsureRegisteredAsync()
{
    await _registrationLock.WaitAsync();
    try
    {
        logger.LogInformation("[Registration] Ensuring worker is registered with hub...");

        // Use existing registration logic
        await RegisterWithHubAsync();

        // Re-warm browser pools
        await _poolManager.InitializeAsync();

        logger.LogInformation("[Registration] Worker re-registered successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Registration] Failed to ensure registration");
        throw;
    }
    finally
    {
        _registrationLock.Release();
    }
}
```

---

### Task 3: Unit Tests for EnsureRegisteredAsync
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Create**:
  - `WorkerService.Tests/WorkerServiceRunnerTests.cs` (new file)
- **Dependencies**: Task 2 complete
- **Implementation Steps**:
  1. Create test class inheriting from test base (if exists) or standalone
  2. Test: `EnsureRegisteredAsync_CallsRegisterAndInitialize`
  3. Test: `EnsureRegisteredAsync_IsIdempotent_MultipleCalls`
  4. Test: `EnsureRegisteredAsync_HandlesRegistrationFailure`
  5. Test: `EnsureRegisteredAsync_ReleasesLock_OnException`
  6. Use mocks for IPoolManager and HttpClient
- **Verification**:
  - [ ] All unit tests pass (4 tests)
  - [ ] Tests cover happy path + error scenarios
  - [ ] Mocks verify RegisterWithHubAsync and InitializeAsync called
  - [ ] Lock release verified even on exception

**Tests**:
```csharp
[Test]
public async Task EnsureRegisteredAsync_CallsRegisterAndInitialize()
{
    // Arrange: Mock successful registration
    // Act: Call EnsureRegisteredAsync()
    // Assert: Verify RegisterWithHubAsync() and InitializeAsync() called
}

[Test]
public async Task EnsureRegisteredAsync_IsIdempotent_MultipleCalls()
{
    // Arrange: Call twice in parallel
    // Act: await Task.WhenAll(EnsureRegisteredAsync(), EnsureRegisteredAsync())
    // Assert: Registration called exactly once (lock prevents duplicates)
}
```

---

### Task 4: Add Timer Gap Detection to Heartbeat
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Modify**:
  - `worker/Services/WorkerServiceRunner.cs` (modify heartbeat timer callback)
- **Dependencies**: Task 2 complete
- **Implementation Steps**:
  1. Add `DateTime _lastHeartbeatTime = DateTime.UtcNow` field
  2. Modify `OnHeartbeatTimer()` callback method
  3. Calculate gap: `var gap = DateTime.UtcNow - _lastHeartbeatTime`
  4. If gap > 2x heartbeat interval: Log warning and call `EnsureRegisteredAsync()`
  5. Update `_lastHeartbeatTime` after successful heartbeat
  6. Handle exceptions without crashing timer
- **Verification**:
  - [ ] Timer gap detection works (simulate by manually advancing time)
  - [ ] EnsureRegisteredAsync() called when gap detected
  - [ ] Normal heartbeats don't trigger re-registration
  - [ ] Exception in gap detection doesn't crash timer
  - [ ] Build succeeds with 0 errors

**Code**:
```csharp
private DateTime _lastHeartbeatTime = DateTime.UtcNow;

private async void OnHeartbeatTimer(object? sender, ElapsedEventArgs e)
{
    try
    {
        var now = DateTime.UtcNow;
        var gap = now - _lastHeartbeatTime;

        // FAST PATH: Detect timer gap (sleep/wake scenario)
        if (gap > TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds * 2))
        {
            logger.LogWarning(
                "[Heartbeat] Detected timer gap of {GapSeconds}s (expected <{ExpectedSeconds}s), triggering re-registration",
                gap.TotalSeconds,
                _options.HeartbeatIntervalSeconds * 2);

            await EnsureRegisteredAsync();
        }

        // Normal heartbeat
        await SendHeartbeatAsync();
        _lastHeartbeatTime = now;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Heartbeat] Error in heartbeat timer callback");
    }
}
```

---

### Task 5: Implement WorkerRegistrationVerifier Service
- **Complexity**: Medium
- **Estimated Time**: 1.5 hours
- **Files to Create**:
  - `worker/Infrastructure/Background/WorkerRegistrationVerifier.cs` (new file, ~150 lines)
- **Dependencies**: Task 2 complete
- **Implementation Steps**:
  1. Create class inheriting from `BackgroundService`
  2. Inject `IHttpClientFactory`, `WorkerOptions`, `ILogger`
  3. Inject `WorkerServiceRunner` to call `EnsureRegisteredAsync()`
  4. Implement `ExecuteAsync()` with periodic timer loop
  5. Create `IsRegisteredWithHubAsync()` method (calls `/diagnostics`)
  6. Parse JSON response and check if `nodeId` exists in workers list
  7. If missing: Call `WorkerServiceRunner.EnsureRegisteredAsync()`
  8. Add timeout (5s) for diagnostics HTTP call
  9. Handle errors gracefully (log and continue loop)
  10. Use cancellation token properly for shutdown
- **Verification**:
  - [ ] Service starts automatically with worker
  - [ ] Calls hub `/diagnostics` every 60s (configurable)
  - [ ] Detects missing worker registration
  - [ ] Triggers re-registration via EnsureRegisteredAsync()
  - [ ] Handles hub unavailability gracefully
  - [ ] Respects cancellation token on shutdown
  - [ ] Build succeeds with 0 errors

**Code**:
```csharp
namespace PlaywrightGrid.Worker.Infrastructure.Background;

public class WorkerRegistrationVerifier : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly WorkerOptions _options;
    private readonly WorkerServiceRunner _runner;
    private readonly ILogger<WorkerRegistrationVerifier> _logger;
    private readonly TimeSpan _verificationInterval;
    private readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(5);

    public WorkerRegistrationVerifier(
        IHttpClientFactory httpFactory,
        WorkerOptions options,
        WorkerServiceRunner runner,
        ILogger<WorkerRegistrationVerifier> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _runner = runner;
        _logger = logger;
        _verificationInterval = TimeSpan.FromSeconds(options.RegistrationVerificationIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait 30s before first check (let initial registration complete)
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        using var timer = new PeriodicTimer(_verificationInterval);

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await VerifyRegistrationAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("[RegistrationVerifier] Shutting down");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RegistrationVerifier] Error during verification");
            }
        }
    }

    private async Task VerifyRegistrationAsync(CancellationToken ct)
    {
        var isRegistered = await IsRegisteredWithHubAsync(ct);

        if (!isRegistered)
        {
            _logger.LogWarning("[RegistrationVerifier] Worker not found in hub diagnostics, re-registering...");
            await _runner.EnsureRegisteredAsync();
        }
        else
        {
            _logger.LogDebug("[RegistrationVerifier] Worker registration verified");
        }
    }

    private async Task<bool> IsRegisteredWithHubAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("Hub");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_httpTimeout);

            var response = await client.GetAsync("/diagnostics", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[RegistrationVerifier] Hub diagnostics returned {StatusCode}", response.StatusCode);
                return false; // Assume not registered if can't verify
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var diagnostics = JsonSerializer.Deserialize<DiagnosticsResponse>(json);

            return diagnostics?.Workers?.ContainsKey(_options.NodeId) == true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate shutdown cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RegistrationVerifier] Failed to verify registration with hub");
            return false; // Assume not registered on error (will trigger re-registration)
        }
    }

    private record DiagnosticsResponse(Dictionary<string, object>? Workers);
}
```

---

### Task 6: Unit Tests for WorkerRegistrationVerifier
- **Complexity**: Medium
- **Estimated Time**: 1 hour
- **Files to Create**:
  - `WorkerService.Tests/WorkerRegistrationVerifierTests.cs` (new file)
- **Dependencies**: Task 5 complete
- **Implementation Steps**:
  1. Create test class with mocked dependencies
  2. Test: `VerifyRegistration_WorkerFound_NoReregistration`
  3. Test: `VerifyRegistration_WorkerNotFound_TriggersReregistration`
  4. Test: `VerifyRegistration_HubUnavailable_AssumesMissingAndReregisters`
  5. Test: `VerifyRegistration_HubTimeout_AssumesMissingAndReregisters`
  6. Test: `ExecuteAsync_RespectsShutdownToken`
  7. Mock IHttpClientFactory to return test responses
  8. Mock WorkerServiceRunner.EnsureRegisteredAsync() to verify calls
- **Verification**:
  - [ ] All unit tests pass (5 tests)
  - [ ] Tests cover happy path + error scenarios
  - [ ] Mock verifies EnsureRegisteredAsync called when worker missing
  - [ ] Timeout handling verified
  - [ ] Shutdown cancellation token respected

---

### Task 7: DI Registration & Startup Integration
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Modify**:
  - `worker/Services/WorkerServiceRunner.cs` (builder.Services.AddHostedService line)
- **Dependencies**: Task 5 complete
- **Implementation Steps**:
  1. Register `WorkerRegistrationVerifier` as hosted service
  2. Ensure WorkerServiceRunner is registered as singleton (needed for injection)
  3. Verify startup order (registration verifier starts after initial registration)
  4. Test full startup sequence locally
- **Verification**:
  - [ ] Worker starts successfully with verifier service
  - [ ] Verifier service logs show periodic checks
  - [ ] No DI resolution errors
  - [ ] Build succeeds with 0 errors

**Code**:
```csharp
// In WorkerServiceRunner.cs, in the builder configuration section
builder.Services.AddSingleton<WorkerServiceRunner>(sp => /* existing registration */);
builder.Services.AddHostedService<WorkerRegistrationVerifier>();
```

---

### Task 8: Implement Metrics for Re-registration
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Modify**:
  - `worker/Infrastructure/Metrics/BrowserPoolMetrics.cs` (add new metrics)
  - `worker/Services/WorkerServiceRunner.cs` (emit metrics in EnsureRegisteredAsync)
  - `worker/Infrastructure/Background/WorkerRegistrationVerifier.cs` (emit metrics)
- **Dependencies**: Task 2, Task 5 complete
- **Implementation Steps**:
  1. Add counter: `worker_reregistration_total{reason="timer_gap|verification|manual"}`
  2. Add counter: `worker_reregistration_success{reason="..."}`
  3. Add counter: `worker_reregistration_failure{reason="..."}`
  4. Add histogram: `worker_reregistration_duration_seconds{reason="..."}`
  5. Emit metrics from EnsureRegisteredAsync (reason="timer_gap" or "verification")
  6. Emit metrics from WorkerRegistrationVerifier (reason="verification")
  7. Track duration using Stopwatch
- **Verification**:
  - [ ] Metrics visible in `/metrics` endpoint
  - [ ] Counters increment on re-registration
  - [ ] Histogram tracks duration correctly
  - [ ] Reason labels distinguish timer_gap vs verification
  - [ ] Build succeeds with 0 errors

**Code**:
```csharp
// In BrowserPoolMetrics.cs
private readonly Counter _reregistrationTotal;
private readonly Counter _reregistrationSuccess;
private readonly Counter _reregistrationFailure;
private readonly Histogram _reregistrationDuration;

public BrowserPoolMetrics()
{
    _reregistrationTotal = Metrics.CreateCounter(
        "worker_reregistration_total",
        "Total worker re-registration attempts",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    _reregistrationSuccess = Metrics.CreateCounter(
        "worker_reregistration_success",
        "Successful worker re-registrations",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    _reregistrationFailure = Metrics.CreateCounter(
        "worker_reregistration_failure",
        "Failed worker re-registrations",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    _reregistrationDuration = Metrics.CreateHistogram(
        "worker_reregistration_duration_seconds",
        "Duration of worker re-registration",
        new HistogramConfiguration { LabelNames = new[] { "reason" } });
}

public void RecordReregistration(string reason, bool success, TimeSpan duration)
{
    _reregistrationTotal.WithLabels(reason).Inc();
    if (success)
        _reregistrationSuccess.WithLabels(reason).Inc();
    else
        _reregistrationFailure.WithLabels(reason).Inc();
    _reregistrationDuration.WithLabels(reason).Observe(duration.TotalSeconds);
}

// In WorkerServiceRunner.cs EnsureRegisteredAsync()
var sw = Stopwatch.StartNew();
try
{
    await RegisterWithHubAsync();
    await _poolManager.InitializeAsync();
    _metrics.RecordReregistration(reason, success: true, sw.Elapsed);
}
catch
{
    _metrics.RecordReregistration(reason, success: false, sw.Elapsed);
    throw;
}
```

---

### Task 9: Integration Tests
- **Complexity**: High
- **Estimated Time**: 2 hours
- **Files to Create**:
  - `Agenix.PlaywrightGrid.Integration.Tests/Tests/Worker/ReregistrationTests.cs` (new file)
- **Dependencies**: Tasks 1-8 complete
- **Implementation Steps**:
  1. Create integration test that starts hub + worker
  2. Test: `Worker_ReregistersAfter_HubRestart`
  3. Test: `Worker_ReregistersAfter_ManualExpiration`
  4. Test: `Worker_MaintainsPoolsAfter_Reregistration`
  5. Use hub `/api/internal/pool/unregister` to manually expire worker
  6. Verify worker re-registers within 60 seconds
  7. Verify browser pools remain intact after re-registration
  8. Use ApiTestBase for fixture management
- **Verification**:
  - [ ] All integration tests pass (3 tests)
  - [ ] Tests simulate real-world expiration scenarios
  - [ ] Tests verify pool counts after re-registration
  - [ ] Tests run in CI/CD pipeline

**Test Example**:
```csharp
[Test]
public async Task Worker_ReregistersAfter_ManualExpiration()
{
    // Arrange: Start worker, verify registered
    var diagnostics1 = await Client.GetFromJsonAsync<DiagnosticsResponse>("/diagnostics");
    Assert.That(diagnostics1.Workers, Contains.Key("worker1"));

    // Act: Manually expire worker via internal endpoint
    await Client.DeleteAsync("/api/internal/pool/unregister?nodeId=worker1");

    // Wait for verification service to detect expiration (max 60s)
    await Task.Delay(TimeSpan.FromSeconds(65));

    // Assert: Worker re-registered
    var diagnostics2 = await Client.GetFromJsonAsync<DiagnosticsResponse>("/diagnostics");
    Assert.That(diagnostics2.Workers, Contains.Key("worker1"));
    Assert.That(diagnostics2.Workers["worker1"].TotalBrowsers, Is.GreaterThan(0));
}
```

---

### Task 10: Environment Variables Documentation
- **Complexity**: Low
- **Estimated Time**: 0.25 hours
- **Files to Modify**:
  - `.env` (add variable with comment)
  - `docker-compose.yml` (add to worker services)
  - `docs/ENVIRONMENT-VARIABLES.md` (document in Worker section)
- **Dependencies**: Task 1 complete
- **Implementation Steps**:
  1. Add `AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS=60` to `.env`
  2. Add variable to all worker services in docker-compose.yml
  3. Document variable in ENVIRONMENT-VARIABLES.md with description, default, example
  4. Follow existing documentation format
- **Verification**:
  - [ ] Variable documented in all 3 files
  - [ ] Documentation follows existing format
  - [ ] Default value matches code (60 seconds)
  - [ ] Example shows valid usage

**Documentation Template**:
```markdown
### AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS

**Description**: Interval (in seconds) for verifying worker registration with hub. If worker is not found in hub diagnostics, it will automatically re-register. This ensures workers recover from sleep/wake cycles and network interruptions.

**Default**: `60`

**Example**: `AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS=60`

**Valid Range**: Must be >= 10 seconds

**Required**: No (uses default if not specified)
```

---

### Task 11: Update CLAUDE.md with Implementation Notes
- **Complexity**: Low
- **Estimated Time**: 0.5 hours
- **Files to Modify**:
  - `CLAUDE.md` (add to "Recent Changes" section)
- **Dependencies**: Tasks 1-10 complete
- **Implementation Steps**:
  1. Document feature in "Recent Changes" section
  2. Use existing template format from CLAUDE.md
  3. Include: Overview, Problem, Solution, Files Created/Modified, Technical Highlights, Benefits
  4. Link to spec document in /specs/worker_re_registration/
  5. Add testing recommendations
  6. Document metrics and observability
- **Verification**:
  - [ ] Documentation follows CLAUDE.md format
  - [ ] All files listed with descriptions
  - [ ] Technical highlights explain key design decisions
  - [ ] Testing recommendations included
  - [ ] Links to spec documents valid

---

## Execution Strategy

### Phase 1: Foundation (Tasks 1-3) - 2 hours
- Build configuration and core re-registration method
- Write unit tests for core method
- Focus: Correctness, idempotency

### Phase 2: Detection Mechanisms (Tasks 4-6) - 3 hours
- Implement timer gap detection (fast path)
- Implement verification service (slow path)
- Write unit tests for both mechanisms
- Focus: Detection reliability, error handling

### Phase 3: Integration (Tasks 7-9) - 3 hours
- Wire up services in DI container
- Implement metrics for observability
- Write integration tests for real-world scenarios
- Focus: End-to-end functionality, observability

### Phase 4: Documentation (Tasks 10-11) - 0.75 hours
- Update environment variables documentation
- Update CLAUDE.md with implementation notes
- Focus: Completeness, clarity

**Total Estimated Time**: 8.75 hours

---

## Rollback Plan

If issues arise during implementation:

- **After Tasks 1-3**: Revert WorkerServiceRunner.cs changes, remove configuration
- **After Tasks 4-6**: Disable timer gap detection (remove gap check), remove verifier service
- **After Tasks 7-9**: Feature flag to disable verification service (don't add to hosted services)
- **After Tasks 10-11**: Documentation only, no code rollback needed

**Emergency Disable**: Set `AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS=999999` to effectively disable verification (checks every ~11 days).

---

## Testing Strategy

**Unit Tests** (Tasks 3, 6):
- Mock IPoolManager, IHttpClientFactory, WorkerServiceRunner
- Test idempotency, lock behavior, error handling
- Test timer gap detection logic
- Test registration verification logic

**Integration Tests** (Task 9):
- Start hub + worker in test environment
- Simulate expiration scenarios (manual, hub restart)
- Verify re-registration within 60 seconds
- Verify browser pools remain functional

**Manual Testing**:
1. Start worker, verify registered: `curl http://localhost:5100/diagnostics | jq .workers`
2. Put laptop to sleep for 10 minutes
3. Wake laptop
4. Wait 60 seconds
5. Verify worker re-registered: `curl http://localhost:5100/diagnostics | jq .workers`
6. Verify browser pools functional: `curl http://localhost:5100/diagnostics | jq '.workers[].totalBrowsers'`

---

## Success Criteria

- [ ] All 11 tasks complete with verification checkboxes ticked
- [ ] All unit tests pass (9 tests total)
- [ ] All integration tests pass (3 tests total)
- [ ] Build succeeds with 0 errors, 0 warnings
- [ ] Manual testing confirms recovery after sleep/wake
- [ ] Metrics visible in Prometheus
- [ ] Documentation complete in all 3 required files

---

## Next Steps

**Stage 4: Implementation (TDD)** - Follow red-green-refactor cycle for each task, track progress incrementally.

Shall I proceed to Stage 4 (Implementation)?
