# Production Deployment with Traefik

**HTTPS, security, and performance tuning for production environments**

---

## Overview

This guide covers production deployment with:
- Automatic HTTPS via Let's Encrypt
- Security middleware and headers
- Rate limiting and DDoS protection
- Performance optimization
- Monitoring and alerts

---

## HTTPS with Let's Encrypt

### 1. Configure Certificate Resolver

Edit `traefik/traefik.yml`:

```yaml
certificateResolvers:
  letsencrypt:
    acme:
      email: "admin@your-domain.com"
      storage: "/letsencrypt/acme.json"
      httpChallenge:
        entryPoint: web

  # Staging (for testing)
  letsencrypt-staging:
    acme:
      email: "admin@your-domain.com"
      storage: "/letsencrypt/acme-staging.json"
      caServer: "https://acme-staging-v02.api.letsencrypt.org/directory"
      httpChallenge:
        entryPoint: web
```

### 2. Update Service Labels

```yaml
hub:
  labels:
    # HTTPS router
    - "traefik.http.routers.hub-secure.rule=Host(`hub.your-domain.com`)"
    - "traefik.http.routers.hub-secure.entrypoints=websecure"
    - "traefik.http.routers.hub-secure.tls=true"
    - "traefik.http.routers.hub-secure.tls.certresolver=letsencrypt"

    # HTTP → HTTPS redirect
    - "traefik.http.routers.hub.rule=Host(`hub.your-domain.com`)"
    - "traefik.http.routers.hub.entrypoints=web"
    - "traefik.http.routers.hub.middlewares=redirect-to-https"
    - "traefik.http.middlewares.redirect-to-https.redirectscheme.scheme=https"
```

### 3. Create acme.json

```bash
# Create certificate storage file
mkdir -p traefik
touch traefik/acme.json
chmod 600 traefik/acme.json
```

### 4. Update .env

```bash
AGENIX_TRAEFIK_ACME_EMAIL=admin@your-domain.com
AGENIX_TRAEFIK_ACME_STAGING=false  # Use production
```

---

## Security

### 1. Security Headers Middleware

`traefik/dynamic/middleware.yml`:

```yaml
http:
  middlewares:
    security-headers:
      headers:
        # HTTPS enforcement
        sslRedirect: true
        stsSeconds: 31536000
        stsIncludeSubdomains: true
        stsPreload: true
        forceSTSHeader: true

        # Security headers
        browserXssFilter: true
        contentTypeNosniff: true
        frameDeny: true
        customFrameOptionsValue: "SAMEORIGIN"

        # CSP
        contentSecurityPolicy: "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'"

        # Referrer policy
        referrerPolicy: "strict-origin-when-cross-origin"

        # Permissions policy
        permissionsPolicy: "geolocation=(), microphone=(), camera=()"
```

### 2. Rate Limiting

```yaml
http:
  middlewares:
    rate-limit:
      rateLimit:
        average: 100  # Requests per second
        burst: 50     # Burst allowance
        period: "1s"

    api-rate-limit:
      rateLimit:
        average: 10
        burst: 20
        period: "1s"
```

Apply to services:

```yaml
labels:
  - "traefik.http.routers.hub.middlewares=rate-limit,security-headers"
```

### 3. IP Whitelist (Admin Endpoints)

```yaml
http:
  middlewares:
    admin-whitelist:
      ipWhiteList:
        sourceRange:
          - "192.168.1.0/24"   # Office network
          - "10.0.0.0/8"        # VPN
```

### 4. Authentication

```yaml
http:
  middlewares:
    admin-auth:
      basicAuth:
        users:
          # Generate with: htpasswd -nb admin password
          - "admin:$apr1$H6uskkkW$IgXLP6ewTrSuBkTrqE8wj/"
```

---

## Performance Optimization

### 1. Compression

```yaml
http:
  middlewares:
    compression:
      compress:
        excludedContentTypes:
          - "text/event-stream"
          - "application/grpc"
```

### 2. Connection Limits

```yaml
labels:
  - "traefik.http.services.hub.loadbalancer.responseForwarding.flushInterval=100ms"
  - "traefik.http.services.hub.loadbalancer.passhostheader=true"
```

### 3. Timeouts

```yaml
http:
  middlewares:
    timeouts:
      timeout:
        readTimeout: "60s"
        writeTimeout: "60s"
        idleTimeout: "180s"
```

---

## Monitoring

### 1. Prometheus Metrics

Traefik exposes metrics on `/metrics` endpoint:

```yaml
# prometheus/prometheus.yml
scrape_configs:
  - job_name: 'traefik'
    static_configs:
      - targets: ['traefik:8080']
```

### 2. Access Logs

Enable structured logging:

```yaml
accessLog:
  format: json
  filePath: "/var/log/traefik/access.log"
  bufferingSize: 100
```

### 3. Health Checks

```bash
# Traefik health
curl http://localhost:8080/ping

# Service health
curl http://localhost:8080/api/http/services
```

---

## Deployment Checklist

- [ ] Domain DNS points to server IP
- [ ] Ports 80/443 open in firewall
- [ ] Let's Encrypt email configured
- [ ] Test with staging first (`AGENIX_TRAEFIK_ACME_STAGING=true`)
- [ ] Security headers enabled
- [ ] Rate limiting configured
- [ ] Access logs enabled
- [ ] Prometheus metrics configured
- [ ] Health check monitoring setup
- [ ] Backup `acme.json` file
- [ ] Test HTTP → HTTPS redirect
- [ ] Verify certificate renewal (after 60 days)

---

## Troubleshooting

### Certificate Issues

```bash
# Check certificate status
docker compose exec traefik cat /letsencrypt/acme.json | jq

# Test with Let's Encrypt staging first
AGENIX_TRAEFIK_ACME_STAGING=true docker compose --profile infrastructure --profile core up

# Force certificate renewal
rm traefik/acme.json
docker compose restart traefik
```

### Rate Limit Testing

```bash
# Test rate limit
for i in {1..200}; do curl http://hub.your-domain.com/health; done

# Should see 429 (Too Many Requests) after threshold
```

---

## Next Steps

- [Migration Guide](migration-guide.md) - Migrate existing deployment
- [Troubleshooting](troubleshooting.md) - Problem solving
- [Configuration Reference](configuration.md) - Advanced configuration
