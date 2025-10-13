# HttpContext TraceIdentifier in Chunked Logging - Specification

**Date**: 2025-01-15
**Status**: Draft

---

## Overview

Add HttpContext.TraceIdentifier support to chunked logging to ensure consistency between ProblemDetails API responses and log entries. Currently, ProblemDetails includes `traceId: httpContext.TraceIdentifier`, but chunked logs only capture OpenTelemetry Activity.Current.TraceId, creating a disconnect between API responses and logs.

---

## Problem Statement

### Current State
- **OperationContext** captures `Activity.Current.TraceId` and `SpanId` (OpenTelemetry standard)
- These flow into Serilog logs via `OperationContextEnricher`
- **ProblemDetails** responses include `httpContext.TraceIdentifier` in the `traceId` field
- **Gap**: `HttpContext.TraceIdentifier` is NOT captured in chunked logs

### Pain Points
1. **Inconsistent traceIds**: API response shows one traceId, logs show different traceId
2. **Hard to correlate**: Support engineers can't easily find logs matching API response traceId
3. **Two identifier systems**: OpenTelemetry Activity.TraceId ≠ HttpContext.TraceIdentifier

### Example of the Problem
```csharp
// Hub endpoint returns ProblemDetails with httpContext.TraceIdentifier
return ProblemDetailsHelpers.NotFound(
    message,
    traceId: httpContext.TraceIdentifier);  // ✅ traceId: "00-abc123..."

// But chunked log above it doesn't include this traceId
chunkedLogger.LogMilestone(
    EventCodes.LogItem.LogItemCreationFailed,  // ❌ No httpContext.TraceIdentifier
    "error=TestItemNotFound testItemUuid={TestItemUuid}",
    testItemGuid);
```

**Result**: User sees traceId `00-abc123...` in API response, but logs only show OpenTelemetry TraceId, which may be different or missing.

---

## User Stories

**As a** support engineer
**I want to** search logs using the traceId from an API error response
**So that** I can quickly find the log entries related to the error

**As a** developer debugging an issue
**I want to** see the same traceId in API responses and logs
**So that** I can correlate API errors with backend operations

**As a** system operator
**I want to** trace a request through multiple services using HttpContext.TraceIdentifier
**So that** I can understand the complete request flow across the system

---

## Acceptance Criteria

- [ ] HttpContext.TraceIdentifier captured in all chunked log entries for HTTP requests
- [ ] HttpContext.TraceIdentifier stored in OperationContext.Properties["HttpTraceId"]
- [ ] HttpTraceId automatically enriched into Serilog output
- [ ] Middleware captures traceId at request start (before endpoint execution)
- [ ] Works for all services: Hub, Ingestion, Housekeeping
- [ ] No changes required to existing endpoint code (automatic via middleware)
- [ ] Logs show both OpenTelemetry TraceId and HttpTraceId for correlation
- [ ] Zero performance overhead (middleware is lightweight)

---

## Constraints

### Technical
- **Must NOT modify** existing chunked logger API (LogMilestone, LogFailure)
- **Must use** existing OperationContext.Properties mechanism (no new storage)
- **Must register** middleware AFTER UseRouting() but BEFORE endpoints
- **Must NOT throw** exceptions if OperationContext is null (graceful degradation)

### Performance
- Middleware execution time < 1ms (negligible overhead)
- No memory allocations beyond storing string in dictionary
- No blocking I/O operations

### Compatibility
- Works with existing Serilog configuration
- Compatible with OpenTelemetry Activity.TraceId (both captured)
- No breaking changes to chunked logging API
- Works for services that don't use HttpContext (gracefully ignored)

---

## Out of Scope

- ❌ Modifying ChunkedLogger API to accept traceId as parameter
- ❌ Removing OpenTelemetry Activity.TraceId (keep both for flexibility)
- ❌ Adding HttpContext.TraceIdentifier to non-HTTP services (worker, background jobs)
- ❌ Custom traceId generation or formatting logic
- ❌ Integration with distributed tracing systems (Jaeger, Zipkin)

---

## Success Metrics

- **Correlation Success Rate**: 100% of API error responses have matching log entries by HttpTraceId
- **Log Search Time**: Reduce from ~2 minutes (manual correlation) to <10 seconds (direct search)
- **Performance Impact**: <0.1% increase in request latency (middleware overhead)
- **Adoption Rate**: All 3 services (Hub, Ingestion, Housekeeping) use middleware within 1 week

---

## Example Log Output (Before/After)

### Before (Missing HttpTraceId)
```json
{
  "Timestamp": "2025-01-15T10:30:45.123Z",
  "Level": "Error",
  "MessageTemplate": "Log item creation failed - test item not found",
  "Properties": {
    "EventCode": "LOG01",
    "OperationId": "CreateLogItem",
    "TraceId": "00-otel-trace-id-123",  // OpenTelemetry only
    "SpanId": "span-456",
    "TestItemUuid": "abc-def-ghi"
  }
}
```

### After (Includes HttpTraceId)
```json
{
  "Timestamp": "2025-01-15T10:30:45.123Z",
  "Level": "Error",
  "MessageTemplate": "Log item creation failed - test item not found",
  "Properties": {
    "EventCode": "LOG01",
    "OperationId": "CreateLogItem",
    "TraceId": "00-otel-trace-id-123",      // OpenTelemetry
    "SpanId": "span-456",
    "HttpTraceId": "00-http-trace-id-789",  // ✅ HttpContext.TraceIdentifier
    "TestItemUuid": "abc-def-ghi"
  }
}
```

**Now support can search logs by HttpTraceId from API response**: `grep "HttpTraceId.*00-http-trace-id-789" logs/*.log`

---

## Open Questions

1. **Should HttpTraceId be required or optional?**
   - Recommendation: Optional (gracefully handle null OperationContext)
   - Reason: Background jobs and workers don't have HttpContext

2. **Should we rename to HttpRequestId for clarity?**
   - Recommendation: Keep "HttpTraceId" for consistency with ProblemDetails "traceId" field
   - Reason: Users search logs using the traceId from API responses

3. **Should we add HttpTraceId to ProblemDetails if different from Activity.TraceId?**
   - Recommendation: No, ProblemDetails already uses HttpContext.TraceIdentifier
   - Reason: This spec is about making LOGS match the API response, not changing the API

---

## Next Steps

1. ✅ Specification complete (this document)
2. 📐 Architecture Planning - Design middleware implementation
3. 📋 Task Breakdown - Break into actionable tasks
4. 💻 Implementation - TDD cycle (tests first)
5. 📚 Documentation - Update CLAUDE.md with new middleware pattern
