# Playwright Grid - AGENTS.md

This guide provides essential commands and conventions for agentic coding assistants working in this repository.

## Build Commands

### Build Entire Solution
```bash
dotnet build PlaywrightGrid.sln -c Debug
```

### Clean Solution
```bash
dotnet clean PlaywrightGrid.sln
```

## Test Commands

### Run All Tests
```bash
dotnet test PlaywrightGrid.sln -c Debug
```

### Run Unit Tests Only (Fast)
```bash
dotnet test WorkerService.Tests/WorkerService.Tests.csproj -c Debug
```

### Run Integration Tests
```bash
dotnet test Agenix.PlaywrightGrid.Integration.Tests/Agenix.PlaywrightGrid.Integration.Tests.csproj -c Debug
```

### Run Single Test by Name
```bash
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

dotnet test --filter "FullyQualifiedName~BorrowTtlSweeperServiceTests"
dotnet test --filter "FullyQualifiedName~CapacityQueueTests.CapacityQueue_EnqueuesCorrectly"
```

### Run Tests Matching Pattern
```bash
dotnet test --filter "FullyQualifiedName~CapacityQueue"
```

## Lint and Format Commands

### Check Formatting (CI will fail if issues found)
```bash
dotnet format --verify-no-changes --severity warn
```

### Auto-fix Formatting Issues
```bash
dotnet format --severity warn
```

### Mandatory Submission Steps
Before submitting any task, you MUST:
1. **Reformat Code**: Run `dotnet format --severity warn` to ensure the codebase remains consistent.
2. **Add License Headers**: Run `bash scripts/add-license-headers.sh` if new files were created.
3. **Verify Build**: Run `dotnet build PlaywrightGrid.sln -c Debug`.
4. **Verify Tests**: Run relevant tests (at least unit tests) to ensure no regressions.

### Add License Headers
```bash
bash scripts/add-license-headers.sh
```

## Code Style Guidelines

### Naming Conventions
- **Classes/Records**: `PascalCase` (`BrowserPoolService`, `LabelMatcher`)
- **Methods**: `PascalCase` (`BorrowAsync`, `ReturnBrowser`)
- **Properties**: `PascalCase` (`BrowserId`, `WebSocketEndpoint`)
- **Fields**: `camelCase` with underscore prefix (`_httpClient`, `_connectionString`)
- **Constants**: `PascalCase` (`DefaultTimeout`, `MaxRetryCount`)
- **Interfaces**: `PascalCase` with 'I' prefix (`IBrowserPool`, `ISessionManager`)
- **Parameters**: `camelCase` (`browserId`, `labelKey`)

### File Organization
- **File-scoped namespaces** (C# 12)
- **Primary constructors** where appropriate
- **Expression-bodied members** for simple implementations
- **Nullable reference types** enabled - use explicit nullability
- **License header** required on all C# files

### Using Statements
- System directives first, sorted alphabetically
- Then other namespaces, sorted alphabetically
- No blank lines between groups (`dotnet_separate_import_directive_groups = false`)

### Indentation and Formatting
- **Indentation**: 4 spaces for C#, 2 spaces for XML/JSON/YAML
- **Line endings**: LF (Unix-style)
- **Braces**: New line before open brace
- **Using statements**: Prefer simple using statements

### Documentation
- XML documentation required for public APIs
- Use `<summary>`, `<param>`, `<returns>`, `<exception>` tags
- Document complex algorithms and business logic

### Error Handling
- Prefer exceptions for exceptional conditions
- Validate parameters with `ArgumentNullException`, `ArgumentException`
- Use `Result<T>` pattern for expected failures where appropriate
- Log errors with context; include relevant IDs

### Async Patterns
- Use `async/await` end-to-end
- Avoid sync-over-async in Hub/Worker request paths
- Use `ConfigureAwait(false)` in library code
- Suffix async methods with `Async`

### Testing Patterns
- **Test framework**: NUnit
- **Test classes**: `{ClassName}Tests`
- **Test methods**: Descriptive names explaining the scenario
- **Structure**: Arrange-Act-Assert pattern for unit tests
- **Mocking**: Use Moq or NSubstitute
- **Assertions**: Use NUnit constraint-based assertions

## Architectural Principles (Domain-Driven Design)

The project follows a **layered architecture** with inward-pointing dependencies:

1.  **Domain Layer** (`Agenix.PlaywrightGrid.Domain/`): Pure business logic, entities, and value objects. **Zero external dependencies.**
2.  **Use Case Layer** (`hub/Application/`, `worker/Application/`): Application logic and interfaces (ports). Depends only on Domain.
3.  **Interface Layer** (`hub/Infrastructure/Web/`, `dashboard/`): API endpoints and UI components. Thin adapters to Use Cases.
4.  **Infrastructure Layer** (`hub/Infrastructure/Adapters/`): Implementation of interfaces using external tech (PostgreSQL, Redis, RabbitMQ).

## Project Structure

### Core Components
- **hub/**: ASP.NET Core Minimal API + SignalR (capacity broker, Redis-backed)
- **worker/**: Worker Service (manages Playwright servers, proxies WS connections)
- **dashboard/**: Blazor UI (run/result viewer, live SignalR feed)
- **ingestion/**: High-throughput event processing service (logs, commands)
- **housekeeping-service/**: Retention policy enforcement and data cleanup
- **Agenix.PlaywrightGrid.Client/**: Client library for borrowing/returning sessions
- **Agenix.PlaywrightGrid.Domain/**: Domain models and abstractions
- **Agenix.PlaywrightGrid.Shared/**: Shared utilities and helpers

### Test Projects
- **WorkerService.Tests**: Unit tests for worker logic (fast, no external services)
- **Agenix.PlaywrightGrid.Integration.Tests**: Integration tests (NUnit, uses Testcontainers)
- **Agenix.PlaywrightGrid.Domain.Tests**: Domain model tests
- **Agenix.PlaywrightGrid.Shared.Tests**: Shared utilities tests
- **Dashboard.Tests**: Blazor component tests
- **PlaywrightHub.Tests**: Hub API tests

### Docker Services (Traefik-enabled)
- **gateway**: http://localhost:8080 (Traefik), http://localhost:8081 (Dashboard)
- **hub**: http://hub.localhost, http://127.0.0.1:5100
- **dashboard**: http://dashboard.localhost, http://127.0.0.1:3001
- **ingestion**: http://ingestion.localhost, port 8082
- **housekeeping**: http://housekeeping.localhost, port 8083
- **prometheus**: http://prometheus.localhost, http://127.0.0.1:9090
- **grafana**: http://grafana.localhost, http://127.0.0.1:3000
- **mailpit**: http://mailpit.localhost, http://127.0.0.1:8025
- **rabbitmq**: http://rabbitmq.localhost, http://127.0.0.1:15672
- **minio**: http://minio.localhost, http://127.0.0.1:9001
- **redis**: localhost:6379
- **postgres**: localhost:5432

## Common Development Tasks

### Start Full Stack Locally
```bash
docker compose up --build
```

### Start Infrastructure Only
```bash
bash scripts/start-infrastructure.sh
```

### Scale Workers
```bash
bash scripts/scale-workers.sh --workers 5
```

### Integration Test Environment Variables
```bash
export GRID_TESTS_USE_LOCAL=1
export HUB_URL=http://127.0.0.1:5100
export HUB_RUNNER_SECRET=runner-secret
export GRID_TESTS_HEALTH_TIMEOUT_SECONDS=120
export LABELS="AppB:Chromium:UAT,AppB:Firefox:UAT"
```

## Recent Architecture Changes

### Hub Chunked Logging Integration (2026-01-02)
- Integrated `ChunkedLogger` into `LogItemsEndpoints` for high-performance milestone logging.
- Added `LOG01-LOG50` series event codes for comprehensive tracking.
- Reduced API calls by 50-90% via batching.

### Client Library Chunked Logging (2025-12-31)
- Implemented `IChunkedLogger` in `Agenix.PlaywrightGrid.Client`.
- Thread-safe buffering and auto-flush on disposal.

### Worker Re-Registration Resilience (2025-12-30)
- Automatic recovery when workers lose registration (hub restarts, Redis expiration).
- Dual-path detection: Fast Path (timer gap) and Slow Path (periodic verification).

### Other Key Features
- **Zombie Process Prevention**: Multi-layer defense to prevent orphaned Node.js sidecar processes.
- **Traefik Reverse Proxy**: Domain-based routing (e.g., `hub.localhost`) with horizontal scaling support.
- **Integration Test Refactoring**: Fluent builders (`LaunchBuilder`, `TestItemBuilder`) and singleton fixtures.
- **History Matrix**: PostgreSQL functions for ReportPortal-style launch and suite history.

## Reference Documentation
- **Event Codes**: `docs/event-codes.md`
- **Traefik Setup**: `docs/traefik/README.md`
- **Design Docs**: `.opencode/plan/`

## Verified Commands

- `dotnet build PlaywrightGrid.sln -c Debug` ✓
- `dotnet test WorkerService.Tests/WorkerService.Tests.csproj` ✓
- `dotnet test PlaywrightGrid.sln` ✓
- `dotnet format --verify-no-changes --severity warn` ✓
