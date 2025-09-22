Playwright…≥# Playwright Grid – Improvement Tasks Checklist

Generated: 2025-09-11 11:30 local time

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
38. [X] Add API to export run details (JSON/NDJSON) for external archiving.
39. [x] Provide Helm chart/Kubernetes manifests with sensible defaults, probes, and resource limits.
40. [ ] Harden Docker images: run as non-root user, drop capabilities, read-only filesystem with writable temp for Playwright.
41. [ ] Slim Docker images further: prune caches (npm, dotnet), multi-stage for Node assets, consolidate OS packages, consider distroless base.
42. [X] Add multi-arch builds (linux/amd64, linux/arm64) for Hub/Worker via buildx.
43. [X] Add image vulnerability scanning (Trivy/GHCR) and SBOM generation during CI.
44. [X] Introduce GitHub Actions CI: build, unit tests, integration tests (with Testcontainers), publish artifacts, and optional Docker image publish.
45. [X] Add workflow caching for dotnet restore, npm playwright installs, and docker layers to speed up CI.
46. [X] Expand unit tests for label matching, options parsing (POOL_CONFIG), and secret handling edge cases.
47. [x] Add integration tests for: secret mismatch (401), capacity exhaustion (503), borrow queue timeout, and node eviction scenarios.
48. [x] Add flaky-test mitigations: deterministic time helpers, extended health timeouts via env, and richer test diagnostics.
49. [x] Provide smoke test for Dashboard SignalR stream (connect, receive events, disconnect) without browsers.
50. [ ] Add load/pressure test harness (NUnit category) with configurable CONCURRENCY/ITERATIONS and asserts on latency percentiles.
52. [ ] Add architecture diagrams (C4 model: Context, Container, Component) and sequence diagram for borrow/return.
53. [X] Create CONTRIBUTING.md (coding standards, commit messages, branching, PR checklist).
54. [X] Establish versioning and release notes; tag releases and publish Agenix.PlaywrightGrid.HubClient to NuGet.
55. [X] Add compatibility matrix documenting supported Playwright versions and Docker base image tags.
57. [x] Add configuration to toggle wildcards separately from trailing fallback/prefix expansion per-environment.
58. [x] Add per-label concurrency caps and fair sharing to prevent one label from starving others.
59. [x] Provide metrics-driven autoscaling hints (HPA annotations) based on borrow queue length and CPU for Workers.
60. [ ] Ensure graceful recovery scenarios: Hub restart does not break in-flight WebSocket sessions; document impact and mitigation.
61. [x] Add health and readiness endpoints separation; ensure /health checks critical dependencies and /ready reflects capacity.
62. [X] Add startup diagnostics dump (effective config, labels registered per node) visible in logs and Dashboard.
63. [x] Implement audit logging for node registration, secret changes, and admin actions.
64. [x] Add command-line tooling or scripts to validate POOL_CONFIG and compute effective capacity before boot.
65. [x] Provide local dev convenience: make .env support across Hub/Worker and docs on docker compose overrides.
66. [x] Improve error messages in Dashboard UI to point to remediation steps (e.g., capacity missing, secret mismatch, WS unreachable).
67. [x] Add browser-specific tuning options (Chromium args, Firefox prefs, WebKit flags) with validation and documentation.
68. [x] Enforce API request size limits and reasonable timeouts in Hub; document limits.
71. [x] Add support for custom labels (e.g., Channel, Headless) with controlled cardinality to avoid metrics explosion.
72. [X] Refactor Dashboard Results pages to use server-driven paging and streaming for command logs.
73. [x] Ensure all public APIs and DTOs have XML docs and nullable annotations; generate API docs from XML.
74. [ ] Introduce coding analyzers (StyleCop/IDisposable analyzers) and fix high-signal warnings.
75. [x] Add guardrails for Redis key naming to avoid collisions; centralize key patterns with tests.

76. [ ] Integrate X11 virtual display (Xvfb) in Worker Docker image: install xvfb, xauth, fonts, and required deps; verify image size impact.
77. [ ] Add WORKER_XVFB_ENABLED env flag (default: true in containers) and DISPLAY management (e.g., :99) in Worker startup.
78. [ ] Implement Worker sidecar/process supervisor to launch Xvfb on boot when enabled; ensure restarts/backoff and logs are captured.
79. [ ] Provide option to use xvfb-run wrapper vs. dedicated Xvfb process; document pros/cons and choose default.
80. [ ] Wire Playwright headful mode support via DISPLAY with environment propagation to browser processes; document headless/headful matrix.
81. [ ] Add health/readiness checks for Xvfb (e.g., xdpyinfo sanity) and expose a metric (worker_xvfb_up) and logs for diagnostics.
82. [ ] Ensure graceful shutdown: stop accepting new sessions, close browsers, then terminate Xvfb cleanly.
83. [ ] Harden security: run Xvfb as non-root, restrict access control (xauth cookie), avoid TCP listeners.
84. [ ] Extend WorkerOptions.FromEnvironment() to parse XVFB-related envs (enabled, display number, screen size, dpi); add unit tests.
85. [ ] Add integration test path that borrows a session with headful=true and validates navigation succeeds under Xvfb.
86. [ ] Update worker/Dockerfile and docker-compose.yml with XVFB packages and env examples; include minimal fonts set and note locales.
87. [ ] Update docs: README.md (usage), docs/Compatibility-Matrix.md (headful notes), and dashboard guidance for troubleshooting Xvfb.
88. [ ] Add troubleshooting playbook: common errors (cannot open display, fonts missing), with steps and env toggles to disable/enable Xvfb.

89. [ ] Enforce HTTPS by default in docker-compose via reverse proxy (Traefik/Nginx), enable HSTS, secure cookies, and strong TLS settings; document local dev exceptions.
90. [ ] Tighten CORS and add CSRF protection where applicable (Dashboard/API forms), with explicit allowed origins and methods.
91. [ ] Introduce JWT/HMAC request signing for Hub API (time-limited tokens minted by Hub) as an alternative to shared secrets; provide migration guidance.
92. [ ] Support secrets from files via *_FILE env convention and optional integration with external secret stores (AWS Secrets Manager/Azure Key Vault/GCP Secret Manager).
93. [ ] Add automated security checks: CodeQL workflow, Dependency/Container update automation (Dependabot/Renovate) with review rules.
94. [ ] Enable horizontal scaling for Hub: move borrow queue to Redis Streams with consumer groups; implement idempotency and deduplication.
95. [ ] Implement distributed leadership for sweeper jobs using Redis (SETNX + TTL) to coordinate multiple Hub instances safely.
96. [ ] Quarantine flapping or failing Worker nodes (cooldown period) and surface quarantine state in Dashboard and metrics.
97. [ ] Add Redis connection options for Sentinel/Cluster and TLS; document configuration and failover behavior.
98. [ ] Provide idempotency keys for Borrow/Return endpoints to handle client retries without duplicate sessions.
99. [ ] Enforce per-Worker max concurrent WebSocket connections (configurable); expose saturation metrics and headroom.
100. [x] Monitor disk/inode usage in Worker; auto-clean old browser caches/traces; emit alerts when thresholds breached.
101. [x] Add WS per-message compression toggle with thresholds to balance CPU vs bandwidth; document defaults.
102. [x] Implement safe sidecar upgrade flow (graceful drain + restart) coordinated with Hub to avoid session drops.
103. [ ] Improve Dashboard accessibility (WCAG 2.1 AA): keyboard navigation, landmarks, focus management, color contrast, ARIA labels.
104. [ ] Gate Dashboard features by role (admin/viewer) based on OIDC group/claim mapping; hide admin endpoints from non-admins. (extends 36)
105. [ ] Allow exporting run artifacts (HAR/trace/logs) and provide deep links to Playwright trace viewer; bulk download.
106. [ ] Add HubClient DI extensions (AddHubClient) with options; support proxies/custom headers; expose retry/jitter tuning knobs.
107. [ ] Implement idempotency support in HubClient for borrow/return (Idempotency-Key header) and transparent retry handling.
108. [ ] Package a CLI (dotnet tool) to interact with the grid: login, list-labels, borrow/return, tail logs, diagnose; publish to NuGet.
109. [ ] Add Prometheus exemplars and trace linkage for borrow latency histograms; propagate runId/traceId via W3C baggage.
110. [ ] Define and codify SLOs with alerting rules (borrow success rate, p95 latency, node heartbeat gap); ship Prometheus/Grafana alerts.
111. [ ] Expand testing with property-based and fuzz tests for label parsing/matching and Hub request validation.
112. [ ] Add chaos tests: Redis outage, Hub/Worker restarts, network partitions, and clock skew; assert recovery within SLO.
113. [ ] Create a nightly soak test pipeline to run long-duration borrow/return cycles and report regressions.
114. [ ] Benchmark critical paths (label matching, Redis operations) with BenchmarkDotNet; track regressions in CI.
115. [ ] Profile Hub/Worker memory/CPU under load; reduce allocations and capture flamegraphs for hot paths.
116. [ ] Add developer tooling: devcontainer setup, Makefile targets, pre-commit hooks (dotnet format, analyzers) and consistent .editorconfig.
117. [x] Optimize Dockerfiles with BuildKit cache mounts and better layer ordering; document cache strategy.
118. [ ] Author a security threat model (STRIDE) and hardening guide; include SRE runbooks and incident response procedures.
119. [ ] Automate diagram generation and publishing (Mermaid/PlantUML) as part of mkdocs; integrate with architecture docs (52).
120. [ ] Validate IPv6 and proxy support end-to-end; document reverse proxy patterns and limitations.
121. [ ] Provide reverse-proxy examples (Traefik/Nginx) with sticky sessions for WS and TLS termination; include compose overrides.
122. [ ] Introduce multi-tenancy: namespaced labels and quotas/rate limits per tenant; surface tenant in metrics and logs.
123. [ ] Enable hot-reload for config via IOptionsMonitor where safe (log levels, borrow strategy flags) without restarts.
124. [ ] Adopt FeatureManagement for feature flags with environment/tenant targeting; wire to existing strategy toggles (56).
125. [ ] Ensure audit logs are tamper-evident and optionally export to external SIEM (OTLP/syslog); add retention controls.
126. [ ] Sign container images (cosign) and publish provenance/SBOM attestations (SLSA level targets) in CI.
127. [ ] Add localization (i18n) to Dashboard with language switcher; ensure date/number formatting respects locale.
128. [ ] Document and optionally support GPU acceleration (NVIDIA/Intel VA-API) for headful runs; provide example images and detection.
129. [ ] Define Redis memory and eviction policies; emit alarms when approaching limits and document tuning guidance.
130. [ ] Add pagination/filtering/count endpoints for admin APIs (nodes, sessions, runs) to aid tooling and Dashboard.
131. [x] Implement a durable store adapter (e.g., PostgreSQL) with schema migrations for long-term run/log retention; make pluggable.
132. [ ] Minimize telemetry PII; add sampling/redaction policies across traces/logs/metrics with config-driven controls.
133. [ ] Add scheduled synthetic monitors (GitHub Actions cron) to hit /ready and perform a basic borrow against a local grid.


134. [x] Introduce RunName as a first-class, optional human-friendly identifier alongside RunId across the platform (Hub, Worker, Dashboard, HubClient).
135. [x] Define validation rules for RunName: trim input, max length 128, allow letters/numbers/space/._- only; reject control chars; document case policy.
136. [x] Domain model: add RunName (string?) to shared DTOs/entities (Run, BorrowRequest/Response, RunSummary) with XML docs and nullability annotations.
137. [x] Hub API: accept RunName in Borrow request payloads (and propagate in response); expose in Run results and SignalR events; update OpenAPI with examples.
138. [x] Backward compatibility: keep RunName optional; default display to RunId when RunName is null/empty; do not break existing clients.
139. [x] Storage: persist RunName alongside RunId in Redis (keys/values); verify schema/read paths; ensure sweeper and TTL logic include RunName where relevant.
140. [x] Hub logging/metrics: include RunName in structured logs as a field (not a metric label) to avoid high-cardinality metrics; add redaction if enabled.
142. [x] Worker: carry RunName in WS proxy scopes and forward in event/log messages to Hub; include in sidecar run context if applicable.
143. [x] HubClient: add optional runName parameter to BorrowAsync and related methods; update overloads and XML docs; maintain existing signatures.
144. [x] Dashboard UI: display RunName prominently in Results and Run detail pages; fall back to RunId if missing; add filter/search by RunName; include in deep links.
145. [x] Dashboard API/adapters: extend view models and queries to surface RunName; ensure server-driven paging/sorting can sort by RunName.
146. [x] Tests – unit: add validation tests for RunName parsing; update DTO serialization tests; cover HubClient overload behavior and null/empty handling.
147. [x] Tests – integration: borrow a session with RunName set and assert it appears in results, SignalR stream, and Dashboard filtering.
149. [x] Documentation: update README, API docs (Swagger snippets), and dashboard guidance with examples using RunName; add examples in docs/cli.md if relevant.
151. [x] Security/PII: clarify that RunName may contain descriptive text; recommend avoiding sensitive data; ensure redaction feature covers RunName if policy set.
152. [x] Non-goals: do not add RunName to metric labels or Redis keys; keep it as data only to avoid cardinality/compat issues; document rationale.



153. [ ] AI and LLM Enhancements (optional, privacy‑first, disabled by default)
154. [ ] Dashboard: "Explain this run" – generate an LLM summary of a run (errors, likely root cause, next steps) from redacted command logs and timings; expose a button on Results/Run pages; include copy-to-issue. Guard with AI_ENABLE=1. 
155. [ ] Failure triage auto‑classification – categorize failures (capacity, auth/secret, WS/connectivity, site under test, timeouts, browser crash) via prompt with few‑shot examples; store category in run metadata and make it filterable in Dashboard.
156. [ ] Natural language search for results – convert free‑text queries (e.g., "Chromium UAT runs failing with timeouts yesterday") into structured filters (App/Browser/Env/Status/Time); provide offline synonym map fallback when AI is disabled.
157. [ ] RAG assistant for docs/config – in‑Dashboard helper that answers "How do I …?" using a local index of project docs (README, tasks, compatibility matrix) and current effective config; prefer local retrieval; only call LLM when explicitly enabled.
158. [ ] Capacity planning recommender – analyze historical label utilization and borrow queue metrics to suggest POOL_CONFIG adjustments and forecast demand by label; present as a report; use classical stats first; optionally add an LLM summary.
159. [ ] Anomaly detection and incident notes – detect spikes in borrow latency, WS disconnects, node heartbeats via simple z‑score/EWMA; open an incident card in Dashboard with an optional "AI incident summary" containing probable causes and suggested checks.
160. [ ] Flakiness detector – cluster intermittent failures across runs/labels; surface flaky labels/tests with confidence; add a weekly dashboard report.
161. [ ] Auto‑remediation hints – when common misconfigurations are detected (PUBLIC_WS_HOST mismatch, secret mismatch, no capacity for label), show contextual remediation steps; optionally generate an issue template with prefilled diagnostics.
162. [ ] AI provider plumbing – provider‑agnostic abstraction (OpenAI, Azure OpenAI, Ollama/local) behind an interface; env: AI_PROVIDER, AI_API_KEY/ENDPOINT, AI_MODEL, AI_ENABLE; strict timeouts, retries, and rate limits.
163. [ ] Safety and cost controls – redact all secrets/PII before prompt; token accounting and per‑day budgets; allow per‑feature enablement; never log prompt/response content, only metadata; document data handling.
164. [ ] Unit tests for prompt builders/mappers and NL→filter translation; add record/replay fixtures for CI (canned responses) so tests run without network/API keys.
165. [ ] Telemetry for AI features – Prometheus counters/histograms for usage and latency; exemplar linkage to runId; dashboards for success/error rates; no high‑cardinality content.
166. [ ] Security review – ensure no secrets (headers, query params) can leak into prompts; honor existing redaction settings; add a global kill switch (AI_ENABLE=0).
167. [ ] Documentation – add docs/ai.md detailing enabling providers, example prompts, privacy expectations, and local dev via Ollama; link from README and Dashboard help.
168. [ ] Non‑goals/guardrails – never make core flows depend on AI; AI features must fail‑safe, degrade gracefully, and offer offline paths (e.g., deterministic synonym search, stats‑only reports).
