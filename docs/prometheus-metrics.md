# Prometheus Metrics Documentation

## Overview

This document describes all Prometheus metrics exposed by the Agenix Playwright Grid worker service for monitoring browser pool health, performance, and reliability.

## Metrics Endpoint

**URL**: `http://<worker-host>:<worker-port>/metrics`

Example: `http://localhost:5100/metrics`

---

## Browser Pool Metrics

### worker_pool_capacity

**Type**: Gauge

**Description**: Total capacity of worker browser pool (number of browser slots configured)

**Labels**:
- `node`: Worker node identifier (e.g., "worker1", "worker2")
- `label`: Browser pool label key (e.g., "myapp:chromium:staging")

**Example Query**:
```promql
# Current pool capacity by node and label
worker_pool_capacity{node="worker1", label="myapp:chromium:staging"}

# Total capacity across all nodes
sum(worker_pool_capacity) by (label)
```

**Use Cases**:
- Capacity planning (ensure sufficient browser slots)
- Identify pool configuration issues
- Track pool size changes over time

---

### worker_pool_available

**Type**: Gauge

**Description**: Number of available (not borrowed) browser slots in the pool

**Labels**:
- `node`: Worker node identifier
- `label`: Browser pool label key

**Example Query**:
```promql
# Available browsers by node
worker_pool_available{node="worker1"}

# Pool utilization percentage
(1 - (worker_pool_available / worker_pool_capacity)) * 100

# Alert when availability drops below 20%
(worker_pool_available / worker_pool_capacity) < 0.2
```

**Use Cases**:
- Real-time utilization monitoring
- Capacity exhaustion alerts
- Autoscaling triggers

---

### worker_borrows_total

**Type**: Counter

**Description**: Total number of browser borrows (test executions started)

**Labels**:
- `node`: Worker node identifier
- `label`: Browser pool label key

**Example Query**:
```promql
# Borrow rate (borrows per second)
rate(worker_borrows_total[5m])

# Total borrows in last hour
increase(worker_borrows_total[1h])

# Borrows by label (identify hotspots)
sum(rate(worker_borrows_total[5m])) by (label)
```

**Use Cases**:
- Track test execution volume
- Identify peak usage times
- Billing/cost allocation per label

---

## Browser Health Check Metrics

### worker_browser_health_check_total

**Type**: Counter

**Description**: Number of browser health checks performed (via CDP protocol)

**Labels**:
- `node`: Worker node identifier
- `label`: Browser pool label key
- `browser`: Browser type (chromium, firefox, webkit)
- `result`: Health check result (success, failure)

**Example Query**:
```promql
# Health check success rate
rate(worker_browser_health_check_total{result="success"}[5m])
  / rate(worker_browser_health_check_total[5m])

# Failed health checks by browser type
sum(rate(worker_browser_health_check_total{result="failure"}[5m])) by (browser)

# Alert on high failure rate (>10%)
(
  rate(worker_browser_health_check_total{result="failure"}[5m])
  / rate(worker_browser_health_check_total[5m])
) > 0.1
```

**Use Cases**:
- Monitor browser stability
- Detect browser-specific issues (e.g., Chromium crashes more than Firefox)
- Health check effectiveness tracking

---

### worker_browser_health_check_duration_seconds

**Type**: Histogram

**Description**: Duration of browser health checks in seconds (WebSocket CDP call latency)

**Labels**:
- `node`: Worker node identifier

**Buckets**: 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 seconds

**Example Query**:
```promql
# 95th percentile health check latency
histogram_quantile(0.95,
  rate(worker_browser_health_check_duration_seconds_bucket[5m])
)

# Slow health checks (>5s)
rate(worker_browser_health_check_duration_seconds_bucket{le="10.0"}[5m])
  - rate(worker_browser_health_check_duration_seconds_bucket{le="5.0"}[5m])

# Alert on slow health checks
histogram_quantile(0.95,
  rate(worker_browser_health_check_duration_seconds_bucket[5m])
) > 5
```

**Use Cases**:
- Detect network latency issues
- Identify unhealthy browsers (slow to respond)
- Performance regression detection

---

### worker_browser_recycle_latency_seconds

**Type**: Histogram

**Description**: Latency from when BrowserHealthChecker sets recycle flag to when ReconcileLoop actually recycles the browser

**Labels**:
- `node`: Worker node identifier
- `label`: Browser pool label key

**Buckets**: 1.0, 2.5, 5.0, 10.0, 15.0, 30.0, 60.0, 120.0 seconds

**Example Query**:
```promql
# 95th percentile recycle latency
histogram_quantile(0.95,
  rate(worker_browser_recycle_latency_seconds_bucket[5m])
)

# Average recycle latency by label
histogram_quantile(0.5,
  rate(worker_browser_recycle_latency_seconds_bucket[5m])
) by (label)

# Alert on slow recycles (>30s at p95)
histogram_quantile(0.95,
  rate(worker_browser_recycle_latency_seconds_bucket[5m])
) > 30
```

**Use Cases**:
- Measure ReconcileLoop responsiveness
- Validate Option C optimizations (should be ~2.5s average)
- Identify ReconcileLoop polling issues

---

## Worker Re-Registration Metrics

### worker_re_registrations_total

**Type**: Counter

**Description**: Number of successful worker re-registrations (recovery after hub restart, Redis expiration, or system sleep)

**Labels**:
- `node`: Worker node identifier
- `trigger`: Re-registration trigger type (`gap_detection`, `periodic_verification`)

**Example Query**:
```promql
# Re-registration rate by trigger
rate(worker_re_registrations_total[1h]) by (trigger)

# Gap detection effectiveness
rate(worker_re_registrations_total{trigger="gap_detection"}[1h])
  / rate(worker_re_registrations_total[1h])

# Alert on frequent re-registrations (>5 per hour)
increase(worker_re_registrations_total[1h]) > 5
```

**Use Cases**:
- Monitor worker-hub connectivity
- Validate gap detection mechanism
- Detect infrastructure instability (frequent re-registrations)

---

### worker_re_registration_errors_total

**Type**: Counter

**Description**: Number of failed worker re-registration attempts

**Labels**:
- `node`: Worker node identifier
- `trigger`: Re-registration trigger type (`gap_detection`, `periodic_verification`)

**Example Query**:
```promql
# Re-registration error rate
rate(worker_re_registration_errors_total[5m])

# Error ratio (errors / total attempts)
rate(worker_re_registration_errors_total[5m])
  / (rate(worker_re_registrations_total[5m]) + rate(worker_re_registration_errors_total[5m]))

# Alert on re-registration failures
rate(worker_re_registration_errors_total[5m]) > 0
```

**Use Cases**:
- Detect hub unavailability
- Monitor network connectivity issues
- Track re-registration reliability

---

## Playwright Version Mismatch

### worker_playwright_version_mismatch

**Type**: Gauge

**Description**: Indicates Playwright version mismatch between worker and sidecar (1 = mismatch, 0 = match)

**Labels**:
- `node`: Worker node identifier
- `expected`: Expected Playwright version (from worker config)
- `actual`: Actual Playwright version (from sidecar package.json)

**Example Query**:
```promql
# Nodes with version mismatch
worker_playwright_version_mismatch == 1

# Alert on version mismatch
worker_playwright_version_mismatch > 0
```

**Use Cases**:
- Detect Playwright version drift
- Validate deployment updates
- Prevent test failures due to version incompatibility

---

## Grafana Dashboard Examples

### Dashboard 1: Browser Pool Overview

**Panels**:

1. **Pool Capacity & Availability (Gauge)**
   ```promql
   worker_pool_capacity{node="worker1"}
   worker_pool_available{node="worker1"}
   ```

2. **Pool Utilization (Graph)**
   ```promql
   (1 - (worker_pool_available / worker_pool_capacity)) * 100
   ```

3. **Borrow Rate (Graph)**
   ```promql
   rate(worker_borrows_total[5m])
   ```

4. **Borrows by Label (Pie Chart)**
   ```promql
   sum(increase(worker_borrows_total[1h])) by (label)
   ```

---

### Dashboard 2: Browser Health Monitoring

**Panels**:

1. **Health Check Success Rate (Gauge)**
   ```promql
   (
     rate(worker_browser_health_check_total{result="success"}[5m])
     / rate(worker_browser_health_check_total[5m])
   ) * 100
   ```

2. **Failed Health Checks (Graph)**
   ```promql
   sum(rate(worker_browser_health_check_total{result="failure"}[5m])) by (browser)
   ```

3. **Health Check Latency Percentiles (Graph)**
   ```promql
   histogram_quantile(0.50, rate(worker_browser_health_check_duration_seconds_bucket[5m]))
   histogram_quantile(0.95, rate(worker_browser_health_check_duration_seconds_bucket[5m]))
   histogram_quantile(0.99, rate(worker_browser_health_check_duration_seconds_bucket[5m]))
   ```

4. **Recycle Latency Heatmap (Heatmap)**
   ```promql
   rate(worker_browser_recycle_latency_seconds_bucket[5m])
   ```

---

### Dashboard 3: Worker Reliability

**Panels**:

1. **Re-Registration Events (Graph)**
   ```promql
   rate(worker_re_registrations_total[1h]) by (trigger)
   ```

2. **Re-Registration Errors (Alert Panel)**
   ```promql
   rate(worker_re_registration_errors_total[5m])
   ```

3. **Gap Detection Effectiveness (Gauge)**
   ```promql
   (
     rate(worker_re_registrations_total{trigger="gap_detection"}[1h])
     / rate(worker_re_registrations_total[1h])
   ) * 100
   ```

4. **Version Mismatch Status (Table)**
   ```promql
   worker_playwright_version_mismatch{node=~".+"}
   ```

---

## Alerting Rules

### Critical Alerts

```yaml
groups:
  - name: worker_critical
    interval: 30s
    rules:
      # Pool exhaustion (no available browsers)
      - alert: BrowserPoolExhausted
        expr: worker_pool_available == 0
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "Browser pool {{ $labels.label }} on {{ $labels.node }} has no available browsers"
          description: "Pool has been exhausted for 2 minutes. Tests are likely queued or failing."

      # High browser health check failure rate
      - alert: HighBrowserHealthCheckFailureRate
        expr: |
          (
            rate(worker_browser_health_check_total{result="failure"}[5m])
            / rate(worker_browser_health_check_total[5m])
          ) > 0.2
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "High browser health check failure rate on {{ $labels.node }}"
          description: "{{ $value | humanizePercentage }} of health checks are failing."

      # Slow browser recycles (p95 > 30s)
      - alert: SlowBrowserRecycle
        expr: |
          histogram_quantile(0.95,
            rate(worker_browser_recycle_latency_seconds_bucket[5m])
          ) > 30
        for: 10m
        labels:
          severity: critical
        annotations:
          summary: "Slow browser recycles on {{ $labels.node }}"
          description: "95th percentile recycle latency is {{ $value }}s (threshold: 30s)"
```

### Warning Alerts

```yaml
groups:
  - name: worker_warnings
    interval: 1m
    rules:
      # Low pool availability (<20%)
      - alert: LowBrowserPoolAvailability
        expr: (worker_pool_available / worker_pool_capacity) < 0.2
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Low browser pool availability on {{ $labels.node }}"
          description: "Pool {{ $labels.label }} has only {{ $value | humanizePercentage }} availability"

      # Frequent re-registrations (>5 per hour)
      - alert: FrequentWorkerReRegistration
        expr: increase(worker_re_registrations_total[1h]) > 5
        labels:
          severity: warning
        annotations:
          summary: "Frequent worker re-registrations on {{ $labels.node }}"
          description: "Worker has re-registered {{ $value }} times in the last hour"

      # Re-registration failures
      - alert: WorkerReRegistrationFailure
        expr: rate(worker_re_registration_errors_total[5m]) > 0
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Worker re-registration failures on {{ $labels.node }}"
          description: "Worker is failing to re-register with hub"

      # Playwright version mismatch
      - alert: PlaywrightVersionMismatch
        expr: worker_playwright_version_mismatch > 0
        labels:
          severity: warning
        annotations:
          summary: "Playwright version mismatch on {{ $labels.node }}"
          description: "Expected {{ $labels.expected }}, got {{ $labels.actual }}"
```

---

## Metric Collection Configuration

### Prometheus Scrape Config

```yaml
scrape_configs:
  - job_name: 'playwright-grid-workers'
    static_configs:
      - targets:
          - 'worker1:5100'
          - 'worker2:5100'
          - 'worker3:5100'
    scrape_interval: 15s
    scrape_timeout: 10s
    metrics_path: '/metrics'
```

### Environment Variables

**ReconcileLoop Interval** (affects recycle latency):
```bash
AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS=5  # Default: 5 seconds (range: 1-60)
```

**Browser Health Check** (disabled by default):
```bash
AGENIX_WORKER_HEALTH_CHECK_ENABLED=true
AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS=30      # Default: 30 seconds
AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS=5        # Default: 5 seconds
AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD=3      # Default: 3 consecutive failures
```

---

## Metric Relationships

### Browser Recycle Flow

```
BrowserHealthChecker detects unhealthy browser
  ↓
worker_browser_health_check_total{result="failure"} increments
  ↓
Sets recycle flag in Redis (timestamp recorded)
  ↓
ReconcileLoop polls every N seconds (AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS)
  ↓
Detects recycle flag, replaces browser
  ↓
worker_browser_recycle_latency_seconds records time delta
  ↓
ReconcileLoop deletes recycle flag
```

**Expected Latencies (Option C Optimizations)**:
- **Average**: ~2.5s (half of ReconcileLoop interval)
- **p95**: ~5s (max one ReconcileLoop interval)
- **p99**: ~10s (if multiple browsers recycling simultaneously)

---

## Troubleshooting Guide

### High Recycle Latency (>30s)

**Symptoms**: `worker_browser_recycle_latency_seconds` p95 > 30s

**Possible Causes**:
1. ReconcileLoop interval too high (increase `AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS`)
2. ReconcileLoop blocked by long-running operations
3. High browser pool contention (many simultaneous recycles)

**Investigation**:
```promql
# Check ReconcileLoop polling frequency
rate(worker_browser_recycle_latency_seconds_count[5m])

# Check concurrent browser operations
sum(worker_pool_capacity - worker_pool_available) by (node)
```

---

### High Health Check Failure Rate

**Symptoms**: `worker_browser_health_check_total{result="failure"}` rate increasing

**Possible Causes**:
1. Browser crashes (CDP WebSocket disconnect)
2. Network issues between worker and browser process
3. Browser timeout (slow to respond to CDP commands)

**Investigation**:
```promql
# Failures by browser type
sum(rate(worker_browser_health_check_total{result="failure"}[5m])) by (browser)

# Health check latency
histogram_quantile(0.95, rate(worker_browser_health_check_duration_seconds_bucket[5m]))
```

---

### Pool Exhaustion

**Symptoms**: `worker_pool_available` == 0 for extended periods

**Possible Causes**:
1. Insufficient pool capacity (increase pool size)
2. Browsers not being returned (hung tests, timeout issues)
3. High test execution volume (autoscaling needed)

**Investigation**:
```promql
# Borrow rate vs capacity
rate(worker_borrows_total[5m]) / worker_pool_capacity

# Average borrow duration (if browsers are being returned)
avg(rate(worker_borrows_total[5m])) by (label)
```

---

## References

- **Prometheus Documentation**: https://prometheus.io/docs/
- **Grafana Dashboards**: https://grafana.com/docs/grafana/latest/dashboards/
- **PromQL Examples**: https://prometheus.io/docs/prometheus/latest/querying/examples/
- **Histogram Best Practices**: https://prometheus.io/docs/practices/histograms/
