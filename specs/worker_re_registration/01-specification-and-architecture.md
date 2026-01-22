# Worker Heartbeat Resilience After Sleep/Wake Cycles

**Date**: 2025-12-30
**Status**: Stage 1 - Specification

---

## Feature: Worker Auto-Recovery from Expired Registration

### Overview
Workers fail to re-register with the hub after laptop sleep/wake cycles or network interruptions. When the hub expires a worker due to missed heartbeats (5+ minutes), the worker remains unaware and continues operating with stale registration, resulting in empty browser pools that never recover.

### Problem Statement

**Current Behavior**:
1. Laptop goes to sleep → Worker heartbeat timer stops firing
2. Hub sweeper expires worker after 5 minutes of no heartbeats
3. Laptop wakes up → Worker heartbeat timer resumes BUT worker doesn't detect it was expired
4. Worker thinks it's still registered, but hub has no record of it
5. Browser pools remain empty indefinitely until manual restart

**Impact**:
- ❌ Browser pools unavailable after sleep/wake (manual restart required)
- ❌ Tests fail with "no browser capacity" errors
- ❌ Workers don't self-heal after network interruptions
- ❌ Production deployments affected by network hiccups

### User Stories

**As a** developer using the grid locally
**I want** workers to automatically recover after my laptop wakes from sleep
**So that** I don't have to manually restart workers every time

**As a** DevOps engineer managing production grid
**I want** workers to self-heal after network interruptions
**So that** the grid remains available without manual intervention

**As a** hub administrator
**I want** to see workers automatically re-register when they detect expiration
**So that** I can trust the grid is resilient to transient failures

### Acceptance Criteria

- [ ] Workers detect missed heartbeats (timer gap > 2x heartbeat interval)
- [ ] Workers verify registration status periodically (every 60 seconds)
- [ ] Workers automatically re-register when detected as expired by hub
- [ ] Workers re-warm browser pools after successful re-registration
- [ ] Workers emit metrics for re-registration events (success/failure)
- [ ] Workers log clear messages when detecting expiration and re-registering
- [ ] No browser sessions lost during re-registration (graceful handling)
- [ ] Hub diagnostics show workers with correct pool counts after recovery

### Constraints

- **Technical**: Must use existing registration API (`POST /api/internal/pool/register`)
- **Performance**: Detection must happen within 60 seconds of expiration
- **Compatibility**: Must not break existing heartbeat mechanism
- **Safety**: Must not create duplicate registrations (idempotent)
- **Browser Sessions**: Must preserve active browser sessions during re-registration

### Out of Scope

- Retry logic for registration failures (use existing retry mechanism)
- Alerting/notifications for repeated re-registrations (Phase 2)
- Automatic pool capacity adjustment after re-registration (use existing config)
- Hub-side detection of worker disconnection (already exists via sweeper)

### Success Metrics

- Workers recover within 60 seconds of hub expiration (p95)
- Zero manual worker restarts required after sleep/wake cycles
- Re-registration success rate > 99% (excluding intentional shutdowns)
- No browser session loss during re-registration

### Technical Context

**Relevant Files**:
- `worker/Services/WorkerServiceRunner.cs` - Heartbeat timer, registration logic
- `worker/Services/PoolManager.cs` - Browser pool warming
- `hub/Infrastructure/Web/BrowserPoolEndpoints.cs` - Registration endpoint
- `hub/Infrastructure/Services/NodeSweeperService.cs` - Worker expiration logic

**Current Heartbeat Mechanism**:
- Timer interval: 30 seconds (`WORKER_HEARTBEAT_INTERVAL_SECONDS`)
- Hub expiration threshold: 5 minutes (no heartbeat received)
- Timer type: `System.Timers.Timer` (does NOT fire during system sleep)

**Root Cause**:
- `System.Timers.Timer` pauses during system sleep
- Worker doesn't detect timer gap or verify hub registration status
- Worker assumes registration is still valid after wake-up

---

## Stage 2: Architecture Planning

### Research: Existing Patterns

**Similar Features in Codebase**:
- **BrowserAutoStopService**: Detects stale browser sessions and auto-stops them (lines 96-475)
- **NodeSweeperService**: Hub-side detection of stale workers (scans every 60s)
- **PidRedisManager**: Worker heartbeat to Redis with automatic recovery (`SendHeartbeatAsync`)
- **WorkerServiceRunner**: Current registration and heartbeat logic (lines 380-450)

**Relevant Patterns** (from CLAUDE.md):
- **Repository Pattern**: Use `IPoolManager` interface for pool operations
- **Background Service**: Use ASP.NET Core `BackgroundService` for detection loop
- **Metrics Pattern**: Use Prometheus metrics for observability (`BrowserPoolMetrics`)

### Approach 1: Registration Verification Loop

**Description**: Add a separate background service that periodically verifies registration status with the hub and re-registers if expired.

**Implementation**:
- **Layer**: Infrastructure (Background Service)
- **Key Classes**:
  - **NEW**: `WorkerRegistrationVerifier` (BackgroundService)
  - **MODIFY**: `WorkerServiceRunner.cs` (expose registration method)
  - **MODIFY**: `WorkerOptions.cs` (add verification interval config)
- **Database Changes**: None
- **API Changes**: None (use existing `/diagnostics` endpoint)

**How It Works**:
1. Background service runs every 60 seconds
2. Calls hub `/diagnostics` endpoint
3. Checks if worker's nodeId exists in workers list
4. If missing: Calls `RegisterWithHubAsync()` and `WarmPoolsAsync()`
5. If present: Verifies pool counts match configuration

**Pros**:
- ✅ Clear separation of concerns (dedicated service for verification)
- ✅ Independent of heartbeat timer (doesn't rely on System.Timers.Timer)
- ✅ Easy to test (mock HTTP calls)
- ✅ Can verify pool correctness (counts match config)
- ✅ Can detect hub restarts (all workers missing from diagnostics)

**Cons**:
- ❌ Additional HTTP call every 60s per worker (4 workers = 4 calls/min)
- ❌ Slight delay in detection (up to 60s + network latency)
- ❌ More complex (new service, new configuration)

**Complexity**: Medium

---

### Approach 2: Heartbeat Timer Gap Detection

**Description**: Enhance existing heartbeat timer callback to detect missed heartbeats and trigger re-registration.

**Implementation**:
- **Layer**: Infrastructure (modify existing WorkerServiceRunner)
- **Key Classes**:
  - **MODIFY**: `WorkerServiceRunner.cs` (add last heartbeat timestamp tracking)
- **Database Changes**: None
- **API Changes**: None

**How It Works**:
1. Track last heartbeat timestamp in field: `_lastHeartbeatTime`
2. On timer callback, calculate gap: `DateTime.UtcNow - _lastHeartbeatTime`
3. If gap > 2x heartbeat interval (60s): Re-register
4. Update `_lastHeartbeatTime` after successful heartbeat

**Pros**:
- ✅ Simple implementation (no new services)
- ✅ No additional HTTP calls (uses existing heartbeat)
- ✅ Fast detection (within 30s of wake-up)
- ✅ Minimal code changes

**Cons**:
- ❌ Relies on System.Timers.Timer resuming after sleep (may not always happen)
- ❌ Doesn't detect hub restart scenarios (worker thinks it's registered, hub has no record)
- ❌ No verification that hub actually has worker registered
- ❌ Can't verify pool correctness

**Complexity**: Low

---

### Approach 3: Hybrid (Timer Gap + Periodic Verification)

**Description**: Combine both approaches - detect timer gaps immediately AND verify registration periodically.

**Implementation**:
- **Layer**: Infrastructure
- **Key Classes**:
  - **NEW**: `WorkerRegistrationVerifier` (lightweight - just verification, no complex logic)
  - **MODIFY**: `WorkerServiceRunner.cs` (add timer gap detection in heartbeat callback)
- **Database Changes**: None
- **API Changes**: None

**How It Works**:
1. **Fast Path**: Heartbeat callback detects timer gap > 60s → immediate re-registration
2. **Slow Path**: Background service verifies registration every 60s → re-register if missing
3. Both paths call same `EnsureRegisteredAsync()` method (idempotent)

**Pros**:
- ✅ Fast recovery for sleep/wake (immediate detection)
- ✅ Resilient to hub restarts (periodic verification)
- ✅ Can verify pool correctness (verification service checks counts)
- ✅ Best of both approaches

**Cons**:
- ❌ Most complex (two detection mechanisms)
- ❌ Slight overhead (HTTP call every 60s + timer gap check every 30s)

**Complexity**: Medium-High

---

### Recommendation: Approach 3 (Hybrid)

**Justification**:
- **Aligns with Defensive Programming** - Multiple layers of detection prevent edge cases
- **Follows DDD Layer Boundaries** - Background service in Infrastructure layer
- **Performance Acceptable** - HTTP call every 60s is negligible (hub diagnostics is cached)
- **Production Ready** - Handles both sleep/wake AND hub restart scenarios
- **Observability** - Can emit metrics for both fast and slow path re-registrations

**Risks & Mitigations**:
- **Risk**: Duplicate re-registration attempts (both paths trigger at same time)
  → **Mitigation**: Use lock in `EnsureRegisteredAsync()` to make it idempotent

- **Risk**: Hub diagnostics endpoint slow/unavailable
  → **Mitigation**: Add timeout (5s) and error handling in verification service

- **Risk**: Re-registration fails during network instability
  → **Mitigation**: Existing retry logic in `RegisterWithHubAsync()` handles this

### Contracts

#### Configuration Changes

```bash
# New environment variable (optional, defaults to 60s)
AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS=60
```

#### New Background Service

```csharp
// worker/Infrastructure/Background/WorkerRegistrationVerifier.cs
public class WorkerRegistrationVerifier : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IPoolManager _poolManager;
    private readonly WorkerOptions _options;
    private readonly ILogger<WorkerRegistrationVerifier> _logger;
    private readonly TimeSpan _verificationInterval;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Verify registration every 60s
        // Call hub /diagnostics
        // Check if nodeId exists
        // If missing: await RegisterAndWarmPoolsAsync()
    }

    private async Task<bool> IsRegisteredWithHubAsync()
    {
        // GET /diagnostics, parse JSON, check for nodeId
    }

    private async Task RegisterAndWarmPoolsAsync()
    {
        // Call WorkerServiceRunner.RegisterWithHubAsync()
        // Call PoolManager.InitializeAsync() to warm pools
    }
}
```

#### Modified WorkerServiceRunner

```csharp
// worker/Services/WorkerServiceRunner.cs
public class WorkerServiceRunner
{
    private DateTime _lastHeartbeatTime = DateTime.UtcNow;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    private async void OnHeartbeatTimer(object? sender, ElapsedEventArgs e)
    {
        // FAST PATH: Detect timer gap
        var now = DateTime.UtcNow;
        var gap = now - _lastHeartbeatTime;

        if (gap > TimeSpan.FromSeconds(_heartbeatIntervalSeconds * 2))
        {
            _logger.LogWarning("[Heartbeat] Detected timer gap of {Gap}s, re-registering...", gap.TotalSeconds);
            await EnsureRegisteredAsync();
        }

        // Normal heartbeat
        await SendHeartbeatAsync();
        _lastHeartbeatTime = now;
    }

    // NEW: Idempotent registration method (called by both fast and slow paths)
    public async Task EnsureRegisteredAsync()
    {
        await _registrationLock.WaitAsync();
        try
        {
            _logger.LogInformation("[Registration] Ensuring worker is registered...");
            await RegisterWithHubAsync(); // Existing method
            await _poolManager.InitializeAsync(); // Re-warm pools
            _logger.LogInformation("[Registration] Worker re-registered successfully");
        }
        finally
        {
            _registrationLock.Release();
        }
    }
}
```

#### Metrics

```csharp
// Worker re-registration metrics
worker_reregistration_total{reason="timer_gap|verification|manual"} counter
worker_reregistration_success{reason="..."} counter
worker_reregistration_failure{reason="..."} counter
worker_reregistration_duration_seconds{reason="..."} histogram
```

### Dependencies

- **External Libraries**: None (use existing HttpClient, System.Timers.Timer)
- **Infrastructure**: Hub `/diagnostics` endpoint (already exists)
- **Other Features**: Existing registration logic in `WorkerServiceRunner.cs`

---

## Next Steps

**Stage 3: Task Breakdown** - Break down implementation into concrete tasks with dependencies and estimates.

Would you like me to proceed to Stage 3 (Task Breakdown)?
