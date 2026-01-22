# HTTP Error Handling with Modal & Retry Logic - Specification

**Date**: 2025-01-18
**Status**: Draft
**SDD Stage**: 1 - Feature Specification

---

## Overview

Implement centralized HTTP error handling for the Blazor dashboard with automatic retry logic, user-friendly error modals, and request deduplication. This replaces scattered inline error handling across 25+ components with a consistent, production-ready error handling strategy.

---

## User Stories

### US1: Network Failure Recovery
**As a** dashboard user
**I want** failed HTTP requests to automatically retry with exponential backoff
**So that** transient network issues don't interrupt my workflow

**Acceptance Criteria**:
- Network failures retry up to 3 times with exponential backoff (2s, 4s, 8s)
- User sees progress indicator during retries ("Retrying 1/3...")
- Success after retry automatically dismisses error modal
- Final failure shows detailed error with technical details

### US2: Clear Error Communication
**As a** dashboard user
**I want** to see user-friendly error messages with technical details available
**So that** I understand what went wrong and can provide context to support

**Acceptance Criteria**:
- Modal displays with user-friendly error message (bold, prominent)
- Technical details section is collapsible (default: collapsed)
- Stack trace is collapsible with copy-to-clipboard button
- Modal matches existing Bootstrap theme (purple gradients)
- Error type is clearly indicated (Network Error, Server Error, Access Denied)

### US3: Intelligent Retry Logic
**As a** dashboard user
**I want** the system to only retry errors that are retryable
**So that** I don't waste time on operations that will never succeed

**Acceptance Criteria**:
- Network failures (SocketException, HttpRequestException) → Retry
- Server errors (500, 502, 503, 504) → Retry
- Timeout errors (408, RequestTimeout) → Retry
- Client errors (400, 401, 403, 404) → No retry, show modal
- Rate limit (429) → Retry with backoff

### US4: Request Deduplication
**As a** dashboard user
**I want** rapid button clicks to not trigger multiple HTTP requests
**So that** I don't accidentally create duplicate operations

**Acceptance Criteria**:
- Concurrent identical requests deduplicated (same endpoint, method, parameters)
- Deduplication key: `{ComponentType}_{HttpMethod}_{Endpoint}_{ParametersHash}`
- First request executes, subsequent requests wait for result
- Deduplication cache cleared after request completes
- No race conditions or deadlocks

### US5: Graceful Degradation
**As a** dashboard user
**I want** partial failures to not break the entire page
**So that** I can still use working features when some data fails to load

**Acceptance Criteria**:
- Component-level error handling (one component failure doesn't crash page)
- Partial data loads supported (e.g., test details loads history but artifacts fail)
- Failed sections show error state with retry button
- Working sections remain interactive

### US6: Error Details Export
**As a** dashboard user
**I want** to save the error details as an image (PNG)
**So that** I can easily share it with the technical team via email or chat

**Acceptance Criteria**:
- Modal footer includes "Save as PNG" button
- Clicking the button captures the current modal content as a high-quality PNG
- The resulting file is automatically downloaded with a timestamped filename
- The capture includes all visible metadata and error messages
- Visual feedback is provided during and after the capture process

---

## Acceptance Criteria

### Functional Requirements

**Error Classification**:
- [ ] System correctly identifies retryable vs non-retryable errors
- [ ] Network failures detected (HttpRequestException, SocketException, etc.)
- [ ] Server errors (5xx) trigger retry
- [ ] Client errors (4xx except 408, 429) do not trigger retry
- [ ] Timeout errors (408) trigger retry
- [ ] Rate limit (429) triggers retry with exponential backoff

**Retry Mechanism**:
- [ ] Polly retry policy configured with 3 attempts
- [ ] Exponential backoff: 2s, 4s, 8s between attempts
- [ ] Retry progress visible to user ("Retrying 2/3...")
- [ ] Successful retry dismisses modal automatically
- [ ] Final failure shows detailed error modal

**Error Modal**:
- [ ] Modal matches Bootstrap theme (purple gradient header)
- [ ] User-friendly message displayed prominently
- [ ] Technical details section collapsible (default: collapsed)
- [ ] Stack trace collapsible with copy button
- [ ] Request information shown (method, endpoint, timestamp)
- [ ] Retry button visible only for retryable errors
- [ ] Save as PNG button functional in footer
- [ ] Dismiss button always visible

**Request Deduplication**:
- [ ] Identical concurrent requests deduplicated
- [ ] Deduplication key includes component, method, endpoint, parameters
- [ ] Cache cleared after request completes
- [ ] No deadlocks or race conditions
- [ ] Stress tested with 10+ rapid clicks

**Base Component**:
- [ ] DashboardComponentBase provides HTTP helpers
- [ ] Error handling standardized across all components
- [ ] Loading states managed consistently
- [ ] Toast notifications for success/info messages
- [ ] Modal errors for failures

### Non-Functional Requirements

**Performance**:
- [ ] Retry overhead <100ms per attempt (excluding network time)
- [ ] Request deduplication lookup <10ms
- [ ] Error modal render time <50ms
- [ ] No memory leaks in long-running sessions

**Usability**:
- [ ] Error messages clear and actionable
- [ ] Technical details accessible but not overwhelming
- [ ] Copy button works on all browsers
- [ ] Modal dismissible via ESC key and click outside
- [ ] ARIA attributes for screen readers

**Reliability**:
- [ ] No infinite retry loops
- [ ] Max retry limit enforced (3 attempts)
- [ ] Timeout prevents hanging requests (30s default)
- [ ] Exception handling prevents unhandled errors

**Compatibility**:
- [ ] Works in Chrome, Firefox, Safari, Edge
- [ ] Mobile responsive (error modal readable on phones)
- [ ] No breaking changes to existing components
- [ ] Backward compatible error handling fallback

---

## Constraints

### Technical Constraints

- **Polly Version**: Use Polly 8.4.1 (latest stable)
- **Bootstrap Theme**: Must match existing purple gradient (`#667eea`, `#764ba2`)
- **No Breaking Changes**: Existing components must continue working during migration
- **Blazor Server**: All error handling must work in Blazor Server context (not WASM)
- **Logging**: Use existing chunked logging infrastructure (no new logging systems)

### Performance Constraints

- **Retry Timeout**: Total retry time (including backoff) must not exceed 20 seconds
- **Modal Render**: Error modal must render in <50ms
- **Memory**: Deduplication cache must not exceed 10MB
- **Concurrent Requests**: Must handle 100+ concurrent requests without performance degradation

### Security Constraints

- **Error Details**: Stack traces only shown to authenticated users
- **Sensitive Data**: No passwords, API keys, or tokens in error messages
- **Rate Limiting**: Respect server rate limits (429 responses)
- **CORS**: Error handling must work with CORS-enabled APIs

### Compatibility Constraints

- **Browser Support**: Chrome 90+, Firefox 88+, Safari 14+, Edge 90+
- **Mobile**: Error modal responsive on screens 320px+
- **Accessibility**: WCAG 2.1 AA compliance for error modal
- **Existing Code**: Must not break 25+ existing components during migration

---

## Out of Scope

**Phase 1 Exclusions** (may be added in future phases):

- ❌ **Offline Support**: No service worker or offline caching
- ❌ **Error Analytics**: No error tracking/aggregation (e.g., Sentry integration)
- ❌ **Custom Retry Strategies**: Only exponential backoff (no jitter, circuit breaker)
- ❌ **Network Diagnostics**: No ping/traceroute tools to diagnose failures
- ❌ **Error Replay**: No ability to replay failed requests from modal
- ❌ **Background Retries**: No background retry for non-blocking operations
- ❌ **Error Notifications**: No email/Slack notifications for errors
- ❌ **Multi-Language Support**: Error messages in English only
- ❌ **Error History**: No persistent error log/history view
- ❌ **Advanced Deduplication**: No semantic deduplication (only exact match)

**Explicitly Not Implementing**:

- Custom retry strategies per component (all use same policy)
- User-configurable retry attempts/backoff (hardcoded 3 attempts)
- Error cause analysis (network vs server vs client)
- Request timeout configuration (fixed 30s)
- Retry queue management (immediate retry only)

---

## Success Metrics

### Quantitative Metrics

**Error Recovery Rate**:
- **Target**: 80% of transient errors recover after retry
- **Measurement**: `(successful_retries / total_retries) * 100`
- **Baseline**: 0% (no retry currently)

**User Frustration Reduction**:
- **Target**: 70% reduction in manual page refreshes
- **Measurement**: Track F5 key presses after errors
- **Baseline**: Current refresh rate after errors

**Request Deduplication Effectiveness**:
- **Target**: 90% of duplicate requests prevented
- **Measurement**: `(deduplicated_requests / total_requests) * 100`
- **Baseline**: 0% (no deduplication currently)

**Modal Response Time**:
- **Target**: Error modal visible within 100ms of failure
- **Measurement**: Time from error to modal render
- **Baseline**: Inline error banners render in ~50ms

**Retry Success Rate**:
- **Target**: >60% of retries succeed within 3 attempts
- **Measurement**: `(successful_retries / total_retries) * 100`
- **Baseline**: N/A (new feature)

### Qualitative Metrics

**User Satisfaction**:
- User feedback: "Errors are clearer and easier to understand"
- User feedback: "System recovers from network issues automatically"
- Support tickets: 50% reduction in "page not loading" issues

**Developer Experience**:
- Component error handling code reduced by 60% (standardized base class)
- New components inherit error handling for free
- Error handling bugs reduced by 70%

**Error Communication Quality**:
- Stack traces accessible but not overwhelming
- Technical details available for debugging
- User-friendly messages for all error types

---

## Dependencies

### External Dependencies

**NuGet Packages**:
- `Polly` v8.4.1 - Retry policy implementation
- `Polly.Extensions` v8.4.1 - Extension methods for HttpClient integration

**Existing Infrastructure**:
- Chunked logging (already implemented) - Used for server-side error logging
- Bootstrap 5 theme (already implemented) - Modal styling
- IHttpClientFactory (already implemented) - HTTP client creation

### Internal Dependencies

**Existing Components** (must not break):
- All 25+ Blazor components must continue working
- Existing error handling (inline banners) must coexist during migration
- SignalR connections must not be affected

**Existing Services**:
- HttpClientFactory - Error handler wraps existing clients
- NavigationManager - Used for redirect on auth errors
- JSRuntime - Used for clipboard copy button

### Feature Dependencies

**Required Before Implementation**:
- ✅ Bootstrap 5 theme established (already done)
- ✅ Blazor Server infrastructure (already done)
- ✅ Component structure (already done)

**Can Be Implemented Independently**:
- Error modal can be implemented before retry logic
- Retry logic can be implemented before deduplication
- Base component can be created before component migration

---

## Risk Assessment

### High Risks

**Risk 1: Component Migration Complexity**
- **Impact**: High (25+ components to migrate)
- **Likelihood**: High (large surface area)
- **Mitigation**:
  - Phased rollout (critical components first)
  - Comprehensive testing after each component migration
  - Rollback plan for each component

**Risk 2: Infinite Retry Loops**
- **Impact**: High (could DOS server)
- **Likelihood**: Medium (if retry logic has bugs)
- **Mitigation**:
  - Hard limit on retry attempts (max 3)
  - Total timeout enforcement (20s max)
  - Extensive retry policy unit tests

**Risk 3: Breaking Existing Functionality**
- **Impact**: High (users cannot access features)
- **Likelihood**: Medium (large refactor)
- **Mitigation**:
  - No changes to existing HTTP calls (wrap, don't replace)
  - Regression test suite before each deployment
  - Feature flag to disable retry logic

### Medium Risks

**Risk 4: Performance Degradation**
- **Impact**: Medium (slower page loads)
- **Likelihood**: Low (well-designed deduplication)
- **Mitigation**:
  - Performance benchmarks before/after
  - Deduplication cache size limits
  - Async/await everywhere (no blocking)

**Risk 5: User Confusion with Modal**
- **Impact**: Medium (users ignore errors)
- **Likelihood**: Low (clear messaging)
- **Mitigation**:
  - User testing with modal design
  - A/B test modal vs inline errors
  - Analytics on modal dismissal rates

### Low Risks

**Risk 6: Browser Compatibility Issues**
- **Impact**: Low (fallback to inline errors)
- **Likelihood**: Low (standard Bootstrap modal)
- **Mitigation**:
  - Cross-browser testing
  - Progressive enhancement (modal → inline)
  - Polyfills if needed

---

## Implementation Notes

### Migration Strategy

**Phase-by-Phase Approach**:
1. **Phase 1**: Infrastructure (base classes, services, modal)
2. **Phase 2**: Critical components (ProjectLaunches, ItemHistoryMatrix)
3. **Phase 3**: Medium priority (TestItemDetails, TestRunDetails)
4. **Phase 4**: Remaining components (all others)
5. **Phase 5**: Testing and validation

**Component Migration Checklist**:
- [ ] Inherit from DashboardComponentBase
- [ ] Replace inline error handling with ErrorModal
- [ ] Wrap HTTP calls with base class helpers
- [ ] Add retry to transient operations
- [ ] Add deduplication to critical operations
- [ ] Test error scenarios (network, server, client)
- [ ] Verify no regressions

### Testing Strategy

**Unit Tests** (60+ tests):
- HttpErrorHandlerService: 15 tests
- RetryPolicies: 10 tests
- RequestDeduplicationService: 10 tests
- ErrorModal component: 10 tests
- DashboardComponentBase: 15 tests

**Integration Tests** (30+ tests):
- Network failure scenarios: 10 tests
- Retry logic scenarios: 10 tests
- Request deduplication scenarios: 5 tests
- Component migration scenarios: 5 tests

**Manual Testing**:
- Cross-browser testing (Chrome, Firefox, Safari, Edge)
- Mobile responsive testing
- Accessibility testing (screen readers, keyboard navigation)
- Performance testing (100+ concurrent requests)

---

## Quality Gates

Before proceeding to Stage 2 (Architecture Planning), verify:

- [x] **Completeness**: All user stories, acceptance criteria, constraints documented
- [x] **Clarity**: Anyone can understand the feature scope
- [x] **Measurable**: Success metrics defined and trackable
- [x] **Feasibility**: No obvious technical blockers
- [x] **Scope**: Out-of-scope items explicitly listed
- [x] **Dependencies**: All external and internal dependencies identified
- [x] **Risks**: High/medium/low risks assessed with mitigations

---

## Next Steps

1. **Review Specification** - Get stakeholder approval
2. **Architecture Planning** (Stage 2) - Design multiple approaches
3. **Task Breakdown** (Stage 3) - Create detailed implementation plan
4. **Implementation** (Stage 4) - Execute with TDD
5. **Documentation** (Stage 5) - Update CLAUDE.md and user guides

---

*This specification follows the SDD workflow defined in CLAUDE.md. Once approved, proceed to Stage 2: Architecture Planning.*
