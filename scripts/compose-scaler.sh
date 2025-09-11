#!/usr/bin/env bash
set -euo pipefail

PROM=${PROMETHEUS_URL:-http://127.0.0.1:9090}
SERVICE=${SERVICE_NAME:-worker2}
MIN=${MIN_REPLICAS:-1}
MAX=${MAX_REPLICAS:-5}
LABEL_MATCH=${LABEL_MATCH:-AppB:Chromium:UAT}
INTERVAL=${INTERVAL_SECONDS:-30}

QUERY="sum(hub_borrow_queue_length{label=\"$LABEL_MATCH\"})"

echo "[scaler] Starting Compose scaler for service=$SERVICE, min=$MIN, max=$MAX, label=$LABEL_MATCH"

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
  encoded=$(python3 - <<PY
import urllib.parse
print(urllib.parse.quote("$QUERY"))
PY
)
  val=$(curl -fsSL "$PROM/api/v1/query?query=$encoded" | jq -r '.data.result[0].value[1]' || echo 0)
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
