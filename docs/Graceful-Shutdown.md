# Graceful Shutdown

This document explains how Playwright Grid (Hub and Worker) behaves during shutdown, how to configure it, and how to integrate it with container orchestrators for zero‑surprise rollouts.

Last updated: 2025-09-01

## Summary
- Hub stops accepting new borrows as soon as shutdown begins and reports not-ready on readiness checks.
- Worker denies new borrows, drains active client WebSocket sessions up to a configurable timeout, cleans up Redis state, and force-terminates sidecars only if sessions remain after the timeout.
- Both components surface clear readiness signals (HTTP 503) to allow load balancers/orchestrators to stop sending traffic before processes exit.

## Hub behavior
When ASP.NET Core triggers ApplicationStopping (e.g., SIGTERM), the Hub:
- Immediately stops accepting new borrow requests.
  - POST /session/borrow responds with 503 Service Unavailable.
  - Response includes `Retry-After: 30` header to hint clients to retry later.
- Readiness endpoint reflects shutdown:
  - GET /health/ready returns 503 so containers are removed from load balancers.
- Existing sessions are unaffected at Hub level; Hub is stateless for live WS proxying (the Worker owns the WebSocket lifecycle).

Relevant code paths:
- hub/Infrastructure/Web/EndpointMappingExtensions.cs
  - Internal `_acceptingBorrows` flag flips to false on `ApplicationStopping`.
  - `/session/borrow` returns 503 when not accepting borrows.
  - `/health/ready` returns 503 when not accepting borrows.

## Worker behavior
When the Worker receives shutdown (ApplicationStopping):
- Sets `_acceptingBorrows = false` to deny new borrows at `/borrow/{labelKey}` with 503 and `Retry-After: 30`.
- Begins graceful drain of active WebSocket sessions:
  - Waits up to `WORKER_DRAIN_TIMEOUT_SECONDS` (default 30s) for all active client WS connections to close.
  - During this period, no new borrows are accepted.
- After waiting:
  - Performs cleanup of Redis lists/keys for this node and removes itself from `nodes` set.
  - If any sessions are still active, logs a warning and force-kills remaining sidecar processes to ensure timely shutdown.
- Readiness reflects shutdown while draining:
  - GET `/health/ready` returns 503, signaling the orchestrator to stop routing new traffic.

Relevant code paths:
- worker/Services/WebServerHost.cs
  - Graceful drain, denying borrows, readiness 503 during shutdown.
- worker/Services/PoolManager.cs
  - Tracks active WS connections per browserId and exposes `HasAnyActiveConnections()` for drain logic.
  - Cleanup of Redis state and optional force-kill of sidecars.

## HTTP status codes and headers
- New borrows denied during shutdown:
  - Hub: POST `/session/borrow` → 503 Service Unavailable, `Retry-After: 30`.
  - Worker: POST `/borrow/{labelKey}` → 503 Service Unavailable, `Retry-After: 30`.
- Readiness while shutting down:
  - Hub: GET `/health/ready` → 503.
  - Worker: GET `/health/ready` → 503.

## Configuration
Environment variables impacting shutdown behavior:
- WORKER_DRAIN_TIMEOUT_SECONDS
  - Default: 30.
  - How long the Worker waits for all active WS sessions to close before force-killing sidecars.
- REDIS_* timeouts (Hub and Worker)
  - Control health ping timings; not shutdown-specific but influence `/health/ready` responsiveness.

Defaults and safety:
- If WORKER_DRAIN_TIMEOUT_SECONDS is not set or invalid, default 30s is used.
- If drain times out, sidecars are force-terminated; this prevents hung shutdowns on orchestrators with hard SIGKILL deadlines.

## Orchestrator integration

### Docker / docker-compose
- The built-in readiness endpoints and 503 responses during shutdown are sufficient for Compose to stop routing requests when using healthchecks or external LB.
- Example healthcheck in docker-compose.yml:

```yaml
healthcheck:
  test: ["CMD", "curl", "-fsS", "http://localhost:5000/health/ready"]
  interval: 5s
  timeout: 2s
  retries: 3
  start_period: 10s
```

Set a drain timeout:

```yaml
environment:
  - WORKER_DRAIN_TIMEOUT_SECONDS=45
```

### Kubernetes
Use readiness probes and preStop hooks to ensure in-flight sessions drain:

```yaml
readinessProbe:
  httpGet:
    path: /health/ready
    port: 5000
  periodSeconds: 5
  timeoutSeconds: 2
  failureThreshold: 1

lifecycle:
  preStop:
    exec:
      command: ["/bin/sh", "-c", "sleep 40"]
```

- Set `terminationGracePeriodSeconds` to be >= WORKER_DRAIN_TIMEOUT_SECONDS + probe buffer. Example: 60.
- The app flips readiness to 503 on shutdown automatically; the preStop sleep gives LBs time to drain before SIGTERM deadlines.

## Observability
- Logs
  - Hub: "[hub] ApplicationStopping: stop accepting new borrows".
  - Worker: "[worker] ApplicationStopping: initiating graceful drain" and possible timeout message.
- Metrics
  - Standard HTTP/ASP.NET metrics are exposed (Prometheus). During shutdown, expect:
    - Increased 503 counts on borrow endpoints.
    - `/health/ready` 503 rate until container exits.
- Dashboard
  - Ongoing sessions should continue; new borrows will fail fast with 503 until workers come back.

## Verification steps
- Local manual test
  1) Start the stack (docker compose up --build).
  2) Borrow a session and connect a client.
  3) Send SIGTERM to a worker container: `docker kill --signal=TERM <worker_container>`.
  4) Observe logs: drain starts; `/health/ready` returns 503; connection persists until closed or timeout.
- Automated tests
  - Unit tests remain green. Integration tests can be extended in future to simulate shutdown; current grid tests rely on Testcontainers bootstrap and are compatible with the behavior.

## Compatibility and client expectations
- Clients should handle 503 responses on borrow and respect `Retry-After` header.
- Existing WebSocket sessions can continue until user closes them or the drain timeout ends.
- No API changes were introduced; the feature is backward compatible.

## FAQ
- Q: Will shutdown interrupt a running Playwright session?
  - A: Not immediately. The worker attempts a graceful drain. If the session exceeds the configured drain timeout, the sidecar is force-terminated to allow shutdown to complete.
- Q: Do I need to change probes?
  - A: Ensure you’re using `/health/ready` for readiness. Liveness can stay on `/health`.
- Q: Can I make drain longer than my platform’s termination grace period?
  - A: You can, but the platform may send SIGKILL before drain ends. Align `terminationGracePeriodSeconds` (K8s) or stop timeout (Docker) with your drain setting.

## References
- Source files:
  - hub/Infrastructure/Web/EndpointMappingExtensions.cs
  - worker/Services/WebServerHost.cs
  - worker/Services/PoolManager.cs
- Related docs:
  - Node Liveness and Sweeper (node TTLs and cleanup)
  - Borrow TTL & Session Persistence
