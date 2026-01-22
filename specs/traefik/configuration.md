# Traefik Configuration Guide

**Complete reference for configuring Traefik routing, middleware, and services**

---

## Table of Contents

- [Overview](#overview)
- [Docker Label Configuration](#docker-label-configuration)
- [Static Configuration](#static-configuration)
- [Dynamic Configuration](#dynamic-configuration)
- [Middleware](#middleware)
- [Advanced Routing](#advanced-routing)
- [Monitoring](#monitoring)

---

## Overview

Traefik configuration is split into two types:

| Type | Purpose | Location | Reload |
|------|---------|----------|--------|
| **Static** | Entrypoints, providers, global settings | `traefik/traefik.yml` | Requires restart |
| **Dynamic** | Routes, middleware, TLS | Docker labels or `traefik/dynamic/*.yml` | Hot-reload |

**Best Practice**: Use Docker labels for service-specific routing (recommended for this project).

---

## Docker Label Configuration

### Basic Routing Pattern

Every service needs these core labels:

```yaml
labels:
  # Router: Defines routing rule
  - "traefik.http.routers.<service-name>.rule=Host(`<domain>`)"
  - "traefik.http.routers.<service-name>.service=<service-name>"
  - "traefik.http.routers.<service-name>.entrypoints=web"

  # Service: Backend configuration
  - "traefik.http.services.<service-name>.loadbalancer.server.port=<port>"
  - "traefik.http.services.<service-name>.loadbalancer.server.scheme=http"

  # Exposure: Enable in Traefik
  - "traefik.expose=true"
```

### Complete Example: Hub Service

```yaml
hub:
  build:
    context: .
    dockerfile: hub/Dockerfile
  labels:
    # ===== Router Configuration =====
    - "traefik.http.routers.hub.rule=Host(`${AGENIX_TRAEFIK_DOMAIN_HUB:-hub.localhost}`)"
    - "traefik.http.routers.hub.service=hub"
    - "traefik.http.routers.hub.entrypoints=web"

    # ===== Service Configuration =====
    - "traefik.http.services.hub.loadbalancer.server.port=5000"
    - "traefik.http.services.hub.loadbalancer.server.scheme=http"

    # ===== Health Check =====
    - "traefik.http.services.hub.loadbalancer.healthcheck.path=/health"
    - "traefik.http.services.hub.loadbalancer.healthcheck.interval=10s"
    - "traefik.http.services.hub.loadbalancer.healthcheck.timeout=3s"

    # ===== Middleware (optional) =====
    # - "traefik.http.routers.hub.middlewares=rate-limit@file,compression@file"

    # ===== Enable in Traefik =====
    - "traefik.expose=true"
```

---

## Label Reference

### Router Labels

| Label | Description | Example |
|-------|-------------|---------|
| `traefik.http.routers.<name>.rule` | Routing rule (Host/Path) | `Host(\`hub.localhost\`)` |
| `traefik.http.routers.<name>.service` | Service to route to | `hub` |
| `traefik.http.routers.<name>.entrypoints` | Entrypoint(s) to listen on | `web` or `web,websecure` |
| `traefik.http.routers.<name>.middlewares` | Middleware chain | `strip-prefix,rate-limit` |
| `traefik.http.routers.<name>.priority` | Router priority (higher = first) | `100` |
| `traefik.http.routers.<name>.tls` | Enable TLS | `true` |
| `traefik.http.routers.<name>.tls.certresolver` | Certificate resolver | `letsencrypt` |

### Service Labels

| Label | Description | Example |
|-------|-------------|---------|
| `traefik.http.services.<name>.loadbalancer.server.port` | Container port | `5000` |
| `traefik.http.services.<name>.loadbalancer.server.scheme` | Protocol | `http` or `https` |
| `traefik.http.services.<name>.loadbalancer.sticky.cookie` | Sticky sessions | `true` |
| `traefik.http.services.<name>.loadbalancer.healthcheck.path` | Health check URL | `/health` |
| `traefik.http.services.<name>.loadbalancer.healthcheck.interval` | Check interval | `10s` |
| `traefik.http.services.<name>.loadbalancer.healthcheck.timeout` | Check timeout | `3s` |

### Middleware Labels

| Label | Description | Example |
|-------|-------------|---------|
| `traefik.http.middlewares.<name>.stripprefix.prefixes` | Strip path prefix | `/api` |
| `traefik.http.middlewares.<name>.ratelimit.average` | Rate limit (req/s) | `100` |
| `traefik.http.middlewares.<name>.compress.excludedcontenttypes` | Compression exclusions | `text/event-stream` |
| `traefik.http.middlewares.<name>.headers.sslredirect` | Force HTTPS | `true` |
| `traefik.http.middlewares.<name>.basicauth.users` | Basic auth users | `admin:$apr1$...` |

---

## Static Configuration

### traefik/traefik.yml

```yaml
# ==============================================================================
# Traefik Static Configuration
# ==============================================================================
# This file configures Traefik's core behavior: entrypoints, providers, API/dashboard.
# Changes require Traefik restart.

# ==============================================================================
# Global Configuration
# ==============================================================================
global:
  checkNewVersion: true
  sendAnonymousUsage: false

# ==============================================================================
# Entrypoints (Ports that Traefik listens on)
# ==============================================================================
entryPoints:
  # HTTP entrypoint
  web:
    address: ":80"
    # Redirect HTTP → HTTPS (production)
    # http:
    #   redirections:
    #     entryPoint:
    #       to: websecure
    #       scheme: https

  # HTTPS entrypoint (production)
  websecure:
    address: ":443"
    # HTTP/3 support (experimental)
    # http3: {}

  # Traefik dashboard
  traefik:
    address: ":8080"

# ==============================================================================
# Providers (Service discovery sources)
# ==============================================================================
providers:
  # Docker provider: Discover services via Docker labels
  docker:
    endpoint: "unix:///var/run/docker.sock"
    exposedByDefault: false  # Only expose services with traefik.expose=true
    network: "agenix-playwright-grid_default"  # Docker network to use
    constraints: "Label(`traefik.expose`, `true`)"  # Additional filter

  # File provider: Load dynamic configuration from files
  file:
    directory: "/etc/traefik/dynamic"
    watch: true  # Hot-reload on file changes

# ==============================================================================
# API & Dashboard
# ==============================================================================
api:
  dashboard: true  # Enable web dashboard
  insecure: true   # Allow dashboard without authentication (dev only!)
  # For production, use middleware authentication:
  # insecure: false

# ==============================================================================
# Logging
# ==============================================================================
log:
  level: INFO  # DEBUG, INFO, WARN, ERROR, FATAL, PANIC
  format: json  # json or common
  # filePath: "/var/log/traefik/traefik.log"

# Access logs (HTTP requests)
accessLog:
  format: json
  # filePath: "/var/log/traefik/access.log"
  bufferingSize: 100
  filters:
    statusCodes:
      - "200-299"  # Success
      - "400-499"  # Client errors
      - "500-599"  # Server errors
    retryAttempts: true
    minDuration: "10ms"

# ==============================================================================
# Metrics (Prometheus)
# ==============================================================================
metrics:
  prometheus:
    addEntryPointsLabels: true
    addRoutersLabels: true
    addServicesLabels: true
    entryPoint: traefik  # Expose metrics on /metrics endpoint

# ==============================================================================
# Certificate Resolvers (HTTPS / Let's Encrypt)
# ==============================================================================
# certificateResolvers:
#   letsencrypt:
#     acme:
#       email: "admin@example.com"
#       storage: "/letsencrypt/acme.json"
#       httpChallenge:
#         entryPoint: web
#   # Staging server (for testing)
#   letsencrypt-staging:
#     acme:
#       email: "admin@example.com"
#       storage: "/letsencrypt/acme-staging.json"
#       caServer: "https://acme-staging-v02.api.letsencrypt.org/directory"
#       httpChallenge:
#         entryPoint: web

# ==============================================================================
# Pilot (Traefik Cloud)
# ==============================================================================
# pilot:
#   token: "your-pilot-token"

# ==============================================================================
# Tracing (OpenTelemetry, Jaeger, Zipkin)
# ==============================================================================
# tracing:
#   otlp:
#     http:
#       endpoint: "http://localhost:4318"
#   serviceName: "traefik"
