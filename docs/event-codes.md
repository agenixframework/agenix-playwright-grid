# Event Codes Reference

## Overview

**Event codes** are stable, standardized identifiers used throughout the Agenix Playwright Grid logging system to mark key operations and milestones. Each event code provides a searchable, consistent way to track specific operations across logs, making it easy to:

- **Filter logs** by specific operation types
- **Set up alerts** for critical events (failures, timeouts, capacity issues)
- **Troubleshoot issues** by tracing operation flows
- **Monitor system health** by tracking success/failure rates

### How Event Codes Appear in Logs

Event codes are logged as **milestones** within chunked log operations. For example:

```
[POOL01] Browser borrow requested - labelKey: myapp:chromium:prod, runId: 123e4567-e89b
[POOL02] Browser allocated successfully - browserId: abc-def, nodeId: worker-1
[POOL03] Browser ready - endpoint: ws://worker-1:5000/browser/abc-def
```

Each event code consists of:
- **Prefix**: Category identifier (POOL, LCH, ITEM, ING, WRK, STG, HKEP, DB, RDS, EVT, NSR, ORP)
- **Number**: Sequential identifier within the category (01-99)

---

## Browser Pool Operations (POOL01-POOL99)

Browser pool operations manage the lifecycle of browser instances, including borrowing, returning, and automatic cleanup of inactive browsers.

### Borrowing Flow

#### POOL01 - Browser Borrow Requested
**What**: A test item has requested to borrow a browser from the pool.

**When**: Called at the start of `BrowserPoolService.TryBorrowBrowserAsync()`.

**Why**: Marks the beginning of a browser allocation request. Used to track borrow latency and success rates.

**Context**: `labelKey` (capacity routing key), `runId` (test item identifier), optional `runName`.

**Example**:
```
[POOL01] Browser borrow requested
  labelKey: myapp:chromium:staging
  runId: 123e4567-e89b-12d3-a456-426614174000
```

---

#### POOL02 - Browser Allocated
**What**: A browser has been successfully allocated from the pool.

**When**: After `TryBorrowFromPoolAsync()` successfully pops a browser from the available queue.

**Why**: Confirms browser allocation succeeded. Used to measure allocation time and track which browsers are in use.

**Context**: `browserId`, `nodeId` (worker node), `browserType`, `wsEndpoint`.

**Example**:
```
[POOL02] Browser allocated successfully
  browserId: abc-def-123
  nodeId: worker-1
  browserType: chromium
```

---

#### POOL03 - Browser Ready
**What**: Browser is ready with WebSocket endpoint.

**When**: After browser metadata is extracted and validated (WebSocket endpoint, browser version, etc.).

**Why**: Indicates browser is fully prepared for test execution. Separates allocation (POOL02) from readiness (POOL03).

**Context**: `browserId`, `wsEndpoint`, `browserVersion`, `playwrightVersion`.

**Example**:
```
[POOL03] Browser ready
  browserId: abc-def-123
  endpoint: ws://worker-1:5000/browser/abc-def-123
```

---

#### POOL04 - Browser Borrow Failed
**What**: No browser capacity available for the requested label key.

**When**: When `TryBorrowFromPoolAsync()` returns null (no available browsers).

**Why**: Critical for capacity monitoring. Indicates pool exhaustion or maintenance mode.

**Context**: `labelKey`, `runId`, error reason (no capacity, maintenance, quarantined nodes).

**Example**:
```
[POOL04] Browser borrow failed
  labelKey: myapp:chromium:prod
  reason: No browser capacity available
```

**Troubleshooting**: Check pool diagnostics (`/diagnostics` endpoint) to verify available browser count.

---

#### POOL05 - Browser Borrow Timeout
**What**: Browser allocation timed out (not currently implemented in code).

**When**: Would occur if borrow operation exceeds timeout threshold.

**Why**: Reserved for future timeout detection. Helps identify slow pool operations.

**Context**: `labelKey`, `runId`, timeout duration.

---

### Return Flow

#### POOL11 - Browser Return Requested
**What**: A test item has requested to return a browser to the pool.

**When**: Called at the start of `BrowserPoolService.ReturnBrowserAsync()`.

**Why**: Marks the beginning of a browser return operation. Used to track return latency.

**Context**: `browserId`, `labelKey`, optional `finalStatus`.

**Example**:
```
[POOL11] Browser return requested
  browserId: abc-def-123
  labelKey: myapp:chromium:staging
```

---

#### POOL12 - Browser Returned to Pool
**What**: Browser successfully moved from in-use to available queue.

**When**: After Lua script atomically moves browser from `browser:inuse:*` to `browser:available:*` queue.

**Why**: Confirms browser returned and available for reuse. Critical for capacity tracking.

**Context**: `browserId`, `labelKey`, `nodeId`.

**Example**:
```
[POOL12] Browser returned to pool successfully
  browserId: abc-def-123
  labelKey: myapp:chromium:staging
```

---

#### POOL13 - Browser Return Failed
**What**: Failed to return browser to pool (not currently used in code).

**When**: Would occur if return operation encounters an error.

**Why**: Reserved for return failure tracking. Helps identify stuck browsers.

**Context**: `browserId`, error details.

---

### Cleanup and Auto-Stop

#### POOL20 - Cleanup Scan Started
**What**: Background service (`BrowserAutoStopService`) has started a cleanup scan.

**When**: At the beginning of each cleanup iteration (every 5 minutes by default).

**Why**: Marks the start of automatic browser release for idle/timed-out test items.

**Context**: Scan parameters (batch size, timeout thresholds).

**Example**:
```
[POOL20] Cleanup scan started
  batchSize: 1000
  idleTimeout: 120s
```

---

#### POOL21 - Browser Released
**What**: Browser successfully released back to the pool during cleanup.

**When**: After `BrowserPoolService.ReturnBrowserAsync()` completes during auto-stop cleanup.

**Why**: Tracks automatic browser cleanup. Helps monitor cleanup effectiveness.

**Context**: `browserId`, `labelKey`, reason for cleanup (idle, timeout, error).

**Example**:
```
[POOL21] Browser released back to pool
  browserId: abc-def-123
  labelKey: myapp:chromium:staging
  reason: idle_timeout
```

---

#### POOL22 - Test Item Auto-Stopped
**What**: Test item automatically stopped due to timeout or inactivity.

**When**: After test item status updated to `AutoStopped` in database.

**Why**: Tracks automatic test termination. Helps identify long-running or stuck tests.

**Context**: Test item ID, `itemType`, reason (idle, timeout, session limit).

**Example**:
```
[POOL22] Test item auto-stopped due to timeout
  itemId: 123e4567-e89b-12d3-a456-426614174000
  itemType: Test
  reason: idle_timeout
```

---

#### POOL23 - Cleanup Failed
**What**: Cleanup operation failed for a test item.

**When**: When exception occurs during cleanup processing in `BrowserAutoStopService`.

**Why**: Alerts operators to cleanup failures. Helps identify systemic issues.

**Context**: Test item ID, error message, stack trace.

**Example**:
```
[POOL23] Cleanup failed
  itemId: 123e4567-e89b-12d3-a456-426614174000
  error: Database connection timeout
```

**Troubleshooting**: Check database connectivity and review error logs for root cause.

---

### Pool Management

#### POOL30 - Pool Initialized
**What**: Browser pool initialized for a specific label key.

**When**: During worker startup when `PoolManager.WarmLabelAsync()` completes.

**Why**: Confirms pool readiness. Tracks initialization time.

**Context**: `labelKey`, target capacity, actual initialized count.

---

#### POOL31 - Capacity Changed
**What**: Browser pool capacity increased or decreased.

**When**: When worker scales capacity up/down (not currently implemented).

**Why**: Tracks dynamic capacity adjustments for monitoring.

**Context**: `labelKey`, old capacity, new capacity.

---

#### POOL32 - Worker Registered
**What**: Worker node registered with hub.

**When**: After `NodeRegistrar.RegisterAsync()` completes successfully.

**Why**: Tracks worker availability. Critical for capacity monitoring.

**Context**: `nodeId`, `workerType` (chromium, firefox, webkit), capacity by label.

---

#### POOL33 - Worker Deregistered
**What**: Worker node deregistered from hub.

**When**: During graceful shutdown or after worker heartbeat failure.

**Why**: Tracks worker departures. Helps identify infrastructure issues.

**Context**: `nodeId`, reason (shutdown, heartbeat timeout, crash).

---

## Launch Operations (LCH01-LCH99)

Launch operations track the lifecycle of test launches, including creation, execution, and completion.

### Launch Lifecycle

#### LCH01 - Launch Created
**What**: A new launch has been created in the system.

**When**: After launch record inserted into `launches` table.

**Why**: Marks the beginning of a test execution session.

**Context**: `launchId`, `projectKey`, `launchNumber`, `name`, `ownerApiKey`.

**Example**:
```
[LCH01] Launch created
  launchId: 789e4567-e89b-12d3-a456-426614174000
  projectKey: admin_default
  launchNumber: 42
```

---

#### LCH02 - Launch Started
**What**: Launch execution has started (tests can now be added).

**When**: After launch status set to `InProgress`.

**Why**: Distinguishes creation from active execution.

**Context**: `launchId`, `startTime`.

---

#### LCH06 - Launch Number Calculation Started
**What**: Started calculating launch number for launch name.

**When**: At the beginning of launch number calculation query in `CreateLaunch`.

**Why**: Tracks launch number assignment. Used to measure calculation latency.

**Context**: `launchName`, `projectKey`.

**Example**:
```
[LCH06] Launch number calculation started
  name: Nightly Regression Suite
  projectKey: admin_default
```

---

#### LCH07 - Launch Number Calculated
**What**: Launch number successfully calculated.

**When**: After MAX(launch_number) + 1 query completes.

**Why**: Confirms sequential numbering succeeded. Tracks calculation time.

**Context**: `launchNumber`, `launchName`.

**Example**:
```
[LCH07] Launch number calculated
  launchNumber: 42
  name: Nightly Regression Suite
```

---

#### LCH08 - Launch Persist Started
**What**: Started persisting launch to database.

**When**: Before INSERT INTO launches statement execution.

**Why**: Marks beginning of database write. Used to measure persistence latency.

**Context**: `launchId`, `launchNumber`, `projectKey`.

**Example**:
```
[LCH08] Launch persist started
  launchId: 789e4567-e89b-12d3-a456-426614174000
  launchNumber: 42
```

---

#### LCH09 - Launch Notification Sent
**What**: SignalR notification successfully sent to project channel.

**When**: After `hubContext.Clients.Group().LaunchUpdated()` completes without error.

**Why**: Confirms real-time update delivered. Used to track notification reliability.

**Context**: `projectKey`, `launchId`, channel name.

**Example**:
```
[LCH09] Launch notification sent
  channel: project:admin_default
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH10 - Launch Notification Failed
**What**: Failed to send SignalR notification to project channel.

**When**: When SignalR hub context throws exception during notification.

**Why**: Tracks notification failures. Helps identify SignalR issues (non-critical, launch creation still succeeded).

**Context**: `projectKey`, error message.

**Example**:
```
[LCH10] Launch notification failed
  projectKey: admin_default
  error: SignalR connection lost (non-critical)
```

---

#### LCH99 - Launch Validation Failed
**What**: Launch creation request failed validation checks.

**When**: During launch creation when validation rules fail (e.g., missing projectKey, invalid name, malformed request).

**Why**: Tracks validation failures before database interaction. Helps identify client-side issues sending invalid requests.

**Context**: `validationError` (specific validation rule that failed), `projectKey` (if available), request parameters.

**Example**:
```
[LCH99] Launch validation failed
  validationError: ProjectKeyRequired
  endpoint: CreateLaunch
```

---

#### LCH13 - Finish Launch Started
**What**: Started processing finish launch request.

**When**: At the beginning of `/finish-launch` endpoint execution.

**Why**: Marks the start of launch finish workflow. Used to measure total finish operation latency.

**Context**: `launchId`.

**Example**:
```
[LCH13] Finish launch started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH14 - Authorization Started
**What**: Started API key authorization check for finish launch.

**When**: Before `AuthorizeApiKeyAsync` call in finish launch endpoint.

**Why**: Tracks authorization latency as part of finish operation.

**Context**: `launchId`.

**Example**:
```
[LCH14] Authorization started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH15 - Terminal State Check Started
**What**: Started checking if launch is in terminal state.

**When**: Before `IsLaunchInTerminalStateAsync` call.

**Why**: Tracks validation latency for terminal state checks.

**Context**: `launchId`.

**Example**:
```
[LCH15] Terminal state check started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH16 - Status Calculation Started
**What**: Started calculating launch status from test aggregations.

**When**: Before reading test aggregation data from database.

**Why**: Marks beginning of status calculation workflow. Used to measure calculation latency.

**Context**: `launchId`, `finishTime`.

**Example**:
```
[LCH16] Status calculation started
  launchId: 789e4567-e89b-12d3-a456-426614174000
  finishTime: 2025-01-15T10:30:00Z
```

---

#### LCH17 - Status Calculation Fallback
**What**: Status calculation fallback to "Finished" due to missing data.

**When**: When aggregation query returns no data (launch not found or no test statistics).

**Why**: Tracks cases where status calculation cannot use test data. Indicates potential data integrity issue.

**Context**: `launchId`.

**Example**:
```
[LCH17] Status calculation fallback
  launchId: 789e4567-e89b-12d3-a456-426614174000
  reason: No aggregation data found
```

---

#### LCH18 - Launch Update Started
**What**: Started updating launch with finish time and status.

**When**: Before executing UPDATE launches statement.

**Why**: Marks beginning of database write operation. Used to measure update latency.

**Context**: `launchId`, `status`.

**Example**:
```
[LCH18] Launch update started
  launchId: 789e4567-e89b-12d3-a456-426614174000
  status: Finished
```

---

#### LCH19 - Launch Updated
**What**: Launch successfully updated in database.

**When**: After UPDATE launches statement completes.

**Why**: Confirms database write succeeded. Tracks rows affected for validation.

**Context**: `launchId`, `rowsAffected`.

**Example**:
```
[LCH19] Launch updated
  launchId: 789e4567-e89b-12d3-a456-426614174000
  rowsAffected: 1
```

---

#### LCH20 - Cache Invalidation Started
**What**: Started invalidating Redis cache for launch.

**When**: Before deleting Redis cache key.

**Why**: Marks beginning of cache invalidation. Used to measure cache operation latency.

**Context**: `launchId`.

**Example**:
```
[LCH20] Cache invalidation started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH21 - Cache Invalidated
**What**: Redis cache successfully invalidated for launch.

**When**: After `KeyDeleteAsync` completes successfully.

**Why**: Confirms cache invalidation succeeded. Ensures fresh data on next read.

**Context**: `launchId`.

**Example**:
```
[LCH21] Cache invalidated
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH22 - Cache Invalidation Failed
**What**: Failed to invalidate Redis cache (non-critical).

**When**: When `KeyDeleteAsync` throws exception.

**Why**: Tracks cache invalidation failures. Non-critical since database is source of truth.

**Context**: `launchId`, `error`.

**Example**:
```
[LCH22] Cache invalidation failed
  launchId: 789e4567-e89b-12d3-a456-426614174000
  error: Redis connection timeout
```

---

### Delete Launch Workflow

#### LCH23 - Delete Launch Started
**What**: Started processing delete launch request.

**When**: At the beginning of `/api/launches/{id}` DELETE endpoint execution.

**Why**: Marks the start of launch deletion workflow. Used to measure total delete operation latency.

**Context**: `launchId`.

**Example**:
```
[LCH23] Delete launch started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH24 - Delete Authorization Started
**What**: Started API key authorization check for delete launch.

**When**: Before `AuthorizeApiKeyAsync` call in delete launch endpoint.

**Why**: Tracks authorization latency as part of delete operation.

**Context**: `launchId`.

**Example**:
```
[LCH24] Delete authorization started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH25 - Delete Transaction Started
**What**: Started database transaction for launch deletion.

**When**: After `BeginTransactionAsync` called in delete launch endpoint.

**Why**: Marks beginning of transactional delete operation. Used to track transaction duration.

**Context**: `launchId`.

**Example**:
```
[LCH25] Delete transaction started
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH26 - Launch Deleted
**What**: Launch successfully deleted from database (including all descendants via CASCADE).

**When**: After DELETE FROM launches statement completes.

**Why**: Confirms database deletion succeeded. Tracks rows affected for validation.

**Context**: `launchId`, `rowsAffected`.

**Example**:
```
[LCH26] Launch deleted
  launchId: 789e4567-e89b-12d3-a456-426614174000
  rowsAffected: 1
```

---

#### LCH27 - Delete Launch Completed
**What**: Delete launch operation completed (transaction committed, cache invalidated).

**When**: After all delete operations complete (database, cache, notifications).

**Why**: Marks successful completion of entire delete workflow.

**Context**: `launchId`, `duration`.

**Example**:
```
[LCH27] Delete launch completed
  launchId: 789e4567-e89b-12d3-a456-426614174000
  duration: 125ms
```

---

#### LCH06 - Launch Finished
**What**: Launch completed successfully or failed.

**When**: After `/finish-launch` endpoint completes.

**Why**: Marks end of test execution. Used to calculate launch duration.

**Context**: `launchId`, `status` (Finished, Failed, Stopped), `finishTime`, test statistics.

**Example**:
```
[LCH06] Launch finished
  launchId: 789e4567-e89b-12d3-a456-426614174000
  status: Finished
  totalTests: 150
  passed: 148
  failed: 2
```

---

#### LCH08 - Launch Failed
**What**: Launch terminated due to critical failure.

**When**: When launch finishes with `Failed` status.

**Why**: Distinguishes failure from successful completion.

**Context**: `launchId`, failure reason, error details.

---

#### LCH03 - Launch Not Found
**What**: Requested launch was not found in the system.

**When**: When an operation is requested on a non-existent launch ID or number.

**Why**: Critical for API error tracking.

**Context**: `launchId` or `launchNumber`.

---

#### LCH04 - Launch Creation Failed
**What**: Failed to create a new launch.

**When**: During POST /api/launches if database or validation fails.

**Why**: Tracks reliability of launch initiation.

**Context**: `projectName`, `error`.

---

#### LCH07 - Launch Already Finished
**What**: Operation attempted on a launch that is already in a terminal state.

**When**: Attempting to update or finish a launch that is already Finished or Failed.

**Why**: Prevents illegal state transitions.

**Context**: `launchId`, `status`.

---

#### LCH05 - Launch Force Finished
**What**: Launch forcefully terminated by user or system.

**When**: After `/force-finish-launch` endpoint completes.

**Why**: Tracks manual intervention. Helps identify problematic launches.

**Context**: `launchId`, `triggeredBy` (user, system), reason.

**Example**:
```
[LCH05] Launch force-finished
  launchId: 789e4567-e89b-12d3-a456-426614174000
  triggeredBy: admin@example.com
  reason: Manual stop request
```

---

### Launch State

#### LCH10 - Launch Status Calculated
**What**: Launch status computed from test item results.

**When**: After `TestResultStatusCalculator` computes final status during finish operation.

**Why**: Documents status calculation logic. Helps debug unexpected statuses.

**Context**: `launchId`, calculated status, test counts by status.

**Example**:
```
[LCH10] Launch status calculated
  launchId: 789e4567-e89b-12d3-a456-426614174000
  status: Failed
  passed: 148
  failed: 2
  stopped: 0
```

---

#### LCH11 - Launch Aggregations Updated
**What**: Launch aggregation counts updated (total tests, passed, failed, etc.).

**When**: After `RecalculateLaunchAggregationsAsync()` completes.

**Why**: Tracks aggregation recalculation for monitoring accuracy.

**Context**: `launchId`, updated counts.

---

#### LCH12 - Launch Auto-Stop Triggered
**What**: Launch automatically stopped due to inactivity timeout.

**When**: After `LaunchAutoStopService` triggers auto-stop.

**Why**: Tracks automatic launch cleanup. Helps identify abandoned launches.

**Context**: `launchId`, timeout threshold, last activity time.

---

### Launch Data

#### LCH30 - Attributes Updated
**What**: Launch attributes modified (tags, labels, metadata).

**When**: After attributes bulk edit operation completes.

**Why**: Tracks metadata changes for audit trail.

**Context**: `launchId`, added attributes, removed attributes.

---

#### LCH31 - Description Updated
**What**: Launch description changed.

**When**: After description update operation completes.

**Why**: Tracks documentation changes.

**Context**: `launchId`, old description, new description.

---

#### LCH32 - Metadata Updated
**What**: Launch metadata fields updated (name, tags, custom fields).

**When**: After metadata update operation completes.

**Why**: Tracks configuration changes.

**Context**: `launchId`, changed fields.

---

### Force Finish Launch Workflow

#### LCH40 - Force Finish Started
**What**: Started processing force finish launch request.

**When**: At the beginning of `/api/launches/{id}/force-finish` POST endpoint execution.

**Why**: Marks the start of force finish workflow. Used to measure total force finish operation latency and track manual intervention.

**Context**: `launchId`, `projectKey`, `reason`.

**Example**:
```
[LCH40] Force finish started
  launchId: 789e4567-e89b-12d3-a456-426614174000
  projectKey: admin_default
  reason: Manual stop request
```

---

#### LCH41 - Force Finish Launch Status Checked
**What**: Launch status checked to verify it's in a stoppable state.

**When**: After launch retrieved from database but before stopping test items.

**Why**: Validates launch can be force-finished. Tracks terminal state validation logic.

**Context**: `launchId`, `currentStatus`, `isStoppable`.

**Example**:
```
[LCH41] Force finish launch status checked
  launchId: 789e4567-e89b-12d3-a456-426614174000
  currentStatus: InProgress
  isStoppable: true
```

---

#### LCH42 - Active Test Items Found
**What**: Found active test items that need to be stopped.

**When**: After querying for test items with session_status IN ('Queued', 'Running').

**Why**: Documents how many test items will be affected by force finish. Helps estimate operation duration.

**Context**: `launchId`, `activeTestItemCount`.

**Example**:
```
[LCH42] Active test items found
  launchId: 789e4567-e89b-12d3-a456-426614174000
  activeTestItemCount: 15
```

---

#### LCH43 - Test Item Stopped
**What**: Individual test item marked as stopped (session_status = 'Stopped', computed_status = 'Cancelled').

**When**: After UPDATE test_items statement completes for each active test item in the loop.

**Why**: Tracks progress of stopping individual test items. Used for detailed audit trail.

**Context**: `testItemId`, `launchId`.

**Example**:
```
[LCH43] Test item stopped
  testItemId: 123e4567-e89b-12d3-a456-426614174000
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH44 - Browser Released
**What**: Browser session returned to pool for stopped test item.

**When**: After `IBrowserPoolService.ReturnBrowserAsync()` completes (or fails).

**Why**: Tracks browser cleanup during force finish. Helps identify browser pool leaks.

**Context**: `testItemId`, `browserId`, `success` (true/false), error message (if failed).

**Example (Success)**:
```
[LCH44] Browser released
  testItemId: 123e4567-e89b-12d3-a456-426614174000
  browserId: chrome-worker1-slot5
  success: true
```

**Example (Failure)**:
```
[WARN][LCH44] Browser release failed
  testItemId: 123e4567-e89b-12d3-a456-426614174000
  browserId: chrome-worker1-slot5
  error: Browser already returned
```

---

#### LCH45 - Force Finish Launch Status Updated
**What**: Launch status updated to terminal state (Stopped) in database.

**When**: After UPDATE launches statement completes.

**Why**: Confirms launch status transition succeeded. Tracks finish_time assignment.

**Context**: `launchId`, `newStatus` (Stopped), `finishTime`.

**Example**:
```
[LCH45] Force finish launch status updated
  launchId: 789e4567-e89b-12d3-a456-426614174000
  newStatus: Stopped
  finishTime: 2025-01-25T14:30:00Z
```

---

#### LCH46 - Force Finish Aggregations Recalculated
**What**: Launch test aggregations recalculated (total_tests, passed_tests, failed_tests, etc.).

**When**: After `RecalculateLaunchAggregationsAsync()` completes.

**Why**: Ensures launch statistics reflect stopped test items. Used to verify aggregation accuracy.

**Context**: `launchId`, updated counts.

**Example**:
```
[LCH46] Force finish aggregations recalculated
  launchId: 789e4567-e89b-12d3-a456-426614174000
  totalTests: 150
  passedTests: 120
  failedTests: 15
  stoppedTests: 15
```

---

#### LCH47 - Force Finish Audit Logged
**What**: Audit log entry created for force finish action.

**When**: After INSERT INTO audit_logs statement completes (or fails).

**Why**: Provides audit trail for manual interventions. Tracks who stopped the launch and why.

**Context**: `launchId`, `userId`, `action`, `reason`, success (true/false), error message (if failed).

**Example (Success)**:
```
[LCH47] Force finish audit logged
  launchId: 789e4567-e89b-12d3-a456-426614174000
  userId: admin@example.com
  action: launch.forceFinish
  reason: Manual stop request
  success: true
```

**Example (Failure)**:
```
[WARN][LCH47] Audit logging failed
  launchId: 789e4567-e89b-12d3-a456-426614174000
  error: audit_logs table not found
```

---

#### LCH48 - Force Finish Cache Invalidated
**What**: Redis cache entry for launch status invalidated.

**When**: After redis.KeyDeleteAsync() completes (or redis is null).

**Why**: Ensures cached launch status doesn't show stale InProgress state. Tracks cache consistency.

**Context**: `launchId`, `cacheKey`, success (true/false), error message (if failed), redis availability.

**Example (Success)**:
```
[LCH48] Force finish cache invalidated
  launchId: 789e4567-e89b-12d3-a456-426614174000
  cacheKey: launch:789e4567-e89b-12d3-a456-426614174000:status
  success: true
```

**Example (Failure)**:
```
[WARN][LCH48] Cache invalidation failed
  launchId: 789e4567-e89b-12d3-a456-426614174000
  error: Redis connection timeout
```

**Example (Redis Unavailable)**:
```
[WARN][LCH48] Redis unavailable for cache invalidation
  launchId: 789e4567-e89b-12d3-a456-426614174000
```

---

#### LCH49 - Force Finish Completed
**What**: Force finish operation completed successfully (transaction committed, SignalR notified).

**When**: After all force finish operations complete (database, browser cleanup, cache, audit, notifications).

**Why**: Marks successful completion of entire force finish workflow. Used to measure end-to-end operation duration.

**Context**: `launchId`, `testItemsStopped`, `browsersReleased`, `duration`.

**Example**:
```
[LCH49] Force finish completed
  launchId: 789e4567-e89b-12d3-a456-426614174000
  testItemsStopped: 15
  browsersReleased: 15
  duration: 1.8s
```

---

## Log Item Operations (LOG01-LOG99)

Log item operations manage the creation, batching, and retrieval of individual log entries associated with test items.

### Creation Flow

#### LOG01 - Log Item Created
**What**: A request to create a single log item has been received.
**When**: At the start of `LogItemsEndpoints.CreateLogItem()`.
**Why**: Marks the beginning of log item creation. Used to track ingestion throughput.
**Context**: `project`, `testItemUuid`.

#### LOG02 - Log Item Creation Failed
**What**: Validation or processing failed for a single log item request.
**When**: When input validation fails or a prerequisite (like test item existence) is not met.
**Why**: Tracks client-side errors or data integrity issues.
**Context**: `error`, `testItemUuid`.

#### LOG03 - Log Item Batch Created
**What**: A request to create a batch of log items has been received.
**When**: At the start of `LogItemsEndpoints.CreateLogItemBatch()`.
**Why**: Tracks batch ingestion volume.
**Context**: `project`, `batchSize`.

#### LOG04 - Log Item Batch Failed
**What**: The entire batch failed validation or processing.
**When**: When the batch is empty or contains no valid items.
**Why**: Monitors batch-level failures.
**Context**: `error`.

### Batch Processing

#### LOG10 - Log Item Batch Validation Started
**What**: Validation of items within a batch has begun.
**When**: Before iterating through batch items.
**Why**: Marks the start of batch processing logic.
**Context**: `batchSize`.

#### LOG11 - Log Item Batch Validation Complete
**What**: All items in the batch have been validated.
**When**: After the validation loop finishes.
**Why**: Tracks how many items successfully passed validation.
**Context**: `validCount`.

#### LOG12 - Log Item Batch Invalid Items Skipped
**What**: Some items in the batch were invalid and will be ignored.
**When**: When `invalidCount > 0` after validation.
**Why**: Tracks data quality within batches.
**Context**: `invalidCount`, `validCount`.

#### LOG13 - Test Item Lookup Started
**What**: Searching for the parent test item in the store.
**When**: During single item creation validation.
**Why**: Tracks dependencies for log items.
**Context**: `testItemUuid`.

#### LOG14 - Test Item Found
**What**: Parent test item successfully located.
**When**: After successful store lookup.
**Why**: Confirms link to parent item.
**Context**: `testItemUuid`, `itemType`.

### Event Publishing

#### LOG20 - Log Item Event Published
**What**: A log item event has been sent to the event publisher.
**When**: Before calling `eventPublisher.PublishLogItemEventAsync()`.
**Why**: Tracks outgoing events for async processing.
**Context**: `logItemId`, `testItemUuid`, `level`.

#### LOG21 - Log Item Event Publish Failed
**What**: Error occurred while publishing log item event.
**When**: In the catch block of event publishing.
**Why**: Monitors event bus health and reliability.
**Context**: `error`, `logItemId`, `correlationId`.

#### LOG22 - Log Item Batch Events Published
**What**: Batch event publishing completed.
**When**: After processing all valid items in a batch.
**Why**: Provides summary metrics for batch operations.
**Context**: `publishedCount`, `failedCount`, `total`.

#### LOG23 - Log Item Event Publish Confirmed
**What**: Event publisher confirmed successful delivery.
**When**: After successful async publish.
**Why**: Verification of successful event handoff.
**Context**: `logItemId`, `correlationId`.

#### LOG24 - Batch Processing Started
**What**: The processing loop for valid batch items has started.
**When**: After validation is complete.
**Why**: Marks transition from validation to processing.
**Context**: `validCount`.

### Query Operations

#### LOG05 - Log Item Retrieved
**What**: A request for a specific log item by ID.
**When**: At the start of `LogItemsEndpoints.GetLogItem()`.
**Why**: Tracks retrieval operations.
**Context**: `project`, `logItemId`.

#### LOG06 - Log Item Retrieval Failed
**What**: Requested log item was not found.
**When**: When store returns null for a log item ID.
**Why**: Monitors 404 rates for log queries.
**Context**: `error`, `logItemId`.

#### LOG07 - Log Items For Test Item Retrieved
**What**: Query for all logs associated with a test item.
**When**: At the start of `LogItemsEndpoints.GetLogItemsForTestItem()`.
**Why**: Tracks bulk retrieval for test items.
**Context**: `project`, `testItemUuid`.

#### LOG08 - Log Items For Launch Retrieved
**What**: Query for all logs associated with a launch.
**When**: At the start of `LogItemsEndpoints.GetLogItemsForLaunch()`.
**Why**: Tracks bulk retrieval for launches.
**Context**: `project`, `launchUuid`.

#### LOG30 - Log Item Query Executed
**What**: General log query execution.
**When**: When a complex query is performed.
**Why**: Tracks query performance.
**Context**: `query`.

#### LOG31 - Log Item Query Failed
**What**: General log query failed.
**When**: In the catch block of query execution.
**Why**: Monitors query engine health.
**Context**: `error`.

#### LOG40 - Query Completed
**What**: Test item log query returned results.
**When**: After store query returns.
**Why**: Tracks result sizes for queries.
**Context**: `testItemUuid`, `count`.

#### LOG41 - Query For Launch Completed
**What**: Launch log query returned results.
**When**: After store query returns.
**Why**: Tracks result sizes for launch queries.
**Context**: `launchUuid`, `count`.

---

## Test Item Operations (ITEM01-ITEM99)

Test item operations track individual test cases, steps, and scenarios within a launch.

### Item Lifecycle

#### ITEM01 - Test Item Created
**What**: A new test item has been created in memory.

**When**: At the start of `TestItemsEndpoints.StartTestItem()` endpoint.

**Why**: Marks test item creation request. Used to measure creation latency.

**Context**: `itemId`, `launchId`, `parentItemId`, `itemType`, `name`.

**Example**:
```
[ITEM01] Test item created
  itemId: 123e4567-e89b-12d3-a456-426614174000
  itemType: Test
  name: Login with valid credentials
```

---

#### ITEM02 - Test Item Persisted to Database
**What**: Test item successfully written to `test_items` table.

**When**: After `IResultsStore.UpsertRunAsync()` completes.

**Why**: Confirms persistence succeeded. Separates creation from storage.

**Context**: `itemId`, `launchId`, database write time.

**Example**:
```
[ITEM02] Test item persisted to database
  itemId: 123e4567-e89b-12d3-a456-426614174000
  dbWriteTime: 15ms
```

---

#### ITEM06 - Test Item Started
**What**: Test item execution started (browser session active).

**When**: After browser successfully borrowed and test execution begins.

**Why**: Marks actual test execution start (distinct from creation).

**Context**: `itemId`, `browserId`, `startTime`.

---

#### ITEM07 - Test Item Finished
**What**: Test item execution completed.

**When**: After `/finish-test-item` endpoint completes.

**Why**: Marks end of test execution. Used to calculate test duration.

**Context**: `itemId`, `status` (Passed, Failed, Skipped), `finishTime`, `durationMs`.

**Example**:
```
[ITEM07] Test item finished
  itemId: 123e4567-e89b-12d3-a456-426614174000
  status: Passed
  duration: 2.3s
```

---

#### ITEM08 - Test Item Failed
**What**: Test item terminated due to failure or error.

**When**: When test finishes with `Failed` or `Errored` status.

**Why**: Distinguishes failure from success. Used for failure rate tracking.

**Context**: `itemId`, error message, stack trace.

**Example**:
```
[ITEM08] Test item failed
  itemId: 123e4567-e89b-12d3-a456-426614174000
  error: Expected "Welcome" but got "Error 500"
```

---

#### ITEM03 - Test Item Not Found
**What**: Requested test item was not found in the system.

**When**: When an operation is requested on a non-existent test item ID or number.

**Why**: Critical for API error tracking.

**Context**: `itemId` or `dbId`.

---

#### ITEM04 - Test Item Creation Failed
**What**: Failed to create a new test item.

**When**: During POST /api/test-items if database or validation fails.

**Why**: Tracks reliability of test initiation.

**Context**: `launchUuid`, `name`, `error`.

---

### Item Hierarchy

#### ITEM10 - Child Item Added
**What**: Child test item added to parent (nested test structure).

**When**: After test item created with `parentItemId` set.

**Why**: Tracks hierarchical test organization. Used for BDD scenarios.

**Context**: `parentId`, `childId`, `childType`.

---

#### ITEM11 - Parent Item Linked
**What**: Test item linked to parent in hierarchy.

**When**: After `parent_item_id` foreign key set in database.

**Why**: Confirms hierarchy relationship established.

**Context**: `itemId`, `parentItemId`.

---

#### ITEM12 - Tree Loaded
**What**: Hierarchical test item tree loaded from database.

**When**: After `GetTestItemWithChildrenAsync()` completes.

**Why**: Tracks tree query performance. Used for UI rendering optimization.

**Context**: Root item ID, max depth, total items loaded, load time.

**Example**:
```
[ITEM12] Tree loaded
  rootItemId: 123e4567-e89b-12d3-a456-426614174000
  maxDepth: 5
  totalItems: 47
  loadTime: 82ms
```

---

### Item Data

#### ITEM20 - Log Item Added
**What**: Log entry added to test item.

**When**: After log item persisted via `LogItemResource` or ingestion service.

**Why**: Tracks logging activity. Used for log volume monitoring.

**Context**: `itemId`, log level, message length.

---

#### ITEM21 - Artifact Uploaded
**What**: Artifact (screenshot, trace, video) uploaded for test item.

**When**: After artifact file uploaded to MinIO and metadata stored.

**Why**: Tracks artifact storage. Used for storage capacity planning.

**Context**: `itemId`, `artifactId`, file type, file size.

**Example**:
```
[ITEM21] Artifact uploaded
  itemId: 123e4567-e89b-12d3-a456-426614174000
  artifactId: abc-def-456
  type: screenshot
  size: 124KB
```

---

#### ITEM22 - Status Updated
**What**: Test item status changed.

**When**: After status field updated in database.

**Why**: Tracks status transitions. Used for state machine validation.

**Context**: `itemId`, old status, new status.

---

#### ITEM23 - Parameters Set
**What**: Test item parameters configured (for parameterized tests).

**When**: After parameters JSONB field updated.

**Why**: Tracks parameterization. Used for test configuration audit.

**Context**: `itemId`, parameter count, parameters JSON.

---

## Ingestion Service Operations (ING01-ING99)

Ingestion service operations track asynchronous event processing from RabbitMQ to database.

### Batch Processing

#### ING01 - Batch Received from RabbitMQ
**What**: Batch of events received from RabbitMQ queue.

**When**: After RabbitMQ consumer delivers batch of messages.

**Why**: Tracks ingestion throughput. Used for queue monitoring.

**Context**: Batch ID, event count, queue name.

**Example**:
```
[ING01] Batch received from RabbitMQ
  batchId: batch-123
  eventCount: 50
  queue: test_items
```

---

#### ING02 - Batch Processing Started
**What**: Started processing batch of events.

**When**: At the beginning of `PostgresBatchWriter` processing.

**Why**: Marks start of batch processing. Used to measure processing latency.

**Context**: Batch ID, event types (test items, logs, commands).

---

#### ING03 - Batch Processing Completed
**What**: Batch processing finished successfully.

**When**: After all events in batch written to database.

**Why**: Confirms successful ingestion. Used for throughput metrics.

**Context**: Batch ID, events written, processing time.

**Example**:
```
[ING03] Batch processing completed
  batchId: batch-123
  eventsWritten: 50
  processingTime: 120ms
```

---

#### ING04 - Batch Processing Failed
**What**: Batch processing failed due to error.

**When**: When exception occurs during batch processing.

**Why**: Alerts to ingestion failures. Helps identify systemic issues.

**Context**: Batch ID, error message, failed event count.

**Example**:
```
[ING04] Batch processing failed
  batchId: batch-123
  error: Database connection timeout
  failedEvents: 50
```

---

### Database Writes

#### ING10 - Test Items Written to Database
**What**: Test item events written to `test_items` table via COPY.

**When**: After `PostgresBatchWriter.WriteTestItemsAsync()` completes.

**Why**: Tracks test item ingestion rate. Used for database load monitoring.

**Context**: Item count, write time.

**Example**:
```
[ING10] Test items written to database
  itemCount: 25
  writeTime: 45ms
```

---

#### ING11 - Log Items Written to Database
**What**: Log item events written to `log_items` table via COPY.

**When**: After `PostgresBatchWriter.WriteLogItemsAsync()` completes.

**Why**: Tracks log ingestion rate. Used for log volume monitoring.

**Context**: Log count, write time, token optimization used.

**Example**:
```
[ING11] Log items written to database
  logCount: 150
  writeTime: 78ms
  tokenOptimization: enabled
```

---

#### ING12 - Commands Written to Database
**What**: Command events written to `command_events` table.

**When**: After command event batch processing completes.

**Why**: Tracks command audit trail ingestion.

**Context**: Command count, write time.

---

#### ING13 - Database Write Failed
**What**: Failed to write events to database.

**When**: When database write operation encounters error.

**Why**: Alerts to persistence failures. Critical for data integrity.

**Context**: Event type (test items, logs, commands), error message, retry count.

**Example**:
```
[ING13] Database write failed
  eventType: log_items
  error: Unique constraint violation
  retryCount: 3
```

---

### Token Optimization

#### ING20 - Log Token Cache Hit
**What**: Found existing log token in Redis cache.

**When**: When `RedisLogTokenCache.GetOrCreateTokenAsync()` finds token in cache.

**Why**: Tracks cache effectiveness. Used to optimize token cache size.

**Context**: Token hash, message length.

---

#### ING21 - Log Token Cache Miss
**What**: Log token not found in cache (new message).

**When**: When token doesn't exist in Redis and must be created.

**Why**: Tracks new unique messages. Used for cache miss rate monitoring.

**Context**: Token hash, message length.

---

#### ING22 - Log Token Created
**What**: New log token created and stored in Redis + PostgreSQL.

**When**: After `CreateNewTokenAsync()` completes.

**Why**: Tracks token creation rate. Used for token table growth monitoring.

**Context**: Token hash, message, first seen timestamp.

**Example**:
```
[ING22] Log token created
  tokenHash: abc123def456
  message: "Test started successfully"
  firstSeen: 2025-01-01T10:30:00Z
```

---

## Worker Node Operations (WRK01-WRK99)

Worker node operations track browser lifecycle, registration, and health checks.

### Browser Lifecycle

#### WRK01 - Browser Startup Requested
**What**: Worker received request to start a browser instance.

**When**: At the start of `SidecarLauncher.StartAsync()`.

**Why**: Marks browser startup request. Used to measure startup latency.

**Context**: `browserType` (chromium, firefox, webkit), `labelKey`.

**Example**:
```
[WRK01] Browser startup requested
  browserType: chromium
  labelKey: myapp:chromium:prod
```

---

#### WRK02 - Playwright Launched
**What**: Playwright process launched successfully.

**When**: After `Process.Start()` for Playwright server succeeds.

**Why**: Confirms Playwright process started. Used for startup success rate.

**Context**: Process ID, browser type, startup time.

**Example**:
```
[WRK02] Playwright launched
  pid: 12345
  browserType: chromium
  startupTime: 1.2s
```

---

#### WRK03 - Browser Connected
**What**: Browser successfully connected and WebSocket endpoint ready.

**When**: After WebSocket endpoint returns successful health check.

**Why**: Confirms browser ready for test execution. Separates launch from readiness.

**Context**: Browser ID, WebSocket endpoint, browser version.

**Example**:
```
[WRK03] Browser connected
  browserId: abc-def-123
  endpoint: ws://worker-1:5000/browser/abc-def-123
  version: Chromium 120.0.6099.109
```

---

#### WRK04 - Browser Startup Failed
**What**: Failed to start browser instance.

**When**: When browser launch or connection fails.

**Why**: Alerts to browser startup issues. Critical for capacity availability.

**Context**: Browser type, error message, retry count.

**Example**:
```
[WRK04] Browser startup failed
  browserType: chromium
  error: Port 5000 already in use
  retryCount: 3
```

---

#### WRK05 - Sidecar Ready
**What**: Sidecar process emitted the ready signal with WebSocket endpoint.

**When**: When `SidecarLauncher` successfully parses the JSON ready signal from sidecar stdout.

**Why**: Confirms sidecar initialized correctly and is ready to accept connections.

**Context**: `browserId`, `wsEndpoint`, `playwrightVersion`, `browserVersion`.

---

#### WRK06 - Sidecar Process Exited
**What**: Sidecar process exited unexpectedly.

**When**: When the sidecar process terminates, especially during startup or unexpectedly after being ready.

**Why**: Identifies sidecar crashes or premature terminations.

**Context**: `exitCode`, `pid`, `browserType`.

---

#### WRK16 - Browser borrowed by client
**What**: A client has successfully connected to a browser slot on this worker.

**When**: Called in `PoolManager.MarkConnectionStart` when a WebSocket proxy connection is established.

**Why**: Tracks active utilization of worker slots from the worker's perspective.

**Context**: `browserId`.

---

#### WRK17 - Browser returned by client
**What**: A client has disconnected from a browser slot on this worker.

**When**: Called in `PoolManager.MarkConnectionEnd` when a WebSocket proxy connection is closed.

**Why**: Tracks when slots become available for recycling or reuse.

**Context**: `browserId`.

---

#### WRK07 - Browser pool warming started
**What**: The worker started the initial warming of browser pools based on configuration.

**When**: During `PoolManager.InitializeAsync()`.

**Why**: Tracks the start of the pool warming process on worker startup.

**Context**: `labelCount`.

---

#### WRK08 - Browser pool warming completed
**What**: The worker finished warming browser pools.

**When**: After `PoolManager.InitializeAsync()` completes.

**Why**: Confirms pools are warmed and ready.

**Context**: `totalBrowsersStarted`.

---

#### WRK18 - Pool resize started
**What**: A pool's target capacity has changed, and the worker is adjusting the number of sidecars.

**When**: In `PoolManager.ReconcileLoopAsync` when a capacity mismatch is detected.

**Why**: Tracks dynamic scaling of browser pools.

**Context**: `labelKey`, `oldCapacity`, `newCapacity`.

---

#### WRK19 - Pool resize completed
**What**: The worker has finished adjusting sidecars for a resized pool.

**When**: After pool resize operations are complete.

**Why**: Confirms scaling operation finished.

**Context**: `labelKey`, `currentCapacity`.

---

#### WRK09 - Browser pool reconciliation started
**What**: The worker started a reconciliation cycle for a specific browser label.

**When**: Periodically in `PoolManager.ReconcileLoopAsync()`.

**Why**: Tracks pool health and auto-scaling/re-warming activities.

**Context**: `labelKey`, `targetCount`.

---

#### WRK10 - Browser pool reconciliation completed
**What**: The worker finished a reconciliation cycle for a specific browser label.

**When**: After `PoolManager.WarmLabelAsync()` completes during reconciliation.

**Why**: Confirms pool state is aligned with target capacity.

**Context**: `labelKey`, `actualCount`.

---

### Browser Cleanup

#### WRK11 - Browser Cleanup Requested
**What**: Worker received request to clean up browser instance.

**When**: At the start of browser cleanup operation.

**Why**: Tracks cleanup requests. Used for resource management monitoring.

**Context**: Browser ID, reason (test finished, timeout, error).

---

#### WRK12 - Browser Closed
**What**: Browser process successfully terminated.

**When**: After browser process exits with code 0.

**Why**: Confirms successful cleanup. Used for cleanup success rate.

**Context**: Browser ID, process ID, exit code.

---

#### WRK13 - Browser Cleanup Failed
**What**: Failed to clean up browser instance.

**When**: When browser termination fails or hangs.

**Why**: Alerts to cleanup issues. Helps identify zombie processes.

**Context**: Browser ID, error message, process ID.

**Example**:
```
[WRK13] Browser cleanup failed
  browserId: abc-def-123
  error: Process did not exit after 30s
  pid: 12345
```

---

#### WRK14 - Orphaned processes cleanup started
**What**: The worker started scanning for orphaned sidecar processes.

**When**: During `PoolManager.InitializeAsync()` or periodic maintenance.

**Why**: Ensures no leaking processes from previous runs.

---

#### WRK15 - Orphaned processes cleanup completed
**What**: The worker finished cleaning up orphaned processes.

**When**: After orphan cleanup finishes.

**Context**: `killedCount`.

---

#### WRK27 - Pool label removed
**What**: A browser pool label is being removed from the worker because it's no longer in the configuration.

**When**: In `PoolManager.InitializeAsync` when comparing current configuration with existing pools.

**Why**: Tracks removal of unused labels and their resources.

**Context**: `labelKey`.

---

#### WRK28 - Browser pruned from pool
**What**: An extra browser instance is being killed to match reduced pool capacity.

**When**: During pool resizing or reconciliation.

**Why**: Tracks resource reclamation when capacity decreases.

**Context**: `browserId`, `labelKey`, `targetCount`.

---

### Worker Registration

#### WRK20 - Worker Registration Started
**What**: Worker started registration process with hub.

**When**: At the start of `NodeRegistrar.RegisterAsync()`.

**Why**: Tracks registration attempts. Used for worker availability monitoring.

**Context**: Worker ID, node type (chromium, firefox, webkit).

**Example**:
```
[WRK20] Worker registration started
  workerId: worker-1
  nodeType: chromium
```

---

#### WRK21 - Registration Sent to Hub
**What**: Registration request sent to hub via HTTP.

**When**: After HTTP POST to `/register` endpoint completes.

**Why**: Confirms request sent. Used to measure registration latency.

**Context**: Worker ID, hub URL, pool configuration.

---

#### WRK22 - Worker Registration Confirmed
**What**: Hub confirmed successful worker registration.

**When**: After hub returns 200 OK response.

**Why**: Confirms worker is available for test execution.

**Context**: Worker ID, assigned capacity, registration timestamp.

**Example**:
```
[WRK22] Worker registration confirmed
  workerId: worker-1
  capacity: {"myapp:chromium:prod": 3}
  timestamp: 2025-01-01T10:00:00Z
```

---

#### WRK23 - Worker Registration Failed
**What**: Failed to register worker with hub.

**When**: When registration HTTP request fails or returns error.

**Why**: Alerts to registration issues. Critical for worker availability.

**Context**: Worker ID, error message, retry count.

**Example**:
```
[WRK23] Worker registration failed
  workerId: worker-1
  error: Connection refused (hub not running)
  retryCount: 5
```

---

#### WRK24 - Worker Registration Verification Started
**What**: Periodic background check to verify the worker is still registered in the hub's active worker list.

**When**: At the start of `WorkerRegistrationVerifier.VerifyRegistrationAsync()`.

**Why**: Provides a "slow path" fallback to ensure the worker remains registered if the "fast path" (timer gap detection) fails or if the hub's state is reset.

**Context**: Worker ID.

---

#### WRK25 - Worker Registration Verification Succeeded
**What**: The worker was found in the hub's active worker list, or re-registration was successfully completed if it was missing.

**When**: After finding the worker in the diagnostics response or after a successful `EnsureRegisteredAsync()` call.

**Why**: Confirms the worker's registration status is healthy.

**Context**: Worker ID.

---

#### WRK26 - Worker Registration Verification Failed
**What**: Failed to verify worker registration or failed to re-register the worker if it was missing.

**When**: When the hub is unreachable, returns an error, or when re-registration fails.

**Why**: Alerts to synchronization issues between the worker and the hub.

**Context**: Worker ID, error message.

---

### Health Checks

#### WRK30 - Worker Health Check Started
**What**: Started worker health check operation.

**When**: At the beginning of periodic health check.

**Why**: Tracks health check execution. Used for monitoring cadence.

**Context**: Worker ID, check type (disk, memory, network).

---

#### WRK31 - Worker Health Check Completed
**What**: Health check completed successfully.

**When**: After all health checks pass.

**Why**: Confirms worker healthy. Used for availability tracking.

**Context**: Worker ID, health status, metrics (disk usage, memory, etc.).

**Example**:
```
[WRK31] Worker health check completed
  workerId: worker-1
  status: healthy
  diskUsage: 45%
  memoryUsage: 60%
```

---

#### WRK32 - Worker Health Check Failed
**What**: Health check detected unhealthy condition.

**When**: When health check threshold exceeded or check fails.

**Why**: Alerts to worker health issues. Critical for capacity management.

**Context**: Worker ID, failed check type, threshold, actual value.

**Example**:
```
[WRK32] Worker health check failed
  workerId: worker-1
  failedCheck: disk_usage
  threshold: 90%
  actual: 95%
```

---

### Heartbeat

#### WRK40 - Heartbeat Started
**What**: Heartbeat background service started.

**When**: During `HeartbeatLoopAsync` startup.

**Why**: Tracks service availability.

**Context**: `nodeId`, `interval`.

---

#### WRK41 - Heartbeat Tick
**What**: Periodic heartbeat successfully sent to Redis.

**When**: After successful update of node status in Redis.

**Why**: Confirms worker is alive and reporting status.

**Context**: `nodeId`, `capacity`.

---

#### WRK42 - Heartbeat Failed
**What**: Failed to send heartbeat to Redis.

**When**: When Redis operation fails.

**Why**: Alerts to communication issues with Redis.

**Context**: `nodeId`, `error`.

---

#### WRK43 - Heartbeat Gap Detected
**What**: Detected a significant gap between heartbeat ticks (e.g., system sleep/wake).

**When**: When the time since last heartbeat exceeds the threshold.

**Why**: Triggers re-registration to ensure hub state is up to date.

**Context**: `gapSeconds`, `thresholdSeconds`.

---

#### WRK44 - Heartbeat Stopped
**What**: Heartbeat background service stopped.

**When**: During service shutdown or cancellation.

**Why**: Tracks service lifecycle.

**Context**: `nodeId`.

---

## Web Server Operations (WSH01-WSH99)

Web server operations track the lifecycle of the worker's internal web server, including startup, endpoint registration, and request processing.

### Lifecycle Flow

#### WSH01 - Web Server Starting
**What**: The web server is beginning its startup process.

**When**: At the start of `WebServerHost.RunAsync()`.

**Why**: Marks the beginning of the web server initialization.

**Context**: `nodeId`.

---

#### WSH02 - Web Server Started
**What**: The web server has successfully started and is ready to accept requests.

**When**: After `app.RunAsync()` or equivalent start call.

**Why**: Confirms the web server is operational.

**Context**: `nodeId`, `urls` (listening addresses).

---

#### WSH03 - Web Server Stopping
**What**: The web server is initiating a graceful shutdown.

**When**: When `ApplicationStopping` is triggered.

**Why**: Marks the beginning of the drain process.

**Context**: `nodeId`.

---

#### WSH04 - Web Server Stopped
**What**: The web server has completely stopped.

**When**: After the web server shutdown is complete.

**Why**: Confirms the web server is no longer running.

**Context**: `nodeId`.

---

#### WSH05 - Endpoints Registered
**What**: API endpoints have been successfully registered in the web application.

**When**: After all `app.Map*` calls are complete.

**Why**: Confirms the routing table is initialized.

**Context**: `endpoints` (list of registered routes).

---

#### WSH06 - Configuration Dumped
**What**: A summary of the effective configuration is logged.

**When**: During web server startup.

**Why**: Useful for troubleshooting environment-specific issues.

**Context**: `config` (redacted configuration object).

---

#### WSH07 - Listening Addresses
**What**: The addresses and ports the server is listening on.

**When**: After the server has bound to its ports.

**Why**: Helps verify binding behavior and port allocation.

**Context**: `addresses`.

---

#### WSH08 - Request Received
**What**: An API request (e.g., /borrow) has been received.

**When**: At the beginning of an endpoint handler.

**Why**: Tracks incoming request volume and types.

**Context**: `path`, `method`, `labelKey` (if applicable).

---

#### WSH09 - Request Processed
**What**: An API request has been successfully processed.

**When**: Before returning a success result from an endpoint handler.

**Why**: Tracks successful request completion and latency.

**Context**: `path`, `method`, `status`.

---

#### WSH10 - Request Failed
**What**: An API request failed to process.

**When**: When returning an error result or catching an exception in an endpoint handler.

**Why**: Tracks failure rates and error types.

**Context**: `path`, `method`, `status`, `error`.

---

## Node Management Operations (NOD01-NOD99)

Node management operations track the hub's perspective on worker registrations and lifecycle.

#### NOD01 - Node Registration Received
**What**: Hub received a registration request from a worker.

**When**: At the beginning of the `/node/register` endpoint handler.

**Why**: Tracks incoming registration requests.

**Context**: Worker ID, remote IP.

---

#### NOD02 - Node Registration Success
**What**: Worker registration successfully processed and persisted in the hub's state.

**When**: After successful persistence of worker metadata in Redis.

**Why**: Confirms a new worker is now part of the grid.

**Context**: Worker ID, capacity, assigned labels.

---

#### NOD03 - Node Registration Update
**What**: Hub updated an existing worker's registration (e.g., heartbeat or capacity change).

**When**: When a registration request is received for an already registered worker.

**Why**: Tracks updates to worker state without full re-registration.

**Context**: Worker ID.

---

#### NOD04 - Node Registration Failed
**What**: Hub failed to process a worker registration request.

**When**: Due to invalid secret, malformed JSON, or database error.

**Why**: Alerts to rejected worker connections.

**Context**: Error message, remote IP.

---

#### NOD05 - Node Registration Cleanup
**What**: Hub removed an expired worker from its active list.

**When**: During periodic node sweeping.

**Why**: Tracks the removal of dead workers.

**Context**: Worker ID.

---

## Storage Operations (STG01-STG99)

Storage operations track artifact uploads, downloads, and deletions in MinIO/S3.

### Upload Operations

#### STG01 - Storage Upload Started
**What**: Started uploading artifact to MinIO/S3.

**When**: At the start of `StorageService.UploadArtifactAsync()`.

**Why**: Tracks upload requests. Used to measure upload latency.

**Context**: Artifact ID, file name, file size, bucket.

**Example**:
```
[STG01] Upload started
  artifactId: 123e4567-e89b-12d3-a456-426614174000
  fileName: screenshot.png
  fileSize: 124KB
  bucket: artifacts
```

---

#### STG02 - Storage Upload Completed
**What**: Artifact successfully uploaded to storage.

**When**: After MinIO `PutObjectAsync()` completes.

**Why**: Confirms upload succeeded. Used for upload success rate.

**Context**: Artifact ID, storage path, upload time.

**Example**:
```
[STG02] Upload completed
  artifactId: 123e4567-e89b-12d3-a456-426614174000
  storagePath: s3://artifacts/2025/01/01/123e4567.png
  uploadTime: 320ms
```

---

#### STG03 - Storage Upload Failed
**What**: Failed to upload artifact to storage.

**When**: When upload operation encounters error.

**Why**: Alerts to storage issues. Critical for artifact reliability.

**Context**: Artifact ID, error message, retry count.

**Example**:
```
[STG03] Upload failed
  artifactId: 123e4567-e89b-12d3-a456-426614174000
  error: Connection timeout to MinIO
  retryCount: 3
```

---

### Download Operations

#### STG10 - Storage Download Started
**What**: Started downloading artifact from MinIO/S3.

**When**: At the start of artifact download request.

**Why**: Tracks download requests. Used for access pattern analysis.

**Context**: Artifact ID, storage path.

---

#### STG11 - Storage Download Completed
**What**: Artifact successfully downloaded from storage.

**When**: After MinIO `GetObjectAsync()` completes.

**Why**: Confirms download succeeded. Used for download success rate.

**Context**: Artifact ID, download time, file size.

---

#### STG12 - Storage Download Failed
**What**: Failed to download artifact from storage.

**When**: When download operation encounters error.

**Why**: Alerts to retrieval issues. Helps identify missing artifacts.

**Context**: Artifact ID, error message (not found, timeout, etc.).

**Example**:
```
[STG12] Download failed
  artifactId: 123e4567-e89b-12d3-a456-426614174000
  error: Object not found in bucket
```

---

### Delete Operations

#### STG20 - Storage Delete Started
**What**: Started deleting artifact from MinIO/S3.

**When**: At the start of artifact deletion (retention cleanup).

**Why**: Tracks deletion requests. Used for storage cleanup monitoring.

**Context**: Artifact ID, storage path, reason (retention, manual delete).

---

#### STG21 - Storage Delete Completed
**What**: Artifact successfully deleted from storage.

**When**: After MinIO `RemoveObjectAsync()` completes.

**Why**: Confirms deletion succeeded. Used for cleanup success rate.

**Context**: Artifact ID, storage path, freed space.

---

#### STG22 - Storage Delete Failed
**What**: Failed to delete artifact from storage.

**When**: When deletion operation encounters error.

**Why**: Alerts to cleanup issues. Helps identify storage inconsistencies.

**Context**: Artifact ID, error message.

---

### Pre-signed URLs

#### STG30 - Pre-signed URL Generated
**What**: Generated pre-signed URL for artifact access.

**When**: After MinIO `PresignedGetObjectAsync()` completes.

**Why**: Tracks URL generation. Used for access pattern analysis.

**Context**: Artifact ID, URL expiration time, access type (inline, download).

**Example**:
```
[STG30] Pre-signed URL generated
  artifactId: 123e4567-e89b-12d3-a456-426614174000
  expiresIn: 1h
  accessType: inline
```

---

#### STG31 - Pre-signed URL Generation Failed
**What**: Failed to generate pre-signed URL.

**When**: When URL generation encounters error.

**Why**: Alerts to access issues. Helps identify permission problems.

**Context**: Artifact ID, error message.

---

## Housekeeping Service Operations (HKEP01-HKEP99)

Housekeeping service operations track automated data retention and cleanup.

### Launch Retention

#### HKEP01 - Launch Retention Check Started
**What**: Started launch retention cleanup scan.

**When**: At the beginning of `LaunchRetentionWorker` execution.

**Why**: Tracks retention job execution. Used for scheduling monitoring.

**Context**: Project key, retention policy (keepLaunches days).

**Example**:
```
[HKEP01] Launch retention check started
  projectKey: admin_default
  retentionPolicy: 30 days
```

---

#### HKEP02 - Launches Deleted
**What**: Launches deleted during retention cleanup.

**When**: After `delete_old_launches()` database function completes.

**Why**: Tracks cleanup volume. Used for retention effectiveness monitoring.

**Context**: Project key, deleted count, cutoff date.

**Example**:
```
[HKEP02] Launches deleted
  projectKey: admin_default
  deletedCount: 15
  cutoffDate: 2024-12-01
```

---

#### HKEP03 - Launch Retention Completed
**What**: Launch retention cleanup finished.

**When**: After all launch cleanup operations complete.

**Why**: Confirms job completion. Used for job duration tracking.

**Context**: Project key, execution time, items deleted.

---

### Log Retention

#### HKEP10 - Log Retention Check Started
**What**: Started log retention cleanup scan.

**When**: At the beginning of `LogRetentionWorker` execution.

**Why**: Tracks log cleanup job execution.

**Context**: Project key, retention policy (keepLogs days).

---

#### HKEP11 - Log Items Deleted
**What**: Log items deleted during retention cleanup.

**When**: After `delete_old_log_items()` database function completes.

**Why**: Tracks log cleanup volume. Critical for database size management.

**Context**: Project key, deleted log count, cutoff date.

**Example**:
```
[HKEP11] Log items deleted
  projectKey: admin_default
  deletedLogItems: 50000
  cutoffDate: 2024-12-25
```

---

#### HKEP12 - Orphaned Log Tokens Cleaned
**What**: Orphaned log tokens deleted (no log items referencing them).

**When**: After orphaned token cleanup in `delete_old_log_items()` completes.

**Why**: Tracks token cleanup. Prevents unbounded token table growth.

**Context**: Project key, deleted token count.

**Example**:
```
[HKEP12] Orphaned tokens cleaned
  projectKey: admin_default
  deletedLogTokens: 1200
  deletedCommandTokens: 300
```

---

#### HKEP13 - Log Retention Completed
**What**: Log retention cleanup finished.

**When**: After all log cleanup operations complete.

**Why**: Confirms job completion. Used for job duration tracking.

**Context**: Project key, execution time, items deleted.

---

### Artifact Retention

#### HKEP20 - Artifact Retention Check Started
**What**: Started artifact retention cleanup scan.

**When**: At the beginning of `AttachmentRetentionWorker` execution.

**Why**: Tracks artifact cleanup job execution.

**Context**: Project key, retention policy (keepAttachments days).

---

#### HKEP21 - Artifacts Deleted from Database
**What**: Artifact metadata deleted from database.

**When**: After `delete_old_attachments()` database function completes.

**Why**: Tracks database cleanup. First step before physical file deletion.

**Context**: Project key, deleted artifact count.

---

#### HKEP22 - Physical Artifact Files Deleted
**What**: Physical artifact files deleted from MinIO/S3.

**When**: After `DeletePhysicalFileAsync()` completes for all artifacts.

**Why**: Confirms physical cleanup. Used for storage reclamation tracking.

**Context**: Project key, deleted file count, freed storage space.

**Example**:
```
[HKEP22] Physical files deleted
  projectKey: admin_default
  deletedFiles: 350
  freedSpace: 2.4GB
```

---

#### HKEP23 - Artifact Retention Completed
**What**: Artifact retention cleanup finished.

**When**: After all artifact cleanup operations complete.

**Why**: Confirms job completion. Used for job duration tracking.

**Context**: Project key, execution time, items deleted, freed space.

---

### Audit Retention

#### HKEP30 - Audit Retention Check Started
**What**: Started audit log retention cleanup scan.

**When**: At the beginning of `AuditRetentionWorker` execution.

**Why**: Tracks audit cleanup job execution.

**Context**: Project key, retention policy (keepAudit days).

---

#### HKEP31 - Audit Entries Deleted
**What**: Audit log entries deleted during retention cleanup.

**When**: After `delete_old_audit_entries()` database function completes.

**Why**: Tracks audit cleanup volume. Prevents unbounded audit table growth.

**Context**: Project key, deleted audit count, cutoff date.

**Example**:
```
[HKEP31] Audit entries deleted
  projectKey: admin_default
  deletedCount: 5000
  cutoffDate: 2024-10-01
```

---

#### HKEP32 - Audit Retention Completed
**What**: Audit retention cleanup finished.

**When**: After audit cleanup operations complete.

**Why**: Confirms job completion. Used for job duration tracking.

**Context**: Project key, execution time, items deleted.

---

## Database Operations (DB01-DB99)

Database operations track schema migrations, connection management, and transactions.

### Migrations (Using DbUp)

#### DB01 - Migration Started
**What**: Started database migration process using DbUp.

**When**: At the beginning of `DbUpMigrations.ApplyAsync()` execution during application startup.

**Why**: Marks the beginning of the migration workflow. Critical for deployment monitoring and startup tracking.

**Context**: `connectionString` (sanitized - password masked as "***").

**Example**:
```
[DB01] Database migration started connection=Host=localhost;Database=playwrightgrid;Username=***;Password=***
```

---

#### DB02 - Database Connection Tested
**What**: Testing database connection before applying migrations.

**When**: After migration starts but before upgrade check.

**Why**: Verifies database connectivity before attempting schema changes. Early failure detection prevents partial migrations.

**Context**: None (connection test in progress).

**Example**:
```
[DB02] Testing database connection
```

---

#### DB03 - Database Ready
**What**: Database connection test succeeded.

**When**: After successful `NpgsqlConnection.Open()` during connection test.

**Why**: Confirms database is reachable and ready for migrations. Separates connectivity from migration logic.

**Context**: None (connection verified).

**Example**:
```
[DB03] Database connection successful
```

---

#### DB04 - Migration Upgrade Check Started
**What**: Started checking if database schema upgrade is required.

**When**: After DbUp upgrader is built but before checking `IsUpgradeRequired()`.

**Why**: Marks beginning of migration detection phase. Used to measure upgrade check latency.

**Context**: None (checking for pending migrations).

**Example**:
```
[DB04] Checking for pending migrations
```

---

#### DB05 - Migration Upgrade Not Required
**What**: Database schema is up to date, no migrations needed.

**When**: When `upgrader.IsUpgradeRequired()` returns `false` (no pending scripts).

**Why**: Confirms database is current. Common case (99% of startups). Used to track "no-op" startups.

**Context**: None (no migrations to apply).

**Example**:
```
[DB05] Database is up to date, no migrations needed
```

---

#### DB06 - Migration Upgrade Required
**What**: Database schema is outdated, migrations detected.

**When**: When `upgrader.IsUpgradeRequired()` returns `true` (pending scripts found).

**Why**: Indicates schema changes will be applied. Alerts operators to schema evolution.

**Context**: None (will log script count in DB07).

**Example**:
```
[DB06] Migrations detected, upgrade required
```

---

#### DB07 - Migration Scripts Discovered
**What**: Migration scripts found and queued for execution.

**When**: After `upgrader.GetScriptsToExecute()` completes.

**Why**: Documents how many migrations will run. Used to estimate upgrade duration.

**Context**: `scriptCount` (number of pending migration scripts).

**Example**:
```
[DB07] Discovered 3 migration script(s) to execute
```

---

#### DB08 - Migration Script Starting
**What**: Individual migration script starting execution.

**When**: When DbUp begins executing a specific migration script (reserved for future use).

**Why**: Tracks individual script execution start. Currently unused (DB09 logs actual execution).

**Context**: `scriptName` (e.g., "V45__add_event_codes.sql").

---

#### DB09 - Migration Script Executing
**What**: Individual migration script currently executing.

**When**: When DbUp's `IUpgradeLog.LogInformation()` receives "Executing Database Server script" message.

**Why**: Documents which script is running. Critical for identifying slow migrations or failures.

**Context**: `scriptName` (e.g., "V45__add_event_codes.sql").

**Example**:
```
[DB09] Executing migration script: V45__add_event_codes.sql
```

---

#### DB10 - Migration Script Completed
**What**: Individual migration script successfully executed.

**When**: After `upgrader.PerformUpgrade()` completes and script is in `result.Scripts` list.

**Why**: Confirms successful script execution. Used to track which migrations were applied.

**Context**: `scriptName` (e.g., "V45__add_event_codes.sql").

**Example**:
```
[DB10] Applied migration: V45__add_event_codes.sql
```

---

#### DB11 - Migration Transaction Started
**What**: Database transaction started for migration execution.

**When**: When DbUp's `IUpgradeLog.LogInformation()` receives "Beginning transaction" message (or explicit log before `PerformUpgrade()`).

**Why**: Marks transactional boundary. All scripts run atomically within a single transaction.

**Context**: None (transaction started via DbUp `.WithTransaction()` option).

**Example**:
```
[DB11] Starting migration transaction
```

or (from DbUp internal logging):
```
[DB11] Beginning migration transaction
```

---

#### DB12 - Migration Transaction Committed
**What**: Migration transaction committed successfully.

**When**: After `upgrader.PerformUpgrade()` succeeds and transaction commits (or when DbUp logs "Committing transaction").

**Why**: Confirms all migrations applied atomically. Critical for schema consistency.

**Context**: None (transaction committed successfully).

**Example**:
```
[DB12] Migration transaction committed successfully
```

or (from DbUp internal logging):
```
[DB12] Committing migration transaction
```

---

#### DB13 - Migration Transaction Rolled Back
**What**: Migration transaction rolled back due to error.

**When**: After `upgrader.PerformUpgrade()` fails and transaction rolls back automatically.

**Why**: Indicates migration failure and automatic rollback. No partial schema changes applied.

**Context**: None (automatic rollback on failure).

**Example**:
```
[DB13] Rolling back migration transaction
```

---

#### DB14 - Migration Verification Started
**What**: Started verifying migration completion.

**When**: After successful migration but before confirming no pending migrations remain.

**Why**: Double-check verification step. Ensures migrations actually applied correctly.

**Context**: None (verification in progress).

**Example**:
```
[DB14] Verifying migration completion
```

---

#### DB15 - Migration Verification Completed
**What**: Verification passed - database is current.

**When**: After second `upgrader.IsUpgradeRequired()` call returns `false` post-migration.

**Why**: Confirms migration success with verification. Strong guarantee of schema consistency.

**Context**: None (verification passed).

**Example**:
```
[DB15] Migration verification passed
```

---

#### DB16 - Migration Version Journal Updated
**What**: DbUp's version journal (schema_migrations table) updated.

**When**: After successful migration - DbUp tracks applied scripts in `schemaversions` table.

**Why**: Documents that version history is current. Used for audit trail.

**Context**: None (journal updated automatically by DbUp).

**Example**:
```
[DB16] Version journal updated successfully
```

---

#### DB17 - Migration Completed Successfully
**What**: Database migration process completed successfully.

**When**: After all migrations applied, verified, and journal updated.

**Why**: Marks successful completion of entire migration workflow. Used to track deployment success.

**Context**: `scriptCount` (number of scripts applied).

**Example**:
```
[DB17] Database migration completed successfully, applied 3 script(s)
```

---

#### DB18 - Migration Connection Failed
**What**: Failed to connect to database during migration.

**When**: When connection test fails with `NpgsqlException` during startup.

**Why**: Alerts to database connectivity issues. Critical for deployment failures.

**Context**: `error` (connection error message - e.g., "Connection refused", "Authentication failed").

**Example**:
```
[DB18] Database connection failed error=Connection refused
```

---

#### DB19 - Migration Failed
**What**: Migration process failed unexpectedly.

**When**: When `upgrader.PerformUpgrade()` returns `result.Successful == false` or general exception occurs.

**Why**: Alerts to migration failures. Critical for deployment rollback decisions.

**Context**: `error` (error message from DbUp or exception).

**Example**:
```
[DB19] Migration failed error=Syntax error at line 42: unexpected token
```

---

### Migration Lifecycle Flow

**Successful Migration Flow**:
```
DB01 (Started)
  ↓
DB02 (Connection Tested)
  ↓
DB03 (Database Ready)
  ↓
DB04 (Upgrade Check Started)
  ↓
DB06 (Upgrade Required) ← or DB05 (No Upgrade Needed) → END
  ↓
DB07 (Scripts Discovered: 3 scripts)
  ↓
DB11 (Transaction Started)
  ↓
DB09 (Executing: V45__add_event_codes.sql)
  ↓
DB10 (Applied: V45__add_event_codes.sql)
  ↓
DB09 (Executing: V46__add_retry_fields.sql)
  ↓
DB10 (Applied: V46__add_retry_fields.sql)
  ↓
... (repeat for each script)
  ↓
DB12 (Transaction Committed)
  ↓
DB14 (Verification Started)
  ↓
DB15 (Verification Completed)
  ↓
DB16 (Journal Updated)
  ↓
DB17 (Completed Successfully: 3 scripts)
```

**Failed Migration Flow**:
```
DB01 (Started)
  ↓
DB02 (Connection Tested)
  ↓
DB18 (Connection Failed: Connection refused) → END (throws exception)
```

or:

```
DB01 (Started)
  ↓
... (normal flow until script execution)
  ↓
DB09 (Executing: V46__bad_migration.sql)
  ↓
DB19 (Migration Failed: Syntax error)
  ↓
DB13 (Transaction Rolled Back)
  ↓
END (throws exception)
```

---

### Connection Management

#### DB30 - Database Connection Opened
**What**: Opened database connection from pool.

**When**: After `NpgsqlConnection.OpenAsync()` completes (general operations, not migration-specific).

**Why**: Tracks connection usage. Used for connection pool monitoring.

**Context**: Connection ID, connection string (sanitized), open time.

---

#### DB31 - Database Connection Closed
**What**: Closed database connection, returned to pool.

**When**: After connection disposed or explicitly closed.

**Why**: Tracks connection lifecycle. Used for connection leak detection.

**Context**: Connection ID, lifetime duration.

---

#### DB32 - Connection Pool Exhausted
**What**: No available connections in pool (all in use).

**When**: When connection request waits due to pool exhaustion.

**Why**: Alerts to connection pool issues. Critical for performance.

**Context**: Pool size, max pool size, waiting requests.

**Example**:
```
[DB32] Connection pool exhausted
  poolSize: 100/100
  waitingRequests: 15
```

---

#### DB33 - Connection Retry Attempted
**What**: Retrying database connection after failure.

**When**: When connection attempt fails and retry logic triggered.

**Why**: Tracks connection resilience. Helps identify network issues.

**Context**: Retry count, error message, backoff delay.

---

### Transactions (General)

#### DB40 - Database Transaction Started
**What**: Started database transaction (general operations, not migration-specific).

**When**: After `BeginTransactionAsync()` called in application code.

**Why**: Tracks transaction usage. Used for transaction duration monitoring.

**Context**: Transaction ID, isolation level.

---

#### DB41 - Database Transaction Committed
**What**: Transaction committed successfully (general operations).

**When**: After `CommitAsync()` completes in application code.

**Why**: Confirms transaction succeeded. Used for transaction success rate.

**Context**: Transaction ID, duration, rows affected.

**Example**:
```
[DB41] Database transaction committed
  transactionId: tx-123
  duration: 45ms
  rowsAffected: 25
```

---

#### DB42 - Database Transaction Rolled Back
**What**: Transaction rolled back (aborted) in general operations.

**When**: After `RollbackAsync()` called (due to error or explicit rollback) in application code.

**Why**: Tracks transaction failures. Helps identify data integrity issues.

**Context**: Transaction ID, rollback reason, error message.

**Example**:
```
[DB42] Database transaction rolled back
  transactionId: tx-123
  reason: Unique constraint violation
  error: Duplicate key value violates unique constraint "pk_test_items"
```

---

## Redis Operations (RDS01-RDS99)

Redis operations track connection lifecycle, key-value operations, set management, and transactions.

### Connection & Lifecycle

#### RDS01 - Redis Client Initialized
**What**: Redis client (manager) has been initialized.

**When**: In the constructor of a Redis-based manager (e.g., `PidRedisManager`).

**Why**: Confirms that the component is ready to handle Redis operations.

**Context**: `workerId`, `endpoint` (sanitized).

**Example**:
```
[RDS01] Redis client initialized for worker worker-1
```

---

#### RDS02 - Redis Connection Connected
**What**: Successful connection to Redis server.

**When**: After `ConnectionMultiplexer.Connect()` succeeds.

**Why**: Indicates the system is connected to its state store.

**Context**: `endpoint`.

---

#### RDS04 - Redis Connection Error
**What**: Error during Redis operation or connection.

**When**: Inside catch blocks for Redis-related exceptions.

**Why**: Critical for monitoring state store health.

**Context**: `operation`, `error`.

---

### Key-Value & Set Operations

#### RDS10 - Redis Key Operation Success
**What**: A key-value operation (Get/Set/Delete) completed successfully.

**When**: After successful execution of string or key commands.

**Why**: Documents state changes.

**Context**: `key`, `operation` (GET/SET/DEL), `ttl` (if applicable).

---

#### RDS20 - Redis Set Operation Success
**What**: A set operation (Add/Remove/Members) completed successfully.

**When**: After successful execution of set commands.

**Why**: Tracks collection-based state (e.g., list of PIDs).

**Context**: `key`, `operation` (SADD/SREM/SMEMBERS), `memberCount`.

---

### Transactions & Bulk Operations

#### RDS30 - Redis Transaction Started
**What**: A Redis transaction block has been created.

**When**: When `IDatabase.CreateTransaction()` is called.

**Why**: Marks the beginning of an atomic multi-key update.

---

#### RDS31 - Redis Transaction Committed
**What**: A Redis transaction was successfully executed.

**When**: When `ITransaction.ExecuteAsync()` returns `true`.

**Why**: Confirms atomic update success.

---

#### RDS32 - Redis Transaction Failed
**What**: A Redis transaction failed to execute (e.g., due to optimistic locking failure).

**When**: When `ITransaction.ExecuteAsync()` returns `false`.

**Why**: Indicates a race condition or conflict in state updates.

---

### Worker Specific Operations

#### RDS50 - Redis Heartbeat Sent
**What**: Worker heartbeat successfully updated in Redis.

**When**: After successful `StringSetAsync` for the heartbeat key.

**Why**: Confirms worker is reporting its liveness correctly.

**Context**: `workerId`, `timestamp`.

---

## Background Services / Orphan Detection (ORP01-ORP99)

Background services monitor system health, perform leader election for distributed tasks, and clean up orphaned resources (like abandoned browser processes or stale worker keys).

### Leader Election

#### ORP01 - Leader Election Started
**What**: Background service is attempting to become the leader for a specific task.

**When**: At the start of the leader election cycle.

**Why**: Tracks leadership contention and activity.

**Context**: `taskName`, `nodeId`.

---

#### ORP02 - Leader Lock Acquired
**What**: Node successfully acquired the leader lock and is now the active leader.

**When**: After successfully acquiring a distributed lock (e.g., in Redis).

**Why**: Confirms which node is responsible for background tasks.

**Context**: `taskName`, `nodeId`, `lockDuration`.

---

#### ORP03 - Leader Lock Renewed
**What**: Active leader successfully renewed its lock.

**When**: During the periodic heartbeat/renewal cycle of the leader.

**Why**: Confirms continuous leadership and prevents lock expiration.

**Context**: `taskName`, `nodeId`.

---

#### ORP04 - Leader Lock Released
**What**: Node explicitly released the leader lock.

**When**: During graceful shutdown or when task completes.

**Why**: Allows other nodes to quickly take over leadership.

**Context**: `taskName`, `nodeId`.

---

### Scanning and Detection

#### ORP10 - Scanning Started
**What**: Started scanning for orphaned resources.

**When**: At the beginning of a detection cycle.

**Why**: Tracks scan frequency and duration.

**Context**: `scanTarget` (e.g., "PIDs", "WorkerKeys").

---

#### ORP11 - Scanning Heartbeat Expired
**What**: Detected a resource with an expired heartbeat.

**When**: During scan, when a heartbeat timestamp is older than the threshold.

**Why**: Identifies potentially orphaned resources.

**Context**: `resourceId`, `lastHeartbeat`, `threshold`.

---

#### ORP12 - Scanning Orphaned Pids Found
**What**: Found browser processes (PIDs) that no longer have an active session.

**When**: During orphan PID scan.

**Why**: Identifies resources that need cleanup to reclaim system capacity.

**Context**: `nodeId`, `pidCount`, `pids`.

---

#### ORP13 - Scanning Complete
**What**: Resource scan finished.

**When**: After all detection logic for the cycle completes.

**Why**: Tracks scan performance.

**Context**: `scanTarget`, `duration`, `foundCount`.

---

### Cleanup Operations

#### ORP20 - Pid Cleanup Started
**What**: Started cleaning up orphaned browser processes.

**When**: Before attempting to kill orphaned PIDs.

**Why**: Tracks cleanup activity.

**Context**: `pidCount`.

---

#### ORP21 - Pid Cleaned
**What**: Successfully killed an orphaned browser process.

**When**: After a PID is successfully terminated.

**Why**: Audit trail for resource reclamation.

**Context**: `pid`, `nodeId`.

---

#### ORP22 - Pid Cleanup Failed
**What**: Failed to kill an orphaned browser process.

**When**: When a termination attempt results in an error.

**Why**: Identifies stuck processes or permission issues.

**Context**: `pid`, `error`.

---

#### ORP23 - Worker Keys Cleanup Started
**What**: Started cleaning up stale worker registration keys from Redis.

**When**: Before removing old worker metadata.

**Why**: Tracks worker registry maintenance.

**Context**: `staleCount`.

---

#### ORP24 - Worker Keys Cleaned
**What**: Stale worker keys removed from the registry.

**When**: After successful deletion of stale keys.

**Why**: Confirms registry cleanup.

**Context**: `removedKeysCount`.

---

### Errors and Failures

#### ORP30 - Detect Failed
**What**: Orphan detection process failed.

**When**: When an unhandled exception occurs during the scan/detect phase.

**Why**: Critical for monitoring background service health.

**Context**: `error`.

---

#### ORP31 - Leader Lock Failed
**What**: Failed to acquire or renew leader lock due to an error.

**When**: When lock operation (Redis/DB) fails.

**Why**: Indicates issues with the distributed coordination layer.

**Context**: `taskName`, `error`.

---

## Usage Patterns

### Filtering Logs by Event Code

Event codes make it easy to filter logs for specific operations:

```bash
# Find all browser borrow operations
grep "POOL01" /var/log/hub.log

# Find all failed operations (error event codes)
grep -E "(POOL04|POOL23|LCH04|ING04|WRK04|STG03)" /var/log/*.log

# Find all cleanup operations
grep -E "(POOL20|POOL21|POOL22)" /var/log/hub.log

# Find all database migrations
grep -E "(DB01|DB02|DB03)" /var/log/hub.log
```

### Setting Up Alerts

Use event codes to configure alerts for critical events:

**Prometheus Alert Rules**:
```yaml
groups:
  - name: browser_pool_alerts
    rules:
      # Alert when browser borrow failures exceed threshold
      - alert: HighBrowserBorrowFailureRate
        expr: rate(log_events{event_code="POOL04"}[5m]) > 0.1
        annotations:
          summary: "High browser borrow failure rate detected"

      # Alert when cleanup failures occur
      - alert: BrowserCleanupFailures
        expr: rate(log_events{event_code="POOL23"}[5m]) > 0
        annotations:
          summary: "Browser cleanup failures detected"
```

**Log Monitoring (e.g., Grafana Loki)**:
```logql
# Count browser borrow failures per minute
sum(rate({job="hub"} |= "POOL04" [1m]))

# Count cleanup operations per hour
count_over_time({job="hub"} |= "POOL20" [1h])
```

### Common Troubleshooting Scenarios

#### Scenario 1: Tests Failing to Start (No Browser Capacity)

**Symptoms**: Tests queued but never start.

**Event Codes to Search**:
- `POOL01` (BorrowRequested) - Confirms borrow requested
- `POOL04` (BorrowFailed) - Indicates no capacity available

**Troubleshooting Steps**:
1. Check logs for `POOL04` events
2. Verify worker registration: search for `WRK22` (RegistrationConfirmed)
3. Check pool diagnostics: `GET /diagnostics`
4. Verify workers healthy: search for `WRK32` (HealthCheckFailed)

---

#### Scenario 2: Slow Test Execution

**Symptoms**: Tests take longer than expected.

**Event Codes to Search**:
- `POOL01` → `POOL02` duration - Measures browser allocation time
- `ITEM01` → `ITEM04` duration - Measures test execution time
- `WRK01` → `WRK03` duration - Measures browser startup time

**Troubleshooting Steps**:
1. Calculate time between `POOL01` and `POOL02` - if >5s, pool contention
2. Calculate time between `WRK01` and `WRK03` - if >10s, browser startup slow
3. Check for `DB12` (ConnectionPoolExhausted) - database bottleneck

---

#### Scenario 3: Data Not Appearing in Dashboard

**Symptoms**: Tests complete but results don't show in UI.

**Event Codes to Search**:
- `ITEM02` (ItemPersisted) - Confirms test item written to database
- `ING03` (BatchCompleted) - Confirms ingestion succeeded
- `ING13` (WriteFailed) - Indicates ingestion failure

**Troubleshooting Steps**:
1. Search for `ITEM02` with test item ID - confirms direct write
2. If not found, check ingestion: search for `ING01` (BatchReceived)
3. If batch received but not completed, search for `ING13` (WriteFailed)
4. Check database connectivity and ingestion service logs

---

#### Scenario 4: Storage Full or Artifacts Missing

**Symptoms**: Artifact uploads fail or artifacts not accessible.

**Event Codes to Search**:
- `STG01` → `STG02` - Successful upload flow
- `STG03` (UploadFailed) - Upload failures
- `HKEP22` (PhysicalFilesDeleted) - Retention cleanup activity

**Troubleshooting Steps**:
1. Search for `STG03` - identifies upload failures
2. Check MinIO connectivity and disk space
3. Search for `HKEP22` - verify retention cleanup not too aggressive
4. Check artifact retention settings: `GET /admin/projects/{key}/settings`

---

## Event Publisher Operations (EVT01-EVT99)

Event publisher operations track the asynchronous publishing of events to the message broker (RabbitMQ).

### Event Publishing

#### EVT01 - Test Item Published
**What**: A test item event has been queued for publishing.

**When**: Logged before spawning the background task in `PublishTestItemEventAsync`.

**Why**: Confirms the request to publish a test item event was received and processed by the publisher.

**Context**: `itemId`, `launchId`, `eventType`.

#### EVT02 - Command Published
**What**: A command event has been queued for publishing.

**When**: Logged before spawning the background task in `PublishCommandEventAsync`.

**Why**: Confirms the request to publish a command event.

**Context**: `eventType`, `runId`.

#### EVT03 - Log Item Published
**What**: A log item event has been queued for publishing.

**When**: Logged before spawning the background task in `PublishLogItemEventAsync`.

**Why**: Confirms the request to publish a log item event.

**Context**: `launchId`, `level`, `metadataSize`.

#### EVT04 - Audit Published
**What**: An audit event has been queued for publishing.

**When**: Logged before spawning the background task in `PublishAuditEventAsync`.

**Why**: Confirms the request to publish an audit event.

**Context**: `action`, `actor`.

#### EVT05 - Artifact Published
**What**: An artifact upload event has been queued for publishing.

**When**: Logged before spawning the background task in `PublishArtifactUploadEventAsync`.

**Why**: Confirms the request to publish an artifact upload event.

**Context**: `artifactId`, `fileName`, `fileSize`.

#### EVT09 - Message Size Logged
**What**: The size of the serialized event message.

**When**: Logged during the publishing operation.

**Why**: Used for monitoring network traffic and identifying unexpectedly large messages.

**Context**: `messageSize`.

### Status and Failures

#### EVT10 - Publish Failed
**What**: Failed to publish an event to the message broker.

**When**: Caught in the background task exception handler.

**Why**: Critical for identifying broken event delivery.

**Context**: `error`, and event-specific IDs.

#### EVT11 - Connection Lost
**What**: Lost connection to the message broker during a publish operation.

**When**: Caught in `PublishEventAsync`.

**Why**: Identifies infrastructure-level connectivity issues.

**Context**: `error`, `queue`.

#### EVT25 - Publish Confirmed
**What**: The message broker has confirmed receipt of the published event.

**When**: Successfully completed `BasicPublish` in `PublishEventAsync`.

**Why**: Confirms end-to-end delivery to the broker.

**Context**: `queue`, `correlationId`.

### Infrastructure

#### EVT20 - Channel Created
**What**: A new RabbitMQ channel has been created.

**When**: In `RabbitMqEventPublisher` constructor.

**Why**: Tracks publisher initialization.

**Context**: `uri`, `connectionName`.

#### EVT21 - Channel Closed
**What**: The RabbitMQ channel has been closed.

**When**: In `Dispose`.

**Why**: Tracks publisher shutdown.

**Context**: `connectionClosed`.

#### EVT22 - Exchange Declared
**What**: A RabbitMQ exchange has been declared.

**When**: In `DeclareInfrastructure`.

**Why**: Tracks infrastructure setup.

**Context**: `exchange`, `type`, `durable`.

#### EVT23 - Queue Declared
**What**: A RabbitMQ queue has been declared.

**When**: In `DeclareInfrastructure`.

**Why**: Tracks infrastructure setup.

**Context**: `queue`.

#### EVT24 - Additional Queue Declared
**What**: An additional RabbitMQ queue has been declared.

**When**: In `DeclareInfrastructure`.

**Why**: Tracks infrastructure setup for non-standard queues.

**Context**: `queue`.

---

## Node Sweeper Operations (NSR01-NSR99)

Node sweeper operations manage the lifecycle of worker nodes, including health monitoring, quarantine, and expiration of stale nodes.

### Scanning and Lifecycle

#### NSR10 - Scanning Started
**What**: The leader node has started scanning worker nodes in Redis.

**When**: At the beginning of `NodeSweeperService.ExecuteAsync()`.

**Context**: `leaderId`.

#### NSR14 - Node Expired
**What**: A node has been identified as expired (not seen for a long time).

**When**: When a node's `LastSeen` timestamp exceeds the expiration threshold.

#### NSR15 - Node Quarantined
**What**: A node has been placed in quarantine due to missing heartbeats.

**When**: When a node is still relatively recent but hasn't sent a heartbeat within the grace period.

#### NSR40 - Scanning Completed
**What**: The scanning process has finished successfully.

**Context**: `nodesProcessed`, `nodesExpired`, `nodesQuarantined`.

---

## Borrow TTL Sweeper Operations (BRT01-BRT99)

The Borrow TTL Sweeper automatically returns browsers to the pool if their allocated time (TTL) has expired or if they have been idle for too long.

### Scanning and TTL Checks

#### BRT01 - Scan Started
**What**: The sweeper has started a new sweep cycle.

**When**: At the beginning of `BorrowTtlSweeperService.ExecuteAsync()` loop.

**Context**: `leader` (status of leader election), `lease` (seconds).

#### BRT11 - TTL Expired
**What**: A browser session has been identified as expired.

**When**: When neither the borrow lease nor the idle lease exists in Redis for a session.

**Context**: `browserId`.

#### BRT31 - Browser Returned
**What**: The browser has been successfully returned to the pool by the sweeper.

**When**: After the Lua script successfully moves the browser from 'in-use' to 'available' list.

**Context**: `browserId`, `labelKey`, `returnedCount`.

#### BRT50 - Run AutoStopped
**What**: A test run has been marked as AutoStopped because all its browsers were returned by the sweeper.

**When**: After returning a browser, if no other browsers are outstanding for that run.

**Context**: `runId`.

#### BRT02 - Scan Completed
**What**: The sweep cycle has finished.

**When**: At the end of the `ExecuteAsync` loop iteration.

**Context**: `processed`, `returned`, `errors`, `durationMs`.

---

## Browser Auto-Stop Operations (BST01-BST99)

The Browser Auto-Stop service monitors active test items and automatically stops those that have exceeded their maximum duration or have been inactive for too long.

### Scanning and Selection

#### BST01 - Scan Started
**What**: The auto-stop service has started a new monitoring cycle.

**When**: At the beginning of `BrowserAutoStopService.ExecuteAsync()`.

**Context**: `interval`, `inactivity`, `max`, `batch`.

#### BST02 - Active Items Retrieved
**What**: A list of candidate test items has been retrieved from the results store.

**When**: After querying the database for active tests/scenarios.

**Context**: `count`.

### Execution

#### BST22 - Browser Returned
**What**: A browser associated with a timed-out test item has been returned to the pool.

**When**: During the auto-stop execution for a specific test item.

**Context**: `browserId`, `itemId`.

#### BST25 - Run Status Updated
**What**: The overall run status has been updated to 'AutoStopped'.

**When**: When all items in a run have been processed and the run itself is now considered stopped.

**Context**: `runId`, `status`.

#### BST40 - Tick Completed
**What**: The auto-stop monitoring cycle has finished.

**When**: At the end of the `ExecuteAsync` loop iteration.

**Context**: `scanned`, `processed`, `released`, `errors`, `durationMs`.

---

## Browser Health Operations (BHC01-BHC99)

Browser health operations track the periodic health checks of sidecar Playwright browsers, ensuring they remain responsive and ready for client connections.

### Health Check Loop

#### BHC01 - Browser Health Check Loop Started
**What**: The background health checker has started a new monitoring cycle.

**When**: At the beginning of `BrowserHealthChecker.CheckAllBrowsersAsync()`.

**Context**: `nodeId`, `interval`, `timeout`, `threshold`.

#### BHC02 - Browser Health Check Loop Completed
**What**: The health check cycle has finished scanning all available browsers.

**When**: At the end of `BrowserHealthChecker.CheckAllBrowsersAsync()`.

**Context**: `checked`, `healthy`, `unhealthy`, `recycled`.

#### BHC03 - Browser Health Check Loop Error
**What**: An unexpected error occurred during the health check loop.

**When**: Caught in the main loop of `BrowserHealthChecker.ExecuteAsync()`.

**Context**: `nodeId`, `message`.

### Individual Health Checks

#### BHC10 - Browser Health Check Started
**What**: A health check has been initiated for a specific browser.

**When**: At the start of `BrowserHealthChecker.CheckBrowserHealthAsync()`.

**Context**: `browserId`, `wsEndpoint`, `timeout`.

#### BHC11 - Browser Health Check Passed
**What**: The browser responded successfully to a lightweight protocol command.

**When**: After a successful `Browser.version` call.

**Context**: `browserId`, `elapsedMs`.

#### BHC12 - Browser Health Check Failed
**What**: The browser failed to respond or returned an invalid response.

**When**: When the protocol command fails or times out.

**Context**: `browserId`, `elapsedMs`, `failures`, `threshold`.

#### BHC13 - Browser Health Check Exception
**What**: An unexpected exception occurred during the browser health check.

**When**: Caught in `BrowserHealthChecker.CheckBrowserHealthAsync()`.

**Context**: `browserId`, `message`, `elapsedMs`.

### Remediation

#### BHC20 - Browser Recycle Triggered
**What**: A browser has exceeded the failure threshold and is being marked for recycling.

**When**: After `failureThreshold` consecutive failures.

**Context**: `browserId`, `browserType`, `labelKey`, `failures`.

#### BHC21 - Browser Recycle Trigger Failed
**What**: Failed to mark a browser for recycling in the results store.

**When**: When the Redis operation to set the recycle flag fails.

**Context**: `browserId`, `message`.

---

## Admin, Projects & Users Operations (ADM01-ADM99)

Administrative operations track user lifecycle, project management, and authentication activities within the grid control plane.

### Authentication & Authorization
- **ADM01 - Login Attempt**: A user tried to authenticate via the dashboard or API.
- **ADM02 - Login Succeeded**: User successfully authenticated.
- **ADM03 - Login Failed**: Invalid credentials or other authentication error.
- **ADM04 - Rate Limit Exceeded**: Too many failed login attempts detected.

### User & Project Management
- **ADM21 - User Created**: A new administrative user was added to the system.
- **ADM31 - Project Created**: A new project was created for organizing test runs.
- **ADM41 - Membership Added**: A user was granted access to a project.

---

## Artifacts Operations (ART01-ART99)

Artifacts operations track high-level management of files (screenshots, traces, logs) associated with test items.

- **ART01 - Artifact Uploaded**: High-level event for successful artifact ingestion.
- **ART02 - Upload Started**: Beginning of the artifact transfer process.
- **ART04 - Upload Failed**: Artifact could not be saved to storage.
- **ART50 - Cache Hit**: Artifact was served from the Redis cache.
- **ART51 - Cache Miss**: Artifact had to be retrieved from primary storage.

---

## Project Settings Operations (PRJ01-PRJ99)

Project settings track configuration changes for individual projects, such as retention policies.

- **PRJ01 - Settings Retrieved**: Project-specific configuration was loaded.
- **PRJ03 - Settings Updated**: Project configuration was modified by an admin.
- **PRJ20 - Settings Persisted**: Changes were successfully saved to the database.

---

## Password Reset Operations (PWD01-PWD99)

- **PWD01 - Reset Requested**: A user initiated a password reset flow.
- **PWD10 - Token Generated**: A secure, short-lived reset token was created.
- **PWD20 - Reset Completed**: The user successfully updated their password.

---

## System Operations (SYS01-SYS99)

System operations track the overall bootstrap process, initialization of default data, and critical system-level failures across all grid components.

### Bootstrap Flow

#### SYS01 - System Bootstrap Started
**What**: The application host has started and is beginning its initialization sequence.

**When**: At the very beginning of `HubServiceRunner.RunAsync()` or `WorkerServiceRunner.RunAsync()`.

**Why**: Marks the start of the service lifecycle. Used to track service restarts and total uptime.

**Context**: Service name, version.

---

#### SYS02 - System Bootstrap Completed
**What**: Initialization sequence finished, all core services are ready, and the web host is starting.

**When**: After all startup tasks (database migrations, DI setup, pool warming, initial registration) are complete.

**Why**: Confirms the service is fully operational and ready to accept traffic.

**Context**: Startup duration.

---

#### SYS03 - System Bootstrap Failed
**What**: A critical error occurred during the initialization sequence that prevents the service from starting.

**When**: When an unhandled exception occurs during bootstrap.

**Why**: Alerts to catastrophic startup failures. Used to identify misconfigurations or dependency issues.

**Context**: Error message, stack trace.

---

#### SYS10 - Bootstrap Admin User Created
**What**: The initial administrator user was created in the database because it was empty.

**When**: During database initialization if no users exist.

**Why**: Tracks the automatic provisioning of the first admin account.

**Context**: Username.

---

#### SYS11 - Bootstrap Default Project Created
**What**: The default "agenix" project was created because the database was empty.

**When**: During database initialization if no projects exist.

**Why**: Tracks the automatic provisioning of the initial project.

**Context**: Project name.

---

#### SYS12 - Bootstrap Membership Created
**What**: Link between the bootstrap admin user and the default project was created.

**When**: During database initialization.

**Context**: User, Project.

---

#### SYS13 - Bootstrap Settings Initialized
**What**: Default system settings were written to the database.

**When**: During database initialization.

**Context**: Settings version.

---

## Event Code Reference Summary

| Category | Code Range | Count | Examples |
|----------|------------|-------|----------|
| Browser Pool | POOL01-POOL99 | 16 | POOL01, POOL02, POOL20 |
| Launch | LCH01-LCH99 | 36 | LCH01, LCH03, LCH10, LCH40 |
| Test Item | ITEM01-ITEM99 | 12 | ITEM01, ITEM02, ITEM20 |
| Ingestion | ING01-ING99 | 11 | ING01, ING03, ING22 |
| Worker | WRK01-WRK99 | 17 | WRK01, WRK03, WRK22, WRK25 |
| Node Management | NOD01-NOD99 | 5 | NOD01, NOD02, NOD05 |
| Storage | STG01-STG99 | 11 | STG01, STG02, STG30 |
| Housekeeping | HKEP01-HKEP99 | 14 | HKEP01, HKEP02, HKEP11 |
| Database | DB01-DB99 | 26 | DB01, DB08, DB17, DB30 |
| Background Services | ORP01-ORP99 | 15 | ORP01, ORP10, ORP21, ORP30 |
| Node Sweeper | NSR01-NSR99 | 17 | NSR01, NSR10, NSR40 |
| Event Publisher | EVT01-EVT99 | 18 | EVT01, EVT10, EVT25 |
| Borrow TTL Sweeper | BRT01-BRT99 | 16 | BRT01, BRT11, BRT31, BRT50 |
| Browser Auto-Stop | BST01-BST99 | 17 | BST01, BST22, BST25, BST40 |
| Admin / Auth | ADM01-ADM99 | ~40 | ADM01, ADM21, ADM31 |
| Artifacts | ART01-ART99 | 16 | ART01, ART02, ART50 |
| Project Settings | PRJ01-PRJ99 | 13 | PRJ01, PRJ03, PRJ20 |
| Password Reset | PWD01-PWD99 | 12 | PWD01, PWD10, PWD20 |
| System | SYS01-SYS99 | 7 | SYS01, SYS02, SYS10 |

**Total Event Codes**: ~309

---

## Additional Resources

- **ChunkedLogger Implementation**: `Agenix.PlaywrightGrid.Shared/Logging/ChunkedLogger.cs`
- **Event Codes Class**: `Agenix.PlaywrightGrid.Shared/Logging/EventCodes.cs`
- **Integration Specifications**: `specs/chunked_logging/` folder
- **Usage Examples**: Search codebase for `_chunkedLogger.LogMilestone(EventCodes.`
