# Local development: .env support and Docker Compose overrides

This guide explains how to use a local `.env` file to configure the Hub, Worker, and Dashboard during development, and how to leverage `docker-compose.override.yml` for easy local tweaks without changing the base Compose file.

**Applies to:**
- Hub (ASP.NET Core Minimal API)
- Worker (Worker Service)
- Dashboard (Blazor Server)

**Status:** Available for local dev; safe to disable in production.

---

## Table of Contents

1. [Quick Start with Startup Script](#quick-start-with-startup-script)
2. [Overview](#overview)
3. [Precedence](#precedence)
4. [Disabling .env loading](#disabling-env-loading)
5. [Complete .env reference](#complete-env-reference)
6. [Docker Compose override pattern](#docker-compose-override-pattern)
7. [Common local dev scenarios](#common-local-dev-scenarios)
8. [Running locally with dotnet](#running-locally-with-dotnet)
9. [Compose variable substitution vs application .env](#compose-variable-substitution-vs-application-env)
10. [Troubleshooting](#troubleshooting)
11. [See also](#see-also)

---

## Quick Start with Startup Script

For the fastest local development experience, use the provided startup script that launches all services in separate terminal tabs/windows.

### Prerequisites

1. **Start infrastructure services via Docker Compose:**
   ```bash
   docker compose up redis postgres prometheus grafana -d
   ```

2. **Ensure `.env` file exists at repository root** (provided in the repo by default).

### Running the startup script

You have two options depending on your preference:

#### Option 1: Separate Terminal Tabs (Recommended for native terminals)

Opens each service in its own terminal tab/window.

**macOS/Linux:**
```bash
bash scripts/run-local-dev.sh
```

**Windows (PowerShell):**
```powershell
.\scripts\run-local-dev.ps1
```

#### Option 2: Inline Mode (Recommended for IntelliJ IDEA / Rider)

Runs all services in the background from a single terminal. Perfect for running inside IntelliJ IDEA or Rider terminal.

**macOS/Linux:**
```bash
bash scripts/run-local-dev-inline.sh
```

**Features:**
- ✅ All services run in background
- ✅ Single terminal window
- ✅ Works perfectly in IntelliJ/Rider terminal
- ✅ Logs saved to `/tmp/pg-*.log`
- ✅ Press `Ctrl+C` to stop all services
- ✅ Auto-restart detection if any service crashes

### What the script does

1. ✅ Verifies Redis and PostgreSQL are running via Docker Compose
2. 🔨 Builds Hub, Dashboard, and Worker projects
3. 🚀 Opens 5 terminal tabs/windows:
   - **Tab 1**: Hub API (`http://localhost:5000`)
   - **Tab 2**: Dashboard (`http://localhost:3001`)
   - **Tab 3**: Worker 1 (Chromium, `ws://127.0.0.1:5200`)
   - **Tab 4**: Worker 2 (Chromium, `ws://127.0.0.1:5201`)
   - **Tab 5**: Worker 3 (Firefox+WebKit, `ws://127.0.0.1:5202`)

### Terminal tab support

- **macOS**: Uses `osascript` to open new Terminal.app tabs
- **Linux**: Detects and uses `gnome-terminal` or `konsole`
- **Windows**: Uses Windows Terminal (`wt.exe`) or fallback to separate PowerShell windows
- **Fallback**: If tab automation is unavailable, the script prints manual commands

### Manual launch (if tab automation doesn't work)

If the script cannot open tabs automatically, run these commands in separate terminals:

```bash
# Terminal 1: Hub
cd hub && dotnet run

# Terminal 2: Dashboard
cd dashboard && dotnet run

# Terminal 3: Worker1
export NODE_ID=worker1
export PUBLIC_WS_PORT=5200
export POOL_CONFIG=AppB:Chromium:UAT=3
cd worker && dotnet run

# Terminal 4: Worker2
export NODE_ID=worker2
export PUBLIC_WS_PORT=5201
export POOL_CONFIG=AppB:Chromium:UAT=3
cd worker && dotnet run

# Terminal 5: Worker3
export NODE_ID=worker3
export PUBLIC_WS_PORT=5202
export POOL_CONFIG=AppB:Firefox:UAT=2,AppB:Webkit:UAT=2
cd worker && dotnet run
```

**Windows (PowerShell):**
```powershell
# Use $env:VARIABLE_NAME=value instead of export
$env:NODE_ID="worker1"
$env:PUBLIC_WS_PORT="5200"
# ... etc
```

### Stopping services

**Option 1 (Separate tabs):** Press `Ctrl+C` in each tab/window, or use:

**macOS/Linux:**
```bash
pkill -f 'dotnet run'
```

**Windows (PowerShell):**
```powershell
Get-Process -Name dotnet | Where-Object { $_.CommandLine -like '*dotnet run*' } | Stop-Process
```

**Option 2 (Inline mode):** Press `Ctrl+C` in the terminal where you ran the script. All services will be stopped automatically.

### Viewing logs (Inline mode)

When using inline mode, logs are written to `/tmp/` directory:

```bash
# View logs in real-time
tail -f /tmp/pg-hub.log        # Hub logs
tail -f /tmp/pg-dashboard.log  # Dashboard logs
tail -f /tmp/pg-worker1.log    # Worker 1 logs
tail -f /tmp/pg-worker2.log    # Worker 2 logs
tail -f /tmp/pg-worker3.log    # Worker 3 logs

# View all logs together
tail -f /tmp/pg-*.log
```

### Customizing worker configuration

Edit `.env` at the repository root to customize worker pools, browser versions, or other settings. Changes require restarting the services.

**Example** (change Worker 3 to all Chromium):
```bash
# In .env
WORKER3_POOL_CONFIG=AppB:Chromium:UAT=4
```

### Troubleshooting the startup script

**Error: "Redis is not running" or "PostgreSQL is not running"**

Start infrastructure services first:
```bash
docker compose up redis postgres prometheus grafana -d
```

**Error: "Failed to build Hub/Dashboard/Worker"**

Check for compilation errors:
```bash
dotnet build hub/PlaywrightHub.csproj
dotnet build dashboard/Dashboard.csproj
dotnet build worker/WorkerService.csproj
```

**Services start but can't connect to each other**

Verify `.env` configuration:
- `HUB_URL=http://localhost:5000` (for Workers)
- `HUB_SIGNALR=http://localhost:5000/ws` (for Dashboard)
- `REDIS_URL=localhost:6379` (not `redis:6379` when running locally)

---

## Overview

For developer convenience, both Hub and Worker load environment variables from a `.env` file if present. This is intended for local runs (e.g., `dotnet run`). In containers and production-like environments, prefer passing explicit environment variables via Docker Compose, Kubernetes, or your orchestrator.

**Key behaviors:**
- On startup, each service scans for a `.env` file starting at the current working directory and walking up the directory tree.
- Non-empty `key=value` pairs are loaded into process environment variables.
- Existing environment variables are not overridden by default.
- You can disable this behavior with `DISABLE_DOTENV=1`.

---

## Precedence

1. **Explicit environment variables** (shell `export`, Compose `environment`, Kubernetes `env`) take precedence.
2. **Values from `.env`** are applied only for keys that are not already set in the environment.

---

## Disabling .env loading

Set `DISABLE_DOTENV=1` to disable application-level `.env` loading. This guard is checked at process startup by both Hub and Worker.

**Examples:**
```bash
# Shell
export DISABLE_DOTENV=1

# Docker Compose
environment:
  - DISABLE_DOTENV=1
```

---

## Complete .env reference

Below is a comprehensive `.env` template with all variables from `docker-compose.yml` and additional options documented in `configuration.md`.

### Example `.env` (at repo root)

```bash
# ==============================================================================
# INFRASTRUCTURE
# ==============================================================================

# Playwright version (used as build arg for worker images)
PLAYWRIGHT_VERSION=1.54.2

# PostgreSQL (for local development)
POSTGRES_PASSWORD=postgres
POSTGRES_USER=postgres
POSTGRES_DB=playwrightgrid


# ==============================================================================
# HUB CONFIGURATION
# ==============================================================================

# Redis connection
REDIS_URL=redis:6379

# Secrets for authentication
HUB_RUNNER_SECRET=runner-secret
HUB_NODE_SECRET=node-secret

# Results storage backend: memory | redis | sqlite | postgres
HUB_RESULTS_BACKEND=postgres

# PostgreSQL connection string (when HUB_RESULTS_BACKEND=postgres)
HUB_RESULTS_POSTGRES=Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid

# Bootstrap configuration (creates default admin user on first run)
HUB_BOOTSTRAP_ENABLED=1
HUB_BOOTSTRAP_ADMIN_USER=admin
HUB_BOOTSTRAP_ADMIN_PASSWORD=agenix-admin
HUB_BOOTSTRAP_DEFAULT_PROJECT=admin_default
HUB_BOOTSTRAP_ADMIN_EMAIL=agenix.admin@domain.com

# Label matching behavior
HUB_BORROW_TRAILING_FALLBACK=true
HUB_BORROW_PREFIX_EXPAND=true
HUB_BORROW_WILDCARDS=false

# Retention policies (optional; defaults apply if not set)
# HUB_RESULTS_TTL_DAYS=30
# HUB_LOGS_TTL_DAYS=7

# Request limits (optional; use defaults for most cases)
# HUB_MAX_CONTROL_BODY_BYTES=65536
# HUB_MAX_LOG_BODY_BYTES=1048576

# Timeouts (optional; use defaults for most cases)
# HUB_REQUEST_HEADERS_TIMEOUT_SECONDS=15
# HUB_KEEP_ALIVE_TIMEOUT_SECONDS=30
# HUB_REQUEST_TIMEOUT_SECONDS=60


# ==============================================================================
# WORKER CONFIGURATION
# ==============================================================================

# Hub connection
HUB_URL=http://127.0.0.1:5100
# For containers: HUB_URL=http://hub:5000

# Redis connection (reuse from hub or override)
# REDIS_URL=localhost:6379

# Node identification
NODE_ID=worker-local
NODE_SECRET=node-secret
NODE_NODE_SECRET=node-node-secret

# Pool configuration (label:capacity mapping)
# Examples:
#   Single browser: POOL_CONFIG=AppB:Chromium:UAT=3
#   Multi-browser:  POOL_CONFIG=AppB:Chromium:UAT=2,AppB:Firefox:UAT=1
#   With regions:   POOL_CONFIG=AppB:Chromium:UAT:EU=2,AppB:Chromium:UAT:US=1
POOL_CONFIG=AppB:Chromium:UAT=2,AppB:Firefox:UAT=1

# Node metadata
NODE_REGION=local
# NODE_OS=linux  # Optional override; auto-detected by default

# Public WebSocket endpoint (for client connections)
PUBLIC_WS_HOST=127.0.0.1
PUBLIC_WS_PORT=5200
PUBLIC_WS_SCHEME=ws

# Playwright version (should match build arg)
PLAYWRIGHT_VERSION=1.54.2

# Browser-specific launch arguments
# Chromium args (space-separated, comma-separated, or JSON array)
CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox --no-proxy-server --disable-ipv6 --disable-quic --disable-http2 --disable-features=UseDNSHttpsSvcb

# Firefox args and preferences
# FIREFOX_ARGS=--headless
# FIREFOX_PREFS={"network.dns.disablePrefetch":true,"browser.cache.disk.enable":false}

# WebKit args
# WEBKIT_ARGS=--no-sandbox --disable-http2

# Playwright server debugging
# PLAYWRIGHT_SERVER_DEBUG=1  # Enable debug logs from Playwright server
# PLAYWRIGHT_SERVER_DEBUG=pw:server,pw:protocol  # Verbose protocol logs

# WebSocket configuration (optional; defaults are usually fine)
# WS_MAX_MESSAGE_BYTES=2097152
# WS_IDLE_TIMEOUT_SECONDS=60
# WS_PING_INTERVAL_SECONDS=15
# WS_COMPRESSION=auto
# WS_COMPRESSION_MIN_BYTES=1024

# Sidecar backoff configuration (optional)
# SIDECAR_BACKOFF_MIN_SECONDS=1
# SIDECAR_BACKOFF_MAX_SECONDS=30
# SIDECAR_BACKOFF_MULTIPLIER=2.0


# ==============================================================================
# DASHBOARD CONFIGURATION
# ==============================================================================

# SignalR WebSocket endpoint for real-time updates
HUB_SIGNALR=http://hub:5000/ws
# For local dotnet run: HUB_SIGNALR=http://127.0.0.1:5100/ws


# ==============================================================================
# LOGGING (applies to all services)
# ==============================================================================

# Log level: Trace | Debug | Information | Warning | Error | Critical
LOG_LEVEL=Information

# Category-specific overrides (comma-separated)
LOG_LEVEL_OVERRIDES=Microsoft.AspNetCore=Warning,System.Net.Http.HttpClient=Warning

# OpenTelemetry (optional)
# ENABLE_OTLP=1
# OTLP_ENDPOINT=http://localhost:4317
```

### Notes on values

- **Quotes:** Quotes around values are optional; simple values like `5200` can be unquoted.
- **Empty lines:** Empty lines and `#` comments are ignored.
- **Arrays:** Browser args can be specified as:
  - Space-separated: `--flag1 --flag2`
  - Comma-separated: `--flag1,--flag2`
  - JSON array: `["--flag1","--flag2"]`
- **JSON objects:** Firefox prefs must be valid JSON: `{"key":value,"key2":value2}`

---

## Docker Compose override pattern

You can use `docker-compose.override.yml` (automatically loaded by Docker Compose) to customize services for local development without modifying the base `docker-compose.yml`.

### Example: Override worker1 configuration

Create `docker-compose.override.yml` in the repo root:

```yaml
services:
  worker1:
    environment:
      - POOL_CONFIG=AppB:Chromium:UAT=5
      - PLAYWRIGHT_SERVER_DEBUG=1
      - LOG_LEVEL=Debug
    ports:
      - "5200:5000"
      - "9229:9229"  # Node.js debugger port

  hub:
    environment:
      - HUB_BORROW_WILDCARDS=true
      - LOG_LEVEL=Debug
```

### Example: Disable a service locally

```yaml
services:
  worker3:
    profiles:
      - disabled  # Worker3 won't start unless explicitly requested
```

### Best practices

- `docker-compose.override.yml` is **git-ignored by default** (check your `.gitignore`)
- Use overrides for:
  - Local debugging settings
  - Different port mappings
  - Development-specific environment variables
  - Volume mounts for hot reload
- Commit a `docker-compose.override.example.yml` for team reference

---

## Common local dev scenarios

### 1. Switch results backend to memory (fastest for local testing)

```bash
# In .env or docker-compose.override.yml
HUB_RESULTS_BACKEND=memory
```

### 2. Run multiple workers with different pools

**docker-compose.override.yml:**
```yaml
services:
  worker1:
    environment:
      - POOL_CONFIG=AppA:Chromium:Dev=3

  worker2:
    environment:
      - POOL_CONFIG=AppB:Firefox:Dev=2,AppB:Webkit:Dev=1

  worker3:
    environment:
      - POOL_CONFIG=AppC:Chromium:Staging=5
```

### 3. Debug Playwright protocol messages

```bash
# In .env or worker environment
PLAYWRIGHT_SERVER_DEBUG=pw:server,pw:protocol
LOG_LEVEL=Debug
```

This will output detailed protocol messages to worker logs, useful for troubleshooting WebSocket issues.

### 4. Customize bootstrap admin credentials

```bash
# In .env
HUB_BOOTSTRAP_ENABLED=1
HUB_BOOTSTRAP_ADMIN_USER=localadmin
HUB_BOOTSTRAP_ADMIN_PASSWORD=devpass123
HUB_BOOTSTRAP_ADMIN_EMAIL=dev@localhost
```

**Note:** Bootstrap only runs on first startup when the database is empty.

### 5. Test with different Playwright versions

```bash
# In .env or as shell variable
PLAYWRIGHT_VERSION=1.48.0

# Rebuild worker images
docker-compose build worker1 worker2 worker3

# Start services
docker-compose up -d
```

### 6. Disable .env loading in containers

```yaml
# docker-compose.override.yml
services:
  hub:
    environment:
      - DISABLE_DOTENV=1

  worker1:
    environment:
      - DISABLE_DOTENV=1
```

This forces containers to only use explicitly declared environment variables.

---

## Running locally with dotnet

For running services directly via `dotnet run` (without containers):

### Hub

```bash
# Set environment variables in your shell
export REDIS_URL=localhost:6379
export HUB_RUNNER_SECRET=runner-secret
export HUB_NODE_SECRET=node-secret
export HUB_RESULTS_BACKEND=memory  # Or postgres with connection string

# Or rely on .env file at repo root
cd /path/to/repo
dotnet run --project hub/PlaywrightHub.csproj
```

**Hub will be available at:** `http://localhost:5000`

### Worker

```bash
export HUB_URL=http://localhost:5000
export REDIS_URL=localhost:6379
export NODE_ID=worker-local
export NODE_SECRET=node-secret
export POOL_CONFIG=AppB:Chromium:UAT=2
export PUBLIC_WS_HOST=127.0.0.1
export PUBLIC_WS_PORT=5200

dotnet run --project worker/WorkerService.csproj
```

**Worker WebSocket will be available at:** `ws://127.0.0.1:5200/ws/{browserId}`

### Dashboard

```bash
export HUB_SIGNALR=http://localhost:5000/ws

dotnet run --project dashboard/Dashboard.csproj
```

**Dashboard will be available at:** `http://localhost:3001`

### Using .env with dotnet run

If `.env` is present at the repo root, variables **not already set** in your shell are applied automatically. You don't need to export them manually.

**Example workflow:**

1. Create `.env` at repo root with all variables
2. Open terminal, navigate to repo root
3. Run `dotnet run --project hub/PlaywrightHub.csproj` (Hub loads .env)
4. Open another terminal, run `dotnet run --project worker/WorkerService.csproj` (Worker loads .env)
5. Open third terminal, run `dotnet run --project dashboard/Dashboard.csproj` (Dashboard loads .env)

---

## Compose variable substitution vs application .env

Compose also supports an `.env` file for variable substitution inside compose files. This is separate from the application-level `.env` that Hub/Worker read on startup.

- **Compose `.env`** → used by Docker to substitute values into your compose YAML; results become container environment variables.
- **Application `.env`** → read by the application process at startup when running locally (e.g., `dotnet run`).

**Guidance:**
- Inside containers, prefer Compose (or orchestrator) environment variables. These will override anything in the app-level `.env` if both are present.
- When running outside containers, the app-level `.env` is a convenient way to avoid exporting many variables manually.

**Examples:**

```bash
# Use default .env next to docker-compose.yml for substitution
docker compose up -d

# Or specify a custom env file for substitution
docker compose --env-file .env.local up -d
```

---

## Troubleshooting

### Variables not taking effect
- Confirm `DISABLE_DOTENV` is not set to `1`.
- Check for typos in keys (they must match exactly, e.g., `HUB_URL`, `NODE_SECRET`).
- Remember precedence: if a variable is already set in your shell or in Compose, the `.env` value will not override it.

### Worker cannot connect via WS
- Ensure `PUBLIC_WS_HOST`/`PUBLIC_WS_PORT` are reachable from the client/test host (not just inside Docker network).

### Hub shows unhealthy
- Verify `REDIS_URL` is correct and Redis is running. See `docker-compose.yml` for the default service name/port.

### `docker compose down -v --rmi all --remove-orphans` says: `Image redis:7 Resource is still in use`
- **Why it happens:** one or more containers outside this Compose project still reference the `redis:7` image (even if stopped). Docker refuses to remove an image that any container uses.
- **Quick checks/fix:**
  - List containers using `redis:7`:
    ```bash
    docker ps -a --filter ancestor=redis:7
    ```
  - Remove them (careful — this force-removes those containers):
    ```bash
    docker rm -f $(docker ps -aq --filter ancestor=redis:7)
    ```
  - Retry your teardown:
    ```bash
    docker compose down -v --rmi all --remove-orphans
    ```
  - If you truly want to remove the base image too (and it is no longer used):
    ```bash
    docker image rm redis:7
    ```
  - Optional house-cleaning (dangerous; removes unused resources globally):
    ```bash
    docker image prune -a
    docker container prune
    docker volume prune
    docker network prune
    ```
- **One-liner** (safe-ish; no-op if nothing matches):
  ```bash
  docker ps -aq --filter ancestor=redis:7 | xargs -r docker rm -f && docker image rm redis:7 || true
  ```
- **Helper script** in this repo:
  ```bash
  bash scripts/compose-clean.sh  # Prompts before destructive actions
  bash scripts/compose-clean.sh --force --prune  # Skip prompts and prune
  ```

---

## See also

- **[scripts/run-local-dev.sh](https://github.com/agenixframework/agenix-playwright-grid/blob/main/scripts/run-local-dev.sh)** – Bash startup script (separate tabs) for local development (macOS/Linux)
- **[scripts/run-local-dev-inline.sh](https://github.com/agenixframework/agenix-playwright-grid/blob/main/scripts/run-local-dev-inline.sh)** – Bash startup script (inline mode) for IntelliJ/Rider (macOS/Linux)
- **[scripts/run-local-dev.ps1](https://github.com/agenixframework/agenix-playwright-grid/blob/main/scripts/run-local-dev.ps1)** – PowerShell startup script for local development (Windows)
- **[.env](https://github.com/agenixframework/agenix-playwright-grid/blob/main/.env)** – Root-level environment configuration for local development
- **[configuration.md](configuration.md)** – Comprehensive reference for all environment variables with detailed explanations, defaults, and constraints
- **[docker-compose.yml](https://github.com/agenixframework/agenix-playwright-grid/blob/main/docker-compose.yml)** – Production-like container configuration
- **[README.md](https://github.com/agenixframework/agenix-playwright-grid/blob/main/README.md)** – Quick start guide and architecture overview
- **[cli.md](cli.md)** – Pool Config Validator CLI and scripts under `scripts/`
- **[scripts/compose-clean.sh](https://github.com/agenixframework/agenix-playwright-grid/blob/main/scripts/compose-clean.sh)** – Cleanup helper script
