# Phase 2: Heterogeneous Worker Pools

## Overview

Extend Phase 1 to support multiple worker types with different browser configurations (Chromium, Firefox, WebKit). This enables running different browser types in separate worker pools while maintaining the dynamic registration benefits of Phase 1.

## Use Cases

### Use Case 1: Multi-Browser Testing
```
┌─────────────────┐
│ Test Suite      │
├─────────────────┤
│ • Login tests   │───► Chromium workers (fast, stable)
│ • API tests     │
│ • E2E flows     │───► Firefox workers (strict standards)
│ • Visual tests  │───► WebKit workers (iOS/Safari)
└─────────────────┘
```

### Use Case 2: Performance Optimization
```
Chromium Pool: 10 workers (high demand)
Firefox Pool:   3 workers (medium demand)
WebKit Pool:    2 workers (low demand, expensive)
```

### Use Case 3: Environment Segregation
```
Staging Environment:
  - Chromium workers only (cost-effective)

Production Environment:
  - Chromium workers (primary)
  - Firefox workers (compatibility)
  - WebKit workers (iOS testing)
```

## Target State

### Architecture

```yaml
# YAML anchors for shared configuration
x-worker-common: &worker-common
  build:
    context: .
    dockerfile: worker/Dockerfile
    args:
      PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
  dns:
    - 1.1.1.1
    - 8.8.8.8
  environment: &worker-env-common
    - REDIS_URL=redis:6379
    - AGENIX_HUB_URL=http://hub:5000
    - AGENIX_WORKER_NODE_SECRET=${AGENIX_WORKER_NODE_SECRET:-node-secret}
    - AGENIX_WORKER_NODE_NODE_SECRET=${AGENIX_WORKER_NODE_NODE_SECRET:-node-node-secret}
    - AGENIX_WORKER_PUBLIC_WS_HOST=${AGENIX_WORKER_PUBLIC_WS_HOST:-127.0.0.1}
    - PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION:-1.54.2}
  depends_on:
    redis:
      condition: service_healthy
    hub:
      condition: service_healthy
  shm_size: "1gb"
  restart: unless-stopped
  profiles:
    - core

services:
  # Chromium workers
  worker-chromium:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_CHROMIUM_REPLICAS:-5}
    ports:
      - "5200-5299:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_CHROMIUM_POOL_CONFIG:-AppB:Chromium:UAT=3}
      - AGENIX_WORKER_PUBLIC_WS_PORT=5200
      - AGENIX_WORKER_CHROMIUM_ARGS=${AGENIX_WORKER_CHROMIUM_ARGS:---disable-dev-shm-usage --no-sandbox}

  # Firefox workers
  worker-firefox:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_FIREFOX_REPLICAS:-3}
    ports:
      - "5300-5399:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_FIREFOX_POOL_CONFIG:-AppB:Firefox:UAT=2}
      - AGENIX_WORKER_PUBLIC_WS_PORT=5300
      - AGENIX_WORKER_FIREFOX_ARGS=${AGENIX_WORKER_FIREFOX_ARGS:---no-sandbox}

  # WebKit workers
  worker-webkit:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_WEBKIT_REPLICAS:-2}
    ports:
      - "5400-5499:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_WEBKIT_POOL_CONFIG:-AppB:Webkit:UAT=2}
      - AGENIX_WORKER_PUBLIC_WS_PORT=5400
      - AGENIX_WORKER_WEBKIT_ARGS=${AGENIX_WORKER_WEBKIT_ARGS:---no-sandbox}
```

## Configuration

### .env File

```bash
# Chromium Workers
WORKER_CHROMIUM_REPLICAS=5
AGENIX_WORKER_CHROMIUM_POOL_CONFIG=AppB:Chromium:UAT=3
AGENIX_WORKER_CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox

# Firefox Workers
WORKER_FIREFOX_REPLICAS=3
AGENIX_WORKER_FIREFOX_POOL_CONFIG=AppB:Firefox:UAT=2
AGENIX_WORKER_FIREFOX_ARGS=--no-sandbox

# WebKit Workers
WORKER_WEBKIT_REPLICAS=2
AGENIX_WORKER_WEBKIT_POOL_CONFIG=AppB:Webkit:UAT=2
AGENIX_WORKER_WEBKIT_ARGS=--no-sandbox

# Common Worker Configuration
AGENIX_WORKER_NODE_SECRET=node-secret
AGENIX_WORKER_NODE_NODE_SECRET=node-node-secret
AGENIX_WORKER_PUBLIC_WS_HOST=127.0.0.1
PLAYWRIGHT_VERSION=1.54.2
```

## Worker Naming Convention

Docker Compose generates container names based on service name:

```
Service: worker-chromium
Replicas: 5

Containers:
  agenix-reportportal-worker-chromium-1
  agenix-reportportal-worker-chromium-2
  agenix-reportportal-worker-chromium-3
  agenix-reportportal-worker-chromium-4
  agenix-reportportal-worker-chromium-5

NodeIds (registered in Hub):
  worker-chromium-1
  worker-chromium-2
  worker-chromium-3
  worker-chromium-4
  worker-chromium-5
```

## Port Allocation Strategy

| Worker Type | Port Range | Max Replicas | Purpose |
|-------------|-----------|--------------|---------|
| Chromium | 5200-5299 | 100 | Primary browser (high demand) |
| Firefox | 5300-5399 | 100 | Standards compliance testing |
| WebKit | 5400-5499 | 100 | iOS/Safari testing |
| **Total** | **5200-5499** | **300** | **All browser types** |

## Implementation Steps

### Step 1: Update .env File

Add browser-specific configuration:

```bash
cat >> .env << 'EOF'

# === Phase 2: Heterogeneous Worker Pools ===

# Chromium Workers (Primary)
WORKER_CHROMIUM_REPLICAS=5
AGENIX_WORKER_CHROMIUM_POOL_CONFIG=AppB:Chromium:UAT=3
AGENIX_WORKER_CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox --no-proxy-server

# Firefox Workers (Standards)
WORKER_FIREFOX_REPLICAS=3
AGENIX_WORKER_FIREFOX_POOL_CONFIG=AppB:Firefox:UAT=2
AGENIX_WORKER_FIREFOX_ARGS=--no-sandbox

# WebKit Workers (iOS/Safari)
WORKER_WEBKIT_REPLICAS=2
AGENIX_WORKER_WEBKIT_POOL_CONFIG=AppB:Webkit:UAT=2
AGENIX_WORKER_WEBKIT_ARGS=--no-sandbox

EOF
```

### Step 2: Update docker-compose.yml

Replace single `worker` service with three browser-specific services:

```yaml
services:
  # === Phase 2: Heterogeneous Worker Pools ===

  worker-chromium:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_CHROMIUM_REPLICAS:-5}
    ports:
      - "5200-5299:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_CHROMIUM_POOL_CONFIG:-AppB:Chromium:UAT=3}
      - AGENIX_WORKER_PUBLIC_WS_PORT=5200
      - AGENIX_WORKER_CHROMIUM_ARGS=${AGENIX_WORKER_CHROMIUM_ARGS:---disable-dev-shm-usage --no-sandbox}

  worker-firefox:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_FIREFOX_REPLICAS:-3}
    ports:
      - "5300-5399:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_FIREFOX_POOL_CONFIG:-AppB:Firefox:UAT=2}
      - AGENIX_WORKER_PUBLIC_WS_PORT=5300
      - AGENIX_WORKER_FIREFOX_ARGS=${AGENIX_WORKER_FIREFOX_ARGS:---no-sandbox}

  worker-webkit:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_WEBKIT_REPLICAS:-2}
    ports:
      - "5400-5499:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_WEBKIT_POOL_CONFIG:-AppB:Webkit:UAT=2}
      - AGENIX_WORKER_PUBLIC_WS_PORT=5400
      - AGENIX_WORKER_WEBKIT_ARGS=${AGENIX_WORKER_WEBKIT_ARGS:---no-sandbox}
```

### Step 3: Deploy

```bash
# Stop Phase 1 workers
docker-compose stop worker
docker-compose rm -f worker

# Start Phase 2 workers
docker-compose up -d worker-chromium worker-firefox worker-webkit

# Verify
docker ps | grep worker

# Expected output:
# agenix-reportportal-worker-chromium-1
# agenix-reportportal-worker-chromium-2
# ... (5 total)
# agenix-reportportal-worker-firefox-1
# agenix-reportportal-worker-firefox-2
# ... (3 total)
# agenix-reportportal-worker-webkit-1
# agenix-reportportal-worker-webkit-2
# ... (2 total)
```

### Step 4: Verify Registration

```bash
# Check Hub registration
docker exec -it agenix-reportportal-redis redis-cli KEYS node:*

# Expected output:
# 1) "node:worker-chromium-1"
# 2) "node:worker-chromium-2"
# 3) "node:worker-chromium-3"
# 4) "node:worker-chromium-4"
# 5) "node:worker-chromium-5"
# 6) "node:worker-firefox-1"
# 7) "node:worker-firefox-2"
# 8) "node:worker-firefox-3"
# 9) "node:worker-webkit-1"
# 10) "node:worker-webkit-2"

# Verify pool configurations
docker exec redis redis-cli HGET node:worker-chromium-1 poolConfig
# Expected: {"AppB:Chromium:UAT":3}

docker exec redis redis-cli HGET node:worker-firefox-1 poolConfig
# Expected: {"AppB:Firefox:UAT":2}

docker exec redis redis-cli HGET node:worker-webkit-1 poolConfig
# Expected: {"AppB:Webkit:UAT":2}
```

## Scaling Operations

### Scale Individual Browser Types

```bash
# Scale Chromium workers to 10
docker-compose up --scale worker-chromium=10 -d

# Scale Firefox workers to 5
docker-compose up --scale worker-firefox=5 -d

# Scale WebKit workers to 0 (disable)
docker-compose stop worker-webkit

# Verify
docker ps | grep worker-chromium | wc -l  # 10
docker ps | grep worker-firefox | wc -l   # 5
docker ps | grep worker-webkit | wc -l    # 0
```

### Scale All Workers

```bash
# Scale all browser types simultaneously
docker-compose up \
  --scale worker-chromium=15 \
  --scale worker-firefox=8 \
  --scale worker-webkit=5 \
  -d
```

### Cost Optimization Example

```bash
# Development environment (cost-effective)
docker-compose up \
  --scale worker-chromium=2 \
  --scale worker-firefox=1 \
  --scale worker-webkit=0 \
  -d

# Staging environment (balanced)
docker-compose up \
  --scale worker-chromium=5 \
  --scale worker-firefox=3 \
  --scale worker-webkit=2 \
  -d

# Production environment (high availability)
docker-compose up \
  --scale worker-chromium=20 \
  --scale worker-firefox=10 \
  --scale worker-webkit=5 \
  -d
```

## Testing Procedures

### Test 1: Multi-Browser Registration

**Steps**:
1. Deploy all worker types: `docker-compose up -d worker-chromium worker-firefox worker-webkit`
2. Wait 10 seconds
3. Check Redis: `docker exec redis redis-cli KEYS node:*`

**Expected**: 10 workers registered (5 Chromium, 3 Firefox, 2 WebKit)

### Test 2: Browser-Specific Pool Configuration

**Steps**:
1. Check Chromium pool: `docker exec redis redis-cli HGET node:worker-chromium-1 poolConfig`
2. Check Firefox pool: `docker exec redis redis-cli HGET node:worker-firefox-1 poolConfig`
3. Check WebKit pool: `docker exec redis redis-cli HGET node:worker-webkit-1 poolConfig`

**Expected**: Each worker has correct browser type in pool config

### Test 3: Independent Scaling

**Steps**:
1. Scale Chromium to 10: `docker-compose up --scale worker-chromium=10 -d`
2. Verify Firefox count unchanged: `docker ps | grep firefox | wc -l`
3. Verify WebKit count unchanged: `docker ps | grep webkit | wc -l`

**Expected**: Only Chromium workers scaled, others unchanged

### Test 4: Port Allocation

**Steps**:
1. Deploy all workers
2. Check Chromium ports: `docker ps | grep chromium | awk '{print $NF}' | grep -o '[0-9]*'`
3. Check Firefox ports: `docker ps | grep firefox | awk '{print $NF}' | grep -o '[0-9]*'`
4. Check WebKit ports: `docker ps | grep webkit | awk '{print $NF}' | grep -o '[0-9]*'`

**Expected**:
- Chromium: 5200-5204
- Firefox: 5300-5302
- WebKit: 5400-5401

### Test 5: Browser Test Execution

**Steps**:
1. Run Chromium test: `curl -X POST http://localhost:5000/api/test-runs -d '{"labelKey":"AppB:Chromium:UAT"}'`
2. Run Firefox test: `curl -X POST http://localhost:5000/api/test-runs -d '{"labelKey":"AppB:Firefox:UAT"}'`
3. Run WebKit test: `curl -X POST http://localhost:5000/api/test-runs -d '{"labelKey":"AppB:Webkit:UAT"}'`

**Expected**: Each test executes on corresponding worker type

### Test 6: Worker Isolation

**Steps**:
1. Stop Chromium workers: `docker-compose stop worker-chromium`
2. Verify Firefox workers still running: `docker ps | grep firefox`
3. Verify WebKit workers still running: `docker ps | grep webkit`

**Expected**: Firefox and WebKit workers unaffected by Chromium shutdown

## Migration from Phase 1

### Backward Compatibility

Phase 1 configuration remains valid:
```yaml
# Phase 1 (still works)
worker:
  <<: *worker-common
  deploy:
    replicas: 3
```

### Migration Path

**Option 1: Gradual Migration**
1. Keep Phase 1 worker service for Chromium tests
2. Add worker-firefox service for new Firefox tests
3. Add worker-webkit service when needed
4. Remove Phase 1 worker service when all tests migrated

**Option 2: Full Migration**
1. Stop Phase 1 worker: `docker-compose stop worker`
2. Deploy Phase 2 workers: `docker-compose up -d worker-chromium worker-firefox worker-webkit`
3. Update test suite to use browser-specific labels
4. Remove Phase 1 worker from docker-compose.yml

## Benefits Over Phase 1

| Feature | Phase 1 | Phase 2 | Improvement |
|---------|---------|---------|-------------|
| Browser Types | Single (homogeneous) | Multiple (heterogeneous) | ✅ Multi-browser support |
| Scaling Granularity | All workers together | Per-browser type | ✅ Fine-grained control |
| Cost Optimization | Limited | High (scale per demand) | ✅ Cost-effective |
| Resource Allocation | Equal per worker | Per-browser configuration | ✅ Optimized allocation |
| Port Management | Single range (5200-5299) | Multiple ranges (5200-5499) | ✅ Organized port layout |
| Worker Identification | Generic (worker-1) | Descriptive (worker-chromium-1) | ✅ Clear identification |

## Known Limitations

1. **Configuration Verbosity**: Three services instead of one
   - **Mitigation**: YAML anchors reduce duplication to ~30 lines per browser

2. **Port Range Constraints**: 100 workers per browser type
   - **Mitigation**: Increase port range if needed (e.g., 5200-5999)

3. **Environment Variable Count**: Multiple browser-specific variables
   - **Mitigation**: Use .env file for centralized management

4. **No Dynamic Browser Type Addition**: Adding new browser requires docker-compose.yml edit
   - **Mitigation**: Pre-define common browsers (Chromium, Firefox, WebKit)

## Rollback to Phase 1

If issues occur, revert to single worker service:

```bash
# Stop Phase 2 workers
docker-compose stop worker-chromium worker-firefox worker-webkit

# Restore Phase 1 configuration
git checkout docker-compose.yml

# Start Phase 1 worker
docker-compose up -d worker
```

## Next Steps

1. **Deploy Phase 2**: Implement heterogeneous worker pools
2. **Test Multi-Browser**: Run comprehensive test suite across all browsers
3. **Monitor Resource Usage**: Track CPU/memory per browser type
4. **Optimize Scaling**: Adjust replica counts based on demand
5. **Plan Phase 3**: Design fully decoupled worker deployment

## Related Documentation

- [README: Dynamic Worker Registration](./README.md)
- [Phase 1: Single Worker Service](./phase-1-single-worker-service.md)
- [Phase 3: Decoupled Deployment](./phase-3-decoupled-deployment.md)
- [Worker Pool Configuration Guide](../../docs/worker-pool-configuration.md)
