# Worker Sidecar Management

This page documents how the Worker manages the Playwright Node sidecar processes: health, restart/backoff strategy, and how errors are surfaced to the Hub and observability tools.

## Overview

Each Worker maintains a warm pool of Playwright browser servers (sidecars), one per capacity slot, per label key. The Worker:
- Launches a Node sidecar using the configured script (PLAYWRIGHT_SIDECAR).
- Reads the sidecar's stdout for a JSON payload that contains wsEndpoint, browser, browserVersion, and optionally playwrightVersion.
- Validates internal connectivity to the wsEndpoint (optional, see WORKER_VALIDATE_WS) during warmup and replacement.
- Exposes a public WebSocket proxy endpoint at ws://{PUBLIC_WS_HOST}:{PUBLIC_WS_PORT}/ws/{browserId}.
- Replaces sidecars that exit unexpectedly and performs continuous reconciliation to keep capacity healthy.

## Health Endpoints

The Worker exposes several HTTP endpoints:

- GET /health: Basic liveness with node id and pool labels. Returns 200 OK.
- GET /health/ready: Readiness that includes Redis ping health and shutdown state. Returns 200 OK if ready; 503 otherwise.
- GET /diagnostics/env: Redacted environment dump for debugging (requires x-hub-secret or x-node-secret header).
- GET /health/sidecars: Sidecar health snapshot per label and browserId, including:
  - slots: [{ browserId, browserType, pid, alive, startedAtUtc, uptimeSeconds, wsInternal, wsPublic }]
  - backoff: { failures, lastFailureUtc, nextDelaySeconds } per label when backoff is active.

## Restart and Backoff Strategy

When a sidecar exits or fails validation during replacement:
- The Worker attempts to start a replacement.
- If repeated failures occur within a short window, the Worker applies exponential backoff before the next replacement attempt to avoid hot loops:
  - Minimum delay: SIDECAR_BACKOFF_MIN_SECONDS (default 1s)
  - Maximum delay: SIDECAR_BACKOFF_MAX_SECONDS (default 30s)
  - Multiplier: SIDECAR_BACKOFF_MULTIPLIER (default 2.0)
  - Failure reset window: SIDECAR_BACKOFF_FAILURE_RESET_SECONDS (default 60s). If no failures occur within this window, the backoff level resets to minimum.

Backoff is tracked per label key to isolate problematic pools.

## Error Surfacing to Hub

The Worker publishes sidecar health and restart/backoff information into Redis so the Hub and Dashboard can display and aggregate it:
- Hash node:{NODE_ID}
  - SidecarFailures:{labelKey}: count of consecutive failures for this label key (resets on success or after reset window)
  - SidecarNextDelaySeconds:{labelKey}: next backoff delay before attempting a replacement
  - SidecarLastFailureUtc:{labelKey}: ISO timestamp (UTC) of the last failure recorded
  - SidecarHealth:{labelKey}: summary string (e.g., "OK" or "Backoff {N}s")
  - PlaywrightVersion: populated from the sidecar payload when available

This keeps Hub changes minimal while enabling visibility in pool and node listings.

## Environment Variables

- PLAYWRIGHT_SIDECAR: Path to the Node script that launches the Playwright server (default launch_playwright_server.js)
- PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS: Time to wait for sidecar wsEndpoint on stdout (default 60)
- PLAYWRIGHT_SERVER_DEBUG: If set (e.g., 1 or pw:server,pw:protocol), enables DEBUG namespace for Node sidecar
- WORKER_VALIDATE_WS=true: During warmup and replacement, validate internal wsEndpoint reachability (one restart allowed)
- PUBLIC_WS_HOST / PUBLIC_WS_PORT / PUBLIC_WS_SCHEME: Advertised public address for WebSocket proxy
- SIDECAR_BACKOFF_MIN_SECONDS: Min backoff
- SIDECAR_BACKOFF_MAX_SECONDS: Max backoff
- SIDECAR_BACKOFF_MULTIPLIER: Backoff multiplier
- SIDECAR_BACKOFF_FAILURE_RESET_SECONDS: Reset window for failures

## Observability

- Prometheus metrics for pool capacity and availability are exported by the Worker (see Metrics-and-Grafana.md).
- Additional counters exist for dropped frames/logs due to backpressure policies.
- Consider scraping /metrics and tracking:
  - worker_pool_capacity
  - worker_pool_available
  - worker_ws_proxy_dropped_frames_total
  - worker_ws_log_dropped_messages_total

## Operational Tips

- If /health/ready is failing due to Redis timeout, increase REDIS_HEALTH_TIMEOUT_MS or check connectivity.
- If capacity is lower than configured, check /health/sidecars to see whether backoff is in effect; also inspect Redis hash node:{NODE_ID} for SidecarHealth:{labelKey} and SidecarNextDelaySeconds:{labelKey}.
- Ensure PUBLIC_WS_* reflects host reachability from your runners (tests) to avoid connect failures.
