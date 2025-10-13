# Traefik Troubleshooting Guide

**Common issues and solutions**

---

## Service Not Accessible (404)

### Symptoms
- `http://hub.localhost` returns 404 Not Found
- Traefik dashboard shows no router for the service

### Solutions

**1. Check service has `traefik.expose=true` label:**
```bash
docker inspect <container-name> | jq '.[0].Config.Labels' | grep -i traefik
```

**2. Verify service is running:**
```bash
docker compose ps | grep hub
```

**3. Check Traefik discovered the service:**
```bash
curl http://localhost:8080/api/http/routers | jq '.[] | select(.name | contains("hub"))'
```

**4. Verify router rule:**
```bash
# Test with explicit Host header
curl -H "Host: hub.localhost" http://localhost/health
```

---

## DNS Not Resolving

### Symptoms
- `ping hub.localhost` fails
- Browser shows "DNS_PROBE_FINISHED_NXDOMAIN"

### Solutions

**1. Add to /etc/hosts:**
```bash
echo "127.0.0.1 hub.localhost dashboard.localhost grafana.localhost" | sudo tee -a /etc/hosts
```

**2. Clear DNS cache:**
```bash
# macOS
sudo dscacheutil -flushcache
sudo killall -HUP mDNSResponder

# Linux
sudo systemd-resolve --flush-caches

# Windows
ipconfig /flushdns
```

---

## Port Conflicts

### Symptoms
- Traefik fails to start: "bind: address already in use"

### Solutions

**1. Find process using port:**
```bash
lsof -i :80
lsof -i :443
```

**2. Use custom ports:**
```bash
export AGENIX_TRAEFIK_HTTP_PORT=8000
export AGENIX_TRAEFIK_HTTPS_PORT=8443
docker compose --profile traefik up
```

---

## Health Check Failures

### Symptoms
- Service shows as unhealthy in Traefik dashboard
- Intermittent 503 errors

### Solutions

**1. Test health endpoint directly:**
```bash
docker compose exec hub curl http://localhost:5000/health
```

**2. Check health check configuration:**
```bash
# View service health check labels
docker inspect hub | jq '.[0].Config.Labels' | grep healthcheck
```

**3. Adjust health check timing:**
```yaml
labels:
  - "traefik.http.services.hub.loadbalancer.healthcheck.interval=30s"
  - "traefik.http.services.hub.loadbalancer.healthcheck.timeout=10s"
```

---

## Certificate Issues (Production)

### Symptoms
- HTTPS not working: "Your connection is not private"
- Let's Encrypt rate limit errors

### Solutions

**1. Use staging first:**
```bash
AGENIX_TRAEFIK_ACME_STAGING=true docker compose --profile traefik up
```

**2. Check certificate storage:**
```bash
cat traefik/acme.json | jq
chmod 600 traefik/acme.json  # Must be 600
```

**3. Verify DNS points to server:**
```bash
dig hub.your-domain.com
nslookup hub.your-domain.com
```

**4. Check firewall allows port 80 (HTTP challenge):**
```bash
curl -v http://your-domain.com/.well-known/acme-challenge/test
```

---

## Load Balancing Not Working

### Symptoms
- All requests go to same ingestion replica
- Sticky sessions not working

### Solutions

**1. Verify multiple replicas running:**
```bash
docker compose ps ingestion
```

**2. Check load balancer configuration:**
```bash
curl http://localhost:8080/api/http/services/ingestion@docker | jq '.loadBalancer'
```

**3. Test without sticky sessions:**
```bash
# Remove cookies between requests
for i in {1..10}; do
  curl -s --cookie-jar - http://ingestion.localhost/health | jq -r '.instance'
done
```

---

## Logs and Debugging

### Enable Debug Logging

Edit `traefik/traefik.yml`:
```yaml
log:
  level: DEBUG
```

### View Traefik Logs

```bash
# Follow logs
docker compose logs -f traefik

# JSON formatted
docker compose logs --no-log-prefix traefik | jq

# Filter by service
docker compose logs traefik | grep "router=hub"
```

### Check Access Logs

```bash
# View recent requests
docker compose exec traefik tail -f /var/log/traefik/access.log

# Parse JSON logs
docker compose exec traefik cat /var/log/traefik/access.log | jq
```

---

## Performance Issues

### High Latency

**1. Check Traefik metrics:**
```bash
curl http://localhost:8080/metrics | grep traefik_service_request_duration
```

**2. Enable response caching:**
```yaml
http:
  middlewares:
    cache:
      plugin:
        souin:
          default_cache:
            ttl: 300s
```

**3. Adjust timeouts:**
```yaml
labels:
  - "traefik.http.routers.hub.middlewares=timeouts"
```

### Memory Usage

**1. Monitor container stats:**
```bash
docker stats traefik
```

**2. Limit access log buffer:**
```yaml
accessLog:
  bufferingSize: 10  # Reduce from 100
```

---

## Common Error Messages

| Error | Cause | Solution |
|-------|-------|----------|
| `404 page not found` | Router not registered | Check `traefik.expose=true` label |
| `503 Service Unavailable` | Backend unhealthy | Check health endpoint |
| `502 Bad Gateway` | Backend not responding | Verify service is running |
| `429 Too Many Requests` | Rate limit exceeded | Adjust rate limit middleware |
| `ERR_SSL_PROTOCOL_ERROR` | TLS misconfiguration | Check certificate resolver |

---

## Getting Help

1. Check [Configuration Guide](configuration.md)
2. Review [Local Development Guide](local-development.md)
3. Consult [Traefik Official Docs](https://doc.traefik.io/traefik/)
4. Open issue in project repository
