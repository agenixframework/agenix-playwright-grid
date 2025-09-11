# Playwright Grid – Helm Chart

This chart deploys the Playwright Grid stack (Hub, Worker(s), Dashboard) with sensible defaults, health/readiness probes, and resource limits. An optional Redis deployment is included for convenience in dev/staging clusters.

## Prerequisites
- Kubernetes v1.24+
- Helm v3.9+

## Quick start
```bash
# Create a namespace (optional)
kubectl create ns pg

# Install with defaults (Redis enabled, 1 Hub, 2 Workers, 1 Dashboard)
helm install grid charts/playwright-grid -n pg

# Check pods and services
kubectl get pods,svc -n pg
```

## Images
By default, the chart references images under ghcr.io/agenixframework/agenix-playwright-grid/* with tag `latest`. Override as needed:
```bash
helm upgrade --install grid charts/playwright-grid -n pg \
  --set image.hub.repository=myrepo/hub,image.hub.tag=1.0.0 \
  --set image.worker.repository=myrepo/worker,image.worker.tag=1.0.0 \
  --set image.dashboard.repository=myrepo/dashboard,image.dashboard.tag=1.0.0
```

## Services and ports
- Hub Service (ClusterIP): port 5000
- Worker Service (ClusterIP): port 5000 (WebSocket endpoint exposed under /ws/{browserId})
- Dashboard Service (ClusterIP): port 3001

Optional Ingress objects can be enabled via `values.yaml` under `ingress.*`.

## Probes
- Hub: liveness → GET /health; readiness → GET /ready
- Worker: liveness → GET /health; readiness → GET /health/ready
- Dashboard: liveness/readiness → GET /health

## Resource limits (defaults)
- Hub: requests 100m/128Mi; limits 500m/512Mi
- Worker: requests 500m/512Mi; limits 2000m/2Gi
- Dashboard: requests 50m/64Mi; limits 300m/256Mi

Adjust via `.Values.hub.resources`, `.Values.worker.resources`, `.Values.Dashboard.resources`.

## Redis
`redis.enabled=true` provisions a simple Redis Deployment/Service (no persistence) for development. For production, disable it and point Hub/Workers to your managed Redis endpoint:
```bash
helm upgrade --install grid charts/playwright-grid -n pg \
  --set redis.enabled=false \
  --set hub.redisUrl=your-redis-host:6379
```

## Worker configuration
Each Worker pod gets a unique `NODE_ID` from `metadata.name`. Key envs are exposed for tuning:
- `worker.poolConfig`: label capacity map (e.g., `AppB:Chromium:UAT=2,...`).
- `worker.publicWs.*`: host/port/scheme advertised to clients. Defaults to the worker Service DNS and port 5000; for external access, configure an Ingress or Service of type LoadBalancer and set these accordingly.
- `worker.shm.enabled/sizeLimit`: mounts a Memory-backed `/dev/shm` to improve Chromium stability.

## Secrets
Hub runner/node secrets are stored in a Secret created by the chart. Override via:
```bash
--set hub.secrets.runnerSecret=runner-secret --set hub.secrets.nodeSecret=node-secret \
--set worker.nodeSecret=node-secret --set worker.nodeNodeSecret=node-node-secret
```

## Uninstall
```bash
helm uninstall grid -n pg
```

## Notes
- This chart favors sensible defaults for dev/test. Review security hardening (non-root, read-only FS, networkPolicies) for production.
- The chart aligns with the repository’s health endpoints and environment variables as documented in `.junie/guidelines.md` and used in `docker-compose.yml`.
