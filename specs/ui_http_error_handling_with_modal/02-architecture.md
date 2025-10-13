# HTTP Error Handling with Modal & Retry Logic - Architecture

**Date**: 2025-01-18
**Status**: Draft
**SDD Stage**: 2 - Architecture Planning

---

## Research: Existing Patterns

### Similar Features in Codebase

**Error Handling Patterns**:
- **ItemHistoryMatrix.razor** (lines 150-180): Manual retry button with full-page error state
  - Pattern: User-triggered retry, inline error display
  - Location: `dashboard/Components/ItemHistoryMatrix.razor`

- **TestItemDetails.razor** (lines 200-250): Full-page error state with reload button
  - Pattern: Component-level error boundary, manual recovery
  - Location: `dashboard/Pages/TestItemDetails.razor`

- **ProjectLaunches.razor** (lines 34-40): Inline error banner with dismiss
  - Pattern: Simple error message, no retry logic
  - Location: `dashboard/Pages/ProjectLaunches.razor`

**HTTP Client Patterns**:
- **IHttpClientFactory**: All components use `HttpFactory.CreateClient(HttpClientNames.Hub)`
  - Location: Injected in component constructors
  - Pattern: Centralized client configuration

**Component Base Classes**:
- **ComponentBase**: Standard Blazor base class
  - Location: Microsoft.AspNetCore.Components
  - Pattern: All components inherit lifecycle methods

**Modal Patterns**:
- **No existing modal components**: Currently using inline error displays
  - Opportunity: Create reusable ErrorModal component

### Relevant Patterns (from CLAUDE.md)

**Repository Pattern** (Application/Ports):
- Interface: `IHttpErrorHandler` → Implementation: `HttpErrorHandlerService`
- Aligns with: DDD layer boundaries (Application → Infrastructure)

**SOLID Principles**:
- **SRP**: Separate concerns (error detection, retry logic, UI display)
- **OCP**: Extensible retry policies without modifying existing code
- **DIP**: Components depend on `IHttpErrorHandler`, not concrete implementation

**DRY Principle**:
- Create `DashboardComponentBase` to eliminate duplicated error handling
- Shared error modal component (not 25+ inline error displays)

---

## Approach Comparison

### Approach 1: Decorator Pattern (HttpClient Wrapper)

**Description**: Wrap `IHttpClientFactory` with a decorator that adds retry and error handling.

**Implementation**:
```csharp
public class RetryHttpClientFactory : IHttpClientFactory
{
    private readonly IHttpClientFactory _inner;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;

    public HttpClient CreateClient(string name)
    {
        var client = _inner.CreateClient(name);
        // Attach Polly policy to client
        return client;
    }
}
```

**Layer**: Infrastructure
**Key Classes**:
- `RetryHttpClientFactory` (decorator)
- `PollyRetryPolicyFactory` (policy builder)

**Database Changes**: None

**API Changes**: None (transparent wrapper)

**Pros**:
- ✅ **Transparent**: No component changes needed
- ✅ **Centralized**: All retry logic in one place
- ✅ **Simple**: Minimal code changes

**Cons**:
- ❌ **Inflexible**: Hard to customize per-component
- ❌ **Error Display**: Still need modal logic in components
- ❌ **Deduplication**: Difficult to implement at factory level
- ❌ **Testing**: Hard to test retry behavior per-component

**Complexity**: Low

---

### Approach 2: Base Component with HTTP Helpers

**Description**: Create `DashboardComponentBase` with HTTP helper methods that include retry, error handling, and deduplication.

**Implementation**:
```csharp
public abstract class DashboardComponentBase : ComponentBase
{
    [Inject] protected IHttpClientFactory HttpFactory { get; set; }
    [Inject] protected IHttpErrorHandler ErrorHandler { get; set; }
    [Inject] protected IRequestDeduplicator Deduplicator { get; set; }

    protected async Task<T?> GetJsonAsync<T>(string endpoint)
    {
        return await Deduplicator.ExecuteAsync(
            GenerateKey(endpoint),
            async () => await GetJsonWithRetryAsync<T>(endpoint));
    }
}
```

**Layer**: Interface (Dashboard components)
**Key Classes**:
- `DashboardComponentBase` (base class)
- `HttpErrorHandlerService` (Application)
- `RequestDeduplicationService` (Application)
- `ErrorModal.razor` (Shared component)

**Database Changes**: None

**API Changes**: None

**Pros**:
- ✅ **Flexible**: Components can override/customize behavior
- ✅ **Type-Safe**: Strongly-typed HTTP helpers
- ✅ **Testable**: Easy to mock services and test components
- ✅ **Gradual Migration**: Components opt-in by inheriting base class
- ✅ **Error Display**: Built-in modal support

**Cons**:
- ❌ **Migration Effort**: 25+ components need to inherit base class
- ❌ **Breaking Change**: Components must change inheritance
- ❌ **Complexity**: More moving parts (base class, services, modal)

**Complexity**: Medium

---

### Approach 3: Middleware Pipeline (HttpMessageHandler)

**Description**: Use delegating handlers in HttpClient pipeline to add retry and error handling.

**Implementation**:
```csharp
builder.Services.AddHttpClient(HttpClientNames.Hub)
    .AddPolicyHandler(GetRetryPolicy())
    .AddHttpMessageHandler<DeduplicationHandler>()
    .AddHttpMessageHandler<ErrorLoggingHandler>();
```

**Layer**: Infrastructure
**Key Classes**:
- `RetryDelegatingHandler` (middleware)
- `DeduplicationDelegatingHandler` (middleware)
- `ErrorLoggingDelegatingHandler` (middleware)

**Database Changes**: None

**API Changes**: None

**Pros**:
- ✅ **Transparent**: No component changes
- ✅ **Pipeline**: Clean separation of concerns
- ✅ **Polly Integration**: Native Polly support

**Cons**:
- ❌ **Error Display**: Still need component-level modal logic
- ❌ **Context Loss**: Hard to pass component context to handlers
- ❌ **Deduplication**: Component-level keys difficult to generate
- ❌ **Customization**: Hard to customize per-component

**Complexity**: Medium

---

## Recommendation: Approach 2 (Base Component with HTTP Helpers)

### Justification

**Aligns with Project Requirements**:
1. **Error Modal Display**: Base component provides built-in modal support
2. **Retry Logic**: HTTP helpers integrate Polly retry policies
3. **Deduplication**: Base component has access to component context for key generation
4. **Gradual Migration**: Components migrate one-by-one (phased rollout)

**Aligns with DDD Principles** (from CLAUDE.md):
- **Layer Boundaries**:
  - Application layer: `IHttpErrorHandler`, `IRequestDeduplicator` (interfaces)
  - Infrastructure layer: `HttpErrorHandlerService`, `RequestDeduplicationService` (implementations)
  - Interface layer: `DashboardComponentBase`, `ErrorModal` (UI)
- **Dependency Inversion**: Components depend on interfaces, not concrete classes

**Aligns with SOLID Principles**:
- **SRP**: Each class has single responsibility (error detection, retry, deduplication, display)
- **OCP**: Extensible (add new error types without modifying existing code)
- **DIP**: Base component depends on injected services (interfaces)

**Enables Future Enhancements**:
- Per-component retry configuration (override base class methods)
- Custom error display (override modal rendering)
- Component-specific deduplication keys (override key generation)

### Risks & Mitigations

**Risk 1: Component Migration Effort**
- **Impact**: 25+ components to migrate
- **Mitigation**:
  - Phased rollout (critical components first)
  - Create migration guide with before/after examples
  - Automated refactoring script where possible

**Risk 2: Breaking Component Inheritance**
- **Impact**: Components may already inherit from other base classes
- **Mitigation**:
  - Check all components for existing inheritance (none found in codebase review)
  - If conflicts exist, use composition instead (inject service)

**Risk 3: Testing Complexity**
- **Impact**: Need to test base class behavior in all components
- **Mitigation**:
  - Comprehensive base class unit tests (60+ tests)
  - Integration tests for each migrated component
  - Regression test suite

---

## Detailed Design

### Architecture Overview

#### Component Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    Dashboard Pages/Components                │
│  ┌──────────────────────────────────────────────────────┐  │
│  │        ProjectLaunches.razor (inherits base)          │  │
│  │        ItemHistoryMatrix.razor (inherits base)        │  │
│  │        TestItemDetails.razor (inherits base)          │  │
│  │        ... (25+ components)                           │  │
│  └────────────┬─────────────────────────────────────────┘  │
│               │ inherits                                     │
│  ┌────────────▼─────────────────────────────────────────┐  │
│  │        DashboardComponentBase.razor                   │  │
│  │  - HTTP helper methods (GetJsonAsync, PostJsonAsync)  │  │
│  │  - Error handling (ShowError, ShowModalError)         │  │
│  │  - Loading state (ShowLoading, HideLoading)           │  │
│  │  - Injected services (ErrorHandler, Deduplicator)     │  │
│  └────────────┬────────────┬─────────────┬───────────────┘  │
│               │            │             │                   │
└───────────────┼────────────┼─────────────┼───────────────────┘
                │            │             │
                │ uses       │ uses        │ uses
                ▼            ▼             ▼
┌───────────────────────────────────────────────────────────────┐
│                    Application Services                       │
│  ┌─────────────────┐  ┌──────────────┐  ┌─────────────────┐ │
│  │IHttpErrorHandler│  │IDeduplicator │  │  IToastService  │ │
│  │   (interface)   │  │ (interface)  │  │   (interface)   │ │
│  └────────┬────────┘  └──────┬───────┘  └────────┬────────┘ │
│           │                  │                    │          │
│  ┌────────▼────────┐  ┌──────▼───────┐  ┌────────▼────────┐ │
│  │HttpErrorHandler │  │Deduplication │  │  ToastService   │ │
│  │    Service      │  │   Service    │  │                 │ │
│  │- IsRetryable()  │  │- ExecuteAsync│  │- Success()      │ │
│  │- GetErrorDetails│  │- GenerateKey │  │- Error()        │ │
│  │- ClassifyError()│  │- ClearCache  │  │- Info()         │ │
│  └─────────────────┘  └──────────────┘  └─────────────────┘ │
└───────────────────────────────────────────────────────────────┘
                │
                │ uses
                ▼
┌───────────────────────────────────────────────────────────────┐
│                    Infrastructure (Polly)                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              RetryPolicies (static class)               │ │
│  │  - GetRetryPolicy(ILogger) → IAsyncPolicy               │ │
│  │  - Exponential backoff: 2s, 4s, 8s                      │ │
│  │  - Max 3 attempts                                        │ │
│  │  - Retryable: Network, 5xx, 408, 429                    │ │
│  └─────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────┘
                │
                │ renders
                ▼
┌───────────────────────────────────────────────────────────────┐
│                    Shared Components                          │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                  ErrorModal.razor                       │ │
│  │  - Bootstrap modal with purple gradient header          │ │
│  │  - User-friendly message (prominent)                    │ │
│  │  - Collapsible technical details                        │ │
│  │  - Collapsible stack trace with copy button             │ │
│  │  - Retry button (conditional)                           │ │
│  │  - Dismiss button                                        │ │
│  └─────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────┘
```

#### Data Flow

```
User Action (e.g., "Load Launches")
  ↓
Component calls: await GetJsonAsync<LaunchDto[]>("/api/launches")
  ↓
DashboardComponentBase.GetJsonAsync()
  ├─→ Generate deduplication key
  │   ↓
  ├─→ RequestDeduplicationService.ExecuteAsync()
  │   ├─→ Check cache for in-flight request
  │   ├─→ If found, return existing Task
  │   └─→ If not found, create new Task
  │       ↓
  └─→ GetJsonWithRetryAsync<T>(endpoint)
      ├─→ Create HttpClient from factory
      │   ↓
      ├─→ Polly.ExecuteAsync(retryPolicy)
      │   ├─→ Attempt 1: Send HTTP request
      │   │   ├─→ Success: Return response
      │   │   └─→ Failure: Check IsRetryable()
      │   │       ├─→ Retryable: Wait 2s, retry
      │   │       └─→ Not retryable: Throw
      │   ├─→ Attempt 2: Wait 2s, send HTTP request
      │   │   └─→ Failure: Wait 4s, retry
      │   ├─→ Attempt 3: Wait 4s, send HTTP request
      │   │   └─→ Failure: Wait 8s, retry
      │   └─→ Final attempt: Throw exception
      │       ↓
      └─→ Exception caught by base class
          ├─→ HttpErrorHandler.GetErrorDetails()
          │   ├─→ Parse ProblemDetails JSON
          │   ├─→ Extract stack trace
          │   ├─→ Generate user-friendly message
          │   └─→ Return ErrorDetails object
          │       ↓
          └─→ ShowModalError()
              ├─→ Set ModalError property
              ├─→ Set ErrorModalVisible = true
              ├─→ StateHasChanged()
              │   ↓
              └─→ ErrorModal.razor renders
                  ├─→ Display user-friendly message
                  ├─→ Collapsible technical details
                  ├─→ Collapsible stack trace
                  ├─→ Retry button (if retryable)
                  └─→ Dismiss button
```

---

## Contracts

### Database Schema Changes

**None required** - This is a pure UI/application layer feature.

---

### DTOs

#### ErrorDetails.cs

```csharp
namespace Dashboard.Application;

/// <summary>
/// Comprehensive error details for display in ErrorModal
/// </summary>
public record ErrorDetails
{
    /// <summary>User-friendly error message</summary>
    public required string Message { get; init; }

    /// <summary>Error title (e.g., "Network Error", "Server Error")</summary>
    public required string Title { get; init; }

    /// <summary>Technical error details (JSON formatted)</summary>
    public string? Details { get; init; }

    /// <summary>Exception stack trace</summary>
    public string? StackTrace { get; init; }

    /// <summary>HTTP status code (if applicable)</summary>
    public int? StatusCode { get; init; }

    /// <summary>Request ID / correlation ID</summary>
    public string? RequestId { get; init; }

    /// <summary>Event code for error categorization (e.g., "LCH03", "DB04", "ADM91")</summary>
    public string? EventCode { get; init; }

    /// <summary>HTTP method (GET, POST, etc.)</summary>
    public string? HttpMethod { get; init; }

    /// <summary>Endpoint URL</summary>
    public string? Endpoint { get; init; }

    /// <summary>Timestamp of error</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether this error is retryable</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Error category (Network, Server, Client, Unknown)</summary>
    public ErrorCategory Category { get; init; }
}

public enum ErrorCategory
{
    Network,    // Connection failures, timeouts
    Server,     // 5xx errors
    Client,     // 4xx errors (except 408, 429)
    RateLimit,  // 429 Too Many Requests
    Unknown     // Unexpected errors
}
```

#### ErrorModalModel.cs

```csharp
namespace Dashboard.Shared;

/// <summary>
/// Model for ErrorModal component display
/// </summary>
public class ErrorModalModel
{
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string? Details { get; set; }
    public string? StackTrace { get; set; }
    public bool ShowRetry { get; set; }
    public EventCallback OnRetry { get; set; }
    public EventCallback OnDismiss { get; set; }
}
```

---

### API Endpoints

**No new endpoints** - This feature enhances client-side error handling only.

**Endpoint Changes**: None (existing endpoints remain unchanged)

---

### Interface Definitions

#### IHttpErrorHandler.cs

```csharp
namespace Dashboard.Application.Ports;

/// <summary>
/// Service for classifying and handling HTTP errors
/// </summary>
public interface IHttpErrorHandler
{
    /// <summary>
    /// Determines if an error should trigger retry logic
    /// </summary>
    bool IsRetryable(Exception ex);

    /// <summary>
    /// Determines if an error should trigger retry based on HTTP response
    /// </summary>
    bool IsRetryable(HttpResponseMessage response);

    /// <summary>
    /// Classifies the error category
    /// </summary>
    ErrorCategory ClassifyError(Exception ex, HttpResponseMessage? response = null);

    /// <summary>
    /// Extracts comprehensive error details for display
    /// </summary>
    Task<ErrorDetails> GetErrorDetailsAsync(
        Exception ex,
        HttpResponseMessage? response = null,
        string? endpoint = null,
        string? httpMethod = null);

    /// <summary>
    /// Gets user-friendly error message from HTTP response
    /// </summary>
    Task<string> GetUserMessageAsync(HttpResponseMessage response);
}
```

#### IRequestDeduplicationService.cs

```csharp
namespace Dashboard.Application.Ports;

/// <summary>
/// Service for preventing duplicate concurrent HTTP requests
/// </summary>
public interface IRequestDeduplicationService
{
    /// <summary>
    /// Executes a request with deduplication
    /// If an identical request is in-flight, returns the existing Task
    /// </summary>
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> operation);

    /// <summary>
    /// Generates a deduplication key from request parameters
    /// </summary>
    string GenerateKey(string componentType, string httpMethod, string endpoint, object? parameters = null);

    /// <summary>
    /// Clears the deduplication cache (for testing)
    /// </summary>
    void ClearCache();
}
```

#### IToastService.cs

```csharp
namespace Dashboard.Application.Ports;

/// <summary>
/// Service for displaying toast notifications
/// </summary>
public interface IToastService
{
    void Success(string message, int durationMs = 3000);
    void Error(string message, int durationMs = 5000);
    void Info(string message, int durationMs = 3000);
    void Warning(string message, int durationMs = 4000);
}
```

---

### Component Contracts

#### DashboardComponentBase.razor

```csharp
namespace Dashboard;

/// <summary>
/// Base class for all dashboard components with built-in error handling
/// </summary>
public abstract class DashboardComponentBase : ComponentBase, IAsyncDisposable
{
    // Injected services
    [Inject] protected IHttpClientFactory HttpFactory { get; set; } = default!;
    [Inject] protected IHttpErrorHandler ErrorHandler { get; set; } = default!;
    [Inject] protected IRequestDeduplicationService Deduplicator { get; set; } = default!;
    [Inject] protected IToastService Toasts { get; set; } = default!;
    [Inject] protected ILogger<DashboardComponentBase> Logger { get; set; } = default!;

    // Error state
    protected bool ErrorModalVisible { get; set; }
    protected ErrorModalModel? ModalError { get; set; }
    protected bool IsLoading { get; set; }

    // HTTP helpers
    protected Task<T?> GetJsonAsync<T>(string endpoint);
    protected Task<HttpResponseMessage> PostJsonAsync<T>(string endpoint, T data);
    protected Task<HttpResponseMessage> PutJsonAsync<T>(string endpoint, T data);
    protected Task<HttpResponseMessage> DeleteAsync(string endpoint);

    // Error display helpers
    protected void ShowError(string message);
    protected void ShowModalError(ErrorDetails details, Func<Task>? retryAction = null);
    protected void DismissError();

    // Loading state helpers
    protected void ShowLoading();
    protected void HideLoading();
}
```

#### ErrorModal.razor

```razor
@* Bootstrap-themed error modal *@
<div class="modal @(Visible ? "show d-block" : "")" tabindex="-1" role="dialog">
    <div class="modal-dialog modal-dialog-centered" role="document">
        <div class="modal-content">
            @* Header with gradient background *@
            <div class="modal-header bg-gradient-purple text-white">
                <h5 class="modal-title">
                    <i class="bi bi-exclamation-triangle-fill me-2"></i>
                    @Title
                </h5>
                <button type="button" class="btn-close btn-close-white" @onclick="OnDismiss"></button>
            </div>

            @* Body with message and collapsible details *@
            <div class="modal-body">
                <p class="fw-bold text-danger">@Message</p>

                @if (!string.IsNullOrWhiteSpace(Details))
                {
                    <div class="accordion">
                        <div class="accordion-item">
                            <h2 class="accordion-header">
                                <button class="accordion-button collapsed" type="button">
                                    Technical Details
                                </button>
                            </h2>
                            <div class="accordion-collapse collapse">
                                <div class="accordion-body">
                                    <pre class="bg-light p-3 rounded">@Details</pre>
                                </div>
                            </div>
                        </div>
                    </div>
                }

                @if (!string.IsNullOrWhiteSpace(StackTrace))
                {
                    <div class="accordion mt-2">
                        <div class="accordion-item">
                            <h2 class="accordion-header">
                                <button class="accordion-button collapsed" type="button">
                                    Stack Trace
                                </button>
                            </h2>
                            <div class="accordion-collapse collapse">
                                <div class="accordion-body">
                                    <button class="btn btn-sm btn-outline-secondary mb-2" @onclick="CopyStackTrace">
                                        <i class="bi bi-clipboard"></i> Copy
                                    </button>
                                    <pre class="bg-light p-3 rounded small">@StackTrace</pre>
                                </div>
                            </div>
                        </div>
                    </div>
                }
            </div>

            @* Footer with action buttons *@
            <div class="modal-footer">
                <button class="btn btn-outline-primary me-auto" @onclick="CaptureModalAsPng">
                    <i class="bi bi-camera"></i> Save as PNG
                </button>
                @if (ShowRetry)
                {
                    <button class="btn btn-primary" @onclick="OnRetry">
                        <i class="bi bi-arrow-clockwise"></i> Retry
                    </button>
                }
                <button class="btn btn-secondary" @onclick="OnDismiss">Dismiss</button>
            </div>
        </div>
    </div>
</div>

@* Modal backdrop *@
@if (Visible)
{
    <div class="modal-backdrop show"></div>
}

@code {
    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }
    [Parameter] public required string Title { get; set; }
    [Parameter] public required string Message { get; set; }
    [Parameter] public string? Details { get; set; }
    [Parameter] public string? StackTrace { get; set; }
    [Parameter] public bool ShowRetry { get; set; }
    [Parameter] public EventCallback OnRetry { get; set; }
    [Parameter] public EventCallback OnDismiss { get; set; }
}
```

---

## Dependencies

### External Libraries

**NuGet Packages to Add**:
```xml
<PackageReference Include="Polly" Version="8.4.1" />
<PackageReference Include="Polly.Extensions.Http" Version="3.0.0" />
```

**Existing Dependencies** (no changes):
- `Microsoft.AspNetCore.Components.Web` - Blazor components
- `Microsoft.Extensions.Http` - IHttpClientFactory
- Bootstrap 5 - Modal styling (already included)

### Infrastructure Dependencies

**Redis**: Not required (deduplication uses in-memory cache)

**RabbitMQ**: Not required (no event publishing)

**Database**: Not required (no persistence)

### Other Features

**Depends On**:
- ✅ IHttpClientFactory (already configured in Program.cs)
- ✅ Bootstrap 5 theme (already included)
- ✅ Chunked logging (for server-side error logging)

**Does Not Affect**:
- SignalR connections (error handling isolated to HTTP)
- Background services (workers, ingestion, housekeeping)
- Database operations (no schema changes)

---

## Quality Gate: Ready for Task Breakdown

Verification:

- [x] **Multiple Approaches**: 3 approaches documented with tradeoffs
- [x] **Clear Recommendation**: Approach 2 chosen with detailed justification
- [x] **Contracts Defined**: DTOs, interfaces, components specified
- [x] **DDD Compliance**: Design follows layer boundaries (Application → Infrastructure → Interface)
- [x] **SOLID Compliance**: SRP, OCP, DIP followed
- [x] **Risk Assessment**: 3 risks identified with mitigations
- [x] **Dependency Analysis**: All dependencies listed
- [x] **Data Flow**: Complete flow diagram from user action to modal display

---

## Next Steps

1. ✅ **Specification Complete** (Stage 1)
2. ✅ **Architecture Planning Complete** (Stage 2)
3. ➡️ **Task Breakdown** (Stage 3) - Create detailed implementation tasks
4. **Implementation** (Stage 4) - Execute with TDD
5. **Documentation** (Stage 5) - Update CLAUDE.md

---

*This architecture follows Approach 2 (Base Component with HTTP Helpers) as recommended above. Proceed to Stage 3: Task Breakdown.*
