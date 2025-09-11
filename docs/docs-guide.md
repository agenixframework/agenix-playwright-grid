# Playwright Grid – Overview & Quick Start

A lightweight, scalable grid for borrowing Playwright browser sessions over WebSocket.

Components
- Hub: capacity broker backed by Redis; exposes HTTP API and SignalR feed
- Worker: manages Playwright sidecar servers and proxies WS connections for borrowed browsers
- Dashboard: Blazor UI for runs/results and live logs via SignalR
- HubClient: thin .NET client used by test runners to borrow and forward logs

Prerequisites
- Docker Desktop or compatible engine and Docker Compose
- .NET 8 SDK (for building source and running tests)

Quick start (Docker Compose)
1) Start the full stack
```bash
docker compose up --build
```
Default host ports
- Hub: http://127.0.0.1:5100 (container 5000)
- Workers (public WS): 5200..5202
- Dashboard: http://127.0.0.1:3001
- Redis: localhost:6379

2) Verify health
- Hub health: curl http://127.0.0.1:5100/health
- Open the Dashboard: http://127.0.0.1:3001

Borrow a browser from a test (C# example)
```csharp
using Agenix.PlaywrightGrid.HubClient;
using Microsoft.Playwright;

var hubUrl = Environment.GetEnvironmentVariable("HUB_URL") ?? "http://127.0.0.1:5100";
var secret = Environment.GetEnvironmentVariable("HUB_RUNNER_SECRET") ?? "runner-secret";

var client = new HubClient(hubUrl, secret);
var labels = new[] { "AppB:Chromium:UAT" }; // label key segments: App:Browser:Env[:Region[:OS…]]

var borrow = await client.BorrowAsync(labels);
try
{
    using var playwright = await Playwright.CreateAsync();
    // Connect to worker-exposed ws endpoint for this browserId
    await using var browser = await playwright.Chromium.ConnectAsync(borrow.WebSocketEndpoint);
    var page = await browser.NewPageAsync();
    await page.GotoAsync("https://playwright.dev");
}
finally
{
    // No explicit return needed; the Hub auto-finishes/auto-returns this session.
}
```

Essential configuration (highlights)
- Secrets
  - HUB_RUNNER_SECRET: header x-hub-secret for runner actions (borrow/return, logs)
  - HUB_NODE_SECRET: worker registration and node actions
- Labels and routing
  - Keys are ordered segments joined by ':' with Browser second: App:Browser:Env[:Region[:OS…]]
  - Matching supports exact, trailing fallback, prefix expansion; wildcards optional
- Logging levels (new)
  - LOG_LEVEL controls the global minimum level
  - LOG_LEVEL_OVERRIDES applies per-category filters, e.g., "WorkerService=Debug,Microsoft.AspNetCore=Warning"
  - Standard .NET keys also work: Logging__LogLevel__Default and Logging__LogLevel__{Category}

Example: docker-compose logging overrides
```yaml
services:
  hub:
    environment:
      LOG_LEVEL: Information
      LOG_LEVEL_OVERRIDES: "Microsoft.AspNetCore=Warning"
  worker1:
    environment:
      LOG_LEVEL: Debug
      LOG_LEVEL_OVERRIDES: "WorkerService=Debug,Microsoft=Warning"
  dashboard:
    environment:
      LOG_LEVEL: Information
```

Running tests
- Unit tests (fast):
  - dotnet test WorkerService.Tests/WorkerService.Tests.csproj -c Debug
- Full solution (uses Testcontainers to provision Hub/Workers/Redis):
  - dotnet test PlaywrightGrid.sln -c Debug
- Attach to a locally running grid instead of Testcontainers:
```bash
export GRID_TESTS_USE_LOCAL=1
export HUB_URL=http://127.0.0.1:5100
export HUB_RUNNER_SECRET=runner-secret
dotnet test tests/GridTests.csproj -c Debug
```

Troubleshooting
- GridTests timing out: ensure Docker is running; increase GRID_TESTS_HEALTH_TIMEOUT_SECONDS; consider GRID_TESTS_REUSE=1
- WS connect failures: verify PUBLIC_WS_HOST/PORT are reachable from the test host; check worker logs
- Capacity unavailable: confirm POOL_CONFIG provides capacity for requested labels
- Secrets mismatch: verify HUB_RUNNER_SECRET/HUB_NODE_SECRET across services

Next steps
- Configuration details and environment variables: configuration.md
- Label matching: Label-Matching.md
- Dashboard guide: ui-user-guide.md
- Improvement roadmap: tasks.md
