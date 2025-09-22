# Playwright Grid – Prometheus Metrics and Grafana Guide

This document explains how metrics are exposed by the Playwright Grid (Hub and Workers), how to view them in Prometheus and Grafana, and how to build actionable dashboards and alerts. It complements the quick references in README.md and the built‑in Grafana provisioning shipped with this repository.

Contents
- Overview and prerequisites
- Where metrics are exposed
- Metrics catalog (Hub and Worker)
- Useful PromQL queries
- Grafana: provisioned dashboard and customization
- Alerting examples
- Troubleshooting
- Environment variables and configuration knobs

Overview and prerequisites
- Components: Hub (ASP.NET Core Minimal API), Workers (Worker Service), optional Dashboard, Redis, Prometheus, Grafana.
- Prometheus scrapes metrics endpoints exposed by Hub and Worker using prometheus-net. Grafana connects to Prometheus and shows pre-provisioned dashboards.
- Prerequisites: Docker/Docker Compose and .NET 8 SDK (for building). Start the stack with: docker compose up --build

Where metrics are exposed
- Hub metrics endpoint: http://127.0.0.1:5100/metrics
- Worker metrics endpoints (per worker): http://127.0.0.1:5200/metrics, http://127.0.0.1:5201/metrics, ... (per docker compose)
- Prometheus UI: http://127.0.0.1:9090
- Grafana UI: http://127.0.0.1:3000 (default: admin/admin)
- Scrape config: prometheus.yml in repo root; the docker compose wiring points Prometheus to the Hub and Worker targets automatically.

Metrics catalog
The following metrics are emitted by the Hub and the Workers. Label conventions align with the label key schema: App:Browser:Env[:Region[:OS…]].

Hub metrics
- hub_borrow_requests_total{label}
  - Type: Counter
  - Description: Total borrow requests received by Hub for the requested label key.
- hub_borrow_latency_seconds_bucket{label,le} / _sum / _count
  - Type: Histogram
  - Description: Borrow latency observed at the Hub (time to locate and hand out a ready session). Use histogram_quantile on buckets.
- hub_borrow_outcomes_total{label,outcome}
  - Type: Counter
  - Description: Borrow outcomes by requested label and outcome. outcome ∈ {success, timeout, denied}.
- hub_pool_available_total{label}
  - Type: Gauge
  - Description: Available prewarmed sessions in the pool for the matched label (updated on borrow/return).
- hub_pool_utilization_ratio{label}
  - Type: Gauge
  - Description: Borrowed/(available+inuse) per matched label at borrow time. Values in [0..1].
- hub_borrow_queue_length{label}
  - Type: Gauge
  - Description: Current queue length for pending borrows per label. Presently 0 until explicit queueing is enabled.

Worker metrics
- worker_pool_capacity{node,label}
  - Type: Gauge
  - Description: Total capacity slots advertised by a worker for a label (from POOL_CONFIG).
- worker_pool_available{node,label}
  - Type: Gauge
  - Description: Currently available (not borrowed) slots on the worker for a label.
- worker_borrows_total{node,label}
  - Type: Counter
  - Description: Number of borrows served by a worker for a label.
- worker_disk_bytes_total{node}, worker_disk_bytes_free{node}, worker_disk_bytes_used{node}
  - Type: Gauge
  - Description: Total/free/used disk bytes on the worker's target filesystem. Used is computed as total-free. Also see worker_disk_usage_ratio.
- worker_disk_usage_ratio{node}
  - Type: Gauge
  - Description: Disk usage ratio in [0..1] for the worker's target filesystem.
- worker_inodes_total{node}, worker_inodes_free{node}, worker_inodes_used{node} (Linux only)
  - Type: Gauge
  - Description: Total/free/used inodes measured via statvfs on Linux. Also see worker_inodes_usage_ratio.
- worker_inodes_usage_ratio{node} (Linux only)
  - Type: Gauge
  - Description: Inode usage ratio in [0..1] on Linux systems.
- worker_cleanup_deleted_files_total{node,reason}, worker_cleanup_deleted_bytes_total{node,reason}
  - Type: Counter
  - Description: Files and bytes deleted by the worker's pressure-driven cleanup sweeps (reason typically "pressure").

Notes
- Some metrics (e.g., hub_pool_utilization_ratio) are sampled/updated during borrow operations; values may remain static between borrows.
- Liveness of workers is primarily tracked in Redis (node_alive:* TTL keys and LastSeen); a dedicated Prometheus metric may be added later.

Useful PromQL queries
Latency quantiles (per label)
- P50: histogram_quantile(0.5, sum(rate(hub_borrow_latency_seconds_bucket[5m])) by (le, label))
- P90: histogram_quantile(0.9, sum(rate(hub_borrow_latency_seconds_bucket[5m])) by (le, label))
- P99: histogram_quantile(0.99, sum(rate(hub_borrow_latency_seconds_bucket[5m])) by (le, label))

Borrow outcomes and error budget
- Per label+outcome rate: sum by (label, outcome) (rate(hub_borrow_outcomes_total[5m]))
- Error rate (non-success): sum by (label) (rate(hub_borrow_outcomes_total{outcome!="success"}[5m]))
- Success ratio: sum by (label) (rate(hub_borrow_outcomes_total{outcome="success"}[5m])) / ignoring(outcome) group_left sum by (label) (rate(hub_borrow_outcomes_total[5m]))

Pool health
- Available per label: hub_pool_available_total
- Utilization per label: hub_pool_utilization_ratio
- Queue length (if/when enabled): hub_borrow_queue_length

Worker capacity and distribution
- Capacity per label across nodes: sum by (label) (worker_pool_capacity)
- Available per label across nodes: sum by (label) (worker_pool_available)
- Per-node distribution for a label: worker_pool_available{label="AppB:Chromium:UAT"}

Grafana: provisioned dashboard and customization
- This repo provisions Prometheus datasource at provisioning/datasources/prometheus.yaml and two dashboards under provisioning/dashboards/:
  - playwright-grid-metrics.json → "Playwright Grid – Hub Metrics"
  - playwright-grid-worker-metrics.json → "Playwright Grid – Worker Metrics"
- After docker compose up, open Grafana at http://127.0.0.1:3000 and navigate to the "Playwright Grid" folder to find both dashboards.
- Hub Metrics panels include:
  - Borrow latency quantiles by label (P50/P90/P99)
  - Borrow outcomes (stacked) by label and outcome
  - Pool utilization ratio by label
  - Borrow queue length
  - Pool available count by label
  - Notes on node heartbeats (tracked in Redis)
- Worker Metrics panels include:
  - Disk usage ratio and bytes (used/total/free) by node
  - Inode usage ratio and counts (used/total/free) by node (Linux)
  - Templated by node to focus on specific workers
- Customization:
  - You can duplicate the dashboards and edit queries/thresholds.
  - Import additional dashboards via Grafana UI or place JSON under provisioning/dashboards.

Alerting examples
Define alert rules in Prometheus or via Grafana Alerting.
- High borrow latency (P90 > 5s for 10m):
  - expr: histogram_quantile(0.9, sum(rate(hub_borrow_latency_seconds_bucket[5m])) by (le, label)) > 5
  - for: 10m
  - labels: severity: warning
- Elevated denial/timeout rate (>5% over 10m):
  - expr: (sum by (label) (rate(hub_borrow_outcomes_total{outcome!="success"}[10m])) / ignoring(outcome) group_left sum by (label) (rate(hub_borrow_outcomes_total[10m]))) > 0.05
  - for: 10m
  - labels: severity: warning
- Utilization saturation (>= 90% utilization for 15m):
  - expr: hub_pool_utilization_ratio >= 0.9
  - for: 15m
  - labels: severity: warning
- Worker disk usage high (>= 90% for 5m):
  - expr: worker_disk_usage_ratio >= 0.9
  - for: 5m
  - labels: severity: warning
- Worker inode usage high (>= 90% for 5m, Linux):
  - expr: worker_inodes_usage_ratio >= 0.9
  - for: 5m
  - labels: severity: warning

Note: Workers also emit log warnings/errors when disk/inode thresholds are breached. Tune via env: DISK_USAGE_HIGH_PCT, DISK_USAGE_CRITICAL_PCT, INODE_USAGE_HIGH_PCT, INODE_USAGE_CRITICAL_PCT.

Troubleshooting
- Prometheus shows no targets:
  - Ensure docker compose is up and containers healthy. Prometheus should scrape hub:5000/metrics and worker:5000/metrics inside the network.
  - If running locally without compose, point Prometheus to the correct host/ports.
- Grafana dashboard empty:
  - Check Prometheus datasource is healthy (Grafana → Connections → Data sources → Prometheus).
  - Verify that borrow activity occurred; many Hub metrics update on borrow/return events.
- Mismatched labels or exploding cardinality:
  - Keep label schema consistent (App:Browser:Env[:...]) and avoid unbounded label values.
- Latency panels show gaps:
  - Histogram requires bucket samples; ensure sustained traffic or expand the query window.
- Worker metrics missing:
  - Verify worker process exposes /metrics (it does by default). Confirm the worker container port mapping in docker compose.

Environment variables and configuration
- Hub
  - HUB_BORROW_TRAILING_FALLBACK=true | HUB_BORROW_PREFIX_EXPAND=true | HUB_BORROW_WILDCARDS=false
  - HUB_NODE_TIMEOUT=60 (seconds)
  - ENABLE_OTLP=1 to enable OpenTelemetry OTLP exporter for traces/metrics (in addition to Prometheus scrape).
  - ENABLE_PROMETHEUS_OTEL=1 optional flag to expose OTEL metrics in Prometheus format via OpenTelemetry; primary exporter used for Hub/Worker custom metrics is prometheus-net’s /metrics.
- Worker
  - PUBLIC_WS_HOST/PORT/SCHEME influence endpoint URLs in logs but do not affect metrics.
  - PLAYWRIGHT_VERSION is surfaced in logs/summary; not a metric by itself.

See also
- README.md for ports and quickstart
- docs/Label-Matching.md for how labels are matched and why label normalization matters for metrics
- provisioning/dashboards/playwright-grid-metrics.json for the current dashboard definition


## RunName and cardinality (Non-goals)
- RunName is intentionally not used as a Prometheus metric label and is not embedded into any Redis key.
- Rationale:
  - Avoid high-cardinality labels: free-form names can explode time series cardinality and harm Prometheus scrape/storage performance and dashboard responsiveness.
  - Preserve compatibility and stability: our Grafana dashboards and Redis keyspace rely on stable label sets and key patterns (labels, node, outcome). RunName is stored as data (e.g., a value associated with runId) and surfaced in logs, SignalR events, and the Dashboard UI.
- Practical implications:
  - Filter/search by RunName via the Dashboard and APIs instead of metrics.
  - Use metrics for fleet-level signals (by label and node); use logs/traces/results for per-run analysis.
- Security/PII: RunName may contain descriptive text. If your policy requires it, enable redaction to suppress RunName in logs.
