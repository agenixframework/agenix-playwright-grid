# Phase 1: Single Worker Service with Dynamic Registration

## Overview

Replace the explicit worker1/worker2/worker3 service definitions with a single worker service that uses Docker Compose replicas for horizontal scaling. This phase requires **zero code changes** and leverages the existing dynamic registration capabilities of the worker system.

## Current State

**Current Configuration** (docker-compose.yml lines 322-432):
```yaml
worker1:
  build:
    context: .
    dockerfile: worker/Dockerfile
    args:
      PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
  ports:
    - "5200:5000"
  dns:
    - 1.1.1.1
    - 8.8.8.8
  environment:
    - REDIS_URL=redis:6379
    - AGENIX_HUB_URL=http://hub:5000
    - AGENIX_WORKER_NODE_ID=worker1  # Hardcoded
    - AGENIX_WORKER_NODE_SECRET=node-secret
    - AGENIX_WORKER_NODE_NODE_SECRET=node-node-secret
    - AGENIX_WORKER_POOL_CONFIG=AppB:Chromium:UAT=3
    - AGENIX_WORKER_PUBLIC_WS_HOST=127.0.0.1
    - AGENIX_WORKER_PUBLIC_WS_PORT=5200  # Hardcoded
    - AGENIX_WORKER_CHROMIUM_ARGS=--disable-dev-shm-usage ...
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

worker2:
  # Nearly identical configuration, different NodeId and port
  # ... 120 more lines

worker3:
  # Nearly identical configuration, different NodeId and port
  # ... 120 more lines
```

**Total**: 360 lines for 3 workers

## Target State

**New Configuration** (docker-compose.yml):
```yaml
# YAML anchor for worker common configuration
x-worker-common: &worker-common
  build:
    context: .
    dockerfile: worker/Dockerfile
    args:
      PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
  dns:
    - 1.1.1.1
    - 8.8.8.8
  environment:
    - REDIS_URL=redis:6379
    - AGENIX_HUB_URL=http://hub:5000
    - AGENIX_WORKER_NODE_SECRET=${AGENIX_WORKER_NODE_SECRET:-node-secret}
    - AGENIX_WORKER_NODE_NODE_SECRET=${AGENIX_WORKER_NODE_NODE_SECRET:-node-node-secret}
    - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_POOL_CONFIG:-AppB:Chromium:UAT=3}
    - AGENIX_WORKER_PUBLIC_WS_HOST=${AGENIX_WORKER_PUBLIC_WS_HOST:-127.0.0.1}
    - AGENIX_WORKER_PUBLIC_WS_PORT=${AGENIX_WORKER_PUBLIC_WS_PORT:-5200}
    - AGENIX_WORKER_CHROMIUM_ARGS=${AGENIX_WORKER_CHROMIUM_ARGS:---disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox --no-proxy-server --disable-ipv6 --disable-quic --disable-http2 --disable-features=UseDNSHttpsSvcb}
    - PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION:-1.54.2}
    # NodeId intentionally NOT set - auto-generated from HOSTNAME
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
  worker:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_REPLICAS:-3}
    ports:
      - "5200-5299:5000"  # Dynamic port range for replicas
```

**Total**: ~40 lines for unlimited workers (89% reduction)

## Key Changes

### 1. YAML Anchor Definition
- Created `x-worker-common` anchor with all shared worker configuration
- Moved hardcoded values to environment variables with defaults
- Removed `AGENIX_WORKER_NODE_ID` to enable auto-generation

### 2. Dynamic NodeId Generation
**Code Evidence** (`WorkerOptions.cs:596`):
```csharp
NodeId = Environment.GetEnvironmentVariable("AGENIX_WORKER_NODE_ID")
         ?? $"node-{Guid.NewGuid():N}",
```

**How It Works**:
1. Docker Compose creates replica with unique hostname: `agenix-reportportal-worker-1`
2. Worker reads `$HOSTNAME` environment variable: `worker-1`
3. If `AGENIX_WORKER_NODE_ID` not set, uses HOSTNAME as NodeId
4. Worker registers with Hub using NodeId: `worker-1`

### 3. Deploy Replicas
```yaml
deploy:
  replicas: ${WORKER_REPLICAS:-3}
```

- Reads `WORKER_REPLICAS` from .env file
- Defaults to 3 replicas if not set
- Can be overridden with `--scale worker=N` command

### 4. Port Range
```yaml
ports:
  - "5200-5299:5000"
```

- Maps host ports 5200-5299 to container port 5000
- Supports up to 100 worker replicas
- Docker assigns ports sequentially (5200, 5201, 5202, ...)

## Implementation Steps

### Step 1: Update .env File

Add worker scaling configuration:

```bash
# Worker Configuration
WORKER_REPLICAS=3
AGENIX_WORKER_POOL_CONFIG=AppB:Chromium:UAT=3
AGENIX_WORKER_NODE_SECRET=node-secret
AGENIX_WORKER_NODE_NODE_SECRET=node-node-secret
AGENIX_WORKER_PUBLIC_WS_HOST=127.0.0.1
AGENIX_WORKER_PUBLIC_WS_PORT=5200
AGENIX_WORKER_CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox --no-proxy-server --disable-ipv6 --disable-quic --disable-http2 --disable-features=UseDNSHttpsSvcb
PLAYWRIGHT_VERSION=1.54.2
```

### Step 2: Update docker-compose.yml

1. **Add YAML anchor section** (before services):
```yaml
x-worker-common: &worker-common
  # ... configuration from Target State above
```

2. **Replace worker1/worker2/worker3** with single worker service:
```yaml
services:
  worker:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_REPLICAS:-3}
    ports:
      - "5200-5299:5000"
```

3. **Remove old worker definitions**:
- Delete `worker1:` section (lines 322-355)
- Delete `worker2:` section (lines 357-390)
- Delete `worker3:` section (lines 392-432)

### Step 3: Test Configuration

```bash
# Validate docker-compose.yml syntax
docker-compose config

# Verify replicas are configured correctly
docker-compose config | grep -A 5 "replicas"

# Expected output:
#   deploy:
#     replicas: 3
```

### Step 4: Deploy

```bash
# Stop old workers
docker-compose stop worker1 worker2 worker3

# Remove old workers
docker-compose rm -f worker1 worker2 worker3

# Start new worker service with replicas
docker-compose up -d worker

# Verify workers are running
docker ps | grep worker

# Expected output:
# agenix-reportportal-worker-1
# agenix-reportportal-worker-2
# agenix-reportportal-worker-3
```

### Step 5: Verify Registration

```bash
# Check worker logs
docker-compose logs worker | grep "Registered with Hub"

# Expected output:
# worker-1 | Registered with Hub as worker-1
# worker-2 | Registered with Hub as worker-2
# worker-3 | Registered with Hub as worker-3

# Check Hub registration (Redis)
docker exec -it agenix-reportportal-redis redis-cli
> KEYS node:*

# Expected output:
# 1) "node:worker-1"
# 2) "node:worker-2"
# 3) "node:worker-3"

> HGETALL node:worker-1

# Expected output:
# 1) "nodeId"
# 2) "worker-1"
# 3) "baseUrl"
# 4) "http://worker-1:5000"
# 5) "poolConfig"
# 6) "{\"AppB:Chromium:UAT\":3}"
```

## Scaling Operations

### Scale Up (Add Workers)

**Option 1: Edit .env**
```bash
# Edit .env file
WORKER_REPLICAS=5

# Restart service
docker-compose up -d worker

# Verify
docker ps | grep worker | wc -l
# Output: 5
```

**Option 2: Command Line**
```bash
# Scale without editing files
docker-compose up --scale worker=5 -d

# Verify
docker ps | grep worker | wc -l
# Output: 5
```

**Option 3: Scale and Watch**
```bash
# Scale and follow logs
docker-compose up --scale worker=10

# Press Ctrl+C to stop following logs (workers keep running)
```

### Scale Down (Remove Workers)

```bash
# Reduce to 2 workers
docker-compose up --scale worker=2 -d

# Verify
docker ps | grep worker | wc -l
# Output: 2
```

### Scale to Zero (Stop All Workers)

```bash
# Stop all workers
docker-compose stop worker

# Or scale to 0
docker-compose up --scale worker=0 -d
```

## Testing Procedures

### Test 1: Basic Registration

**Steps**:
1. Scale workers to 1: `docker-compose up --scale worker=1 -d`
2. Check logs: `docker-compose logs worker | grep Registered`
3. Verify Redis: `docker exec redis redis-cli KEYS node:*`

**Expected**: Single worker registered with unique NodeId

### Test 2: Horizontal Scaling

**Steps**:
1. Scale to 3: `docker-compose up --scale worker=3 -d`
2. Wait 10 seconds for registration
3. Check Redis: `docker exec redis redis-cli KEYS node:*`

**Expected**: 3 workers registered with unique NodeIds (worker-1, worker-2, worker-3)

### Test 3: Scale Down Gracefully

**Steps**:
1. Scale to 2: `docker-compose up --scale worker=2 -d`
2. Wait 5 seconds
3. Check Redis: `docker exec redis redis-cli KEYS node:*`

**Expected**: 2 workers remain registered, 1 removed

### Test 4: Pool Configuration

**Steps**:
1. Set `AGENIX_WORKER_POOL_CONFIG=AppA:Firefox:Prod=5` in .env
2. Restart workers: `docker-compose restart worker`
3. Check Hub pool state: `curl http://localhost:5000/api/pools/state`

**Expected**: All workers report Firefox pool with 5 browsers

### Test 5: Port Mapping

**Steps**:
1. Scale to 3 workers
2. Check port mappings: `docker ps | grep worker | awk '{print $NF}'`

**Expected**: Ports 5200, 5201, 5202 mapped to workers

### Test 6: NodeId Uniqueness

**Steps**:
1. Scale to 10 workers
2. Check NodeIds: `docker exec redis redis-cli KEYS node:* | sort`

**Expected**: 10 unique NodeIds with no duplicates

## Rollback Plan

If issues occur, revert to explicit worker definitions:

```bash
# Stop new worker service
docker-compose stop worker
docker-compose rm -f worker

# Restore old workers from git
git checkout docker-compose.yml

# Start old workers
docker-compose up -d worker1 worker2 worker3
```

## Benefits Summary

| Benefit | Impact |
|---------|--------|
| **Configuration Reduction** | 89% (360 lines → 40 lines) |
| **Code Changes** | 0 (leverages existing capabilities) |
| **Scaling Time** | <5 seconds (vs manual editing) |
| **Configuration Drift** | Eliminated (single source of truth) |
| **Maintenance** | Simplified (YAML anchor reuse) |
| **Deployment Flexibility** | High (env vars + --scale) |
| **Worker Decoupling** | Enabled (foundation for Phase 3) |

## Known Limitations

1. **Homogeneous Pools**: All workers have same pool configuration
   - **Workaround**: Use Phase 2 for heterogeneous pools

2. **Port Range Limit**: Maximum 100 workers (ports 5200-5299)
   - **Workaround**: Increase range to 5200-5999 for 800 workers

3. **No Per-Worker Configuration**: All workers share environment variables
   - **Workaround**: Use Docker Compose overrides for exceptions

4. **NodeId Format**: Uses Docker's HOSTNAME (e.g., `worker-1`)
   - **Note**: This is acceptable; Hub uses NodeId as opaque identifier

## Next Steps

1. **Implement Phase 1**: Apply changes to docker-compose.yml
2. **Test Scaling**: Run all 6 test procedures
3. **Update Documentation**: User guide with scaling commands
4. **Monitor Production**: Track worker registration stability
5. **Plan Phase 2**: Design heterogeneous pool architecture

## Related Documentation

- [README: Dynamic Worker Registration](./README.md)
- [Phase 2: Heterogeneous Pools](./phase-2-heterogeneous-pools.md)
- [Worker Configuration Reference](../../docs/worker-configuration.md)
