# Playwright Grid – Improvement Tasks Checklist

Generated: 2025-08-21 18:55 local time

The following is an ordered, actionable checklist covering architectural and code-level improvements across Hub, Worker, Dashboard, HubClient, tests, and containerization. Check items as they are completed.

1. [X] Establish a shared Domain model package for LabelKey parsing/validation to ensure consistent rules across Hub, Worker, Dashboard, and tests.
2. [X] Introduce a central Label Matching strategy service with unit tests (exact → trailing fallback → prefix expansion → optional wildcards) and pluggable settings.
3. [X] Add input validation and normalization for label keys (trim, case policy, segment count min/max, forbidden characters) with clear 4xx errors.
4. [X] Define API versioning (e.g., /api/v1) for Hub endpoints and reserve room for breaking changes.
5. [X] Introduce ProblemDetails-based error responses in Hub for consistent 4xx/5xx payloads.
6. [X] Add OpenAPI/Swagger to Hub with minimal surface (security header documented) and examples for borrow/return.
7. [X] Implement request correlation (Correlation-Id header or generated) propagated as runId/browserId across Hub, Worker, and Dashboard logs.
8. [X] Add distributed tracing via OpenTelemetry (traces, metrics, logs) with exporters configurable (OTLP/Prometheus).
9. [X] Expand Prometheus metrics: borrow latency histogram, borrow outcomes (success/timeout/denied), pool utilization per label, queue length, node heartbeats.
10. [X] Introduce a capacity queue in Hub for pending borrows with timeout and fairness (per-label and per-run caps) to reduce thundering herd.
11. [X] Implement node heartbeat/liveness tracker with configurable timeout; evict stale nodes and reclaim/expire orphaned sessions.
12. [X] Add borrow TTL and auto-return on timeout; persist session state to Redis to survive Hub restarts.
13. [X] Harden Redis usage: resilience (timeouts, retries with jitter, circuit breaker), connection settings, and health checks integrated into readiness.
14. [ ] Support secret rotation: accept multiple HUB_RUNNER_SECRET/HUB_NODE_SECRET values (comma-separated) and log deprecation windows.
15. [ ] Redact secrets and PII in logs; ensure headers and sensitive values never appear in structured logs.
16. [ ] Add rate limiting (per IP and per runner id) on Hub borrow/return to protect from abuse; return 429 with Retry-After.
17. [ ] Add optional IP allowlist or token-based auth (e.g., PAT via header) for Hub API alongside shared secrets.
18. [X] Implement graceful shutdown: Hub stops accepting new borrows; Worker drains sessions and returns cleanly on SIGTERM.
19. [X] Enforce maximum WebSocket message size and idle timeouts in Worker; send periodic pings and close dead connections.
20. [X] Add backpressure controls in Worker WS proxy (bounded channels, drop policy, and metrics for drops).
21. [X] Strengthen Worker sidecar management: sidecar health endpoint, restart/backoff strategy, and clear error surfacing to Hub.
22. [X] Make PLAYWRIGHT_VERSION reporting authoritative: validate against sidecar; surface mismatch in Dashboard and metrics.
23. [X] Improve WorkerOptions.FromEnvironment() with strong typing, defaults, range checks, and detailed validation errors.
24. [X] Replace ad-hoc HttpClient usage in HubClient with IHttpClientFactory and resilience (timeouts, retries, transient error policy).
25. [X] Add CancellationToken overloads to HubClient methods (BorrowAsync, ReturnAsync, SendApiLogAsync).
26. [X] Introduce domain-specific exceptions in HubClient (CapacityUnavailableException, AuthenticationException, ProtocolException).
27. [X] Batch and rate-limit HubClient log sending; add async buffering to minimize impact on runner.
28. [X] Add optional log redaction in PlaywrightEventForwarder (query param scrub, headers whitelist) and sampling controls.
29. [X] Ensure nullability annotations are correct across Hub, Worker, HubClient; enable nullable warnings as errors on CI.
30. [X] Audit async paths to avoid sync-over-async; ensure proper ConfigureAwait usage in library code where applicable.
31. [X] Standardize structured logging (Serilog or built-in ILogger scopes) with runId/browserId scope enrichment.
32. [X] Provide configurable log levels and per-component overrides via environment.
33. [X] Add graceful error pages and dashboard error boundaries for SignalR disconnections with auto-retry/backoff.
34. [X] Implement virtualization/pagination for Dashboard results and command logs to prevent UI slowdowns on large runs.
35. [x] Add filtering/search on Dashboard (by App, Browser, Env, Region, Status, runId) and deep links.
36. [ ] Introduce authentication for Dashboard (OIDC/OAuth2) with role-based access (viewer/admin); secure SignalR hub accordingly.
37. [X] Add retention policies for run results and logs (TTL in Redis; optional durable store adapter e.g., PostgreSQL/SQLite).
38. [ ] Add API to export run details (JSON/NDJSON) for external archiving.
39. [ ] Provide Helm chart/Kubernetes manifests with sensible defaults, probes, and resource limits.
40. [ ] Harden Docker images: run as non-root user, drop capabilities, read-only filesystem with writable temp for Playwright.
41. [ ] Slim Docker images further: prune caches (npm, dotnet), multi-stage for Node assets, consolidate OS packages, consider distroless base.
42. [X] Add multi-arch builds (linux/amd64, linux/arm64) for Hub/Worker via buildx.
43. [X] Add image vulnerability scanning (Trivy/GHCR) and SBOM generation during CI.
44. [X] Introduce GitHub Actions CI: build, unit tests, integration tests (with Testcontainers), publish artifacts, and optional Docker image publish.
45. [X] Add workflow caching for dotnet restore, npm playwright installs, and docker layers to speed up CI.
46. [ ] Expand unit tests for label matching, options parsing (POOL_CONFIG), and secret handling edge cases.
47. [ ] Add integration tests for: secret mismatch (401), capacity exhaustion (503), borrow queue timeout, and node eviction scenarios.
48. [ ] Add flaky-test mitigations: deterministic time helpers, extended health timeouts via env, and richer test diagnostics.
49. [ ] Provide smoke test for Dashboard SignalR stream (connect, receive events, disconnect) without browsers.
50. [ ] Add load/pressure test harness (NUnit category) with configurable CONCURRENCY/ITERATIONS and asserts on latency percentiles.
52. [ ] Add architecture diagrams (C4 model: Context, Container, Component) and sequence diagram for borrow/return.
53. [X] Create CONTRIBUTING.md (coding standards, commit messages, branching, PR checklist).
54. [X] Establish versioning and release notes; tag releases and publish Agenix.PlaywrightGrid.HubClient to NuGet.
55. [ ] Add compatibility matrix documenting supported Playwright versions and Docker base image tags.
56. [ ] Implement feature flags for borrow strategies and dashboard features to allow safe rollout.
57. [ ] Add configuration to toggle wildcards separately from trailing fallback/prefix expansion per-environment.
58. [ ] Add per-label concurrency caps and fair sharing to prevent one label from starving others.
59. [ ] Provide metrics-driven autoscaling hints (HPA annotations) based on borrow queue length and CPU for Workers.
60. [ ] Ensure graceful recovery scenarios: Hub restart does not break in-flight WebSocket sessions; document impact and mitigation.
61. [ ] Add health and readiness endpoints separation; ensure /health checks critical dependencies and /ready reflects capacity.
62. [X] Add startup diagnostics dump (effective config, labels registered per node) visible in logs and Dashboard.
63. [ ] Implement audit logging for node registration, secret changes, and admin actions.
64. [ ] Add command-line tooling or scripts to validate POOL_CONFIG and compute effective capacity before boot.
65. [ ] Provide local dev convenience: make .env support across Hub/Worker and docs on docker compose overrides.
66. [ ] Improve error messages in Dashboard UI to point to remediation steps (e.g., capacity missing, secret mismatch, WS unreachable).
67. [ ] Add browser-specific tuning options (Chromium args, Firefox prefs, WebKit flags) with validation and documentation.
68. [ ] Enforce API request size limits and reasonable timeouts in Hub; document limits.
69. [ ] Add per-run storage quotas for logs to prevent Redis bloat; evict with LRU and surface warnings.
70. [ ] Provide end-to-end example repo or script showing borrowing via HubClient and capturing a screenshot.
71. [ ] Add support for custom labels (e.g., Channel, Headless) with controlled cardinality to avoid metrics explosion.
72. [X] Refactor Dashboard Results pages to use server-driven paging and streaming for command logs.
73. [ ] Ensure all public APIs and DTOs have XML docs and nullable annotations; generate API docs from XML.
74. [ ] Introduce coding analyzers (StyleCop/IDisposable analyzers) and fix high-signal warnings.
75. [ ] Add guardrails for Redis key naming to avoid collisions; centralize key patterns with tests.
