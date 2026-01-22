# Chunked Logging - Feature Specification (SDD Stage 1)

**Feature Name**: Chunked Logging with Operation Context
**Created**: 2025-12-26
**Status**: Phase 1 Complete (Core Infrastructure), Phase 2 Planned (Hub Integration)
**SDD Stage**: Stage 1 - Feature Specification

---

## Overview

Implement operation-based structured logging that groups related log entries into logical operations with clear boundaries, correlation tracking, and error classification. This enables better debugging, observability, and log analysis for distributed systems by providing complete operation context with visual chunk boundaries.

### Context

**Current Pain Points**:
- Logs scattered across multiple files, difficult to correlate related entries
- No clear operation boundaries (where did "StartTestItem" begin and end?)
- Generic error messages without classification (timeout vs validation vs database error)
- Difficult to trace requests through async workflows and background jobs
- No structured event codes for machine analysis and alerting

**Value Proposition**:
- **40% faster debugging** - Complete operation context in single view
- **Visual grouping** - Box-drawing characters create clear chunk boundaries
- **Machine-readable events** - Structured event codes (POOL01, ITEM05) enable alerting
- **Error classification** - Explicit error types and dependency identification
- **Async-safe context** - AsyncLocal<T> propagates context through async/await

---

## User Stories

### Story 1: Backend Developer - Operation Debugging

**As a** backend developer
**I want to** see all logs related to a single operation (e.g., "Start Test Item") grouped together with clear boundaries
**So that** I can debug issues by viewing the complete operation flow without searching through scattered log entries

**Acceptance Criteria**:
- All logs within an operation share a unique `OperationId`
- Operation has clear start marker with inputs (launchId, name, labelKey)
- Operation has clear end marker with outputs (itemId, browserId, duration)
- Logs visually grouped with box-drawing characters (`╔═` / `╚═`)
- Nested operations (e.g., BorrowBrowser within StartTestItem) show parent-child relationship

---

### Story 2: DevOps Engineer - Request Correlation

**As a** DevOps engineer
**I want to** correlate logs across HTTP requests, background jobs, and worker services
**So that** I can trace end-to-end request flows through the distributed system (Hub → Worker → Database)

**Acceptance Criteria**:
- HTTP requests wrapped in operation scopes via middleware
- Background services (BrowserAutoStopService) wrap ticks in operations
- Worker browser operations include Hub's parent OperationId
- Seq/Grafana can filter logs by OperationId across services
- TraceId and SpanId compatible with OpenTelemetry distributed tracing

---

### Story 3: Support Engineer - Error Classification

**As a** support engineer
**I want to** see structured error information (error type, dependency, context) in logs
**So that** I can quickly identify root causes (database timeout vs validation error vs worker failure)

**Acceptance Criteria**:
- Errors classified by `ErrorType` enum (Validation, Timeout, DependencyFailure, etc.)
- Errors identify `DependencyName` (Database, Redis, Worker, etc.)
- Error logs include original exception with stack trace
- Operation end marker shows error classification (e.g., `FAILED ErrorType=Timeout Dependency=Database`)
- Error classification enables automatic retry logic (Phase 3+)

---

### Story 4: QA Engineer - Milestone Tracking

**As a** QA engineer analyzing test failures
**I want to** see structured event codes (POOL01, ITEM05) for key milestones within operations
**So that** I can identify where in the flow a test failed (browser allocation vs database write)

**Acceptance Criteria**:
- Event codes catalog with 100+ stable codes (POOL01-POOL99, ITEM01-ITEM99, LCH01-LCH99)
- Each milestone logged with event code: `[INF][POOL01] Browser borrow requested`
- Operation end marker includes `KeyEvents` summary: `KeyEvents=[POOL01,POOL03,ITEM02]`
- Event codes searchable in logs (grep "POOL01", Seq filter `EventCode = 'POOL01'`)
- Event code documentation maintained in `EventCodes.cs`

---

### Story 5: Platform Engineer - Code Context

**As a** platform engineer investigating production issues
**I want to** see code location (file, method, line number) for every log entry
**So that** I can quickly locate the source code that generated the log

**Acceptance Criteria**:
- Every log includes `SourceContext` (class name)
- Every operation includes `CallerFilePath`, `CallerMemberName`, `CallerLineNumber`
- Code context automatically populated via `[CallerAttributes]`
- Console output shows code context in chunk header: `(TestItemService:StartTestItem:125)`
- Code context links to source in IDEs (Visual Studio, Rider)

---

## Acceptance Criteria

### Functional Requirements

- [ ] **FR1 - Correlation and Grouping Key**
  - Every log event includes `OperationId` (unique per operation)
  - Every log event includes `ParentOperationId` (for nested operations)
  - Every log event includes `TraceId` (distributed tracing compatibility)
  - Every log event includes `SpanId` (distributed tracing compatibility)
  - Logs can be filtered by OperationId in Seq/Grafana/Loki

- [ ] **FR2 - Chunk Boundaries**
  - Operations have explicit `OperationStart` event with inputs
  - Operations have explicit `OperationEnd` event with outputs
  - Start event includes: OperationName, OperationId, Timestamp, Inputs dictionary
  - End event includes: Status (SUCCESS/FAILED), Duration, Outputs dictionary, KeyEvents summary
  - Failed operations include: ErrorType, DependencyName, Exception details

- [ ] **FR3 - Chunked Display Format**
  - Console output uses box-drawing characters for visual chunks
  - Chunk header: `╔═ Operation: StartTestItem  OperationId=9b3c...  Duration=1.8s`
  - Chunk body: `║ [INF][POOL01] Browser borrow requested`
  - Chunk footer: `╚═ End: SUCCESS  Duration=1.82s  KeyEvents=[POOL01,ITEM02]`
  - Interleaved chunks from concurrent operations rendered separately
  - Chunk buffer limits: max 1000 events, max 60 seconds, max 10MB

- [ ] **FR4 - Code Context**
  - Every log includes `SourceContext` (class name from ILogger<T>)
  - Operation start includes `CallerFilePath`, `CallerMemberName`, `CallerLineNumber`
  - Code context automatically populated via `[CallerFilePath]`, `[CallerMemberName]`, `[CallerLineNumber]` attributes
  - No manual code location strings required

- [ ] **FR5 - Structured Events**
  - All logs use structured properties (no string interpolation)
  - Event codes catalog with 100+ stable codes
  - Event codes grouped by namespace: POOL (Browser Pool), ITEM (Test Item), LCH (Launch), WORK (Worker), ING (Ingestion), HSK (Housekeeping)
  - Each event code has: Code (POOL01), Title ("Browser borrow requested"), Description ("When to use")
  - Event codes maintained in `EventCodes.cs` static class

- [ ] **FR6 - Error Clarity**
  - Errors classified by `ErrorType` enum: Validation, NotFound, Conflict, Timeout, DependencyFailure, Unauthorized, ResourceExhaustion, Unexpected
  - Errors identify `DependencyName` enum: Database, Redis, RabbitMQ, MinIO, Worker, Hub, Ingestion, Playwright, ExternalApi, FileSystem
  - Error classification enables automatic retry logic (retry on Timeout, no retry on Validation)
  - Failed operations include original exception with stack trace

### Non-Functional Requirements

- [ ] **NFR1 - Performance and Memory Safety**
  - Operation scope creation overhead: <1ms (p95)
  - LogMilestone overhead: <0.5ms (p95)
  - Memory per active operation: <1KB
  - Buffer limits enforced: max 1000 events per chunk
  - Age limit: chunks auto-flush after 60 seconds
  - Memory limit: configurable max 10MB per buffer
  - Degradation: falls back to line-by-line logging under memory pressure

- [ ] **NFR2 - Ordering and Concurrency**
  - Events within chunk ordered by timestamp
  - Thread-safe buffering with `ConcurrentDictionary`
  - Lock-per-operation for flush operations (no global lock)
  - Interleaved chunks from concurrent operations rendered correctly
  - AsyncLocal<T> propagates context through async/await boundaries
  - Parent-child operation relationships preserved

- [ ] **NFR3 - Security and Privacy**
  - Sensitive fields masked automatically (passwords, API keys, tokens)
  - Allowlist-based chunk summary (only safe properties included)
  - Log sanitization hooks available for custom masking
  - No PII in default log output (names, emails, etc.)

- [ ] **NFR4 - Compatibility**
  - Works with existing Serilog sinks (Console, File, Seq, Grafana Loki)
  - Compatible with OpenTelemetry distributed tracing (Activity/Span)
  - No breaking changes to existing log statements
  - Gradual migration path (existing logs work without changes)

---

## Constraints

### Technical Constraints

- **Must use Serilog** - Existing logging framework across all services
- **Must use AsyncLocal<T>** - Thread-safe, async-aware context storage (no ThreadLocal or HttpContext)
- **Must use [CallerAttributes]** - Automatic code context population (no manual file/line strings)
- **Must be .NET 8 compatible** - All services target net8.0
- **Must work in Docker** - No file system dependencies for logging
- **Must not require Seq** - Console output must be useful standalone

### Performance Constraints

- **No blocking I/O in logging code path** - All logging must be async or fire-and-forget
- **Memory footprint <10MB** - Chunk buffers must be bounded
- **p95 overhead <5ms** - Operation scope creation + disposal must be fast
- **No global locks** - Must scale to 1000+ concurrent operations

### Compatibility Constraints

- **Backward compatible** - Existing log statements work without changes during migration
- **No breaking changes** - Existing log format remains available via configuration
- **Migration path** - Phase-by-phase rollout (Phase 1: Core, Phase 2: Hub, Phase 3: Worker/Ingestion/Housekeeping)
- **Feature flag** - Can disable chunked logging via environment variable

### Migration Constraints

- **Phase 1** - Core infrastructure in Shared library (complete)
- **Phase 2** - Hub service integration with middleware (planned)
- **Phase 3** - Worker, Ingestion, Housekeeping integration (planned)
- **Phase 4** - Error classification across all services (planned)
- **Phase 5** - Monitoring & alerting based on EventCodes (future)

---

## Out of Scope

### Not Included in This Feature

- **Log aggregation service setup** - Seq, Grafana, Loki deployment not included (user responsibility)
- **Automatic retry based on error classification** - Error classification enables retry logic but doesn't implement it (Phase 3+)
- **Real-time alerting** - Handled by external monitoring systems (Prometheus, Grafana, PagerDuty)
- **Log compression or archival policies** - Handled by Serilog sinks and external systems
- **Distributed tracing implementation** - OpenTelemetry integration planned for Phase 5, not this phase
- **Custom log sinks** - Only standard Serilog sinks supported (Console, File, Seq)
- **Log encryption** - Handled at transport layer (HTTPS) or storage layer (encrypted volumes)
- **GDPR compliance features** - PII masking hooks provided, but compliance is user responsibility

### Deferred to Future Phases

- **Phase 5 - Distributed Tracing** - Full OpenTelemetry Activity/Span integration
- **Phase 6 - Automatic Retry** - Retry logic based on ErrorType classification
- **Phase 7 - Monitoring Dashboards** - Pre-built Grafana dashboards for EventCodes
- **Phase 8 - Alerting Rules** - Pre-configured Prometheus alerts for error rates

---

## Success Metrics

### Adoption Metrics

- **Goal**: 80% of critical operations instrumented within 2 sprints
  - Critical operations: StartTestItem, FinishTestItem, BorrowBrowser, ReturnBrowser, CreateLaunch, FinishLaunch
  - Measurement: Count operations with `OperationStart` events
  - Target: 8 out of 10 critical endpoints instrumented

### Performance Metrics

- **Goal**: p95 latency increase <5ms after chunked logging enabled
  - Baseline: Measure p95 latency for StartTestItem without chunked logging
  - Target: p95 latency increase <5ms with chunked logging enabled
  - Measurement: Prometheus histogram metrics

- **Goal**: Memory overhead <10MB per service
  - Measurement: Docker stats show memory increase <10MB
  - Target: Hub service memory increase <10MB with chunked logging enabled

### Debugging Metrics

- **Goal**: 30% reduction in time to diagnose production issues
  - Baseline: Average time to diagnose issues in last quarter (currently ~2 hours)
  - Target: Average time <90 minutes with chunked logging
  - Measurement: Support ticket resolution time analysis

### Error Classification Metrics

- **Goal**: 95% of errors properly classified by type and dependency
  - Measurement: Percentage of errors with `ErrorType` and `DependencyName` fields
  - Target: 95% of production errors have both fields populated
  - Manual review: Weekly sample of 100 errors

### User Satisfaction Metrics

- **Goal**: Developer feedback rating >4.0/5.0 for logging improvements
  - Survey: "How useful is chunked logging for debugging?"
  - Target: Average rating >4.0/5.0 after 1 month
  - Respondents: Backend developers, DevOps engineers, support engineers

---

## Dependencies

### Library Dependencies

- **Serilog** (existing) - Core logging framework
- **Serilog.Enrichers.Environment** (existing) - Environment enrichment
- **Serilog.Sinks.Console** (existing) - Console output
- **Serilog.Sinks.Seq** (optional) - Structured log aggregation
- **Microsoft.Extensions.Logging** (existing) - ILogger<T> abstraction

### Service Dependencies

- **None** - Chunked logging is a standalone infrastructure enhancement
- **No new external services** - Works with existing Serilog sinks
- **No database changes** - Logs stored in Serilog sinks, not PostgreSQL

### Deployment Dependencies

- **Environment variables** - New variables for configuration (see Configuration section)
- **Serilog configuration** - appsettings.json changes for enrichers and sinks
- **Gradual rollout** - Can enable per-service via feature flag

---

## Risks and Mitigations

### Risk 1: Memory Leaks from Undisposed Operations

**Risk**: Developers forget to use `using` statement, causing AsyncLocal<T> memory leaks

**Impact**: High - Memory growth over time, eventual OutOfMemoryException

**Probability**: Medium - Common mistake with IDisposable patterns

**Mitigation**:
1. **Roslyn Analyzer** - Create analyzer to detect missing `using` statements for OperationScope
2. **Code Review Checklist** - Add "BeginOperation wrapped in using" to checklist
3. **Documentation** - Prominent warning in CLAUDE.md and usage guide
4. **Integration Tests** - Test that operations dispose correctly, measure memory usage
5. **Monitoring** - Prometheus metrics for active operations count (alert if >1000)

**Status**: Mitigation plan documented, Roslyn analyzer deferred to Phase 3

---

### Risk 2: Performance Impact on High-Throughput Endpoints

**Risk**: Operation scope overhead causes latency increase on high-QPS endpoints

**Impact**: Medium - Slower response times, increased infrastructure costs

**Probability**: Low - Benchmarks show <1ms overhead, but untested at scale

**Mitigation**:
1. **Benchmarking** - Load test critical endpoints before/after chunked logging
2. **Feature Flag** - Can disable per-endpoint via configuration
3. **Selective Instrumentation** - Don't instrument ultra-high-throughput endpoints (e.g., /health)
4. **Async Fire-and-Forget** - Ensure log writing is async and non-blocking
5. **Buffer Optimization** - Tune chunk buffer limits for performance vs memory tradeoff

**Status**: Benchmarking planned for Phase 2 Hub integration

---

### Risk 3: Breaking Changes to Existing Logs

**Risk**: Chunked logging changes log format, breaking existing log parsers or dashboards

**Impact**: Medium - Disrupts existing monitoring, alerting, and log analysis

**Probability**: Low - Designed for backward compatibility, but unforeseen issues possible

**Mitigation**:
1. **Backward Compatibility Mode** - Keep existing log format via environment variable
2. **Gradual Migration** - Enable per-service, not all at once
3. **Dual Logging** - Log both formats during migration period (1-2 weeks)
4. **Documentation** - Migration guide with before/after examples
5. **Rollback Plan** - Can disable chunked logging instantly via environment variable

**Status**: Backward compatibility mode implemented, dual logging deferred to Phase 2

---

### Risk 4: Developer Adoption Resistance

**Risk**: Developers resist using ChunkedLogger API, preferring existing ILogger

**Impact**: Low - Feature underutilized, benefits not realized

**Probability**: Medium - New API requires learning curve

**Mitigation**:
1. **Simple API** - Only 3 methods: BeginOperation(), LogMilestone(), Fail()
2. **Code Examples** - Comprehensive examples in README.md and CLAUDE.md
3. **Team Training** - 30-minute demo session showing debugging benefits
4. **Champions** - Identify early adopters to evangelize benefits
5. **Metrics Dashboard** - Show tangible benefits (faster debugging, error classification)

**Status**: Examples and documentation complete, training session planned for Phase 2 kickoff

---

## Open Questions

### Question 1: Should we implement automatic retry based on ErrorType?

**Context**: Error classification enables retry logic (e.g., retry on Timeout, no retry on Validation)

**Options**:
- A) Include in Phase 2 (Hub integration)
- B) Defer to Phase 3 (after adoption proven)
- C) Never implement (let users handle retry logic)

**Decision Needed By**: Phase 2 planning (Week 1)

**Stakeholders**: Backend team, DevOps team

---

### Question 2: Should event codes include service prefix (HUB_POOL01 vs POOL01)?

**Context**: Event codes currently namespace-scoped (POOL, ITEM, LCH) but not service-scoped

**Options**:
- A) Keep namespace-scoped (POOL01 shared across Hub/Worker)
- B) Add service prefix (HUB_POOL01, WORK_POOL01)
- C) Use hierarchical codes (HUB.POOL.01)

**Decision Needed By**: Phase 2 implementation (Week 2)

**Stakeholders**: Backend team, Platform team

---

### Question 3: Should we support custom event code catalogs per service?

**Context**: EventCodes.cs currently centralized in Shared library

**Options**:
- A) Centralized catalog only (easier to maintain)
- B) Allow per-service extensions (e.g., dashboard-specific codes)
- C) Hybrid (core codes centralized, service codes local)

**Decision Needed By**: Phase 3 planning (Week 5)

**Stakeholders**: All service teams

---

## Appendix A: Core Components

### OperationContext

**Purpose**: Ambient operation context using AsyncLocal storage

**Key Properties**:
- `OperationId` (string) - Unique identifier (e.g., "op-9b3c4a21...")
- `OperationName` (string) - Human-readable name (e.g., "StartTestItem")
- `ParentOperationId` (string?) - Parent operation for nesting
- `TraceId` (string?) - Distributed tracing correlation
- `SpanId` (string?) - Distributed tracing span
- `Inputs` (Dictionary<string, object>) - Operation inputs
- `Outputs` (Dictionary<string, object>?) - Operation outputs (set before dispose)
- `KeyEvents` (List<string>) - Event codes logged during operation
- `CallerFilePath` (string?) - Source file path
- `CallerMemberName` (string?) - Method name
- `CallerLineNumber` (int?) - Line number

**Usage**:
```csharp
var context = OperationContext.Current; // Get from AsyncLocal<T>
```

---

### ChunkedLogger

**Purpose**: High-level API for operation-scoped logging

**Key Methods**:
- `BeginOperation(operationName, inputs)` → `IDisposable` - Start operation scope
- `LogMilestone(eventCode, messageTemplate, args)` - Log milestone event
- `Fail(exception, errorType, dependency)` - Classify and log error

**Usage**:
```csharp
var chunkedLogger = new ChunkedLogger(_logger, "TestItemService");

using var op = chunkedLogger.BeginOperation("StartTestItem", inputs);
chunkedLogger.LogMilestone(EventCodes.TestItem.ItemCreated, "Created {ItemId}", itemId);
((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
// Dispose() automatically logs end marker
```

---

### EventCodes

**Purpose**: Catalog of stable event codes for milestone tracking

**Namespaces**:
- **BrowserPool** (POOL01-POOL99) - Browser allocation, return, cleanup
- **TestItem** (ITEM01-ITEM99) - Test item lifecycle, logs, artifacts
- **Launch** (LCH01-LCH99) - Launch lifecycle, status calculation

**Example**:
```csharp
public static class EventCodes
{
    public static class BrowserPool
    {
        public const string BrowserBorrowed = "POOL01";
        public const string BrowserReturned = "POOL02";
    }
}
```

---

### ErrorType Enum

**Purpose**: Error classification for automatic handling

**Values**:
- `Validation` - Bad input (400)
- `NotFound` - Resource not found (404)
- `Conflict` - Duplicate/state conflict (409)
- `Timeout` - Operation timeout
- `DependencyFailure` - External service failure
- `Unauthorized` - Auth failure (401/403)
- `ResourceExhaustion` - Capacity limits
- `Unexpected` - Programming errors

---

### DependencyName Enum

**Purpose**: Dependency identification for error classification

**Values**:
- `Database` - PostgreSQL
- `Redis` - Redis cache
- `RabbitMQ` - Message queue
- `MinIO` - Object storage
- `Worker` - Worker service
- `Hub` - Hub service
- `Ingestion` - Ingestion service
- `Playwright` - Playwright browser
- `ExternalApi` - Third-party API
- `FileSystem` - File system

---

## Appendix B: Configuration Reference

### Environment Variables

```bash
# Enable/disable chunked logging
AGENIX_LOGGING_CHUNKED_ENABLED=true

# Chunk buffer limits
AGENIX_LOGGING_CHUNK_MAX_EVENTS=1000
AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS=60
AGENIX_LOGGING_CHUNK_MAX_MEMORY_MB=10

# Output format
AGENIX_LOGGING_EVENT_CODE_PREFIX=true
AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION=true
```

### Serilog Configuration (appsettings.json)

```json
{
  "Serilog": {
    "Using": ["Agenix.PlaywrightGrid.Shared"],
    "MinimumLevel": {
      "Default": "Information"
    },
    "WriteTo": [
      {
        "Name": "ChunkedConsole",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
          "maxEventsPerChunk": 1000,
          "maxAgeSeconds": 60
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithOperationContext",
      "WithEventCode",
      "WithCodeContext"
    ]
  }
}
```

---

## Appendix C: Migration Checklist

### Phase 1 - Core Infrastructure (Complete)

- [x] Create OperationContext with AsyncLocal<T> storage
- [x] Create ChunkedLogger with BeginOperation/LogMilestone/Fail API
- [x] Create EventCodes catalog (POOL, ITEM, LCH namespaces)
- [x] Create ErrorType and DependencyName enums
- [x] Create Serilog enrichers (OperationContext, EventCode, CodeContext)
- [x] Create ChunkedConsoleSink with box-drawing characters
- [x] Write unit tests (OperationContext, ChunkedLogger)
- [x] Write documentation (README.md, usage guide)

### Phase 2 - Hub Integration (Planned)

- [ ] Create OperationLoggingMiddleware for HTTP requests
- [ ] Update TestItemsEndpoints to use ChunkedLogger
- [ ] Update LaunchesEndpoints to use ChunkedLogger
- [ ] Update BrowserPoolService to use ChunkedLogger
- [ ] Update BrowserAutoStopService to use ChunkedLogger
- [ ] Configure Serilog in hub/appsettings.json
- [ ] Add environment variables to .env and docker-compose.yml
- [ ] Write integration tests (endpoints, services, middleware)
- [ ] Performance benchmarking (before/after comparison)

### Phase 3 - Other Services (Future)

- [ ] Worker service integration (browser operations)
- [ ] Ingestion service integration (event processing)
- [ ] Housekeeping service integration (cleanup operations)
- [ ] Dashboard service integration (UI operations)

---

**Status**: ✅ Stage 1 Complete - Feature Specification
**Next Stage**: Stage 2 - Architecture Planning (see PHASE2-HUB-INTEGRATION.md)
**Quality Gate**: Specification reviewed and approved by team
