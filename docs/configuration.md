# Configuration Guide

This guide consolidates configuration for Hub, Worker, Dashboard, tests, and Docker. It also highlights defaults, ranges, and common pitfalls.

For a quick start, see docs-guide.md and docker-compose.yml in the repo root.

## Hub

Environment variables:
- REDIS_URL: Redis host:port (default redis:6379)
- HUB_RUNNER_SECRET: Shared secret runners must send via header x-hub-secret
- HUB_NODE_SECRET: Shared secret workers must send for /node/register
- HUB_BORROW_TRAILING_FALLBACK: Enable trailing fallback label matching (true/false; default true)
- HUB_BORROW_PREFIX_EXPAND: Enable prefix expansion (true/false; default true)
- HUB_BORROW_WILDCARDS: Enable explicit wildcards in label matching (true/false; default false)
  - Per‑environment overrides supported: append _{EnvironmentName}, e.g., HUB_BORROW_WILDCARDS_Production=true. Overrides apply to each flag independently.
- HUB_RESULTS_BACKEND: memory (default), redis, sqlite, or postgres. Selects the results store adapter used by the Hub.
- HUB_RESULTS_TTL_SECONDS / HUB_RESULTS_TTL_DAYS: Retention TTL for run summaries and tests (Redis and Postgres adapters support TTL).
- HUB_LOGS_TTL_SECONDS / HUB_LOGS_TTL_DAYS: Retention TTL for command logs (Redis and Postgres adapters support TTL).
  - Legacy: HUB_RESULTS_RETENTION_DAYS is still accepted for backward compatibility (applies to runs/tests/logs if specific TTLs are not set).
- HUB_RESULTS_SQLITE: Connection string/path for sqlite backend (default "Data Source=results.db").
- HUB_RESULTS_POSTGRES: Connection string for Postgres backend (default in compose: Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid).
- HUB_MAX_CONTROL_BODY_BYTES: Max body size for control endpoints (borrow/return/register/test), default 65536 (64 KiB), clamp 8 KiB..1 MiB.
- HUB_MAX_LOG_BODY_BYTES: Max body size for log endpoints (/results/browser/.../commands and /api-logs), default 1048576 (1 MiB), clamp 8 KiB..16 MiB.
- HUB_REQUEST_HEADERS_TIMEOUT_SECONDS: Request headers timeout for Kestrel, default 15s, clamp 5..120.
- HUB_KEEP_ALIVE_TIMEOUT_SECONDS: Keep-alive timeout for Kestrel, default 30s, clamp 5..300.
- HUB_REQUEST_TIMEOUT_SECONDS: Default per-request timeout enforced via middleware, default 60s, clamp 5..600.

Notes on limits:
- Requests exceeding the configured per-path body limits are rejected with HTTP 413 (application/problem+json).
- Reasonable defaults are chosen to protect the Hub; increase cautiously if your clients send larger batches of logs.

Ports:
- HTTP: container 5000 (default host 5100 in docker-compose.yml)

Notes:
- Health: GET /health should return 200 OK if Hub is up and Redis reachable.
- Diagnostics are surfaced via dashboard and APIs documented elsewhere.

## Worker

Core:
- HUB_URL: Hub base URL (default http://hub:5000)
- REDIS_URL: Redis host:port (default redis:6379)
- NODE_ID: Unique identifier for the worker (default generated node-xxxxxxxx)
- NODE_SECRET: Secret for node registration/authentication with Hub
- NODE_NODE_SECRET: Secondary secret for node-to-node scenarios (optional)
- POOL_CONFIG: Comma-separated label-to-count mapping, e.g. AppA:Chromium:Staging=3,AppB:Firefox:UAT=2
- NODE_REGION: Added to labels; shows up in Dashboard (default local)
- NODE_OS: Optional override for detected OS label

Playwright sidecar:
- PLAYWRIGHT_SIDECAR: Script launching the Node sidecar (default launch_playwright_server.js)
- PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS: Wait for wsEndpoint on stdout (default 60; clamp 5..600)
- PLAYWRIGHT_SERVER_DEBUG: Set to 1 or pw:server,pw:protocol for Node sidecar DEBUG logs
- WORKER_VALIDATE_WS: If "true", validate internal wsEndpoint on warmup/replacement (one restart allowed)
- CHROMIUM_ARGS: Space-, comma-separated, or JSON array of Chromium flags. Applied only for Chromium.
- CHROME_ARGS: Alias for CHROMIUM_ARGS when CHROMIUM_ARGS is not set (backward-compat convenience).
- WEBKIT_ARGS: Space-, comma-separated, or JSON array of flags passed when launching WebKit.
- FIREFOX_ARGS: Optional extra arguments for Firefox launch (limited effect; most tuning is via prefs).
- FIREFOX_PREFS: Firefox user prefs as JSON object or as key=value pairs separated by comma/semicolon/newlines.
  - Validation: malformed entries are ignored; keys must be non-empty (dot-separated recommended), values are coerced
    to boolean/number when obvious (true/false, numeric), otherwise treated as strings.

Examples:
```yaml
services:
  worker3:
    environment:
      - WEBKIT_ARGS=--no-sandbox --disable-http2
      - FIREFOX_ARGS=--headless
      - FIREFOX_PREFS={"network.dns.disablePrefetch":true,"browser.cache.disk.enable":false}
```

Public WebSocket address:
- PUBLIC_WS_HOST / PUBLIC_WS_PORT / PUBLIC_WS_SCHEME: Advertised host, port, scheme (default ws). If unset, the node id may be used inside the cluster network.

WebSocket limits and backpressure:
- WS_MAX_MESSAGE_BYTES: Max message size (default 2,097,152; clamp 8 KiB..16 MiB)
- WS_IDLE_TIMEOUT_SECONDS: Idle timeout (default 60; clamp 5..600)
- WS_PING_INTERVAL_SECONDS: Keepalive ping interval (default 15; clamp 5..300)
- WS_LOG_CHANNEL_CAPACITY: Capacity for async log forwarding channel (default 256; clamp 16..8192)
- WS_LOG_DROP_POLICY: DropNewest (default) or DropOldest when log channel is full
- WS_PROXY_CHANNEL_CAPACITY: Capacity for WS proxy frame channels (default 1024; clamp 32..65536)
- WS_PROXY_DROP_POLICY: DropNewest (default) or DropOldest for proxy channels

Restart/backoff for sidecar replacement:
- SIDECAR_BACKOFF_MIN_SECONDS: Minimum delay (default 1; clamp 1..120)
- SIDECAR_BACKOFF_MAX_SECONDS: Maximum delay (default 30; clamp 1..600; coerced to >= min)
- SIDECAR_BACKOFF_MULTIPLIER: Growth factor (default 2.0; clamp 1.1..5.0)
- SIDECAR_BACKOFF_FAILURE_RESET_SECONDS: Reset window (default 60; clamp 10..600)

Redis timeouts (optional fine-tuning):
- REDIS_CONNECT_TIMEOUT_MS (default 5000)
- REDIS_SYNC_TIMEOUT_MS (default 5000)
- REDIS_ASYNC_TIMEOUT_MS (default 5000)
- REDIS_HEALTH_TIMEOUT_MS (default 1000 for /health/ready ping)

Observability toggles:
- ENABLE_OTLP=1 to export OTLP traces/metrics
- ENABLE_PROMETHEUS_OTEL=1 to expose Prometheus via OpenTelemetry (in addition to prometheus-net)
- OTEL_EXPORTER_OTLP_ENDPOINT (default http://localhost:4317)
- OTEL_EXPORTER_OTLP_PROTOCOL: grpc (default) or http/protobuf

## Logging configuration

Global logging controls are available across Hub, Worker, and Dashboard via environment variables:
- LOG_LEVEL: sets the global minimum level (Trace|Debug|Information|Warning|Error|Critical|None)
- LOG_LEVEL_OVERRIDES: per-category overrides using `Category=Level` pairs; multiple pairs can be separated by comma, semicolon, or newlines
- Standard .NET alternatives are also supported: `Logging__LogLevel__Default` and `Logging__LogLevel__{Category}`

Examples
```yaml
services:
  hub:
    environment:
      LOG_LEVEL: Information
      LOG_LEVEL_OVERRIDES: "Microsoft.AspNetCore=Warning"
  worker1:
    environment:
      LOG_LEVEL: Debug
      LOG_LEVEL_OVERRIDES: |
        WorkerService=Debug
        Microsoft=Warning
  dashboard:
    environment:
      Logging__LogLevel__Default: Information
      Logging__LogLevel__Microsoft.AspNetCore: Warning
```

Notes
- Framework noise from `Microsoft.AspNetCore` is set to Warning by default; you can relax/tighten via overrides.
- Overrides are additive: the most specific category match applies.

## Dashboard
- HUB_SIGNALR: Hub base URL for SignalR (default http://hub:5000/ws)
- Port: default host 3001 in docker-compose.yml

## Tests (GridTests)
Env knobs used by integration suite:
- GRID_TESTS_SKIP_CONTAINERS=1: Don’t start Testcontainers (requires a running grid)
- GRID_TESTS_USE_LOCAL=1: Use an already running local grid (docker compose up)
- HUB_URL, HUB_RUNNER_SECRET: When attaching to local grid
- GRID_TESTS_FORCE_BUILD=1: Force docker build (default true)
- GRID_TESTS_REUSE=1, GRID_TESTS_SKIP_CLEANUP=1: Speed up iteration
- GRID_TESTS_HEALTH_TIMEOUT_SECONDS: Increase when building images on cold cache
- GRID_TESTS_RESULTS_BACKEND: redis (default) or postgres — when postgres, TestEnvironment provisions a PostgreSQL container and wires the Hub.
- GRID_TESTS_POSTGRES_IMAGE / GRID_TESTS_POSTGRES_DB / GRID_TESTS_POSTGRES_USER / GRID_TESTS_POSTGRES_PASSWORD: Optional overrides for the Postgres container (defaults: postgres:16-alpine / playwrightgrid / postgres / postgres).
- LABELS: Comma-/semicolon-/newline-separated label list for borrowing
- CONCURRENCY, ITERATIONS: Pressure test tuning (defaults 4 and 3)

## Examples

### Docker Compose (excerpt)
```yaml
services:
  hub:
    image: agenix/grid-hub:dev
    environment:
      REDIS_URL: redis:6379
      HUB_RUNNER_SECRET: runner-secret
      HUB_NODE_SECRET: node-secret
  worker1:
    image: agenix/grid-worker:dev
    environment:
      HUB_URL: http://hub:5000
      REDIS_URL: redis:6379
      NODE_ID: worker1
      NODE_SECRET: node-secret
      POOL_CONFIG: AppA:Chromium:Staging=3,AppB:Firefox:UAT=1
      NODE_REGION: local
      PUBLIC_WS_HOST: 127.0.0.1
      PUBLIC_WS_PORT: "5200"
      WS_MAX_MESSAGE_BYTES: "2097152"
      WS_IDLE_TIMEOUT_SECONDS: "60"
      WS_PING_INTERVAL_SECONDS: "15"
```

### Local run of tests against a composed grid
```bash
export GRID_TESTS_USE_LOCAL=1
export HUB_URL=http://127.0.0.1:5100
export HUB_RUNNER_SECRET=runner-secret
 dotnet test tests/GridTests.csproj -c Debug
```

## Common Pitfalls
- PUBLIC_WS_HOST/PORT must be reachable by the test host (not just inside Docker network).
- Ensure Node and Playwright versions inside the worker image are aligned with expectations; see worker logs for the detected Playwright version.
- If borrowing hangs, verify Hub /health is ready and that there is capacity for the requested labels.

## See also
- Worker Sidecar Management: worker-sidecar-management.md
- Metrics and Grafana: Metrics-and-Grafana.md
- Label Matching: Label-Matching.md


## Pool config validator

Before starting a Worker, you can validate POOL_CONFIG and see the effective capacity computed from your labels.

See the full CLI documentation: CLI Reference (cli.md).

- Bash (macOS/Linux):
  - scripts/validate-pool-config.sh --pool "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"
- PowerShell (Windows):
  - scripts/validate-pool-config.ps1 --pool "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"
- Or directly via dotnet:
  - dotnet run --project worker/WorkerService.csproj -- validate-pool-config --pool "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"

Notes
- If --pool is omitted, the validator reads POOL_CONFIG from the environment.
- Use --json for machine-readable output.
- Label keys are validated using the shared Domain rules (App:Browser:Env[:...], Browser as second segment); duplicates are normalized and summed.


## Local development: .env support and Compose overrides

For local developer convenience, both Hub and Worker now load environment variables from a .env file if present. This is optional and disabled in containers by default.

How it works
- On process start, a lightweight loader looks for a .env file starting from the current working directory and walking up. If found, non-empty key=value lines are applied to process environment variables.
- Existing environment variables are not overridden by default.
- You can disable this behavior by setting DISABLE_DOTENV=1 (useful in production or when variables must come exclusively from the host/container).

Precedence
1) Explicit environment variables (export, container env) take precedence.
2) Values from .env are applied only for keys not already set.

Example .env (repo root)
```
# Hub
REDIS_URL=redis:6379
HUB_RUNNER_SECRET=runner-secret
HUB_NODE_SECRET=node-secret
HUB_BORROW_TRAILING_FALLBACK=true
HUB_BORROW_PREFIX_EXPAND=true
HUB_BORROW_WILDCARDS=false

# Worker
HUB_URL=http://127.0.0.1:5100
REDIS_URL=localhost:6379
NODE_ID=worker-local
NODE_SECRET=node-secret
POOL_CONFIG=AppB:Chromium:UAT=2,AppB:Firefox:UAT=1
NODE_REGION=local
PUBLIC_WS_HOST=127.0.0.1
PUBLIC_WS_PORT=5200

# Logging
LOG_LEVEL=Information
LOG_LEVEL_OVERRIDES=Microsoft.AspNetCore=Warning
```

Disable .env loading
- Set DISABLE_DOTENV=1 to opt-out. This guard is checked on startup by both Hub and Worker.

Compose overrides and .env
- Docker Compose supports a docker-compose.override.yml file that is automatically applied alongside docker-compose.yml. Use it to tweak environment and ports for local work without editing the base file.
- Compose also supports a .env file for variable substitution within compose files. This is separate from the application-level .env loading described above.

Example docker-compose.override.yml (local ports and env)
```yaml
services:
  hub:
    ports:
      - "5100:5000"
    environment:
      LOG_LEVEL: Debug
  worker1:
    ports:
      - "5200:5200"
    environment:
      PUBLIC_WS_HOST: 127.0.0.1
      PUBLIC_WS_PORT: "5200"
      NODE_REGION: local
  dashboard:
    ports:
      - "3001:3001"
```

Using Compose with an env file for substitutions
- Put key=value pairs in a file and pass it via --env-file or rely on default .env next to your compose file:
```bash
# Uses default .env in the same directory
docker compose up -d

# Or specify a custom env file
docker compose --env-file .env.local up -d
```

Notes
- Application-level .env loading is a convenience for running Hub/Worker directly via dotnet run; inside containers you typically pass environment via compose or orchestrator.
- If both Compose substitution and application .env are used, be mindful of which layer sets variables first. Compose env becomes container environment, which takes precedence over app-level .env.
