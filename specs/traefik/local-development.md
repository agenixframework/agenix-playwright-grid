# Local Development with Traefik

**Setup and debugging guide for local development with Traefik**

---

## Quick Start

### 1. Start Services with Traefik

```bash
# Start infrastructure + application services
docker compose --profile infrastructure --profile core up -d

# View logs
docker compose logs -f traefik

# Check Traefik status
docker compose ps traefik
```

### 2. Verify Service Discovery

```bash
# Open Traefik dashboard
open http://traefik.localhost:8080

# Check registered routers
curl http://localhost:8080/api/http/routers | jq '.[] | {name, rule, service}'

# Check registered services
curl http://localhost:8080/api/http/services | jq
```

### 3. Access Services

| Service | URL | Purpose |
|---------|-----|---------|
| Hub | http://hub.localhost | API Backend |
| Dashboard | http://dashboard.localhost | Web UI |
| Grafana | http://grafana.localhost | Metrics Visualization |
| Traefik Dashboard | http://traefik.localhost:8080 | Routing Configuration |

---

## /etc/hosts Configuration

Most systems automatically resolve `*.localhost` to `127.0.0.1`. If not working, add manually:

```bash
# Edit /etc/hosts (macOS/Linux)
sudo nano /etc/hosts

# Add entries
127.0.0.1 hub.localhost
127.0.0.1 dashboard.localhost
127.0.0.1 grafana.localhost
127.0.0.1 prometheus.localhost
127.0.0.1 minio.localhost
127.0.0.1 rabbitmq.localhost
127.0.0.1 mailpit.localhost
127.0.0.1 traefik.localhost
```

**Windows**: Edit `C:\Windows\System32\drivers\etc\hosts`

---

## Docker Compose Profiles

### Available Profiles

| Profile | Services Included | Use Case |
|---------|-------------------|----------|
| `infrastructure` | redis, postgres, rabbitmq, minio, mailpit, traefik | Platform/infrastructure layer |
| `core` | hub, dashboard, workers, ingestion, housekeeping | Application services |
| `monitoring` | grafana, prometheus | Observability stack |

### Usage

```bash
# Infrastructure only (databases, queues, storage)
docker compose --profile infrastructure up -d

# Infrastructure + Application (recommended for development)
docker compose --profile infrastructure --profile core up -d

# Full stack with monitoring
docker compose --profile infrastructure --profile core --profile monitoring up -d
```

---

## Debugging

### Check Service Registration

```bash
# List all registered routers
docker compose exec traefik wget -qO- http://localhost:8080/api/http/routers | jq

# Check specific service
curl http://localhost:8080/api/http/services/hub@docker | jq
```

### Test Health Checks

```bash
# Check hub health
curl -v http://hub.localhost/health

# Check via Traefik dashboard
open http://traefik.localhost:8080/dashboard/#/http/services/hub@docker
```

### View Traefik Logs

```bash
# Follow logs
docker compose logs -f traefik

# Filter by service
docker compose logs traefik | grep hub

# JSON formatted logs
docker compose logs --no-log-prefix traefik | jq
```

---

## Common Issues

### 1. Service Not Accessible

**Problem**: `http://hub.localhost` returns 404

**Solutions**:
```bash
# Check if service has traefik.expose=true label
docker inspect <container-id> | jq '.[0].Config.Labels' | grep traefik

# Verify service is running
docker compose ps hub

# Check Traefik discovered the service
curl http://localhost:8080/api/http/routers | jq '.[] | select(.service=="hub@docker")'
```

### 2. DNS Not Resolving

**Problem**: `hub.localhost` doesn't resolve

**Solutions**:
```bash
# Test DNS resolution
ping hub.localhost

# Add to /etc/hosts if needed
echo "127.0.0.1 hub.localhost" | sudo tee -a /etc/hosts

# Clear DNS cache (macOS)
sudo dscacheutil -flushcache
```

### 3. Port Conflicts

**Problem**: Traefik fails to start (port 80/443 in use)

**Solutions**:
```bash
# Find process using port 80
lsof -i :80

# Use custom ports
export AGENIX_TRAEFIK_HTTP_PORT=8000
export AGENIX_TRAEFIK_HTTPS_PORT=8443
docker compose --profile infrastructure --profile core up
```

---

## Testing

### Test Routing

```bash
# Test hub routing
curl -H "Host: hub.localhost" http://localhost/health

# Test with different methods
curl -X GET http://hub.localhost/api/projects
curl -X POST http://hub.localhost/api/launches -d '{...}'
```

### Test Load Balancing (Ingestion)

```bash
# Send 10 requests
for i in {1..10}; do
  curl -s http://ingestion.localhost/health | jq -r '.instance'
done

# Should see alternating replica IDs
```

### Test Health Checks

```bash
# Stop a service
docker compose stop hub

# Verify Traefik removes it (503 error expected)
curl -v http://hub.localhost/health

# Check dashboard
open http://traefik.localhost:8080/dashboard/#/http/services
```

---

## Performance Tuning

### Connection Pooling

```yaml
labels:
  - "traefik.http.services.hub.loadbalancer.passhostheader=true"
  - "traefik.http.services.hub.loadbalancer.responseForwarding.flushInterval=100ms"
```

### Timeouts

```yaml
labels:
  - "traefik.http.services.hub.loadbalancer.healthcheck.timeout=3s"
  - "traefik.http.routers.hub.middlewares=timeout@file"
```

In `traefik/dynamic/middleware.yml`:
```yaml
http:
  middlewares:
    timeout:
      timeout:
        readTimeout: "30s"
        writeTimeout: "30s"
        idleTimeout: "90s"
```

---

## Next Steps

- [Configuration Guide](configuration.md) - Advanced routing and middleware
- [Production Guide](production.md) - HTTPS and production deployment
- [Troubleshooting](troubleshooting.md) - Detailed problem solving
