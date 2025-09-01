# Agenix Playwright Grid — UI User Guide

Date: 2025-08-26

Audience: Test Engineers, QA, Developers, SREs

## 1. Overview
The Playwright Grid Dashboard provides real-time visibility into test runs, capacity, workers, and logs. It connects to the Hub via SignalR and aggregates run-level and protocol-level data in one place.

- Default URL (docker compose): http://127.0.0.1:3001

## 2. Prerequisites
- Docker Desktop (or compatible engine), Docker Compose
- .NET 8 SDK (for development and local builds)
- Ports available on host: Hub 5100, Dashboard 3001 (per docker-compose.yml)

## 3. Starting the Grid
- From the project root: `docker compose up --build`
- Open the Dashboard at: http://127.0.0.1:3001
- Hub default: http://127.0.0.1:5100 (container 5000)
- Ensure Redis is reachable at localhost:6379 (container name redis)

## 4. Dashboard Home
The Home view summarizes Grid health and activity:
- Hub status and version; SignalR connection indicator
- Workers online, Playwright version, and heartbeat
- Pools and capacity per label (e.g., App:Browser:Env)
- Recent runs with status (Running/Passed/Failed/Canceled)

Navigation: Home, Runs, Workers, Pools/Labels, Metrics, Settings

## 5. Runs
Use the Runs page to explore historical and in-progress executions.

Filtering:
- Time window: Last 15 min, 1h, 24h, or custom range
- Status: Running, Passed, Failed, Canceled
- Labels: search by label prefix (e.g., AppB:Chromium:UAT)
- Runner/test metadata: filter by suite name, branch, commit (if forwarded)

Actions:
- Open a run to view details
- Copy Run ID to clipboard for cross-referencing in logs or CI

## 6. Run Details
Summary:
- Label and browser: e.g., AppB:Chromium:UAT, channel/version if available
- Worker/node info: Node ID, region, sidecar Playwright version
- Timing: borrow start/end, durations, queue time if any

Logs and artifacts:
- Protocol commands (Playwright protocol mirrored by the worker)
- API/application logs forwarded by the runner via HubClient
- Screenshots/videos (when enabled in tests) with download links
- Console/network events as provided by the runner

Tips:
- Use search to filter protocol log lines by keyword (selector, URL, etc.)
- Expand/collapse groups by page/frame
- Toggle to view only warnings/errors

## 7. Live Feed
The live feed uses SignalR to stream events from the Hub:
- Borrow/return events with labels and node selection
- Worker joins/leaves, capacity changes, pool updates
- Real-time run log lines if the run is currently active

Environment variable (Dashboard) for Hub SignalR base: `HUB_SIGNALR` (default `http://hub:5000/ws` inside compose)

## 8. Workers
View all registered workers (nodes) and their status:
- Node ID, region (`NODE_REGION`), and labels attached to the node's pools
- Sidecar Playwright version and browser channels
- Heartbeat/last seen; online/offline indicator
- Public WS endpoint (`PUBLIC_WS_HOST`/`PORT`/`SCHEME`) for client connections

Scaling tips:
- Add workers with labels that match hot pools (e.g., `AppB:Chromium:UAT=3`)
- Use Grafana utilization panels to identify where to add capacity

## 9. Pools and Labels
Labels are ordered keys joined by `:` with Browser second for consistency, e.g., `App:Browser:Env[:Region[:OS…]]`.

Hub matching modes:
- Exact match
- Trailing fallback (`HUB_BORROW_TRAILING_FALLBACK=true`)
- Prefix expansion (`HUB_BORROW_PREFIX_EXPAND=true`)
- Optional wildcards (`HUB_BORROW_WILDCARDS=false` by default)

In the UI, search/filter pools by label text; inspect counts, in-use vs free, and node distribution.

## 10. Seeing Your Test Logs in the UI
Use `Agenix.PlaywrightGrid.HubClient` to attribute tests and forward runner logs to the Dashboard:
- `SetCurrentTestAsync(testName)` to tag subsequent log lines to the current test
- `SendApiLogAsync(message)` to forward custom runner logs to the run detail view
- The worker mirrors Playwright protocol commands; enable screenshots/videos in your Playwright config as usual

## 11. Dashboard Environment Variables
Configure the Dashboard via environment variables (see docker-compose for examples):
- `HUB_SIGNALR = http://hub:5000/ws` (Hub SignalR base URL)
- Optional: brand titles and links via appsettings or env (if exposed)

## 12. Troubleshooting
Dashboard shows Hub offline:
- Verify docker compose is running and Hub is healthy at `/health` (http://127.0.0.1:5100/health)
- Ensure Dashboard `HUB_SIGNALR` matches Hub internal/external URL

No runs appear:
- Check worker capacity for requested labels; verify `POOL_CONFIG` on workers
- Confirm `PUBLIC_WS_HOST/PORT` are reachable from the test host

Live feed not updating:
- Inspect browser console for SignalR errors; check CORS and base URL

Artifacts missing:
- Ensure your tests record screenshots/videos; confirm the worker publishes artifact links

## 13. FAQ
- Do I need Playwright installed locally to use the Dashboard?
  - No. The workers carry the Playwright sidecars; the Dashboard visualizes data from Hub/Workers.
- Can I link a Dashboard run back to CI?
  - Yes. Forward build URL/commit via HubClient metadata; the UI renders it when provided.
- How are labels shown?
  - Labels appear on runs, pools, and workers. Use prefix search to find related pools.

## 14. References
- Root README: ../README.md
- Docs: [TestClient-Usage.md](./TestClient-Usage.md), [PlaywrightDotNet-pw-api.md](./PlaywrightDotNet-pw-api.md)
- Docker compose defaults: ../docker-compose.yml

---
Word version: docs/ui-user-guide.doc.xml opens directly in Microsoft Word. Use File → Save As… and choose .docx to convert.
