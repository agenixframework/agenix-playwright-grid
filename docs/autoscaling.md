# Autoscaling hints (HPA) for Workers

This guide provides metrics-driven autoscaling hints for Worker pods using CPU and borrow queue length. It assumes:
- Prometheus scraping is enabled (the Worker exposes /metrics via prometheus-net).
- Hub exposes the borrow queue length metric `hub_borrow_queue_length{label="..."}` per label.
- A Prometheus Adapter (custom-metrics API) or KEDA is installed to let HPA read Prometheus metrics.

## Recommended signals
- CPU utilization (resource metric): target 70% averageUtilization on container/pod CPU.
- Borrow queue length (external/pods metric via Prometheus): scale out when queue length per label exceeds available capacity for sustained periods.

Queue metric to use:
- Hub: `hub_borrow_queue_length{label="<LabelKey>"}` – integer count per label.

If you prefer worker-local signals only, you can rely on CPU and pool utilization (`worker_pool_available`, `worker_pool_capacity`), but queue length is the best early-indicator of demand.

## Option A: HPA v2 with Prometheus Adapter (custom.metrics.k8s.io)

Example: scale workers when CPU > 70% OR borrow queue length (any label) > 0 for 2 minutes.

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: worker-hpa
  namespace: playwright-grid
  annotations:
    # Hints for operators: using Prometheus Adapter; adjust queries below as needed.
    autoscaling.alpha.kubernetes.io/behavior: |
      scaleUp:
        stabilizationWindowSeconds: 60
      scaleDown:
        stabilizationWindowSeconds: 300
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: worker
  minReplicas: 1
  maxReplicas: 10
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70
    - type: External
      external:
        metric:
          name: hub_borrow_queue_length
          selector:
            matchLabels:
              label: "AppB:Chromium:UAT" # choose your hot label or use multiple HPAs per label
        target:
          type: AverageValue
          averageValue: 1 # scale when avg queue > 1 across pods
```

Notes:
- With Prometheus Adapter, you typically configure a rules mapping to expose `hub_borrow_queue_length` as an External metric. Example rule:
  - seriesQuery: 'hub_borrow_queue_length{namespace="playwright-grid"}'
  - resources: { overrides: { namespace: { resource: "namespace" } } }
  - name: { as: "hub_borrow_queue_length" }
  - metricsQuery: 'sum(hub_borrow_queue_length{<<.LabelMatchers>>}) by (label)'
- Consider separate HPAs per label group if you route labels to distinct Deployments (recommended for isolation).

## Option B: KEDA ScaledObject (Prometheus trigger)

If using KEDA, a Prometheus trigger can scale based on the queue length query directly.

```yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: worker-scaledobject
  namespace: playwright-grid
  annotations:
    keda.sh/behavior: |
      scaleUp:
        stabilizationWindow: 60s
      scaleDown:
        stabilizationWindow: 300s
spec:
  scaleTargetRef:
    name: worker
  minReplicaCount: 1
  maxReplicaCount: 10
  cooldownPeriod: 120
  triggers:
    - type: prometheus
      metadata:
        serverAddress: http://prometheus-server.playwright-grid.svc:9090
        metricName: hub_borrow_queue_length
        threshold: "1"
        query: sum(hub_borrow_queue_length{label="AppB:Chromium:UAT"})
    - type: cpu
      metadata:
        type: Utilization
        value: "70"
```

## Deployment annotations (hints)

Even without changing manifests, you can annotate the Worker Deployment with hints for operators and tooling:

```yaml
metadata:
  annotations:
    grid.autoscaling/enabled: "true"
    grid.autoscaling/queueMetric: "hub_borrow_queue_length"
    grid.autoscaling/cpuTargetUtilization: "70"
    grid.autoscaling/exampleLabel: "AppB:Chromium:UAT"
```

These annotations are non-functional by themselves but provide consistent conventions for platform teams.

## CPU requests/limits baseline

Set sensible CPU requests/limits so HPA based on CPU behaves predictably:
- requests:
  cpu: 200m
- limits:
  cpu: 1000m

Tune per environment. Ensure Prometheus scrapes Hub and Worker metrics endpoints.

## Troubleshooting
- No scaling on queue: verify Prometheus Adapter/KEDA rules; run `kubectl get --raw "/apis/custom.metrics.k8s.io/v1beta1" | jq` to see exposed metrics.
- Selectors: ensure your chosen `label` matches the label keys used by your runs (Browser second segment convention).
- Avoid thrash: use stabilization windows and a small cooldown.

## References
- Metrics exposed
  - Hub: hub_borrow_queue_length, hub_borrow_latency_seconds, hub_borrow_outcomes_total, hub_pool_available_total, hub_pool_utilization_ratio
  - Worker: worker_pool_capacity, worker_pool_available, worker_borrows_total, worker_playwright_version_mismatch
- See docker-compose.yml and docs for ports and scraping.
