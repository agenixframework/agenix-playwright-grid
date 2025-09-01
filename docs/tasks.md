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
13. [ ] Harden Redis usage: resilience (timeouts, retries with jitter, circuit breaker), connection settings, and health checks integrated into readiness.
14. [ ] Support secret rotation: accept multiple HUB_RUNNER_SECRET/HUB_NODE_SECRET values (comma-separated) and log deprecation windows.
15. [ ] Redact secrets and PII in logs; ensure headers and sensitive values never appear in structured logs.
16. [ ] Add rate limiting (per IP and per runner id) on Hub borrow/return to protect from abuse; return 429 with Retry-After.
17. [ ] Add optional IP allowlist or token-based auth (e.g., PAT via header) for Hub API alongside shared secrets.
18. [ ] Implement graceful shutdown: Hub stops accepting new borrows; Worker drains sessions and returns cleanly on SIGTERM.
19. [ ] Enforce maximum WebSocket message size and idle timeouts in Worker; send periodic pings and close dead connections.
20. [ ] Add backpressure controls in Worker WS proxy (bounded channels, drop policy, and metrics for drops).
21. [ ] Strengthen Worker sidecar management: sidecar health endpoint, restart/backoff strategy, and clear error surfacing to Hub.
22. [ ] Make PLAYWRIGHT_VERSION reporting authoritative: validate against sidecar; surface mismatch in Dashboard and metrics.
23. [ ] Improve WorkerOptions.FromEnvironment() with strong typing, defaults, range checks, and detailed validation errors.
24. [X] Replace ad-hoc HttpClient usage in HubClient with IHttpClientFactory and resilience (timeouts, retries, transient error policy).
25. [ ] Add CancellationToken overloads to HubClient methods (BorrowAsync, ReturnAsync, SendApiLogAsync).
26. [ ] Introduce domain-specific exceptions in HubClient (CapacityUnavailableException, AuthenticationException, ProtocolException).
27. [ ] Batch and rate-limit HubClient log sending; add async buffering to minimize impact on runner.
28. [ ] Add optional log redaction in PlaywrightEventForwarder (query param scrub, headers whitelist) and sampling controls.
29. [ ] Ensure nullability annotations are correct across Hub, Worker, HubClient; enable nullable warnings as errors on CI.
30. [ ] Audit async paths to avoid sync-over-async; ensure proper ConfigureAwait usage in library code where applicable.
31. [ ] Standardize structured logging (Serilog or built-in ILogger scopes) with runId/browserId scope enrichment.
32. [ ] Provide configurable log levels and per-component overrides via environment.
33. [ ] Add graceful error pages and dashboard error boundaries for SignalR disconnections with auto-retry/backoff.
34. [ ] Implement virtualization/pagination for Dashboard results and command logs to prevent UI slowdowns on large runs.
35. [X] Add filtering/search on Dashboard (by App, Browser, Env, Region, Status, runId) and deep links.
36. [ ] Introduce authentication for Dashboard (OIDC/OAuth2) with role-based access (viewer/admin); secure SignalR hub accordingly.
37. [ ] Add retention policies for run results and logs (TTL in Redis; optional durable store adapter e.g., PostgreSQL/SQLite).
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
51. [ ] Introduce a structured configuration guide (docs/configuration.md) with examples and common pitfalls.
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
76. [ ] Provide sample Grafana dashboards for new metrics (latency, queue, utilization, errors) and alerts.



---

#### Documentation — MkDocs (Material) Site Plan

Goal: Publish docs/ as a polished documentation site using MkDocs with the Material theme, deployed to GitHub Pages via gh-pages.

Scope:
- Source directory: docs/
- Styling: docs/assets/styles.css (small overrides)
- Navigation: docs/index.md + existing Markdown pages
- Deployment: GitHub Actions → gh-pages branch → GitHub Pages

Tasks:
1) Create MkDocs configuration
- Add mkdocs.yml at repo root.
- Set site_name, site_url, repo_url, docs_dir: docs.
- Enable Material theme, common features, and wire extra_css: .assets/styles.css.
- Define nav using current files (Borrow-TTL-and-Session-Persistence.md, Node-Liveness-and-Sweeper.md, tasks.md). 

2) Prepare docs/index.md
- Create a brief landing page with links to the key guides.
- Keep relative links so the site works under /<repo>.

3) Add custom CSS
- Place docs/.assets/styles.css with minimal, tasteful overrides (typography, code blocks). Keep it small since Material already provides excellent defaults.

4) Local preview docs
- Install: pip install mkdocs-material.
- Run locally: mkdocs serve and validate the site (nav, styling, internal links).

5) GitHub Actions workflow for deploy
- Add .github/workflows/docs.yml that:
  - Triggers on changes to docs/** or mkdocs.yml.
  - Builds with mkdocs build --strict.
  - Publishes ./site to gh-pages using peaceiris/actions-gh-pages.

6) Enable GitHub Pages
- Settings → Pages → Deploy from a branch → gh-pages, folder “/”.
- Verify the site URL and that assets (CSS, images) load correctly.

7) Documentation hygiene
- Ensure images (if any) live under docs/.assets/img/ and are referenced relatively.
- Keep links relative (no hard-coded domain) to support forks and PR previews.
- Add a short “Docs” blurb to README linking to the hosted site.

8) Nice-to-have (post-MVP)
- Enable search fine-tuning (Material search features are on by default).
- Add dark/light palette tuning if desired.
- Add redirects if you rename pages (mkdocs-redirects plugin).

Acceptance criteria:
- Visiting the GitHub Pages URL renders a Material-themed site with at least the following pages:
  - Home (index)
  - Borrow TTL & Session Persistence
  - Node Liveness and Sweeper
  - Tasks (this checklist)
- nav in the left sidebar matches the configured order.
- Extra CSS is applied (code blocks styled, minor typographic tweaks) without breaking Material.
- The workflow rebuilds and deploys on every push to main that changes docs/** or mkdocs.yml.
- Build is strict (fails on missing links or config errors).

Operational notes:
- To run locally:
  - python -m pip install --upgrade pip
  - pip install mkdocs-material
  - mkdocs serve (preview at http://127.0.0.1:8000)
- Keep CSS minimal; rely on Material for most styling.
- If a custom domain is used, configure CNAME in gh-pages (actions-gh-pages supports cname: input).

Templates (copy/paste and adjust):

mkdocs.yml (repo root)
```yml
site_name: Playwright Grid Docs
site_url: https://<your-user>.github.io/<repo>/
repo_url: https://github.com/<your-user>/<repo>
docs_dir: docs

theme:
  name: material
  features:
    - navigation.instant
    - navigation.tracking
    - navigation.top
    - content.code.copy
    - content.tabs.link
    - search.suggest
    - search.highlight
  palette:
    - media: "(prefers-color-scheme: light)"
      scheme: default
      primary: blue
      accent: light blue
    - media: "(prefers-color-scheme: dark)"
      scheme: slate
      primary: blue
      accent: light blue

extra_css:
  - .assets/styles.css  # relative to docs_dir

nav:
  - Home: index.md
  - Guides:
      - Borrow TTL & Session Persistence: Borrow-TTL-and-Session-Persistence.md
      - Node Liveness and Sweeper: Node-Liveness-and-Sweeper.md
  - Project:
      - Tasks: tasks.md
```

docs/index.md
```md
# Playwright Grid Documentation

Explore the grid architecture, hub/worker behavior, and testing strategy.

- [Borrow TTL & Session Persistence](Borrow-TTL-and-Session-Persistence.md)
- [Node Liveness and Sweeper](Node-Liveness-and-Sweeper.md)
- [Tasks](tasks.md)
```

docs/.assets/styles.css (minimal overrides)
```css
:root {
  --pg-accent: #5b9cff;
}

.md-typeset a { text-decoration: none; }
.md-typeset a:hover { text-decoration: underline; }

/* Refine code block background and rounding */
.md-typeset pre > code {
  border-radius: 8px;
}

/* Optional: accent color for blockquotes */
.md-typeset blockquote {
  border-left: 0.25rem solid var(--pg-accent);
}
```

.github/workflows/docs.yml
```yml
name: Deploy Docs (MkDocs)

on:
  push:
    branches: [ main ]
    paths:
      - 'docs/**'
      - 'mkdocs.yml'
  workflow_dispatch: {}

permissions:
  contents: write

jobs:
  build-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.x'

      - name: Install MkDocs + Material
        run: |
          python -m pip install --upgrade pip
          pip install mkdocs-material

      - name: Build site
        run: mkdocs build --strict

      - name: Deploy to GitHub Pages
        uses: peaceiris/actions-gh-pages@v3
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: ./site
          publish_branch: gh-pages
```

Checklist (for this section):
- [ ] mkdocs.yml added and committed
- [ ] docs/index.md created/updated
- [ ] docs/.assets/styles.css added (or verified)
- [ ] docs site builds locally (mkdocs build / serve)
- [ ] GH Actions workflow merged to main
- [ ] GitHub Pages configured to gh-pages
- [ ] First deployment successful; site visually verified
