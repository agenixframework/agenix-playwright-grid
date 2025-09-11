# Dashboard UI — User Guide

This guide covers the Playwright Grid Dashboard for viewing runs, results, and live logs.

Overview
- Live run feed via SignalR (auto-reconnect with backoff).
- Runs list with filtering, sorting, and paging.
- Run details: summary, timeline, per-browser command logs, and metadata.
- Deep links to runs by runId.

Access
- Default URL in local compose: http://127.0.0.1:3001
- Configure HUB_SIGNALR for the Dashboard to point to the Hub’s ws endpoint (see docker-compose.yml).

Runs page
- Columns: RunId, App, Browser, Env, Status, Started, Completed, Duration, Failures.
- Filters: App, Browser, Env, Region, Status, runId. Use the filter bar to narrow down.
- Pagination and virtualization keep the UI responsive on large datasets.

Run details
- Summary header shows core metadata and status.
- Command log panel streams events (worker protocol and optional runner logs) with time ordering.
- Use the search box to filter commands by keyword.
- Copy runId using the action in the header for sharing.

Live feed and resilience
- The Dashboard uses SignalR to receive updates. If disconnected, it shows an inline warning and automatically retries with exponential backoff.
- Error boundaries provide friendly messages and retry actions when transient errors occur.

Troubleshooting
- No runs appearing:
  - Ensure the Hub is reachable from the Dashboard (HUB_SIGNALR env).
  - Verify Hub /health is OK and Redis is up.
- Logs not streaming:
  - Check browser console for SignalR connection errors (CORS, URL mismatch).
  - Confirm the runId is correct and that workers are forwarding protocol logs.
- Missing data or slow UI on large runs:
  - The Results pages use server-driven paging and streaming; verify Hub and Redis health.

Related docs
- Overview & Quick Start: docs-guide.md
- Metrics and Grafana: Metrics-and-Grafana.md
- Capacity Queue: Capacity-Queue.md
- Label Matching: Label-Matching.md
