# Playwright Grid – Project Development Guidelines (for contributors)

This document captures project-specific knowledge to accelerate development, debugging, and testing. It assumes proficiency with .NET, Docker, and Playwright.

## Solution layout and build

Projects
- hub (ASP.NET Core Minimal API + SignalR): capacity broker backed by Redis.
- worker (Worker Service): manages sidecar Playwright servers and proxies WS connections.
- dashboard (Blazor): run/result viewer and live SignalR feed.
- Agenix.PlaywrightGrid.HubClient: thin client for borrowing/returning sessions and forwarding logs.
- tests (GridTests): integration tests that exercise hub/worker/Redis via Docker Testcontainers or a locally running grid.
- WorkerService.Tests: unit tests targeting the worker’s internal logic (no external services).

Build targets
- .NET 8 (nullable enabled across main projects). Build everything from the root:
  - dotnet build PlaywrightGrid.sln -c Debug

Containerized dev stack (recommended for end-to-end)
- Prereqs: Docker Desktop or compatible engine, Docker Compose, .NET 8 SDK.
- Start full stack (Hub, 2+ Workers, Dashboard, Redis, Prometheus, Grafana):
  - docker compose up --build
- Default ports (host):
  - Hub: http://127.0.0.1:5100 (container 5000)
  - Workers: 5200..5202 (per Docker compose) for public WS
  - Dashboard: http://127.0.0.1:3001
  - Prometheus: http://127.0.0.1:9090, Grafana: http://127.0.0.1:3000
  - Redis: localhost:6379 (container name redis)

Key runtime environment variables
- Hub
  - REDIS_URL=redis:6379
  - HUB_RUNNER_SECRET=runner-secret (must be sent by clients via x-hub-secret)
  - HUB_NODE_SECRET=node-secret (for /node/register)
  - HUB_BORROW_TRAILING_FALLBACK=true | HUB_BORROW_PREFIX_EXPAND=true | HUB_BORROW_WILDCARDS=false
- Worker
  - HUB_URL=http://hub:5000
  - REDIS_URL=redis:6379, NODE_ID=worker1, NODE_SECRET=node-secret
  - POOL_CONFIG=AppA:Chromium:Staging=3,AppB:Firefox:UAT=1 (labelKey=count, comma-separated)
  - NODE_REGION=local (added to Labels)
  - PUBLIC_WS_HOST/PORT/SCHEME for client-facing ws://host:port/ws/{browserId}
  - PLAYWRIGHT_VERSION is reported/pinned by the sidecar; Playwright channels/args can be customized (CHROMIUM_ARGS etc.)
- Dashboard
  - HUB_SIGNALR=http://hub:5000/ws

Labels and routing
- Label keys are ordered segments joined by ':' and should keep Browser second for consistency, e.g. App:Browser:Env[:Region[:OS…]].
- Hub matching supports exact, trailing fallback, prefix expansion, and optional wildcards (see README.md).

## Testing

Test projects and frameworks
- WorkerService.Tests: NUnit unit tests. Fast, no external services.
- tests (GridTests): NUnit integration. Can self-provision Docker containers via DotNet.Testcontainers or attach to a locally running grid. Uses Agenix.PlaywrightGrid.HubClient + Microsoft.Playwright to borrow a browser and perform a simple navigation.

Running tests
- Unit tests only (fast path):
  - dotnet test WorkerService.Tests/WorkerService.Tests.csproj -c Debug
- Full solution (runs units + integration):
  - dotnet test PlaywrightGrid.sln -c Debug
  - During this session, fullSolution ran all suites successfully (15/15) by self-provisioning containers; this demonstrates the default integration flow works on a machine with Docker.

Integration test orchestrator (tests/TestEnvironment.cs)
- Testcontainers behavior is controlled via environment variables:
  - GRID_TESTS_SKIP_CONTAINERS=1 → don’t start containers; GridTests will mark tests Inconclusive if Hub health is not available.
  - GRID_TESTS_USE_LOCAL=1 → use an already running local grid (e.g., docker compose up) instead of Testcontainers.
    - Optionally set HUB_URL (default http://127.0.0.1:5100) and HUB_RUNNER_SECRET (default runner-secret).
  - GRID_TESTS_FORCE_BUILD=1 (default true) → force docker build of hub/worker images tagged gridtests/* for the run.
  - GRID_TESTS_REUSE=1 → reuse stable container names across runs to speed up iteration.
  - GRID_TESTS_SKIP_CLEANUP=1 → skip pre-flight cleanup of orphaned containers.
  - GRID_TESTS_HEALTH_TIMEOUT_SECONDS=120 → timeout when waiting for Hub /health.
- Notes:
  - If Docker is unavailable, TestEnvironment asserts Inconclusive automatically.
  - Images built for integration tests use stable tags gridtests/hub:dev and gridtests/worker:dev to favor reuse.

GridTests parameters and environment knobs
- Borrowed label set is configurable:
  - LABELS env var → comma/semi-colon/newline-separated list (e.g., LABELS="AppB:Chromium:UAT,AppB:Firefox:UAT").
  - Defaults cover Chromium/Firefox/WebKit on AppB:...:UAT when not overridden.
- Concurrency/iterations for pressure test can be tuned via env (or NUnit parameters):
  - CONCURRENCY (default 4), ITERATIONS (default 3).
- Example local run against a locally composed grid:
  - export GRID_TESTS_USE_LOCAL=1
  - export HUB_URL=http://127.0.0.1:5100
  - export HUB_RUNNER_SECRET=runner-secret
  - dotnet test tests/GridTests.csproj -c Debug

Adding a new test (demonstrated and validated in this session)
- For a quick demonstration, add a unit test under WorkerService.Tests:
  - File: WorkerService.Tests/DemoSanityTest.cs
    - using NUnit.Framework;
      
      namespace WorkerService.Tests;
      
      public class DemoSanityTest
      {
          [Test]
          public void Demo_Addition_Works()
          {
              Assert.That(2 + 2, Is.EqualTo(4));
          }
      }
- Run: dotnet test WorkerService.Tests/WorkerService.Tests.csproj
  - In this session the suite reported 13/13 passed with the demo test included.
- Clean up: remove the demo file when done to keep the repo minimal (we removed it at the end of validation).

Practical troubleshooting tips for tests
- GridTests not progressing / timing out:
  - Ensure Docker daemon is running; set GRID_TESTS_USE_LOCAL=1 to attach to a composed stack if preferred.
  - Increase GRID_TESTS_HEALTH_TIMEOUT_SECONDS when building images on cold cache or on slow networks.
  - Use GRID_TESTS_REUSE=1 to avoid rebuilds, and GRID_TESTS_SKIP_CLEANUP=1 to skip preflight removals during iteration.
- Browser connect failures within GridTests:
  - Verify workers have capacity for requested labels (POOL_CONFIG) and that PUBLIC_WS_HOST/PORT reflect host reachability from the test host.
  - Use Dashboard at http://127.0.0.1:3001 to inspect live runs and command logs.
- SELECTIVE execution:
  - Use environment filters rather than NUnit parameters for simplicity, e.g., LABELS=..., CONCURRENCY=..., ITERATIONS=....

## Development and debugging practices

Code style and conventions
- Nullable reference types are enabled in core projects (Hub, Worker, HubClient). Favor explicit nullability annotations.
- Prefer async/await end-to-end; avoid sync-over-async in Hub/Worker request paths.
- Keep label schema consistent: App:Browser:Env[:...], Browser second segment.
- Do not introduce runtime dependencies that require host-specific configuration into the Worker unless guarded by env variables (tests rely on deterministic env parsing in WorkerOptions.FromEnvironment()).

Observability
- Prometheus metrics exposed by Hub/Worker; Grafana dashboards are provisioned under provisioning/.
- Use TestContext.WriteLine/Progress in tests (already implemented) to surface timing and container logs; HubClient supports forwarding runner-side log lines via SendApiLogAsync/SetCurrentTestAsync.

Local iteration patterns
- For rapid cycles on integration flows without Testcontainers:
  - docker compose up --build
  - export GRID_TESTS_USE_LOCAL=1; export HUB_URL=http://127.0.0.1:5100; export HUB_RUNNER_SECRET=runner-secret
  - dotnet test tests/GridTests.csproj -c Debug
- When iterating Worker internals:
  - dotnet test WorkerService.Tests/WorkerService.Tests.csproj -c Debug

Notes on Playwright
- GridTests use Microsoft.Playwright to connect to worker-exposed ws endpoints. Playwright binaries are handled within worker containers and do not require local installation for the test host.
- If pinning or changing versions, keep PLAYWRIGHT_VERSION aligned across Dockerfiles and reported versions to avoid confusion in Dashboard run summaries.

## Verified commands (this session)
- dotnet build PlaywrightGrid.sln -c Debug
- dotnet test WorkerService.Tests/WorkerService.Tests.csproj → 12/12 passed (baseline)
- Added a demo test under WorkerService.Tests and re-ran → 13/13 passed; then removed the demo test
- dotnet test PlaywrightGrid.sln → 15/15 passed with Testcontainers provisioning

This file is the only change left from the validation process; all temporary additions were removed.
