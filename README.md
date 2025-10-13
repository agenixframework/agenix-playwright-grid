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
- Configurable matching: exact, trailing‑segment fallback, prefix expansion, optional wildcards (see
  docs/Label-Matching.md).
- Simple HTTP APIs for borrow/return; secure via shared secrets.
- Built‑in metrics (Prometheus) and dashboards (Grafana provisioning).
- See docs/Metrics-and-Grafana.md for a comprehensive metrics and Grafana guide.

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
      -d '{"labelKey":"AppB:Chromium:UAT","runName":"Smoke UAT #123"}'
    - Response contains browserId, webSocketEndpoint (to connect via Playwright), and browserType.
    - Note: runName is optional; omit it to fall back to displaying RunId in the Dashboard.
    - Tip: open the Dashboard filtered to this run name: http://127.0.0.1:3001/results?runName=Smoke%20UAT%20%23123
5) Return it:
    - curl -s -X POST http://127.0.0.1:5100/session/return \
      -H 'content-type: application/json' \
      -H 'x-hub-secret: runner-secret' \
      -d '{"labelKey":"AppB:Chromium:UAT","browserId":"<id-from-borrow>"}'

Label schema (routing keys)
Labels are ordered, `:`‑separated segments. Keep `Browser` second for consistent routing and metrics.

See also: [Session distribution across workers](#session-distribution) for how capacity is aggregated across workers.
For detailed matching strategy, see docs/Label-Matching.md. For a deep dive, see docs/distribution.md.

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
For the full label matching strategy (exact → trailing fallback → prefix expansion → optional wildcards) and
configuration knobs, see docs/Label-Matching.md.

<a id="session-distribution"></a>

## Session distribution across workers

- Borrows are distributed across workers that advertise capacity for the matched label. Aggregate capacity is the sum
  across workers.
- A single borrowed session is pinned to the selected worker; it does not move during the session.

See docs/distribution.md for details on how sessions are spread across workers.

Hub configuration (env)

- REDIS_URL=redis:6379
- HUB_NODE_SECRET=node-secret (required by workers for /node/register)
- API authentication via Bearer token (required for client API access)
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

Redis topologies and TLS (Hub & Worker)

- Standalone (default)
    - REDIS_URL=redis:6379 or redis://[:password@]host:port[/db]
    - Timeouts (ms): REDIS_CONNECT_TIMEOUT_MS, REDIS_SYNC_TIMEOUT_MS, REDIS_ASYNC_TIMEOUT_MS (defaults 5000)
- TLS
    - Prefer rediss:// in REDIS_URL. Alternatively set REDIS_SSL=1 and optional REDIS_SSL_HOST for SNI/hostname
      validation.
- Cluster
    - Set REDIS_MODE=cluster (or REDIS_USE_CLUSTER=1).
    - Provide multiple endpoints in REDIS_URL (comma/semicolon/space separated):
        - Example: REDIS_URL=redis-cluster-0:6379,redis-cluster-1:6379,redis-cluster-2:6379
    - Behavior: the client auto-discovers slots and routes commands; failovers are handled by StackExchange.Redis with
      retries and backoff.
- Sentinel (single primary with replicas)
    - Set REDIS_MODE=sentinel (or REDIS_USE_SENTINEL=1).
    - REDIS_SENTINELS=sentinel1:26379,sentinel2:26379[,sentinel3:26379]
    - REDIS_SENTINEL_MASTER=<serviceName> (e.g., mymaster)
    - Optional: REDIS_SENTINEL_PASSWORD for auth to Sentinel; password/DB for the data connection can be supplied via
      REDIS_URL (e.g., redis://:pass@dummy/0).
    - Behavior: the Hub/Worker resolve the current primary from Sentinel at startup and connect to it. On connectivity
      issues the client retries with exponential backoff. If Sentinel elects a new primary at a different address,
      restarting Hub/Workers will re-resolve to the new primary. Future versions may add automatic re-resolution during
      runtime.

Dashboard configuration (env)

- HUB_SIGNALR=http://hub:5000/ws

HTTP API summary

API limits and timeouts

- Default request body limits: 64 KiB for control endpoints (borrow/return/register/test), 1 MiB for log endpoints.
- Default timeouts: 15s headers, 30s keep-alive, 60s per-request timeout. All are configurable; see
  docs/configuration.md.

- POST /session/borrow
    - Headers: `Authorization: Bearer <API_KEY>`
    - Body: `{ "labelKey": "App:Browser:Env[:...]" , "runName": "Optional" }`
    - 200 OK: `{ "browserId": "...", "webSocketEndpoint": "ws://...", "browserType": "chromium|firefox|webkit" }`
    - 503 if no capacity; 401 if unauthorized; 4xx on bad input.
    - RunName validation (optional field):
        - Trimmed; empty is treated as not supplied.
        - Max length 128.
        - Allowed chars: letters, digits, space, dot (.), underscore (_), hyphen (-).
        - Control characters are not allowed.
        - Case policy: casing is preserved; comparisons/search (e.g., Dashboard) are case-insensitive.
        - May contain descriptive text to help humans identify runs (e.g., "Smoke UAT #123").
        - Security/PII: avoid including secrets or personal data. To prevent RunName appearing in hub logs, set
          HUB_REDACT_RUNNAME=1 (UI/storage still show the provided value).
- POST /session/return
    - Headers: `Authorization: Bearer <API_KEY>`
    - Body: `{ "labelKey": "...", "browserId": "..." }`
- POST /node/register
    - Headers: `x-hub-secret: <HUB_NODE_SECRET>`
    - Body: `{ "NodeId": "worker1", "Apps": ["AppA"], "Capacity": 3, "Labels": {"region":"eu"} }`
- GET /health → `{ status: "ok" }`
- GET /nodes → list of node ids
- GET /ws → SignalR hub (dashboard)

Metrics and observability

- Hub and Workers expose Prometheus metrics at `http://<host-port>/metrics` (compose maps hub 5100, workers 5200+).
- Prometheus is preconfigured to scrape hub and workers (see prometheus/prometheus.yml).
- Grafana is provisioned; open http://127.0.0.1:3000 and explore dashboards.
- Admin metrics: Hub exposes low-cardinality admin metrics for Projects/Users. Panels are available in Grafana under "
  Playwright Grid – Admin Metrics" (uid pw-grid-admin-metrics):
    - hub_admin_active_projects (gauge)
    - hub_admin_active_users (gauge)
    - hub_admin_membership_changes_total (counter, label: action=add|remove|role_update)
    - Important admin operations are traced via ActivitySource 'playwright-hub.admin' and exported via OpenTelemetry
      when ENABLE_OTLP=1.

Testing (Agenix Test Platform)

Integration tests support two deployment modes:

**Docker Mode (Default)**:
```bash
# Start infrastructure and services
docker compose --profile infrastructure --profile core up -d

# Run tests against Docker containers
dotnet test Agenix.PlaywrightGrid.Integration.Tests/
```

**Local Mode**:
```bash
# Start infrastructure in Docker
docker compose --profile infrastructure up -d

# Start services locally (in separate terminals)
./scripts/run-local-dev-inline.sh

# Run tests against local services
AGENIX_TESTS_USE_LOCAL=true dotnet test Agenix.PlaywrightGrid.Integration.Tests/
```

**Configuration Options**:
- `AGENIX_TESTS_USE_LOCAL` - Set to `true` for local mode (default: `false`)
- `AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS` - Hub health check timeout (default: 120)
- `AGENIX_TESTS_HEALTH_POLL_INTERVAL_SECONDS` - Health check polling interval (default: 1.0)
- `AGENIX_TESTS_WORKER_TIMEOUT_SECONDS` - Worker readiness timeout (default: 60)
- `AGENIX_TESTS_EXPECTED_WORKERS` - Expected worker count (default: 3)

**Advanced Usage**:
```bash
# Override Hub URL and health timeout
HUB_URL=http://127.0.0.1:5100 \
AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS=180 \
AGENIX_TESTS_USE_LOCAL=true \
dotnet test Agenix.PlaywrightGrid.Integration.Tests/
```

Guidelines

- Keep `Browser` as the second segment to preserve consistent routing and observability.
- Fix a global segment order after `Env` to avoid label drift and duplicates.
- Normalize values and restrict vocabularies to reduce cardinality.
- Maintain backward compatibility: continue accepting `App:Browser:Env`; shorter requests can match longer pools (if
  prefix expansion enabled).

Troubleshooting

- 401/403 when borrowing: ensure `Authorization: Bearer <API_KEY>` header is provided with a valid API key.
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

## Pinning Playwright version and browser flags

You can pin the Playwright version used by worker images and control per-browser launch flags and Firefox preferences
via docker-compose.

Workers print the Playwright version at startup to the container logs (both the configured env value and the detected
installed NPM package version).

- PLAYWRIGHT_VERSION
    - Build arg used when building worker images to install a specific Playwright NPM version.
    - Also passed as a runtime env so the sidecar script reports it in its JSON line.
- CHROMIUM_ARGS / CHROME_ARGS
    - Space-, comma-separated, or JSON array of Chromium flags. CHROME_ARGS is an alias if CHROMIUM_ARGS is not set.
    - Applied only when launching Chromium.
- WEBKIT_ARGS
    - Space-, comma-separated, or JSON array of flags passed when launching WebKit.
- FIREFOX_ARGS
    - Optional extra arguments for Firefox launch (limited effect; most tuning is via prefs).
- FIREFOX_PREFS
    - Firefox user prefs as JSON object or key=value pairs separated by comma/semicolon/newlines.
    - Validation: malformed entries are ignored; values are coerced to boolean/number when obvious; otherwise strings.

Example (docker-compose.yml):

worker1:
build:
context: ./worker
args:
PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
environment:

- PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION:-1.54.2}
- CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox

worker3:
environment:

- WEBKIT_ARGS=--disable-http2
- FIREFOX_ARGS=--headless
- FIREFOX_PREFS={"network.dns.disablePrefetch":true,"browser.cache.disk.enable":false}

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


Run with published images (GHCR)
--------------------------------
If you don't want to build locally, you can run the stack against images published to GitHub Container Registry.

Prereqs

- If images are private: docker login ghcr.io

Environment variables (can also be placed in a .env file at repo root)

- GHCR_OWNER = your GitHub org or username (required)
- GHCR_REPO = agenix-playwright-grid (defaults to this repo name if not set)
- IMAGE_TAG = latest, or a specific version produced by CI (e.g., 1.2.3)

Start the stack using the images override file

    export GHCR_OWNER=<your-gh-username-or-org>
    # optional overrides
    export IMAGE_TAG=latest
    # export GHCR_REPO=agenix-playwright-grid

    docker compose -f docker-compose.yml -f docker-compose.images.yml pull
    docker compose -f docker-compose.yml -f docker-compose.images.yml up -d

Notes

- The override file disables local builds and points hub/worker/dashboard to ghcr.io/${GHCR_OWNER}/$
  {GHCR_REPO}-<service>:${IMAGE_TAG}.
- Available services: -hub, -worker, -dashboard. Redis/Prometheus/Grafana already use public images.
- CI pushes multi-arch images (linux/amd64, linux/arm64) and tags both ${IMAGE_TAG} (e.g., 1.2.3) and latest.
- To scale workers, either duplicate worker sections/ports in docker-compose.yml or use: docker compose up -d --scale
  worker1=2 (when using a generalized worker service). In this repo workers are declared as worker1/2/3; adjust
  POOL_CONFIG and ports accordingly.

## Safe sidecar upgrade – who calls it and how

There are two supported ways to trigger the safe sidecar upgrade flow (graceful drain + restart) added to Workers.

- Recommended (for Dashboard/CI/CD): call the Hub admin endpoint, which fans out a Redis trigger that each target Worker
  reacts to. This avoids exposing per‑Worker secrets to the UI.
    - POST http://<hub-host>:5000/admin/nodes/{nodeId}/sidecar/upgrade
        - Auth: Authorization: Bearer <API_KEY>
        - nodeId: a specific node id (e.g., worker1) or the literal all to trigger every registered node.
        - Example:
            - curl -s -X POST http://127.0.0.1:5100/admin/nodes/all/sidecar/upgrade -H 'Authorization: Bearer
              your-api-key'
    - What happens:
        - Hub sets a short‑lived key node_upgrade:{nodeId} in Redis for each target.
        - Each Worker watches for its own node_upgrade:{NodeId} and, when seen, performs the drain → recycle idle
          slots → wait (up to WORKER_DRAIN_TIMEOUT_SECONDS) → force‑kill if needed → warm pools sequence, then clears
          the key.

- Direct (ops-only, when you can reach the Worker): call the Worker admin endpoint per node.
    - POST http://<worker-host>:5000/admin/sidecar/upgrade
        - Auth: x-node-secret: <NODE_NODE_SECRET>
    - Example:
        - curl -s -X POST http://127.0.0.1:5200/admin/sidecar/upgrade -H 'x-node-secret: node-node-secret'

Notes

- The flow withdraws this node’s availability from Hub first so no new sessions are assigned while it drains.
- Active sessions are given up to WORKER_DRAIN_TIMEOUT_SECONDS (default 30s) to complete; after that, sidecars are
  force‑restarted.
- This is designed to be invoked by the Dashboard (future button) or CI/CD pipelines via the Hub endpoint; direct Worker
  calls are intended for operators only.

## Storage backends (Results vs Admin)

- Results store (runs/tests/command logs): controlled by HUB_RESULTS_BACKEND with supported values memory, redis,
  sqlite, postgres. Exactly one backend is active at a time; there is no dual-write for results.
    - If HUB_RESULTS_BACKEND has an unknown value (e.g., a common typo like "postgress"), the Hub falls back to the
      in-memory results store and logs a warning on startup.
- Admin entities (Projects, Users, Memberships): Redis is the primary store for reads, listings, and secondary indexes (
  by name/email). Optionally, writes can be mirrored to Postgres for durability by setting HUB_ADMIN_BACKEND=postgres.
  If HUB_ADMIN_BACKEND is not set, the Hub will reuse HUB_RESULTS_BACKEND to decide whether to enable mirroring.
    - To explicitly disable admin mirroring even when the backend is postgres, set HUB_ADMIN_MIRROR=0 (or false). When
      mirroring is disabled, admin data is stored only in Redis.
    - Reads for admin APIs currently come from Redis; Postgres is a write-mirror for durability/archive purposes.

Example (docker compose):

- HUB_RESULTS_BACKEND=postgres → results go to Postgres.
- HUB_ADMIN_BACKEND=postgres (or omit and let it inherit from HUB_RESULTS_BACKEND) → admin writes mirror to Postgres as
  well, while reads continue from Redis.
- To avoid admin mirroring, add HUB_ADMIN_MIRROR=0.

## Test Result Upload (Structured Test Reporting)

Playwright Grid supports structured test result uploads with hierarchical steps, attachments, and detailed metadata.
Upload individual test cases with nested steps, screenshots, videos, traces, and logs for rich visualization in the
Dashboard.

### Features

- **Batch Upload**: Upload multiple test results in a single API call
- **Hierarchical Steps**: Nested test steps with timing information
- **Artifact Storage**: Upload screenshots, videos, traces, and logs
- **Real-time Updates**: SignalR notifications for live test status
- **Rich Visualization**: Interactive test case viewer with expandable step tree

### Quick Start

#### 1. Upload Test Results via Client Library

```csharp
using Agenix.PlaywrightGrid.Client;

var gridService = new PlaywrightGridService(new PlaywrightGridClientOptions
{
    HubUrl = "http://localhost:5100",
    ApiKey = "your-api-key",
    ProjectKey = "your-project"
});

// Create a suite and run
var suite = await gridService.Suites.CreateAsync(new CreateSuiteDto
{
    SuiteName = "Login Tests",
    ProjectKey = "your-project"
});

var run = await gridService.TestRuns.CreateAsync(new CreateTestRunDto
{
    SuiteId = suite.SuiteId,
    RunName = "Nightly Run #42"
});

// Upload test case with hierarchical steps
var testCase = new TestCaseDetailDto
{
    RunId = run.RunId,
    TestId = "test-login-001",
    TestTitle = "Should login with valid credentials",
    Status = "passed",
    StartTime = DateTime.UtcNow.AddMinutes(-5),
    EndTime = DateTime.UtcNow,
    DurationMs = 300000,
    Steps = new List<TestStepDto>
    {
        new()
        {
            Title = "Navigate to /login",
            Category = "action",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            DurationMs = 1500,
            Steps = new List<TestStepDto>()
        },
        new()
        {
            Title = "Fill username input",
            Category = "action",
            StartTime = DateTime.UtcNow.AddMinutes(-4.98),
            DurationMs = 500,
            Steps = new List<TestStepDto>()
        }
    },
    Attachments = new List<TestAttachmentDto>()
};

await gridService.TestRuns.UploadTestCaseAsync(testCase);

// Upload screenshot artifact
await gridService.TestRuns.UploadArtifactFromFileAsync(
    run.RunId,
    testCase.TestId,
    "path/to/screenshot.png"
);
```

#### 2. View Results in Dashboard

Navigate to: `http://localhost:3001/{projectKey}_default/results/{runId}`

Click the **"Test Cases"** tab to view structured test results with:

- Hierarchical step tree (expandable/collapsible)
- Artifact gallery (screenshots, videos, traces)
- Timing information for each step
- Error details for failed tests

#### 3. Batch Upload (Multiple Tests)

```csharp
var batchRequest = new BatchTestResultsRequest
{
    Tests = new List<TestCaseDetailDto> { testCase1, testCase2, testCase3 }
};

var response = await gridService.TestRuns.UploadBatchTestResultsAsync(
    run.RunId,
    batchRequest
);

Console.WriteLine($"Uploaded {response.UploadedTests}/{response.TotalTests} tests");
```

### API Endpoints

- `POST /api/runs/{runId}/test-results/batch` - Upload multiple test results
- `POST /api/runs/{runId}/tests` - Upload single test result
- `GET /api/runs/{runId}/tests` - Get all test cases for a run
- `GET /api/runs/{runId}/tests/{testId}` - Get single test case
- `POST /api/runs/{runId}/tests/{testId}/artifacts` - Upload artifact (multipart/form-data)
- `GET /api/runs/{runId}/tests/{testId}/artifacts` - Get artifacts for test
- `GET /api/artifacts/{**path}` - Download artifact
- `DELETE /api/runs/{runId}/tests/{testId}` - Delete test case

### Configuration

Environment variables (docker-compose.yml or appsettings.json):

```bash
ARTIFACTS_STORAGE_PATH=/app/artifacts
ARTIFACT_MAX_SIZE_MB=100
HUB_URL=http://localhost:5100
```

### Example with curl

```bash
# Upload test result
curl -X POST http://localhost:5100/api/runs/test-run-123/tests \
  -H "Content-Type: application/json" \
  -H "X-Project-Key: your-project-key" \
  -d '{
    "runId": "test-run-123",
    "testId": "test-001",
    "testTitle": "Login Test",
    "status": "passed",
    "startTime": "2025-01-20T10:00:00Z",
    "endTime": "2025-01-20T10:05:00Z",
    "durationMs": 300000,
    "steps": [],
    "attachments": []
  }'

# Upload screenshot
curl -X POST http://localhost:5100/api/runs/test-run-123/tests/test-001/artifacts \
  -H "X-Project-Key: your-project-key" \
  -F "file=@screenshot.png"
```

### MVP Limitations

This is an MVP implementation. Production enhancements planned:

- Cloud storage (S3/Azure Blob) - currently uses local filesystem
- Advanced authentication/authorization per project
- Rate limiting and quota enforcement
- Artifact CDN and caching
- Performance optimization for large test suites (>1000 tests)
