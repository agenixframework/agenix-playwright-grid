# Playwright Grid Documentation

Welcome to the Agenix Playwright Grid documentation. This site covers the Hub, Worker, Dashboard, HubClient, configuration, and operations guidance for running scalable Playwright browser sessions.

Highlights
- Overview & Quick Start: docs-guide.md
- Configuration Guide: configuration.md
- Label Matching strategy and routing: Label-Matching.md
- Capacity Queue (pending borrows, fairness, timeouts): Capacity-Queue.md
- Borrow TTL and Session Persistence: Borrow-TTL-and-Session-Persistence.md
- Node Liveness and Sweeper: Node-Liveness-and-Sweeper.md
- Metrics and Grafana: Metrics-and-Grafana.md
- Compatibility Matrix: Compatibility-Matrix.md

Getting started
- Use docker compose up --build to launch Hub, Workers, Dashboard, and Redis.
- See README.md in the repo root for port map and environment variables.
- For test-driven validation, use the GridTests project which self-provisions containers via Testcontainers.

Projects
- hub: ASP.NET Core Minimal API + SignalR. Capacity broker backed by Redis.
- worker: Worker Service that manages Playwright sidecars and proxies WS connections.
- dashboard: Blazor app to view runs, results, and live SignalR feed.
- Agenix.PlaywrightGrid.HubClient: Thin client for borrowing/returning sessions and forwarding logs.
- tests: Integration tests (GridTests) and unit tests.

Concepts
- Labels and routing: App:Browser:Env[:Region[:...]] with Browser as the second segment. Matching supports exact, trailing fallback, prefix expansion, and optional wildcards.
- Capacity Queue: Fair pending-borrow queue with per-label and per-run caps; integrates with per-label concurrency caps to prevent starvation.
- Borrow TTL: Automatic session return on lease expiry with persisted session state for Hub restarts.
- Node liveness: Heartbeat and sweeper to evict stale nodes and reclaim capacity.

Where to go next
- If you are integrating a test runner, see CLI Reference (cli.md) and Test Client Usage (TestClient-Usage.md).
- If you operate the grid, read Capacity-Queue.md and Metrics-and-Grafana.md.
- To understand label rules, read Label-Matching.md and Configuration Guide.
