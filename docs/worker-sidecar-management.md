# Worker Sidecar Management

This page explains how the Worker manages Playwright sidecar processes and how the Hub interacts with workers to keep capacity healthy.

Overview
- Each Worker maintains a pool of pre-warmed Playwright browser servers (sidecars) per label (POOL_CONFIG).
- When a session is borrowed, the Worker proxies the WebSocket connection to the selected sidecar and mirrors protocol logs to the Hub.
- After a return (or auto-return via TTL), the Hub asks the Worker to recycle the sidecar so that a fresh instance is prepared for the next borrower.

Key environment variables (Worker)
- HUB_URL: Hub base URL (e.g., http://hub:5000 in compose)
- REDIS_URL: Redis connection (e.g., redis:6379)
- NODE_ID: unique worker id (e.g., worker1)
- NODE_SECRET: must match HUB_NODE_SECRET on the Hub
- POOL_CONFIG: comma-separated label=count pairs, e.g., "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"
- NODE_REGION: optional label segment appended to each capacity entry
- PUBLIC_WS_HOST/PORT/SCHEME: host reachability for clients to connect to ws://{host}:{port}/ws/{browserId}
- PLAYWRIGHT_VERSION: reported/pinned by sidecar; keep aligned with Docker image
- CHROMIUM_ARGS / FIREFOX_ARGS / WEBKIT_ARGS: optional browser-specific tuning flags

Sidecar lifecycle
1) Startup: Worker initializes pools per POOL_CONFIG and spawns sidecar servers.
2) Borrow: A specific sidecar (browser instance) is dedicated to the borrower; websocket traffic is proxied through the Worker.
3) Return: Upon return, Worker receives a recycle:{browserId} marker (short TTL) in Redis and tears down the sidecar, then replenishes capacity back into available:{labelKey}.
4) Failure handling: Sidecar processes are supervised with restart/backoff; health signals are published to Redis and included in logs.

Health and readiness
- The Worker exposes internal health checks and periodically reports liveness to the Hub via Redis.
- If a Worker is lost, the Hub’s NodeSweeperService reclaims capacity by pruning orphaned entries; see Node-Liveness-and-Sweeper.md.

Operational guidance
- Ensure PUBLIC_WS_HOST/PORT are set to values reachable from your test hosts (not container-internal addresses).
- Keep PLAYWRIGHT_VERSION consistent across images to avoid confusion in the Dashboard summaries.
- Use NODE_REGION (and additional segments) to control routing and visibility for multi-region deployments.

Metrics and observability
- Prometheus metrics are exposed for capacity and proxy behavior (drops, queueing); see Metrics-and-Grafana.md.
- Logs include runId/browserId scopes for correlation across Hub and Worker.

Related docs
- Configuration: configuration.md
- Capacity Queue and Concurrency Caps: Capacity-Queue.md
- Node Liveness and Sweeper: Node-Liveness-and-Sweeper.md
