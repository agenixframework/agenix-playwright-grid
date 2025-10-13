# Docker Compose Profiles

This document describes the Docker Compose profile system used for flexible deployment of Agenix Playwright Grid services.

---

## Overview

Docker Compose profiles allow you to selectively start groups of services based on your deployment needs. This provides:

- **Flexibility**: Start only the services you need
- **Resource Efficiency**: Don't waste resources on unused services
- **Environment Customization**: Different profiles for dev, staging, production
- **Modular Architecture**: Add/remove features without changing docker-compose.yml

---

## Available Profiles

### `infrastructure` Profile
**Infrastructure and platform services**

**Services:**
- `redis` - Cache and session storage
- `postgres` - Primary database
- `rabbitmq` - Message queue for event-driven architecture
- `minio` - S3-compatible object storage for artifacts
- `mailpit` - SMTP server and email testing UI
- `traefik` - Reverse proxy and load balancer

**Use Case:**
- Required foundation for all deployments
- Infrastructure layer that all application services depend on
- Provides data storage, messaging, and routing

**Start Command:**
```bash
docker compose --profile infrastructure up -d
```

**Benefits:**
- ✅ All infrastructure services in one profile
- ✅ Clean separation between platform and application
- ✅ Traefik included for domain-based routing
- ✅ Production-ready email and object storage

---

### `core` Profile
**Application services for production deployment**

**Services:**
- `hub` - Core API and orchestration service
- `dashboard` - Web UI
- `worker1`, `worker2`, `worker3` - Playwright browser workers
- `ingestion` - Event-driven data ingestion service (2 replicas)
- `housekeeping` - Background cleanup and retention service

**Use Case:**
- All application services required for production
- Complete test execution platform
- Event-driven architecture with ingestion pipeline
- Automated data retention and cleanup

**Start Command:**
```bash
# Core services only (requires infrastructure)
docker compose --profile infrastructure --profile core up -d
```

**Benefits:**
- ✅ Complete application stack in one profile
- ✅ Includes ingestion for high-volume test execution
- ✅ Automated housekeeping for data retention
- ✅ Horizontal scaling for ingestion (2 replicas)

---

### `monitoring` Profile
**Observability and metrics stack**

**Services:**
- `grafana` - Visualization and dashboards
- `prometheus` - Metrics collection and alerting

**Use Case:**
- Performance monitoring
- Production observability
- Capacity planning
- Debugging and troubleshooting

**Start Command:**
```bash
# Infrastructure + Core + Monitoring
docker compose --profile infrastructure --profile core --profile monitoring up -d
```

**Access:**
- Grafana: http://localhost:3000 or http://grafana.localhost (with Traefik)
- Prometheus: http://localhost:9090 or http://prometheus.localhost (with Traefik)

---

## Common Deployment Scenarios

### Minimal Development Environment
```bash
# Just infrastructure (databases, queues, storage)
docker compose --profile infrastructure up -d
```
**Includes:** Redis, PostgreSQL, RabbitMQ, MinIO, Mailpit, Traefik

**Access:**
- Traefik Dashboard: http://traefik.localhost:8080
- MinIO Console: http://minio.localhost
- RabbitMQ Management: http://rabbitmq.localhost
- Mailpit UI: http://mailpit.localhost

---

### Full Application Stack (Recommended)
```bash
# Infrastructure + Application Services
docker compose --profile infrastructure --profile core up -d
```
**Includes:** All infrastructure + Hub, Dashboard, 3 Workers, Ingestion, Housekeeping

**Access via Traefik:**
- Dashboard: http://dashboard.localhost
- Hub API: http://hub.localhost
- Ingestion: http://ingestion.localhost

**Direct Access (fallback):**
- Dashboard: http://localhost:3001
- Hub API: http://localhost:5100

---

### Production with Monitoring
```bash
# Full stack with observability
docker compose --profile infrastructure --profile core --profile monitoring up -d
```
**Includes:** Everything + Grafana + Prometheus

**Access:**
- Dashboard: http://dashboard.localhost
- Grafana: http://grafana.localhost
- Prometheus: http://prometheus.localhost

---

## Profile Dependencies

**Important:** Profile dependencies to understand:

| Profile | Depends On | Reason |
|---------|------------|--------|
| `infrastructure` | None | Foundation layer |
| `core` | `infrastructure` | Requires databases, queues, storage |
| `monitoring` | None (optional: `infrastructure` for scraping) | Can monitor external services |

**Best Practice:** Always start `infrastructure` before `core`:
```bash
# ✅ Correct order
docker compose --profile infrastructure --profile core up -d

# ❌ Core services will fail (missing dependencies)
docker compose --profile core up -d
```

---

## Profile Management Commands

### Start Specific Profiles
```bash
# Single profile
docker compose --profile infrastructure up -d

# Multiple profiles
docker compose --profile infrastructure --profile core --profile monitoring up -d
```

### Stop All Services
```bash
# Stop all running services
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v
```

### View Running Services
```bash
# All running containers
docker compose ps

# Filter by specific services
docker compose ps hub dashboard worker1
```

### Scale Services
```bash
# Scale ingestion service to 4 replicas
docker compose --profile infrastructure --profile core up -d --scale ingestion=4

# Scale workers
docker compose --profile infrastructure --profile core up -d --scale worker1=2 --scale worker2=2
```

---

## Access URLs by Profile

### Infrastructure Profile

| Service | Domain (Traefik) | Direct Port | Purpose |
|---------|------------------|-------------|---------|
| Traefik Dashboard | http://traefik.localhost:8080 | :8080 | Service discovery and routing |
| MinIO Console | http://minio.localhost | :9001 | Object storage management |
| RabbitMQ Mgmt | http://rabbitmq.localhost | :15672 | Message queue monitoring |
| Mailpit UI | http://mailpit.localhost | :8025 | Email testing and debugging |

**Notes:**
- Redis (6379), PostgreSQL (5432), RabbitMQ AMQP (5672), MinIO S3 (9000) don't have web UIs
- Traefik routes all HTTP traffic to services automatically

### Core Profile

| Service | Domain (Traefik) | Direct Port | Purpose |
|---------|------------------|-------------|---------|
| Dashboard | http://dashboard.localhost | :3001 | Main web interface |
| Hub API | http://hub.localhost | :5100 | Core API endpoints |
| Ingestion | http://ingestion.localhost | :8081 | Event ingestion service |
| Housekeeping | - | :8082 | Health check only |

**Notes:**
- Workers don't have web UIs (WebSocket endpoints on ports 5200-5202)
- Ingestion has 2 replicas (load balanced by Traefik)

### Monitoring Profile

| Service | Domain (Traefik) | Direct Port | Purpose |
|---------|------------------|-------------|---------|
| Grafana | http://grafana.localhost | :3000 | Dashboards and visualization |
| Prometheus | http://prometheus.localhost | :9090 | Metrics and alerts |

---

## Environment-Specific Profiles

### Local Development
```bash
# Lightweight stack for development
docker compose --profile infrastructure --profile core up -d
```
**Services:** 11 containers (infrastructure + core)
**Memory:** ~4-6GB RAM
**Use Case:** Active development with hot reload

---

### Staging/QA
```bash
# Full stack for testing
docker compose --profile infrastructure --profile core --profile monitoring up -d
```
**Services:** 13 containers (+ monitoring)
**Memory:** ~5-7GB RAM
**Use Case:** Integration testing, performance validation

---

### Production
```bash
# Production deployment with all features
docker compose --profile infrastructure --profile core --profile monitoring up -d
```
**Services:** 13 containers
**Memory:** ~6-8GB RAM
**Features:** Full observability, automatic cleanup, load balancing

**Production Checklist:**
- ✅ Enable HTTPS in Traefik (Let's Encrypt)
- ✅ Configure retention policies in housekeeping
- ✅ Set up Grafana alerts
- ✅ Scale ingestion based on load
- ✅ Backup PostgreSQL regularly
- ✅ Monitor disk usage (MinIO artifacts)

---

## Troubleshooting

### Services Not Starting

**Problem:** Core services fail to start.

**Solution:** Ensure infrastructure is running first:
```bash
# Start infrastructure and wait for health checks
docker compose --profile infrastructure up -d
docker compose ps  # Check all services are "healthy"

# Then start core
docker compose --profile core up -d
```

### Traefik Not Routing

**Problem:** Cannot access services via domain names (hub.localhost).

**Solution:** Verify Traefik is running and services are registered:
```bash
# Check Traefik is in infrastructure profile
docker compose ps traefik

# View Traefik dashboard
open http://traefik.localhost:8080

# Check if services have traefik.expose=true labels
docker compose config | grep traefik.expose
```

### Port Conflicts

**Problem:** Port already in use errors.

**Solution:** Check what's using the port:
```bash
# Check port usage
lsof -i :80    # Traefik HTTP
lsof -i :5432  # PostgreSQL
lsof -i :6379  # Redis

# Stop conflicting services or modify .env:
AGENIX_TRAEFIK_HTTP_PORT=8080
```

### Health Check Failures

**Problem:** Services stuck in "unhealthy" state.

**Solution:** Check service logs:
```bash
# View logs for specific service
docker compose logs hub
docker compose logs postgres

# View health check logs
docker inspect <container-id> | jq '.[0].State.Health'
```

---

## Next Steps

- [Traefik Configuration](configuration.md) - Advanced Traefik settings
- [Local Development Guide](local-development.md) - Development workflows
- [Production Deployment](production.md) - Production best practices
- [Troubleshooting](troubleshooting.md) - Common issues and solutions

---

**See Also:**
- [Docker Compose Profiles Documentation](https://docs.docker.com/compose/profiles/)
- [Traefik Documentation](https://doc.traefik.io/traefik/)
