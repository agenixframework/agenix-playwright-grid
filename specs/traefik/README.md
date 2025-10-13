# Traefik Integration for Agenix Playwright Grid

**Modern reverse proxy and load balancer for unified service access**

---

## Table of Contents

- [Overview](#overview)
- [Why Traefik?](#why-traefik)
- [Architecture](#architecture)
- [Routing Table](#routing-table)
- [Quick Start](#quick-start)
- [Features](#features)
- [Access URLs](#access-urls)
- [Next Steps](#next-steps)

---

## Overview

Traefik is integrated into Agenix Playwright Grid as a modern reverse proxy and load balancer, providing:

- **Single Entry Point**: Access all services via clean domain names instead of port numbers
- **Service Discovery**: Automatic routing configuration via Docker labels
- **Load Balancing**: Distribute traffic across multiple service replicas
- **Health Checks**: Automatic removal of unhealthy service instances
- **Production Ready**: Built-in HTTPS support with Let's Encrypt
- **Zero Downtime**: Rolling updates with automatic traffic management

---

## Why Traefik?

### Before Traefik (Port-Based Access)
```
❌ http://localhost:5100        → Hub
❌ http://localhost:3001        → Dashboard
❌ http://localhost:3000        → Grafana
❌ http://localhost:9090        → Prometheus
❌ http://localhost:9001        → MinIO Console
❌ http://localhost:15672       → RabbitMQ Management
❌ http://localhost:8025        → Mailpit Web UI
```

**Problems:**
- Need to remember 7+ port numbers
- Port conflicts with other services
- No load balancing for scaled services
- Manual HTTPS configuration
- No automatic health checks

### After Traefik (Domain-Based Access)
```
✅ http://hub.localhost          → Hub
✅ http://dashboard.localhost    → Dashboard
✅ http://grafana.localhost      → Grafana
✅ http://prometheus.localhost   → Prometheus
✅ http://minio.localhost        → MinIO Console
✅ http://rabbitmq.localhost     → RabbitMQ Management
✅ http://mailpit.localhost      → Mailpit Web UI
✅ http://traefik.localhost      → Traefik Dashboard
```

**Benefits:**
- Clean, memorable domain names
- No port conflicts
- Automatic load balancing for ingestion replicas
- Built-in HTTPS with Let's Encrypt (production)
- Automatic health checks and failover
- Centralized routing configuration

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         External Traffic                         │
│                      (http://hub.localhost)                      │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
                  ┌──────────────────────┐
                  │   Traefik Gateway    │
                  │   (Port 80/443)      │
                  │                      │
                  │  - Service Discovery │
                  │  - Load Balancing    │
                  │  - Health Checks     │
                  │  - TLS Termination   │
                  └──────────┬───────────┘
                             │
        ┌────────────────────┼────────────────────┐
        │                    │                    │
        ▼                    ▼                    ▼
┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│  Hub Service  │  │   Dashboard   │  │   Grafana     │
│  (port 5000)  │  │  (port 3001)  │  │  (port 3000)  │
└───────────────┘  └───────────────┘  └───────────────┘

        │                    │                    │
        ▼                    ▼                    ▼
┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│  Prometheus   │  │ MinIO Console │  │   RabbitMQ    │
│  (port 9090)  │  │  (port 9001)  │  │  (port 15672) │
└───────────────┘  └───────────────┘  └───────────────┘

        │
        ▼
┌───────────────────────────────┐
│  Ingestion Service (Replicas) │
│  ┌───────────┐  ┌───────────┐ │
│  │ Replica 1 │  │ Replica 2 │ │  ← Load Balanced
│  └───────────┘  └───────────┘ │
│     (8081)          (8081)     │
└───────────────────────────────┘
```

### Key Components

| Component | Description | Port |
|-----------|-------------|------|
| **Traefik Gateway** | Reverse proxy and load balancer | 80 (HTTP), 443 (HTTPS), 8080 (Dashboard) |
| **Docker Provider** | Automatic service discovery via Docker labels | - |
| **Let's Encrypt** | Automatic HTTPS certificates (production) | - |
| **Health Checks** | Monitor service health and remove unhealthy instances | - |

---

## Routing Table

| Service | Local Domain | Container Port | Path | Health Check |
|---------|--------------|----------------|------|--------------|
| **Hub** | `hub.localhost` | 5000 | `/` | `/health` |
| **Dashboard** | `dashboard.localhost` | 3001 | `/` | `/health` |
| **Grafana** | `grafana.localhost` | 3000 | `/grafana` | `/api/health` |
| **Prometheus** | `prometheus.localhost` | 9090 | `/` | `/-/healthy` |
| **MinIO Console** | `minio.localhost` | 9001 | `/` | `/minio/health/live` |
| **RabbitMQ Mgmt** | `rabbitmq.localhost` | 15672 | `/` | `/api/health/checks/alarms` |
| **Mailpit Web UI** | `mailpit.localhost` | 8025 | `/` | `/` |
| **Ingestion** | `ingestion.localhost` | 8081 | `/` | `/health` |
| **Traefik Dashboard** | `traefik.localhost` | 8080 | `/` | `/ping` |

**Notes:**
- Grafana supports both `grafana.localhost` (subdomain) and `http://hub.localhost/grafana` (path-based)
- Ingestion service has 2 replicas with round-robin load balancing + sticky sessions
- All health checks run every 10 seconds with 3 retries before removal

---

## Quick Start

### Prerequisites

- Docker Compose v2.0+
- `/etc/hosts` configured for `*.localhost` domains (usually works by default)

### Understanding Docker Compose Profiles

The project uses Docker Compose profiles for flexible deployment:

**Available Profiles:**
- `infrastructure` - Platform services (redis, postgres, rabbitmq, minio, mailpit, traefik)
- `core` - Application services (hub, dashboard, workers, ingestion, housekeeping)
- `monitoring` - Observability stack (grafana, prometheus)

### 1. Start Services

```bash
# Infrastructure + Application (recommended)
docker compose --profile infrastructure --profile core up -d

# Full stack with monitoring
docker compose --profile infrastructure --profile core --profile monitoring up -d

# Infrastructure only (for testing)
docker compose --profile infrastructure up -d

# Verify Traefik is running
docker ps | grep traefik
```

### 2. Verify Service Discovery

```bash
# Check Traefik dashboard for registered services
open http://traefik.localhost:8080

# Or check via API
curl http://localhost:8080/api/http/routers | jq
```

### 3. Access Services

```bash
# Hub
open http://hub.localhost

# Dashboard
open http://dashboard.localhost

# Grafana
open http://grafana.localhost

# Traefik Dashboard
open http://traefik.localhost:8080
```

### 4. Test Load Balancing (Ingestion)

```bash
# Send requests to ingestion service
for i in {1..10}; do
  curl -s http://ingestion.localhost/health | jq
done

# Check which replica served each request in Traefik dashboard
open http://traefik.localhost:8080/dashboard/#/http/services/ingestion@docker
```

### 5. Stop Services

```bash
# Stop all services
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v
```

---

## Features

### 1. Service Discovery

Traefik automatically discovers services via Docker labels:

```yaml
labels:
  - "traefik.http.routers.hub.rule=Host(`hub.localhost`)"
  - "traefik.http.routers.hub.service=hub"
  - "traefik.http.services.hub.loadbalancer.server.port=5000"
  - "traefik.expose=true"
```

**Benefits:**
- No manual configuration files
- Services register automatically on startup
- Dynamic routing updates without restart

### 2. Load Balancing

Multiple ingestion replicas are load-balanced automatically:

```yaml
ingestion:
  deploy:
    replicas: 2
  labels:
    - "traefik.http.services.ingestion.loadbalancer.sticky.cookie=true"
```

**Strategies:**
- **Round-robin**: Default load balancing algorithm
- **Sticky sessions**: Cookie-based session affinity for stateful services
- **Health checks**: Unhealthy replicas removed automatically

### 3. Health Checks

Traefik monitors service health and removes unhealthy instances:

```yaml
labels:
  - "traefik.http.services.hub.loadbalancer.healthcheck.path=/health"
  - "traefik.http.services.hub.loadbalancer.healthcheck.interval=10s"
```

**Behavior:**
- Checks every 10 seconds
- 3 failed checks → instance removed
- Automatic re-addition when healthy

### 4. Middleware

Traefik supports middleware for request/response transformation:

- **Strip Prefix**: Remove path prefix before forwarding (e.g., `/grafana` → `/`)
- **Rate Limiting**: Protect services from abuse
- **Compression**: Gzip/Brotli compression for responses
- **Security Headers**: HSTS, CSP, X-Frame-Options
- **Authentication**: Basic auth for admin endpoints
- **CORS**: Cross-origin request handling

See [configuration.md](configuration.md) for details.

### 5. HTTPS Support (Production)

Traefik integrates with Let's Encrypt for automatic HTTPS:

```yaml
command:
  - --certificatesresolvers.letsencrypt.acme.email=admin@example.com
  - --certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json
  - --certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web
```

See [production.md](production.md) for production deployment guide.

---

## Access URLs

### Development (Local)

| Service | URL | Credentials |
|---------|-----|-------------|
| **Dashboard** | http://dashboard.localhost | (configured in app) |
| **Hub API** | http://hub.localhost/api | API Key |
| **Grafana** | http://grafana.localhost | admin/admin |
| **Prometheus** | http://prometheus.localhost | - |
| **MinIO Console** | http://minio.localhost | minioadmin/minioadmin |
| **RabbitMQ Management** | http://rabbitmq.localhost | guest/guest |
| **Mailpit Web UI** | http://mailpit.localhost | - |
| **Traefik Dashboard** | http://traefik.localhost:8080 | - |

### Production (with HTTPS)

Replace `localhost` with your production domain (e.g., `hub.example.com`).

**HTTPS automatically enabled when:**
- Let's Encrypt resolver configured
- Domain points to server IP
- Port 80/443 accessible from internet

---

## Next Steps

### Local Development
- [Local Development Guide](local-development.md) - Setup, debugging, testing
- [Configuration Guide](configuration.md) - Labels, middleware, static/dynamic config
- [Troubleshooting Guide](troubleshooting.md) - Common issues and solutions

### Production Deployment
- [Production Guide](production.md) - HTTPS, security, performance tuning
- [Migration Guide](migration-guide.md) - Migrate from direct port access

### Advanced Topics
- [Configuration Reference](configuration.md#advanced-routing) - Path-based routing, WebSocket support
- [Middleware Configuration](configuration.md#middleware) - Rate limiting, authentication, compression
- [Monitoring](configuration.md#monitoring) - Prometheus metrics, access logs

---

## Backwards Compatibility

**Direct port access still works!**

Even with Traefik enabled, all services expose their original ports:

```bash
# Traefik routing
http://hub.localhost          → Hub

# Direct port access (still works)
http://localhost:5100         → Hub
```

This ensures:
- Existing scripts and tools continue working
- Gradual migration path
- Development flexibility

To disable direct port access (production), comment out `ports:` sections in `docker-compose.yml`.

---

## Profile-Based Deployment

The project uses Docker Compose profiles for flexible deployment:

```bash
# Infrastructure only (databases, queues, storage, traefik)
docker compose --profile infrastructure up -d

# Infrastructure + Application (recommended)
docker compose --profile infrastructure --profile core up -d

# Full stack with monitoring
docker compose --profile infrastructure --profile core --profile monitoring up -d
```

**Available Profiles:**
- `infrastructure` - redis, postgres, rabbitmq, minio, mailpit, traefik
- `core` - hub, dashboard, workers, ingestion, housekeeping
- `monitoring` - grafana, prometheus

See [profiles.md](profiles.md) for detailed documentation.

---

## Support & Resources

- **Documentation**: [docs/traefik/](.)
- **Traefik Official Docs**: https://doc.traefik.io/traefik/
- **ReportPortal Reference**: https://github.com/reportportal/reportportal (inspiration for this integration)
- **Issues**: Create issue in project repository

---

**Last Updated**: 2025-12-07
**Traefik Version**: v3.0
**Docker Compose Version**: v2.0+
