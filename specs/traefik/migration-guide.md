# Migration Guide: Port-Based → Traefik Routing

**Step-by-step guide to migrate from direct port access to Traefik**

---

## Overview

This guide covers migrating an existing Agenix Playwright Grid deployment from direct port access to Traefik-based routing.

**Timeline**: 2-4 hours
**Downtime**: None (parallel deployment supported)
**Reversible**: Yes (direct ports still work)

---

## Migration Strategy

### Phase 1: Parallel Deployment (Recommended)
Run both access methods simultaneously, then gradually switch over.

### Phase 2: Full Migration
Disable direct port access once Traefik is validated.

---

## Phase 1: Add Traefik (Parallel)

### Step 1: Backup Configuration

```bash
# Backup existing configuration
cp docker-compose.yml docker-compose.yml.backup
cp .env .env.backup

# Backup data (optional)
docker compose exec postgres pg_dump -U postgres playwrightgrid > backup.sql
```

### Step 2: Update docker-compose.yml

Add Traefik service and labels (detailed in main implementation).

### Step 3: Update .env

Add Traefik variables:

```bash
# Traefik Configuration
AGENIX_TRAEFIK_ENABLED=true
AGENIX_TRAEFIK_HTTP_PORT=80
AGENIX_TRAEFIK_HTTPS_PORT=443
AGENIX_TRAEFIK_DASHBOARD_PORT=8080

# Domains (use .localhost for local dev)
AGENIX_TRAEFIK_DOMAIN_HUB=hub.localhost
AGENIX_TRAEFIK_DOMAIN_DASHBOARD=dashboard.localhost
# ... (all service domains)
```

### Step 4: Start Infrastructure Services

```bash
# Start infrastructure profile (includes Traefik)
docker compose --profile infrastructure up -d

# Verify Traefik started
docker compose ps traefik
curl http://localhost:8080/ping
```

### Step 5: Start Application Services

```bash
# Start core profile (application services)
docker compose --profile infrastructure --profile core up -d

# This will start hub, dashboard, workers, ingestion, housekeeping
# All services will automatically register with Traefik
```

### Step 6: Verify Both Access Methods

```bash
# Direct port access (existing)
curl http://localhost:5100/health

# Traefik routing (new)
curl http://hub.localhost/health

# Both should return same response
```

### Step 7: Update Client Applications

Gradually update applications to use new URLs:

**Before:**
```csharp
var client = new Service("http://localhost:5100", "project-key", "api-key");
```

**After:**
```csharp
var client = new Service("http://hub.localhost", "project-key", "api-key");
```

### Step 8: Monitor for Issues

```bash
# Watch Traefik logs
docker compose logs -f traefik

# Check health checks
open http://traefik.localhost:8080/dashboard/#/http/services
```

---

## Phase 2: Disable Direct Ports (Optional)

### When to Do This

- All clients migrated to Traefik URLs
- Traefik validated in production for 1+ weeks
- Monitoring shows no direct port access

### Step 1: Identify Direct Port Usage

```bash
# Check access logs for direct port access
docker compose logs nginx | grep ":5100"
docker compose logs dashboard | grep "direct"
```

### Step 2: Remove Port Mappings

Edit `docker-compose.yml`, comment out `ports:` sections:

```yaml
hub:
  # ports:
  #   - "5100:5000"  # Disabled - use Traefik routing
  labels:
    - "traefik.http.routers.hub.rule=Host(`hub.localhost`)"
    # ...
```

### Step 3: Restart Services

```bash
docker compose up -d hub dashboard
```

### Step 4: Verify Direct Access Blocked

```bash
# Should fail (connection refused)
curl http://localhost:5100/health

# Should work
curl http://hub.localhost/health
```

---

## Rollback Procedure

### If Issues Occur During Migration

**Step 1: Restore Configuration**
```bash
cp docker-compose.yml.backup docker-compose.yml
cp .env.backup .env
```

**Step 2: Restart Services**
```bash
docker compose down
docker compose up -d
```

**Step 3: Verify Direct Access Works**
```bash
curl http://localhost:5100/health
```

### If Database Issues

```bash
# Restore from backup
docker compose exec -T postgres psql -U postgres playwrightgrid < backup.sql
```

---

## Testing Checklist

- [ ] Traefik dashboard accessible
- [ ] All services show in Traefik dashboard
- [ ] Hub API accessible via domain
- [ ] Dashboard accessible via domain
- [ ] WebSocket connections work (SignalR)
- [ ] Grafana accessible and graphs load
- [ ] Load balancing works (ingestion replicas)
- [ ] Health checks detect unhealthy services
- [ ] Direct port access still works (phase 1)
- [ ] SSL/TLS works (production only)

---

## Common Migration Issues

### Issue: Existing Connections Dropped

**Solution**: Restart services gradually, one at a time.

### Issue: WebSocket Connections Fail

**Solution**: Add WebSocket headers to labels:
```yaml
labels:
  - "traefik.http.routers.hub.middlewares=websocket-headers"
```

### Issue: Session Loss

**Solution**: Enable sticky sessions:
```yaml
labels:
  - "traefik.http.services.dashboard.loadbalancer.sticky.cookie=true"
```

---

## Production Migration

### Prerequisites

- [ ] DNS configured (hub.your-domain.com → server IP)
- [ ] Firewall allows ports 80/443
- [ ] SSL certificate configured (Let's Encrypt)
- [ ] Tested in staging environment first

### Additional Steps

1. **Configure HTTPS**: Add Let's Encrypt certificate resolver
2. **Update DNS**: Point domains to server
3. **Enable Security Headers**: Add security middleware
4. **Configure Rate Limiting**: Protect against abuse
5. **Setup Monitoring**: Prometheus metrics + alerts

See [Production Guide](production.md) for details.

---

## Timeline

| Phase | Duration | Downtime |
|-------|----------|----------|
| Backup | 10 minutes | None |
| Add Traefik | 30 minutes | None |
| Update docker-compose.yml | 60 minutes | None |
| Restart services | 20 minutes | <1 min per service |
| Verify both methods | 30 minutes | None |
| Update clients | 1-2 weeks | None |
| Disable direct ports | 30 minutes | <1 minute |
| **Total** | **2-4 hours + client updates** | **~5 minutes total** |

---

## Support

If you encounter issues:

1. Check [Troubleshooting Guide](troubleshooting.md)
2. Review [Local Development Guide](local-development.md)
3. Consult [Configuration Reference](configuration.md)
4. Open issue in project repository
