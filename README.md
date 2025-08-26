![Logo][1]

Playwright C# Grid – Flexible multi‑segment label pools ![Icon][2]
==============

A lightweight, composable grid for Playwright powered by .NET 8. It consists of a Hub, one or more Workers, and an
optional Dashboard. Capacity routing is driven by simple, ordered label keys (e.g., `App:Browser:Env[:Region[:OS…]]`).
Workers pre‑warm Playwright servers and the Hub assigns sessions based on label matching with configurable fallback
rules. Prometheus/Grafana observability is included.

What you get

- Flexible, ordered label keys for routing capacity across apps, browsers, envs, regions, OS, channels, etc.
- Pre‑warmed pools per label for low‑latency borrows.
- Configurable matching: exact, trailing‑segment fallback, prefix expansion, optional wildcards.
- Simple HTTP APIs for borrow/return; secure via shared secrets.
- Built‑in metrics (Prometheus) and dashboards (Grafana provisioning).

Components and default ports

- Hub (ASP.NET Core Minimal API + SignalR): http://127.0.0.1:5100 (container 5000)
- Workers (launch Playwright sidecars, proxy WS): http://127.0.0.1:5200, :5201, :5202 (container 5000)
- Dashboard (Blazor): http://127.0.0.1:3001
- Prometheus: http://127.0.0.1:9090
- Grafana: http://127.0.0.1:3000 (default creds admin/admin)
- Redis: localhost:6379 (container name redis)

Quickstart

1) Prereqs: Docker, Docker Compose, .NET 8 SDK (for building/running tests).
2) Start everything: docker compose up --build
3) Verify:
    - Hub health: curl http://127.0.0.1:5100/health
    - Dashboard: open http://127.0.0.1:3001
    - Grafana: open http://127.0.0.1:3000 (admin/admin)
4) Borrow a browser (example):
    - curl -s -X POST http://127.0.0.1:5100/session/borrow \
      -H 'content-type: application/json' \
      -H 'x-hub-secret: runner-secret' \
      -d '{"labelKey":"AppB:Chromium:UAT"}'
    - Response contains browserId, webSocketEndpoint (to connect via Playwright), and browserType.
5) Return it:
    - curl -s -X POST http://127.0.0.1:5100/session/return \
      -H 'content-type: application/json' \
      -H 'x-hub-secret: runner-secret' \
      -d '{"labelKey":"AppB:Chromium:UAT","browserId":"<id-from-borrow>"}'

Label schema (routing keys)
Labels are ordered, `:`‑separated segments. Keep `Browser` second for consistent routing and metrics.

- 3‑part (baseline): `App:Browser:Env`
- 4‑part: `App:Browser:Env:Region`
- 5‑part: `App:Browser:Env:Region:OS`
- Rich: `App:Browser:Env:OS:Headless:Locale`
  Suggested vocabularies
- App: AppA, AppB
- Browser: Chromium, Firefox, Webkit
- Env: UAT, Staging, ProdSim
- Region: EU, US, APAC
- OS: linux, win11, mac
- Channel: stable, beta, dev, nightly
- Headless: headless, headed
- Locale: en-US, fr-FR
- BrowserVersion: 120, 121 (optional when pinning)

Worker capacity (POOL_CONFIG)
Each worker advertises capacity as: `labelKey=count` comma‑separated.

- Examples
    - Baseline: `AppA:Chromium:Staging=2,AppB:Chromium:Staging=1`
    - Region split: `AppA:Chromium:UAT:EU=2,AppA:Chromium:UAT:US=1`
    - OS split: `AppB:Chromium:UAT:linux=2,AppB:Chromium:UAT:win11=1`
    - Channels/headless mix: `AppB:Chromium:UAT:stable:headless=2,AppB:Chromium:UAT:beta:headed=1`

Borrowing and matching (Hub)
Clients request capacity with a label key. The more specific, the tighter the match. Strategy (configurable):

1) Exact match on the full label.
2) Trailing fallback (optional): drop last segment repeatedly (`App:Browser:Env:Region → App:Browser:Env`).
3) Prefix expansion (optional): treat missing trailing segments as ANY; match more specific pools starting with the
   requested prefix (e.g., request `App:Browser:Env` matches `App:Browser:Env:EU`).
4) Wildcards (optional): allow `*` in any segment (e.g., `App:Chromium:*:EU`, `*:Firefox:Staging`).

Hub configuration (env)

- REDIS_URL=redis:6379
- HUB_RUNNER_SECRET=runner-secret (required by clients as header `x-hub-secret`)
- HUB_NODE_SECRET=node-secret (required by workers for /node/register)
- HUB_NODE_TIMEOUT=60 (seconds for node liveness)
- DASHBOARD_URL=http://localhost:3001 (dashboard redirect target)
- HUB_BORROW_TRAILING_FALLBACK=true
- HUB_BORROW_PREFIX_EXPAND=true
- HUB_BORROW_WILDCARDS=false

Worker configuration (env)

- HUB_URL=http://hub:5000
- REDIS_URL=redis:6379
- NODE_ID=worker1 (unique per worker)
- NODE_SECRET=node-secret
- NODE_NODE_SECRET=node-node-secret
- POOL_CONFIG=AppA:Chromium:Staging=3 (comma‑separated list)
- NODE_REGION=local (example label)
- NODE_EXE=node (path to NodeJS)
- PLAYWRIGHT_SIDECAR=launch_playwright_server.js
- PLAYWRIGHT_VERSION=1.54.2 (reported by sidecar; also used as build arg to pin installed version)
- CHROMIUM_ARGS="--flag1 --flag2" or "--flag1,--flag2" or JSON ["--flag1","--flag2"] (applies only to Chromium)
- PUBLIC_WS_HOST=127.0.0.1, PUBLIC_WS_PORT=5200, PUBLIC_WS_SCHEME=ws (maps to ws://host:port/ws/{browserId})

Dashboard configuration (env)

- HUB_SIGNALR=http://hub:5000/ws

HTTP API summary

- POST /session/borrow
    - Headers: `x-hub-secret: <HUB_RUNNER_SECRET>`
    - Body: `{ "labelKey": "App:Browser:Env[:...]" }`
    - 200 OK: `{ "browserId": "...", "webSocketEndpoint": "ws://...", "browserType": "chromium|firefox|webkit" }`
    - 503 if no capacity; 401 if secret mismatch; 4xx on bad input.
- POST /session/return
    - Headers: `x-hub-secret: <HUB_RUNNER_SECRET>`
    - Body: `{ "labelKey": "...", "browserId": "..." }`
- POST /node/register
    - Headers: `x-hub-secret: <HUB_NODE_SECRET>`
    - Body: `{ "NodeId": "worker1", "Apps": ["AppA"], "Capacity": 3, "Labels": {"region":"eu"} }`
- GET /health → `{ status: "ok" }`
- GET /nodes → list of node ids
- GET /ws → SignalR hub (dashboard)

Metrics and observability

- Hub and Workers expose Prometheus metrics at `http://<host-port>/metrics` (compose maps hub 5100, workers 5200+).
- Prometheus is preconfigured to scrape hub and workers (see prometheus.yml).
- Grafana is provisioned; open http://127.0.0.1:3000 and explore dashboards.

Testing (GridTests)

- Start the grid: docker compose up --build
- In another shell: GRID_TESTS_USE_LOCAL=1 dotnet test
- Optional overrides:
  HUB_URL=http://127.0.0.1:5100 \
  HUB_RUNNER_SECRET=runner-secret \
  GRID_TESTS_HEALTH_TIMEOUT_SECONDS=120 \
  GRID_TESTS_USE_LOCAL=1 dotnet test
- Other flags:
    - GRID_TESTS_SKIP_CONTAINERS=1  (use if HUB_URL points elsewhere)
    - GRID_TESTS_REUSE=1            (reuse previous containers; default off)
    - GRID_TESTS_FORCE_BUILD=1      (force image rebuild; default on)
    - GRID_TESTS_SKIP_CLEANUP=1     (skip preflight cleanup)
    - GRID_TESTS_HEALTH_TIMEOUT_SECONDS=NN

Guidelines

- Keep `Browser` as the second segment to preserve consistent routing and observability.
- Fix a global segment order after `Env` to avoid label drift and duplicates.
- Normalize values and restrict vocabularies to reduce cardinality.
- Maintain backward compatibility: continue accepting `App:Browser:Env`; shorter requests can match longer pools (if
  prefix expansion enabled).

Troubleshooting

- 401/403 when borrowing: ensure header `x-hub-secret` matches HUB_RUNNER_SECRET.
- 503 No browser available: increase POOL_CONFIG counts or add workers for that label.
- Cannot connect to WS endpoint: verify worker PUBLIC_WS_HOST/PORT map to your host; compose examples map 5200+ to
  container 5000.
- Tests hang on health: raise GRID_TESTS_HEALTH_TIMEOUT_SECONDS or confirm hub at http://127.0.0.1:5100/health.
- Not seeing Playwright commands (e.g., navigate) in Dashboard:
    - The worker proxies the Playwright WebSocket and mirrors protocol text messages to the Hub while your test is
      connected. Ensure the test connects to the ws endpoint returned by /session/borrow (it points to ws://<
      worker-host>/ws/{browserId}).
    - Make sure HUB_URL is correctly set in the worker so it can POST mirrored messages to the Hub.
    - For extra verbosity from the Playwright server itself, set environment PLAYWRIGHT_SERVER_DEBUG=1 on the worker
      containers; this enables Node DEBUG namespaces 'pw:server,pw:protocol' for the sidecar and you will see
      server-side command traces in worker logs.
- Want API-level client logs in tests (C#): implement Microsoft.Playwright.ILogger and pass it via options when
  launching/connecting, or set logging in your test framework output. This shows calls like page.GotoAsync in the test
  logs.

Contributing

- PRs and issues are welcome. Please include reproduction steps and label schemas involved.

License

- See the repository license.

## Pinning Playwright version and Chromium flags

You can pin the Playwright version used by worker images and control Chromium launch flags via docker-compose.

Workers print the Playwright version at startup to the container logs (both the configured env value and the detected
installed NPM package version).

- PLAYWRIGHT_VERSION
    - Build arg used when building worker images to install a specific Playwright NPM version.
    - Also passed as a runtime env so the sidecar script reports it in its JSON line.
- CHROMIUM_ARGS
    - Space-, comma-separated, or JSON array of Chromium flags.
    - Only applied when launching Chromium; ignored for Firefox/WebKit.

Example (docker-compose.yml):

worker1:
build:
context: ./worker
args:
PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
environment:
- PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION:-1.54.2}
- CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox

Tip: Place PLAYWRIGHT_VERSION=1.54.2 in a .env file to apply project-wide.

## Design: Test Results & Commands Dashboard

A proposal is available describing a separate dashboard page to view Playwright test results, drill into test details,
and live‑tail relevant commands executed by the Playwright server.

- Scope: design only (no implementation in this change)
- Routes (proposed): `/results`, `/results/{runId}`
- Transport/Storage: Redis + SignalR (proposed), short TTL, optional durable sink later

Read the full proposal: docs/TestResultsDashboard-Approach.md

## Documentation: where to update docs

- Primary documentation lives in the docs/ folder.
- Test Client usage (HubClient in test runners): see docs/TestClient-Usage.md.
- For the Test Results Dashboard and making Playwright API/protocol logs appear under the “Tests” tab, update
  docs/TestResultsDashboard-Approach.md. It covers:
    - Borrow/Return with runId attribution.
    - Setting current test via HubClient.SetCurrentTestAsync so logs group under a TestId.
    - Worker-sourced protocol mirroring to POST /results/browser/{browserId}/commands.
    - Runner-sourced API/protocol logs to POST /results/browser/{browserId}/api-logs (use
      HubClient.SendApiLogAsync/SendApiLogsAsync from Agenix.PlaywrightGrid.HubClient).
- Keep this README as the entry point for Quickstart, configuration, and a brief HTTP API summary. If you add/remove
  endpoints, reflect that here.
- Optional: add notes or structure guides in docs/README.md to help contributors navigate docs.

PR checklist: include relevant documentation updates when changing endpoints, dashboard behavior, or client APIs.

[1]: .assets/logos/agenix-logo-large.png "Agenix"

[2]: .assets/icons/icon-64.png "Agenix"
