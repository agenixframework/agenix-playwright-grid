# Session distribution across workers

This page explains how the Hub distributes borrows across multiple Workers and what it means for capacity, routing, and client connections.

## What is distributed (and what isn’t)
- Distributed: the Hub spreads concurrent borrows across any Workers that advertise capacity for the requested label. Aggregate capacity is the sum of per-Worker slots.
- Not distributed: a single Playwright session is never split between Workers. Once a session is borrowed, it is pinned to the selected Worker until returned or disconnected.

## Capacity math at a glance
- Each Worker declares one or more label pools via `POOL_CONFIG`, for example: `AppX:Chromium:UAT=3`.
- The Hub aggregates capacity across Workers for the same label key.

Example:
- Worker1: `POOL_CONFIG=AppX:Chromium:UAT=3`
- Worker2: `POOL_CONFIG=AppX:Chromium:UAT=3`
- Effective capacity for `AppX:Chromium:UAT` = 6 concurrent sessions.

If you request a 7th session while all 6 are in use, the Hub will deny or time out the request (a fair waiting queue is on the roadmap).

## Selection strategy and pinning
- The Hub matches the requested label using the configured rules: exact → trailing fallback → prefix expansion → optional wildcards (see HUB_BORROW_* settings).
- When multiple Workers have free slots for the matching label, the Hub assigns the borrow to any eligible Worker with free capacity (today typically first-available). The session remains pinned to that Worker until returned or the connection ends.

## Label keys and matching
- Recommended label shape: `App:Browser:Env[:Region[:OS…]]` with Browser as the second segment.
- Relevant Hub settings (environment variables):
  - `HUB_BORROW_TRAILING_FALLBACK=true|false`
  - `HUB_BORROW_PREFIX_EXPAND=true|false`
  - `HUB_BORROW_WILDCARDS=false|true`
- To share capacity across Workers, ensure they advertise the same label key (or rely on matching behaviors you explicitly enable).

## Networking: how the client connects
- After a successful borrow, the Hub returns the Worker’s public WebSocket endpoint for Playwright.
- The runner connects directly to the Worker’s WS; the Hub does not proxy Playwright commands end-to-end.
- Configure the Worker’s public endpoint via:
  - `PUBLIC_WS_HOST`
  - `PUBLIC_WS_PORT`
  - `PUBLIC_WS_SCHEME` (ws or wss)
- These must be reachable from the test host (your machine or CI agent).

## Example configuration
Two Workers sharing a Chromium pool for the same app/env:

Worker1:
- `POOL_CONFIG=AppX:Chromium:UAT=3`
- `PUBLIC_WS_HOST=127.0.0.1` (adjust for your network)
- `PUBLIC_WS_PORT=5200`

Worker2:
- `POOL_CONFIG=AppX:Chromium:UAT=3`
- `PUBLIC_WS_HOST=127.0.0.1`
- `PUBLIC_WS_PORT=5201`

Hub (typical via docker compose):
- `REDIS_URL=redis:6379`
- `HUB_RUNNER_SECRET=runner-secret`
- `HUB_NODE_SECRET=node-secret`
- `HUB_BORROW_TRAILING_FALLBACK=true`
- `HUB_BORROW_PREFIX_EXPAND=true`
- `HUB_BORROW_WILDCARDS=false`

## How to verify distribution
- Dashboard: open http://127.0.0.1:3001 and start multiple borrows; you should see sessions landing on different Workers.
- Integration tests (against a local grid):
  1) `docker compose up --build`
  2) `export GRID_TESTS_USE_LOCAL=1`
  3) `export HUB_URL=http://127.0.0.1:5100`
  4) `export HUB_RUNNER_SECRET=runner-secret`
  5) `dotnet test tests/GridTests.csproj -c Debug`
- Metrics: Prometheus/Grafana show per-Worker utilization and total capacity.

## Troubleshooting
- Borrow succeeds but WS connect fails:
  - `PUBLIC_WS_*` must reflect an address reachable from the test host.
  - Confirm ports are exposed and not blocked by a firewall.
- Requests never match capacity:
  - Ensure Workers advertise the exact label key you borrow.
  - Review `HUB_BORROW_*` settings; disable wildcards unless intentional.
  - Validate `POOL_CONFIG` formatting: `LabelKey=Count`, comma-separated for multiple pools.
- Starvation or uneven spread:
  - Current strategy is first-available; fairness and queues are on the roadmap.

## FAQ
- Does having two Workers with `=3` each give me six total sessions? Yes—aggregate capacity is 6.
- Can a single session move between Workers? No—once allocated, it stays on the selected Worker.
- What happens on the 7th borrow when 6 are busy? The request is denied or times out (queueing is on the roadmap).
- Do I need Playwright locally for GridTests? No—binaries are handled inside the Worker containers.

## Quick checklist for distributed runs
- [ ] Workers advertise the same label (e.g., `AppX:Chromium:UAT`).
- [ ] `PUBLIC_WS_*` points to reachable endpoints from the runner.
- [ ] Hub matching settings (`HUB_BORROW_*`) align with your label usage.
- [ ] Dashboard/metrics confirm sessions spread across Workers.
- [ ] Secrets (`HUB_RUNNER_SECRET`, `HUB_NODE_SECRET`) are consistent across components.
