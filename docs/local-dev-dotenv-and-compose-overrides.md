# Local development: .env support and Docker Compose overrides

This guide explains how to use a local `.env` file to configure the Hub and Worker during development, and how to leverage `docker-compose.override.yml` for easy local tweaks without changing the base Compose file.

Applies to:
- Hub (ASP.NET Core Minimal API)
- Worker (Worker Service)

Status: Available for local dev; safe to disable in production.

## Overview

For developer convenience, both Hub and Worker load environment variables from a `.env` file if present. This is intended for local runs (e.g., `dotnet run`). In containers and production-like environments, prefer passing explicit environment variables via Docker Compose, Kubernetes, or your orchestrator.

Key behaviors:
- On startup, each service scans for a `.env` file starting at the current working directory and walking up the directory tree.
- Non-empty `key=value` pairs are loaded into process environment variables.
- Existing environment variables are not overridden by default.
- You can disable this behavior with `DISABLE_DOTENV=1`.

## Precedence

1) Explicit environment variables (shell `export`, Compose `environment`, Kubernetes `env`) take precedence.
2) Values from `.env` are applied only for keys that are not already set in the environment.

## Disabling .env loading

Set `DISABLE_DOTENV=1` to disable application-level `.env` loading. This guard is checked at process startup by both Hub and Worker.

Examples:
- Shell: `export DISABLE_DOTENV=1`
- Compose: `environment: { DISABLE_DOTENV: "1" }`

## Example `.env` (at repo root)

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

Notes:
- Quotes around values are optional; simple values like `5200` can be unquoted.
- Empty lines and `#` comments are ignored.

## Running locally with dotnet

- Hub: `dotnet run --project hub/PlaywrightHub.csproj`
- Worker: `dotnet run --project worker/WorkerService.csproj`

If `.env` is present, variables not already set in your shell are applied automatically.

## Docker Compose overrides

Docker Compose supports a `docker-compose.override.yml` file that is applied alongside `docker-compose.yml`. Use this to adjust local ports, logging levels, or environment variables for local work without editing the base file.

Example `docker-compose.override.yml`:

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

Bring up the stack:

```bash
docker compose up --build
```

## Compose variable substitution vs application .env

Compose also supports an `.env` file for variable substitution inside compose files. This is separate from the application-level `.env` that Hub/Worker read on startup.

- Compose `.env` → used by Docker to substitute values into your compose YAML; results become container environment variables.
- Application `.env` → read by the application process at startup when running locally (e.g., `dotnet run`).

Guidance:
- Inside containers, prefer Compose (or orchestrator) environment variables. These will override anything in the app-level `.env` if both are present.
- When running outside containers, the app-level `.env` is a convenient way to avoid exporting many variables manually.

Examples:

```bash
# Use default .env next to docker-compose.yml for substitution
docker compose up -d

# Or specify a custom env file for substitution
docker compose --env-file .env.local up -d
```

## Troubleshooting

- Variables not taking effect
  - Confirm `DISABLE_DOTENV` is not set to `1`.
  - Check for typos in keys (they must match exactly, e.g., `HUB_URL`, `NODE_SECRET`).
  - Remember precedence: if a variable is already set in your shell or in Compose, the `.env` value will not override it.

- Worker cannot connect via WS
  - Ensure `PUBLIC_WS_HOST`/`PUBLIC_WS_PORT` are reachable from the client/test host (not just inside Docker network).

- Hub shows unhealthy
  - Verify `REDIS_URL` is correct and Redis is running. See `docker-compose.yml` for the default service name/port.

## See also

- Configuration Guide: configuration.md
- Docker Compose file: `docker-compose.yml`
- Pool Config Validator CLI: cli.md (and scripts under `scripts/`)
