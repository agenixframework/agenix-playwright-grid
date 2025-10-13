# ProblemDetails Error Handler - Stage 1: Specification

**Date**: 2026-01-12
**Author**: Claude Code
**Status**: Draft

---

## Overview

Implement consistent, standardized RFC 7807 ProblemDetails error responses across all Hub API endpoints, integrated with the existing EventCodes system for stable error identifiers and comprehensive observability.

---

## Problem Statement

### Current State

The Hub API has inconsistent error handling across 320+ error responses in 13 endpoint files:

1. **Inconsistent Format**: Endpoints return various error formats:
   - `new { error = "message" }` (most common)
   - Plain text strings
   - Custom anonymous objects
   - Some ProblemDetails (via existing middleware)

2. **No Event Code Integration**: Error responses don't include EventCodes despite having a comprehensive EventCodes system (460+ event codes across 15 categories)

3. **Security Risks**: Raw exception messages and stack traces may be exposed to clients

4. **Poor Observability**: No standardized error identifiers for metrics, monitoring, or alerting

5. **No Field-Level Validation**: Validation errors don't provide field-specific messages (ModelState style)

### Impact

- **Developer Experience**: Inconsistent error handling makes API difficult to integrate
- **Debugging**: No stable identifiers to track error patterns
- **Security**: Potential information disclosure via exception messages
- **Monitoring**: Cannot create meaningful error metrics or alerts

---

## User Stories

### Story 1: API Consumer Receives Standardized Validation Errors

**As an** API consumer (SDK developer, frontend developer)
**I want to** receive validation errors in RFC 7807 ProblemDetails format with field-level error messages
**So that** I can display specific validation feedback to end users

**Acceptance Criteria**:
- All validation errors (4xx) return `application/problem+json` content type
- Response includes `errors` dictionary with field names as keys
- Each field has array of error messages
- Response includes `traceId` for debugging
- Response includes `eventCode` from EventCodes system (e.g., `ADM91` for validation failures)

**Example Response**:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/api/project-settings",
  "traceId": "00-abc123-def456-01",
  "eventCode": "PRJ02",
  "errors": {
    "launchInactivityTimeout": ["Invalid value. Allowed: 1h, 3h, 6h, 12h, 1d, 3d, 7d"],
    "keepLaunches": ["Invalid value. Allowed: 7d, 14d, 30d, 90d, 180d"]
  }
}
```

### Story 2: Operations Team Monitors Error Patterns

**As an** operations engineer
**I want** stable event codes in error responses and logs
**So that** I can create dashboards, alerts, and track error patterns over time

**Acceptance Criteria**:
- All error responses include `eventCode` field
- Event codes are stable and documented (use existing EventCodes system)
- Event codes map to specific error categories (validation, not found, dependency failure, etc.)
- Logs include event codes for correlation with API responses
- Can query Prometheus metrics by event code

**Example Metrics**:
```
http_errors_total{eventCode="PRJ02", status="400"} 45
http_errors_total{eventCode="DB04", status="500"} 3
http_errors_total{eventCode="WSH10", status="500"} 12
```

### Story 3: Developer Debugs Production Issues

**As a** backend developer
**I want** safe error messages in production with detailed logging server-side
**So that** I can debug issues without exposing sensitive information to clients

**Acceptance Criteria**:
- Client receives safe, generic error message for 5xx errors
- Full exception details logged server-side with event code
- TraceId links client response to server logs
- No secrets, PII, or stack traces in client responses
- Event code identifies the specific failure scenario

**Example Response** (5xx):
```json
{
  "type": "https://httpstatuses.com/500",
  "title": "Internal Server Error",
  "status": 500,
  "detail": "A database error occurred while processing your request.",
  "instance": "/api/launches",
  "traceId": "00-xyz789-uvw012-01",
  "eventCode": "DB04"
}
```

**Example Log** (server-side):
```
[ERROR] Database transaction failed [DB04]
  TraceId: 00-xyz789-uvw012-01
  Endpoint: POST /api/launches
  Exception: Npgsql.NpgsqlException: 23505: duplicate key value violates unique constraint "launches_pkey"
  Stack: [full stack trace]
```

---

## Acceptance Criteria

### Validation Errors (4xx)

- [ ] Return `400 Bad Request` (or `422 Unprocessable Entity`) for validation failures
- [ ] Content-Type: `application/problem+json`
- [ ] Include `type`, `title`, `status`, `instance`, `traceId`, `eventCode` fields
- [ ] Include `errors` dictionary with field-level messages (ModelState style)
- [ ] Cover:
  - [ApiController] automatic model validation
  - Manual ModelState checks in endpoints
  - Validation exceptions from handlers/pipelines
  - Business rule violations (e.g., invalid enum values, constraint violations)

### Server Errors (5xx)

- [ ] Return appropriate status codes: `500`, `503`, `504`
- [ ] Content-Type: `application/problem+json`
- [ ] Include `type`, `title`, `status`, `detail`, `instance`, `traceId`, `eventCode` fields
- [ ] Safe error messages only (no secrets, PII, stack traces)
- [ ] Full exception details logged server-side with event code
- [ ] Map exception types to appropriate event codes:
  - `NpgsqlException` → `EventCodes.Database.*` (DB01-DB99)
  - `RedisException` → `EventCodes.Redis.*` (RDS01-RDS99)
  - `TimeoutException` → Context-specific event code
  - `Generic Exception` → `EventCodes.WebServer.RequestFailed` (WSH10)

### Global Implementation

- [ ] Centralized middleware/exception handler
- [ ] No duplicate error handling logic across endpoints
- [ ] Configurable via dependency injection
- [ ] Extensible for new error types
- [ ] Helper methods for common error scenarios

### EventCodes Integration

- [ ] All error responses include `eventCode` from existing EventCodes system
- [ ] Event codes mapped to error types:
  - Validation failures → `ADM91`, `PRJ02`, etc.
  - Not found → Context-specific codes (e.g., `LCH03` for launch not found)
  - Dependency failures → `DB04`, `RDS02`, etc.
  - Unexpected errors → `WSH10`
- [ ] Event codes logged for server-side observability
- [ ] Event codes included in metrics/monitoring

---

## Constraints

### Technical Constraints

- **Framework**: ASP.NET Core 8 Minimal APIs
- **Existing Middleware**: Must integrate with existing 4xx/5xx normalization middleware (HubServiceRunner.cs:611-708)
- **EventCodes System**: Must use existing `EventCodes.cs` (460+ event codes)
- **Logging**: Must use existing Serilog and ChunkedLogger infrastructure
- **Metrics**: Must integrate with existing Prometheus metrics

### Performance Constraints

- **Latency**: Error handling must not add >10ms to request processing
- **Memory**: Must not buffer entire response in memory (streaming support)
- **Throughput**: Must handle 1000+ req/sec without performance degradation

### Security Constraints

- **No Information Disclosure**: 5xx errors must not expose:
  - Database connection strings
  - Internal file paths
  - Stack traces
  - Environment variables
  - Configuration secrets
- **Safe Error Messages**: Generic messages for 5xx, specific for 4xx
- **Audit Trail**: All errors logged server-side with full context

### Compatibility Constraints

- **Backward Compatibility**: Existing clients must continue to work
- **RFC 7807 Compliance**: Must follow ProblemDetails standard
- **No Breaking Changes**: Existing endpoint signatures unchanged
- **Migration Path**: Gradual rollout across 13 endpoint files (320+ responses)

---

## Out of Scope

### Phase 1 (Current Scope)

Focus on standardizing error responses and EventCodes integration.

### Deferred to Phase 2

- **Localization**: Error messages in multiple languages
- **Retry Hints**: `Retry-After` headers for rate limiting
- **Detailed Error Taxonomy**: Fine-grained error type classification
- **Custom Error Pages**: HTML error pages for browser requests
- **Error Analytics Dashboard**: Grafana dashboards for error patterns

### Explicitly NOT Included

- **Client SDK Changes**: SDK updates are separate effort
- **Frontend Error Handling**: Dashboard UI changes out of scope
- **Database Schema Changes**: No new tables/columns needed
- **Migration of Old Errors**: Old logs remain unchanged

---

## Success Metrics

### Coverage Metrics

- **Target**: 100% of error responses use ProblemDetails format
- **Baseline**: ~20% (existing middleware converts some responses)
- **Measurement**: Manual code review + integration tests

### Consistency Metrics

- **Target**: All error responses include `eventCode` field
- **Baseline**: 0% (event codes not in error responses currently)
- **Measurement**: Integration tests verify eventCode presence

### Observability Metrics

- **Target**: 95% of production errors have matching event codes in logs and responses
- **Baseline**: N/A (new metric)
- **Measurement**: Correlation analysis between API responses and logs

### Performance Metrics

- **Target**: <10ms overhead for error response generation
- **Baseline**: ~2ms (existing middleware)
- **Measurement**: Load testing with intentional errors

---

## Dependencies

### Existing Systems

- **EventCodes.cs**: 460+ event codes across 15 categories (ADM, LCH, PRJ, DB, RDS, etc.)
- **ErrorTypes.cs**: Enum with 8 error classifications (Validation, NotFound, Conflict, Timeout, DependencyFailure, Unauthorized, ResourceExhaustion, Unexpected)
- **4xx/5xx Normalization Middleware**: Existing middleware in HubServiceRunner.cs (lines 611-708)
- **OperationLoggingMiddleware**: Logs operation context with ErrorType enum
- **ChunkedLogger**: Structured logging infrastructure

### External Dependencies

- **ASP.NET Core 8**: ProblemDetails support built-in
- **Microsoft.AspNetCore.Http.HttpResults**: Results.Problem() helper
- **System.Text.Json**: JSON serialization

### Team Dependencies

- **No Blockers**: Can be implemented independently by backend team

---

## Risks and Mitigations

### Risk 1: Performance Impact from Response Buffering

**Probability**: Medium
**Impact**: High
**Mitigation**:
- Use streaming ProblemDetails generation (no full response buffering)
- Benchmark before/after implementation
- Monitor production latency metrics

### Risk 2: Breaking Changes for Existing Clients

**Probability**: Low
**Impact**: High
**Mitigation**:
- Maintain backward compatibility by keeping existing error format structure
- Add new fields (eventCode, traceId) without removing old ones
- Phase rollout: test with internal tools first, then external APIs
- Document changes in API changelog

### Risk 3: EventCode Mapping Complexity

**Probability**: Medium
**Impact**: Medium
**Mitigation**:
- Start with coarse-grained mappings (e.g., all validation → ADM91)
- Refine mappings iteratively based on usage patterns
- Document mapping decisions in code comments

### Risk 4: Large-Scale Refactoring Introduces Bugs

**Probability**: High
**Impact**: Medium
**Mitigation**:
- Refactor incrementally (one endpoint file at a time)
- Comprehensive integration tests before refactoring
- Code review for each endpoint file
- Canary deployment with gradual rollout

---

## Next Steps

After approval of this specification:

1. **Stage 2**: Architecture Planning
   - Design ProblemDetailsHelpers API
   - Plan EventCode mapping strategy
   - Design global exception handler integration
   - Define helper method signatures

2. **Stage 3**: Task Breakdown
   - Break down 320+ error responses into manageable batches
   - Define dependency order (helper library → middleware → endpoints)
   - Estimate complexity and timeline

3. **Stage 4**: Implementation
   - TDD cycle for each component
   - Incremental refactoring of endpoint files
   - Integration testing

4. **Stage 5**: Documentation
   - Update API documentation
   - Document EventCode mappings
   - Update AGENTS.md with error handling patterns
