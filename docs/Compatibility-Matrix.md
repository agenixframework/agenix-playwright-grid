# Compatibility Matrix

This page documents the currently verified Playwright versions and the Docker base images used by the Grid. It also provides guidance on how to pin/upgrade Playwright in Worker images and align client versions.

> Note
> The Grid uses a Node.js Playwright sidecar inside each Worker container and proxies WebSocket connections from clients. To avoid protocol/schema drift, the Playwright client used by your tests must match the sidecar version (major.minor alignment; patch differences are typically OK).

## Summary (verified in this repo)

- Default Playwright version: `1.54.2` (configurable via build arg and env)
- Hub base image: `mcr.microsoft.com/dotnet/aspnet:8.0`
- Worker base images:
  - Runtime: `mcr.microsoft.com/dotnet/aspnet:8.0`
  - Node layer: `node:20-bookworm-slim` (copied from builder image layer)
  - Linux distro: Debian Bookworm (Playwright system deps installed via `npx playwright install --with-deps`)

## Matrix

| Playwright sidecar (JS) | Microsoft.Playwright client | Worker Docker build arg | Worker env (reported) | Base runtime images | Notes |
|---|---|---|---|---|---|
| 1.54.x (default 1.54.2) | 1.54.x | `PLAYWRIGHT_VERSION=1.54.2` | `PLAYWRIGHT_VERSION=1.54.2` | dotnet/aspnet:8.0 + node:20-bookworm-slim | Verified via docker compose and integration tests. |

- Other Playwright minor versions likely work if you rebuild the Worker with the matching version and run with compatible clients; however, only the version(s) above are verified by this repository’s tests in CI.

## How version pinning works

- Worker image pins the Playwright JS package through a build argument and environment variable:
  - Dockerfile (worker):
    - `ARG PLAYWRIGHT_VERSION=1.54.2`
    - `ENV PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION}`
    - `npm i --no-save "playwright@${PLAYWRIGHT_VERSION}"`
    - `npx playwright install --with-deps`
- At runtime, Workers also expose `PLAYWRIGHT_VERSION` in environment and metrics to surface mismatches.
- Tests and local docker-compose set the same version for consistency.

## Upgrading Playwright safely

1. Choose a target minor version (e.g., 1.55.x).
2. Build Workers with the new version:
   - docker compose build --build-arg PLAYWRIGHT_VERSION=1.55.0 worker1 worker2 worker3
3. Set environment for containers to reflect the same version (for visibility):
   - `PLAYWRIGHT_VERSION=1.55.0`
4. Align your test client library:
   - Use Microsoft.Playwright 1.55.x in your test code to match the sidecar.
5. Validate end-to-end:
   - Run unit tests and integration tests. Check dashboard diagnostics for the reported Playwright version and ensure the mismatch metric is 0.

## Docker base image tags

- Hub: `mcr.microsoft.com/dotnet/aspnet:8.0`
- Worker: `mcr.microsoft.com/dotnet/aspnet:8.0` (runtime), plus Node from `node:20-bookworm-slim` copied into the runtime layer.
- Rationale: keeps images small, provides up-to-date security updates, and ensures Playwright’s system dependencies are satisfied on Debian Bookworm.

## Compose example (keeping versions aligned)

```yaml
services:
  worker1:
    build:
      context: .
      dockerfile: worker/Dockerfile
      args:
        PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
    environment:
      - PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION:-1.54.2}
```

Tip: Place `PLAYWRIGHT_VERSION=1.54.2` in a `.env` file to apply project‑wide.
