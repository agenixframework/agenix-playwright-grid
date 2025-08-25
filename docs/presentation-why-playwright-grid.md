# Why Agenix Playwright Grid

Subtitle: When to choose a self-hosted Playwright Grid over direct CI runs or BrowserStack

Date: 2025-08-25
Audience: QA/Dev Leads, SRE, Engineering Managers

---

## TL;DR
- Use Grid when you need consistent, scalable, cost‑efficient, and observable Playwright execution across many repos/teams.
- Compared to running Playwright directly in CI:
  - Centralizes capacity and reduces flakiness by pre‑warming browsers and reusing pools.
  - Gives you labels (App:Browser:Env[:Region[:OS…]]) to route the right capacity automatically.
  - Adds first‑class observability (Prometheus/Grafana) and a live Dashboard with protocol/API logs.
- Compared to BrowserStack/SaaS clouds:
  - Lowers cost at sustained scale, improves data control/compliance, and removes vendor queueing.
  - Provides LAN‑grade latency to your services and unlimited customization of browser flags/versions.

---

## The problem with “Playwright directly in CI”
- Cold starts and environment drift
  - Installing browsers/OS deps on every job wastes minutes and increases flake risk.
  - Multiple pipelines pinning different Playwright versions → inconsistent results across repos.
- Fragmented capacity and under‑utilization
  - Each project over‑provisions CI runners to hit SLAs; no global pooling.
  - Peaks in one repo/star branch cause queuing while other runners idle.
- Limited observability and forensics
  - Logs and traces distributed across countless jobs; no cross‑suite view.
  - Hard to correlate protocol‑level events, API logs, screenshots/videos, and test metadata.
- Network and security
  - Egress patterns vary per job; hard to apply consistent network policies or IP allowlists.
  - Secrets handling is duplicated across repos and pipelines.

---

## How the Grid solves this
- Pre‑warmed capacity
  - Workers keep Playwright sidecars hot; Hub borrows/returns sessions in milliseconds.
- Label‑driven routing
  - Use ordered keys `App:Browser:Env[:Region[:OS…]]` to match exactly, fallback by trailing segments, or expand prefixes.
  - Guarantees the right browser/channel/OS for each app/env without bespoke pipeline logic.
- Centralized observability
  - Built‑in Prometheus metrics and Grafana dashboards.
  - Dashboard shows runs with mirrored protocol commands and runner‑forwarded API logs.
- Simple, secure APIs
  - Minimal endpoints for borrow/return; secrets for runners and nodes; Redis‑backed state.
- Elastic scaling
  - Add/remove workers per pool without touching test repos or CI definitions.

---

## Why not just use BrowserStack (or similar)?
Pros of SaaS clouds
- Zero infra to start; broad browser/device catalog; shared responsibility for upkeep.

Cons at scale
- Cost and concurrency
  - Minute‑based billing and plan caps lead to queues or expensive upgrades under sustained load.
- Data, compliance, and performance
  - Test data and traffic traverse vendor infra; IP allowlists and data residency can be limiting.
  - Cross‑DC latency to your services can slow tests and increase flakiness.
- Limited customization
  - Browser flags, experimental protocols, pinned Playwright versions, and OS tuning are constrained by the vendor’s images.
- Lock‑in
  - Switching vendors or bringing execution in‑house later can be costly.

Grid advantages
- Predictable cost curve (own the hardware or autoscale in your cloud).
- LAN‑close to your services → lower latency and more stable tests.
- Full control over images, flags, and Playwright versions; consistent across teams.
- First‑class integration with your observability stack.

---

## Cost and capacity: simple model
- Direct CI
  - Install browsers per run: 1–3 min overhead × N jobs/day.
  - Parallelism limited by per‑repo runner quotas; idle capacity elsewhere can’t help.
- BrowserStack
  - Concurrency is plan‑bound; bursts cause queues.
  - Per‑minute pricing scales linearly with test time.
- Grid (self‑hosted)
  - Pre‑warm overhead amortized; near‑zero setup time per borrow.
  - Global pool across all repos; unused capacity is reused instantly.
  - Scale horizontally: add workers labeled to the hot pools.

Tip: Start with a small node pool (e.g., 2–3 workers per hot label), measure utilization in Grafana, grow only where needed.

---

## Security and compliance
- Stable egress via workers; put them in the same VPC/VNet as your services or behind a controlled NAT.
- Centralized secrets (HUB_RUNNER_SECRET, HUB_NODE_SECRET) vs spreading secrets across many CI pipelines.
- Data locality: you choose the region/cloud; keep traffic inside your boundaries.

---

## Developer experience
- Consistent local vs CI behavior: the same borrow/return APIs work locally and in pipelines.
- Faster feedback via hot pools and fewer flakes from cold installs.
- Dashboard helps triage quickly with protocol/API logs, screenshots, and test attribution.

---

## When to prefer alternatives
- Direct CI may be enough if:
  - You have a small suite, few contributors, and no strong parallelism/latency needs.
  - Infrequent runs and no desire to maintain any infra.
- BrowserStack/SaaS may be better if:
  - You need wide real‑device coverage or legacy OS/browser variants the grid doesn’t target.
  - You want a purely managed service with no ops surface area.

---

## Adoption path (low‑risk)
1) Compose the stack locally: `docker compose up --build`.
2) Point a subset of suites to the Grid using Agenix.PlaywrightGrid.HubClient and labels.
3) Compare runtime and flake rates vs baseline.
4) Incrementally move suites; add workers only for hot labels.
5) Wire Prometheus/Grafana dashboards into your observability and set SLOs.

---

## FAQ
- How do we keep Playwright versions consistent?
  - Pin via Dockerfiles and PLAYWRIGHT_VERSION env; workers report the version to Dashboard.
- Can we isolate teams or apps?
  - Yes, by label namespaces (e.g., AppA:Chromium:Staging) and per‑pool worker assignments.
- What about spikes during release time?
  - Temporarily add workers or adjust pool counts; no changes in test repos.
- Do we lose any Playwright features?
  - No. Tests connect to the worker‑proxied WebSocket exposed to the client. You can still use channels/flags.

---

## Appendix: Feature matrix snapshot
- Routing
  - Exact, trailing fallback, prefix expansion, optional wildcards.
- Observability
  - Prometheus metrics; Grafana dashboards; protocol/API log mirroring.
- Security
  - Shared‑secret auth; stable egress; optional per‑pool isolation.
- Operations
  - Redis‑backed Hub state; horizontal scaling; Docker‑first.

Links
- Root README: ../README.md
- Test client usage: ./TestClient-Usage.md
- Playwright .NET API logging: ./PlaywrightDotNet-pw-api.md
