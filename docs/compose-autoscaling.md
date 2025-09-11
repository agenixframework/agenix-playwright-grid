# Autoscaling with Docker Compose (practical patterns)

Docker Compose does not provide native autoscaling (no HPA equivalent). However, you can achieve practical, metrics-informed scaling during local/dev or simple single-host deployments using the following approaches.

These patterns work with this repo’s compose stack (hub, workers, redis, prometheus, grafana, dashboard).

## 1) Manual scaling via Compose

- Scale workers up/down interactively:
  - docker compose up -d --scale worker1=1 --scale worker2=1 --scale worker3=0
- If you use a single worker service name (recommended for scaling), refactor worker1/worker2/worker3 into one service `worker` and then:
  - docker compose up -d --scale worker=3

Notes:
- When using multiple distinct services (worker1, worker2, worker3), each has a unique NODE_ID and port mapping; scaling replicas per service is less convenient. If you want simple scaling, consider one `worker` service with NODE_ID derived from hostname or container index.

## 2) Profiles & overrides for preset sizes

Create an override file (docker-compose.override.autoscale.yml) with increased replicas for workers. With Compose v2 on Swarm you can use `deploy.replicas`; in plain Compose, replicas are controlled by `--scale`.

Example override (document-only; not required to commit):

```yaml
# docker-compose.override.autoscale.yml
services:
  worker1:
    # No deploy.replicas in classic Compose; use --scale
    # Keep environment identical, scale with CLI.
  worker2:
  worker3:
```

Usage:
- docker compose -f docker-compose.yml -f docker-compose.override.autoscale.yml up -d
- docker compose up -d --scale worker1=1 --scale worker2=2 --scale worker3=1

## 3) Metrics-informed scaling with a small helper script

You can run a simple loop that watches Prometheus for hub_borrow_queue_length and adjusts replicas using `docker compose up --scale`.

Example script (save as scripts/compose-scaler.sh):

```bash
#!/usr/bin/env bash
set -euo pipefail

PROM=${PROMETHEUS_URL:-http://127.0.0.1:9090}
SERVICE=${SERVICE_NAME:-worker2}
MIN=${MIN_REPLICAS:-1}
MAX=${MAX_REPLICAS:-5}
LABEL_MATCH=${LABEL_MATCH:-AppB:Chromium:UAT}
INTERVAL=${INTERVAL_SECONDS:-30}

# Query sums queue across labels matching exactly; change query as needed
QUERY="sum(hub_borrow_queue_length{label=\"$LABEL_MATCH\"})"

scale() {
  local n=$1
  n=$(( n < MIN ? MIN : n ))
  n=$(( n > MAX ? MAX : n ))
  echo "[scaler] Scaling $SERVICE to $n (queue=$2)"
  docker compose up -d --scale "$SERVICE=$n" >/dev/null
}

current=$MIN
scale "$current" 0

while true; do
  # Prometheus instant query
  val=$(curl -fsSL "$PROM/api/v1/query?query=$(python3 -c "import urllib.parse,sys;print(urllib.parse.quote(sys.argv[1]))" "$QUERY")" | jq -r '.data.result[0].value[1]' || echo 0)
  # Basic policy: queue 0-> keep; 1..3 -> +1; >3 -> +2
  q=${val%.*}
  desired=$current
  if (( q == 0 )); then
    desired=$(( current > MIN ? current - 1 : current ))
  elif (( q > 3 )); then
    desired=$(( current + 2 ))
  else
    desired=$(( current + 1 ))
  fi

  if (( desired != current )); then
    scale "$desired" "$q"
    current=$desired
  else
    echo "[scaler] No change (queue=$q, replicas=$current)"
  fi

  sleep "$INTERVAL"
done
```

Usage:
- Ensure Prometheus is up (default http://127.0.0.1:9090 per compose). Install curl, jq, python3 in your host.
- export SERVICE_NAME=worker2; export LABEL_MATCH="AppB:Chromium:UAT"; export MIN_REPLICAS=1; export MAX_REPLICAS=4
- bash scripts/compose-scaler.sh

Notes:
- This is a simple local tool; no TLS/auth or advanced backoff. Adjust query and policy for your needs.
- It acts on a single service name. If you consolidate workers to one service (`worker`) you’ll get better ergonomics.

## 4) Consolidate workers for easier scaling

Current compose defines worker1/worker2/worker3 with different ports and NODE_IDs. To leverage `--scale`, consider switching to a single `worker` service and using ephemeral published ports or reverse proxy for WS routing. For local dev, you can:
- Keep just worker2 as `worker` and scale it: docker compose up -d --scale worker=3
- Set PUBLIC_WS_HOST to your host and omit explicit host port mappings by using an ingress or a small TCP router if needed.

Example (conceptual) single service snippet:

```yaml
services:
  worker:
    build:
      context: .
      dockerfile: worker/Dockerfile
    environment:
      - HUB_URL=http://hub:5000
      - REDIS_URL=redis:6379
      - NODE_SECRET=node-secret
      - POOL_CONFIG=AppB:Chromium:UAT=3
      - PUBLIC_WS_HOST=127.0.0.1
      - PUBLIC_WS_PORT=5200
    # No fixed host port mapping; rely on docker network or a simple proxy
```

Then scale:
- docker compose up -d --scale worker=3

Be mindful that each replica needs a unique NODE_ID. If omitted, the worker can default NODE_ID from container hostname (recommended enhancement); otherwise, pass NODE_ID via environment using Compose’s index: not natively supported, but you can use docker-compose variable expansion with service index in Swarm; for plain Compose, leave NODE_ID empty and let the app generate a unique ID.

## 5) CPU-based hints

Compose does not provide CPU-based autoscaling. You can still:
- Set CPU limits/ reservations (deploy.resources) for documentation and portability (effective in Swarm; for plain Compose, they map to Docker constraints):

```yaml
services:
  worker2:
    deploy:
      resources:
        limits:
          cpus: '1.0'
        reservations:
          cpus: '0.25'
```

- Feed CPU metrics to your script from cAdvisor/Prometheus (node-exporter + cAdvisor) and combine with queue length similar to the example script.

## TL;DR
- Use docker compose up --scale worker=N for manual scaling.
- Prefer a single worker service for ergonomic scaling.
- Optionally run a tiny scaler script that polls Prometheus hub_borrow_queue_length and adjusts replicas.
- For production-grade autoscaling, use Kubernetes/KEDA as documented in Autoscaling Hints (HPA).
