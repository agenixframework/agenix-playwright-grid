# Borrow TTL, Auto-Return, and Session Persistence

Generated: 2025-08-31 (local time)

This document describes how the Hub manages the lifetime of borrowed Playwright sessions using a lease/TTL, how timed-out sessions are auto-returned to the pool, and how session metadata is persisted in Redis to survive Hub restarts.

## Overview

- When a client borrows a browser via the Hub, the Hub now creates a short-lived lease and persists minimal session metadata in Redis.
- If the borrower does not return the session within the TTL, a background sweeper automatically returns the capacity to the pool and asks the Worker to recycle the browser instance.
- Persisted session metadata enables the Hub to resume management after restarts.

## Configuration

Environment variables (Hub):
- HUB_BORROW_TTL_SECONDS
  - Default: 900 (15 minutes). Minimum enforced: 60 seconds; maximum: 24 hours.
  - Sets the default lease time for a borrowed session.
- HUB_BORROW_TTL_SWEEP_SECONDS
  - Default: 10 seconds when not specified.
  - Controls the interval for the background sweeper that checks for expired leases and auto-returns.

Per-request override (borrow body):
- ttlSeconds (integer) can be supplied in the /session/borrow request body to override the default.
  - Enforced bounds: 60 <= ttlSeconds <= 86400.

## API Usage

Borrow endpoint:
- POST /session/borrow
- Headers: x-hub-secret: <HUB_RUNNER_SECRET>
- Body:
  - Required: labelKey (string), e.g., "AppB:Chromium:UAT"
  - Optional: ttlSeconds (int) to override default TTL
- Behavior:
  - On successful borrow, the response body is unchanged (contains browserId, webSocketEndpoint, etc.).
  - The Hub stores a lease key and a session record in Redis (see Redis keys below).

Return endpoint:
- POST /session/return
- Headers: x-hub-secret: <HUB_RUNNER_SECRET>
- Body: { labelKey: string, browserId: string }
- Behavior:
  - Returning is idempotent. Whether the item is found in the in-use list or already returned, the Hub cleans up the lease and session keys.
  - On client WebSocket disconnect, the Worker automatically issues POST /session/return (best-effort) to finalize the session and restore capacity so runs do not remain in the Running state.
  - When a runId correlation is available, the Hub will mark the run Failed if any test failures were recorded; otherwise Passed. A clean auto-return is equivalent to a Passed run; you may see an AutoReturn event in the log while the run status is Passed.

## Redis Keys and Lifecycle

The following keys are written and maintained by the Hub:
- available:{labelKey} (list): items ready to be borrowed.
- inuse:{labelKey} (list): items currently borrowed; each item is a JSON blob including browserId, nodeId, etc.
- session:{browserId} (hash): persisted metadata for a borrowed session.
  - Fields:
    - browserId: string
    - labelKey: string
    - runId: string
    - nodeId: string (may be empty)
    - borrowedAtUtc: ISO-8601 UTC timestamp
    - ttlSeconds: string-int value of the lease
- borrow_ttl:{browserId} (string): the lease key with an expiration equal to the TTL. Presence indicates the lease is still active.
- browser_run:{browserId} (string, TTL ~ 6h): lightweight mapping to attribute logs to a runId.
- browser_test:{browserId} (string, transient): optional mapping used by log forwarding.
- recycle:{browserId} (string, ~2 minutes): marker that asks the Worker to tear down the sidecar/browser and replenish capacity.

Lifecycle summary:
1. Borrow success → move one item from available: to inuse: atomically (Lua), set borrow_ttl and write session:{browserId}.
2. Client uses the browser (WS connection is between client and Worker).
3. Return path:
   - If client returns before TTL: Hub moves item back to available:, cleans lease/session keys, posts recycle marker, updates results/logs.
   - If client does not return and TTL expires: BorrowTtlSweeperService detects missing borrow_ttl, atomically moves the in-use item back to available:, cleans up session and mappings, and sets recycle marker.

## Auto-Return Sweeper

BorrowTtlSweeperService (Hosted Service):
- Runs periodically (HUB_BORROW_TTL_SWEEP_SECONDS; default 10s).
- Scans session:* keys. For each session:
  - If borrow_ttl:{browserId} exists → lease still active, skip.
  - If lease is missing → TTL expired:
    - Atomically move from inuse:{labelKey} to available:{labelKey} using the same return Lua script as the /session/return endpoint.
    - Clean up: browser_run:/browser_test:, borrow_ttl:, session: keys.
    - Emit a recycle:{browserId} marker for the Worker to refresh capacity.
- Logs one line per sweep with processed, returned, and error counts.

Notes:
- The sweeper tolerates Hub restarts: persisted session:{browserId} holds the context needed to return capacity even if the Hub was down when the TTL elapsed.
- Capacity queue signaling is not required for correctness; availability is restored and new borrows will see it. Future enhancements may add queue wake-ups from the sweeper.

## Operational Guidance and Troubleshooting

- Extending a lease mid-run: not currently supported via an API; borrowers should choose an appropriate ttlSeconds when borrowing.
- If you see sessions sticking in inuse: without a matching borrow_ttl:, the sweeper will clean them on its next pass.
- Use Prometheus metrics and Dashboard to observe borrow outcomes and pool utilization. TTL expirations currently do not have a dedicated metric, but can be inferred from logs and capacity changes.
- For long-running tests, increase HUB_BORROW_TTL_SECONDS or set ttlSeconds per request.
- Ensure clocks are reasonably in sync across Hub and Workers; the Hub uses UTC timestamps for session metadata and a separate alive TTL for worker liveness.

## Examples

Borrow with a custom TTL (20 minutes):

Request:

```
POST /session/borrow
x-hub-secret: runner-secret
Content-Type: application/json

{
  "labelKey": "AppB:Chromium:UAT",
  "ttlSeconds": 1200
}
```

Return:

```
POST /session/return
x-hub-secret: runner-secret
Content-Type: application/json

{
  "labelKey": "AppB:Chromium:UAT",
  "browserId": "b-123"
}
```

## Compatibility and Defaults

- Backward compatible: existing clients that do not send ttlSeconds inherit the default HUB_BORROW_TTL_SECONDS (15 minutes by default).
- Hub restarts: persisted session metadata allows the Hub to reconcile and auto-return expired sessions post-restart.

## Related Documents

- docs/tasks.md (Improvement #12)
- Configuration and deployment: see README.md and docker-compose.yml for environment wiring.
