# Phase 3: Decoupled Worker Deployment

## Overview

Fully decouple worker deployment from core infrastructure by creating a separate `docker-compose.workers.yml` file. This enables workers to be deployed independently, scaled independently, and managed as optional components that can run on separate hosts or orchestrators.

## Strategic Goals

1. **Infrastructure Independence**: Core services (Hub, Redis, PostgreSQL) run independently
2. **Elastic Worker Scaling**: Add/remove workers without affecting core services
3. **Multi-Host Deployment**: Deploy workers on separate Docker hosts for load distribution
4. **Cost Optimization**: Run workers on-demand (scale to zero when idle)
5. **Simplified Maintenance**: Core infrastructure changes don't require worker restarts
6. **Cloud-Native Architecture**: Workers become stateless, disposable compute units

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│ docker-compose.yml (Core Infrastructure)                     │
├──────────────────────────────────────────────────────────────┤
│ ┌─────────┐  ┌──────────┐  ┌────────┐  ┌──────────┐        │
│ │ Gateway │  │   Hub    │  │ Redis  │  │ Postgres │        │
│ └─────────┘  └──────────┘  └────────┘  └──────────┘        │
│                    ▲                                         │
│                    │ Registration API                        │
└────────────────────┼─────────────────────────────────────────┘
                     │
                     │ http://hub.example.com/register
                     │
┌────────────────────┼─────────────────────────────────────────┐
│ docker-compose.workers.yml (Worker Pool)                     │
├────────────────────┼─────────────────────────────────────────┤
│                    │                                         │
│  ┌─────────────────┴────────────────┐                       │
│  │     External Network              │                       │
│  │  agenix-reportportal-network     │                       │
│  └─────────────────┬────────────────┘                       │
│                    │                                         │
│  ┌─────────────────┴────────────────┐                       │
│  │  Worker Services (Replicas)      │                       │
│  ├──────────────────────────────────┤                       │
│  │ worker-chromium (replicas: N)    │                       │
│  │ worker-firefox  (replicas: M)    │                       │
│  │ worker-webkit   (replicas: K)    │                       │
│  └──────────────────────────────────┘                       │
│                                                              │
│  Optional: Can run on different Docker host                 │
└──────────────────────────────────────────────────────────────┘
```

## Target State

### File Structure

```
/project-root/
├── docker-compose.yml          # Core infrastructure (Hub, Redis, Postgres)
├── docker-compose.workers.yml  # Worker pools (Chromium, Firefox, WebKit)
├── .env                        # Shared configuration
├── .env.workers                # Worker-specific overrides (optional)
└── scripts/
    ├── start-infrastructure.sh # Start core services
    ├── start-workers.sh        # Start worker pools
    ├── scale-workers.sh        # Scale workers dynamically
    └── stop-workers.sh         # Stop workers (keep core running)
```

### docker-compose.yml (Core Infrastructure)

```yaml
name: agenix-reportportal

# Shared configuration anchors
x-common-env: &common-env
  REDIS_URL: redis:6379
  POSTGRES_HOST: postgres
  POSTGRES_PORT: 5432
  POSTGRES_DB: ${POSTGRES_DB:-playwrightgrid}
  POSTGRES_USER: ${POSTGRES_USER:-pguser}
  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-pgpass}

services:
  # Gateway
  gateway:
    image: traefik:v3.6.4
    container_name: agenix-reportportal-gateway
    ports:
      - "80:80"
      - "443:443"
      - "8080:8080"
    # ... gateway configuration

  # Hub
  hub:
    build:
      context: .
      dockerfile: hub/Dockerfile
    ports:
      - "5000:5000"
    environment:
      <<: *common-env
      - AGENIX_HUB_URL=http://hub:5000
    depends_on:
      redis:
        condition: service_healthy
      postgres:
        condition: service_healthy
    profiles:
      - core

  # Redis
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5
    profiles:
      - core

  # PostgreSQL
  postgres:
    image: postgres:15-alpine
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-playwrightgrid}
      POSTGRES_USER: ${POSTGRES_USER:-pguser}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-pgpass}
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "pguser"]
      interval: 5s
      timeout: 3s
      retries: 5
    profiles:
      - core

  # RabbitMQ
  rabbitmq:
    image: rabbitmq:3.12-management
    container_name: agenix-reportportal-rabbitmq
    ports:
      - "5672:5672"
      - "15672:15672"
    profiles:
      - core

  # MinIO
  minio:
    image: minio/minio:latest
    container_name: agenix-reportportal-minio
    ports:
      - "9000:9000"
      - "9001:9001"
    profiles:
      - core

# External network for worker connection
networks:
  default:
    name: agenix-reportportal-network
    external: false
```

### docker-compose.workers.yml (Worker Pools)

```yaml
# Worker deployment configuration (decoupled from core infrastructure)
# This file can be deployed independently on the same or different Docker host

name: agenix-reportportal-workers

# YAML anchors for worker configuration
x-worker-common: &worker-common
  build:
    context: ${WORKER_BUILD_CONTEXT:-.}
    dockerfile: worker/Dockerfile
    args:
      PLAYWRIGHT_VERSION: ${PLAYWRIGHT_VERSION:-1.54.2}
  dns:
    - 1.1.1.1
    - 8.8.8.8
  environment: &worker-env-common
    # Hub connection (can point to remote Hub)
    - AGENIX_HUB_URL=${AGENIX_HUB_URL:-http://hub:5000}
    - REDIS_URL=${REDIS_URL:-redis:6379}

    # Worker authentication
    - AGENIX_WORKER_NODE_SECRET=${AGENIX_WORKER_NODE_SECRET:-node-secret}
    - AGENIX_WORKER_NODE_NODE_SECRET=${AGENIX_WORKER_NODE_NODE_SECRET:-node-node-secret}

    # WebSocket configuration
    - AGENIX_WORKER_PUBLIC_WS_HOST=${AGENIX_WORKER_PUBLIC_WS_HOST:-127.0.0.1}

    # Playwright version
    - PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION:-1.54.2}

    # NodeId intentionally NOT set - auto-generated from HOSTNAME
  shm_size: "1gb"
  restart: unless-stopped

services:
  # Chromium workers
  worker-chromium:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_CHROMIUM_REPLICAS:-5}
    ports:
      - "${WORKER_CHROMIUM_PORT_START:-5200}-${WORKER_CHROMIUM_PORT_END:-5299}:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_CHROMIUM_POOL_CONFIG:-AppB:Chromium:UAT=3}
      - AGENIX_WORKER_PUBLIC_WS_PORT=${WORKER_CHROMIUM_PORT_START:-5200}
      - AGENIX_WORKER_CHROMIUM_ARGS=${AGENIX_WORKER_CHROMIUM_ARGS:---disable-dev-shm-usage --no-sandbox}

  # Firefox workers
  worker-firefox:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_FIREFOX_REPLICAS:-3}
    ports:
      - "${WORKER_FIREFOX_PORT_START:-5300}-${WORKER_FIREFOX_PORT_END:-5399}:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_FIREFOX_POOL_CONFIG:-AppB:Firefox:UAT=2}
      - AGENIX_WORKER_PUBLIC_WS_PORT=${WORKER_FIREFOX_PORT_START:-5300}
      - AGENIX_WORKER_FIREFOX_ARGS=${AGENIX_WORKER_FIREFOX_ARGS:---no-sandbox}

  # WebKit workers
  worker-webkit:
    <<: *worker-common
    deploy:
      replicas: ${WORKER_WEBKIT_REPLICAS:-2}
    ports:
      - "${WORKER_WEBKIT_PORT_START:-5400}-${WORKER_WEBKIT_PORT_END:-5499}:5000"
    environment:
      <<: *worker-env-common
      - AGENIX_WORKER_POOL_CONFIG=${AGENIX_WORKER_WEBKIT_POOL_CONFIG:-AppB:Webkit:UAT=2}
      - AGENIX_WORKER_PUBLIC_WS_PORT=${WORKER_WEBKIT_PORT_START:-5400}
      - AGENIX_WORKER_WEBKIT_ARGS=${AGENIX_WORKER_WEBKIT_ARGS:---no-sandbox}

# Connect to external network created by core infrastructure
networks:
  default:
    name: agenix-reportportal-network
    external: true
```

### .env.workers (Worker-Specific Configuration)

```bash
# Worker Build Configuration
WORKER_BUILD_CONTEXT=.
PLAYWRIGHT_VERSION=1.54.2

# Hub Connection (can point to remote Hub)
AGENIX_HUB_URL=http://hub:5000
REDIS_URL=redis:6379

# Worker Authentication
AGENIX_WORKER_NODE_SECRET=node-secret
AGENIX_WORKER_NODE_NODE_SECRET=node-node-secret

# WebSocket Configuration
AGENIX_WORKER_PUBLIC_WS_HOST=127.0.0.1

# Chromium Workers
WORKER_CHROMIUM_REPLICAS=5
WORKER_CHROMIUM_PORT_START=5200
WORKER_CHROMIUM_PORT_END=5299
AGENIX_WORKER_CHROMIUM_POOL_CONFIG=AppB:Chromium:UAT=3
AGENIX_WORKER_CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox

# Firefox Workers
WORKER_FIREFOX_REPLICAS=3
WORKER_FIREFOX_PORT_START=5300
WORKER_FIREFOX_PORT_END=5399
AGENIX_WORKER_FIREFOX_POOL_CONFIG=AppB:Firefox:UAT=2
AGENIX_WORKER_FIREFOX_ARGS=--no-sandbox

# WebKit Workers
WORKER_WEBKIT_REPLICAS=2
WORKER_WEBKIT_PORT_START=5400
WORKER_WEBKIT_PORT_END=5499
AGENIX_WORKER_WEBKIT_POOL_CONFIG=AppB:Webkit:UAT=2
AGENIX_WORKER_WEBKIT_ARGS=--no-sandbox
```

## Deployment Scripts

### start-infrastructure.sh

```bash
#!/bin/bash
# Start core infrastructure services

set -e

echo "Starting Agenix ReportPortal core infrastructure..."

# Start core services
docker-compose --profile core up -d

# Wait for services to be healthy
echo "Waiting for services to be healthy..."
docker-compose ps

echo "Core infrastructure started successfully!"
echo "Hub available at: http://localhost:5000"
echo "Gateway available at: http://localhost"
```

### start-workers.sh

```bash
#!/bin/bash
# Start worker pools (can run on same or different host)

set -e

echo "Starting Agenix ReportPortal worker pools..."

# Check if core network exists
if ! docker network inspect agenix-reportportal-network &> /dev/null; then
    echo "Error: Core network 'agenix-reportportal-network' not found"
    echo "Please start core infrastructure first: ./start-infrastructure.sh"
    exit 1
fi

# Start workers
docker-compose -f docker-compose.workers.yml --env-file .env.workers up -d

# Show worker status
echo "Worker pools started successfully!"
docker-compose -f docker-compose.workers.yml ps

echo ""
echo "Worker counts:"
echo "  Chromium: $(docker ps | grep worker-chromium | wc -l) workers"
echo "  Firefox:  $(docker ps | grep worker-firefox | wc -l) workers"
echo "  WebKit:   $(docker ps | grep worker-webkit | wc -l) workers"
```

### scale-workers.sh

```bash
#!/bin/bash
# Dynamically scale worker pools

set -e

BROWSER_TYPE=$1
REPLICAS=$2

if [ -z "$BROWSER_TYPE" ] || [ -z "$REPLICAS" ]; then
    echo "Usage: ./scale-workers.sh <browser-type> <replicas>"
    echo "Example: ./scale-workers.sh chromium 10"
    echo ""
    echo "Browser types: chromium, firefox, webkit, all"
    exit 1
fi

echo "Scaling workers..."

case $BROWSER_TYPE in
    chromium)
        docker-compose -f docker-compose.workers.yml up --scale worker-chromium=$REPLICAS -d
        ;;
    firefox)
        docker-compose -f docker-compose.workers.yml up --scale worker-firefox=$REPLICAS -d
        ;;
    webkit)
        docker-compose -f docker-compose.workers.yml up --scale worker-webkit=$REPLICAS -d
        ;;
    all)
        docker-compose -f docker-compose.workers.yml up \
            --scale worker-chromium=$REPLICAS \
            --scale worker-firefox=$REPLICAS \
            --scale worker-webkit=$REPLICAS \
            -d
        ;;
    *)
        echo "Error: Unknown browser type '$BROWSER_TYPE'"
        exit 1
        ;;
esac

echo "Scaling complete!"
docker-compose -f docker-compose.workers.yml ps
```

### stop-workers.sh

```bash
#!/bin/bash
# Stop all workers (keep core infrastructure running)

set -e

echo "Stopping worker pools..."

docker-compose -f docker-compose.workers.yml down

echo "Workers stopped successfully!"
echo "Core infrastructure still running."
```

## Multi-Host Deployment

### Architecture

```
┌────────────────────────────────────────────────────┐
│ Host 1: Core Infrastructure (hub.example.com)     │
├────────────────────────────────────────────────────┤
│ docker-compose.yml                                 │
│  ├── Gateway (port 80, 443)                       │
│  ├── Hub (port 5000)                              │
│  ├── Redis (port 6379)                            │
│  ├── Postgres (port 5432)                         │
│  └── RabbitMQ (port 5672)                         │
└────────────────────────────────────────────────────┘
                       ▲
                       │ External network
                       │ (Internet or VPN)
                       │
       ┌───────────────┴───────────────┐
       │                               │
┌──────┴──────────────┐     ┌─────────┴──────────────┐
│ Host 2: US Workers  │     │ Host 3: EU Workers     │
├─────────────────────┤     ├────────────────────────┤
│ .env.workers:       │     │ .env.workers:          │
│  AGENIX_HUB_URL=    │     │  AGENIX_HUB_URL=       │
│   http://hub:5000   │     │   http://hub:5000      │
│                     │     │                        │
│ Workers:            │     │ Workers:               │
│  - Chromium x10     │     │  - Chromium x10        │
│  - Firefox x5       │     │  - Firefox x5          │
│  - WebKit x3        │     │  - WebKit x3           │
└─────────────────────┘     └────────────────────────┘
```

### Multi-Host .env.workers

**Host 2 (US Region):**
```bash
# Point to remote Hub
AGENIX_HUB_URL=http://hub.example.com:5000
REDIS_URL=hub.example.com:6379

# US-specific configuration
WORKER_CHROMIUM_REPLICAS=10
WORKER_FIREFOX_REPLICAS=5
WORKER_WEBKIT_REPLICAS=3

# Label workers by region
AGENIX_WORKER_CHROMIUM_POOL_CONFIG=AppB:Chromium:US-PROD=3
```

**Host 3 (EU Region):**
```bash
# Point to remote Hub
AGENIX_HUB_URL=http://hub.example.com:5000
REDIS_URL=hub.example.com:6379

# EU-specific configuration
WORKER_CHROMIUM_REPLICAS=10
WORKER_FIREFOX_REPLICAS=5
WORKER_WEBKIT_REPLICAS=3

# Label workers by region
AGENIX_WORKER_CHROMIUM_POOL_CONFIG=AppB:Chromium:EU-PROD=3
```

## Deployment Workflows

### Workflow 1: Local Development

```bash
# Start everything on localhost
./scripts/start-infrastructure.sh
./scripts/start-workers.sh

# Scale workers for heavy testing
./scripts/scale-workers.sh all 10

# Stop workers when done (save resources)
./scripts/stop-workers.sh
```

### Workflow 2: Staging Environment

```bash
# Host 1: Start core infrastructure
docker-compose --profile core up -d

# Host 2: Start workers
docker-compose -f docker-compose.workers.yml up -d
```

### Workflow 3: Production Multi-Region

```bash
# Host 1: Core infrastructure (US datacenter)
docker-compose --profile core up -d

# Host 2: US workers
docker-compose -f docker-compose.workers.yml \
  --env-file .env.workers.us up -d

# Host 3: EU workers
docker-compose -f docker-compose.workers.yml \
  --env-file .env.workers.eu up -d

# Host 4: APAC workers
docker-compose -f docker-compose.workers.yml \
  --env-file .env.workers.apac up -d
```

### Workflow 4: On-Demand Workers

```bash
# Scale to zero during off-hours
./scripts/stop-workers.sh

# Scale up during business hours
./scripts/start-workers.sh

# Scale for load testing
./scripts/scale-workers.sh chromium 50
```

## Testing Procedures

### Test 1: Core-Worker Separation

**Steps**:
1. Start core: `./scripts/start-infrastructure.sh`
2. Verify Hub accessible: `curl http://localhost:5000/health`
3. Start workers: `./scripts/start-workers.sh`
4. Verify registration: `docker exec redis redis-cli KEYS node:*`

**Expected**: Workers register with Hub despite being in separate compose file

### Test 2: Worker-Only Restart

**Steps**:
1. Start full stack (core + workers)
2. Restart workers: `./scripts/stop-workers.sh && ./scripts/start-workers.sh`
3. Verify core services: `curl http://localhost:5000/health`

**Expected**: Core services unaffected by worker restart

### Test 3: Scale to Zero

**Steps**:
1. Stop all workers: `./scripts/stop-workers.sh`
2. Verify core running: `docker-compose ps`
3. Start workers again: `./scripts/start-workers.sh`

**Expected**: Core services continue running, workers restart successfully

### Test 4: Multi-Host Simulation

**Steps**:
1. Start core: `./scripts/start-infrastructure.sh`
2. Update .env.workers: `AGENIX_HUB_URL=http://host.docker.internal:5000`
3. Start workers: `./scripts/start-workers.sh`
4. Verify remote registration: `docker exec redis redis-cli KEYS node:*`

**Expected**: Workers connect to Hub via host.docker.internal

### Test 5: Independent Scaling

**Steps**:
1. Start core and workers
2. Scale Chromium: `./scripts/scale-workers.sh chromium 20`
3. Verify core uptime: `docker-compose ps | grep hub`

**Expected**: Core services show no restarts or downtime

## Migration from Phase 2

Phase 3 has been implemented with **backwards compatibility**. Phase 2 deployment mode (workers in main docker-compose.yml) remains the default. Phase 3 (decoupled workers) is opt-in.

### Deployment Options

**Option 1: Phase 2 Mode (Default - Backwards Compatible)**
```bash
# Workers defined in docker-compose.yml (commented out)
# Uses existing workflow
docker compose --profile infrastructure --profile core up -d

# Run tests with Phase 2 mode (default)
./scripts/run-docker-compose-test.sh
```

**Option 2: Phase 3 Mode (Decoupled Workers)**
```bash
# Start core infrastructure
./scripts/start-infrastructure.sh

# Start workers separately
./scripts/start-workers.sh

# Run tests with Phase 3 mode
./scripts/run-docker-compose-test.sh --phase3
```

### Migration Steps

1. **Create docker-compose.workers.yml** ✅ (Created)
2. **Create .env.workers** ✅ (Created)
3. **Create deployment scripts** ✅ (Created)
4. **Update docker-compose.yml** ✅ (Workers removed, network configured)
5. **Update test script** ✅ (Added --phase3 flag support)

### Rollback to Phase 2

Phase 2 deployment mode is still available:

```bash
# Workers are commented out in docker-compose.yml
# Uncomment worker services if needed, or use docker-compose.workers.yml

# Run with Phase 2 mode (default)
./scripts/run-docker-compose-test.sh
```

## Benefits Over Phase 2

| Feature | Phase 2 | Phase 3 | Improvement |
|---------|---------|---------|-------------|
| Worker Independence | Coupled to core | Fully decoupled | ✅ Independent deployment |
| Core Restarts Impact | Restarts workers | No impact | ✅ Zero downtime |
| Multi-Host Support | No | Yes | ✅ Distributed deployment |
| Scale to Zero | Manual | Script-based | ✅ Automated scaling |
| Configuration Files | 1 (docker-compose.yml) | 2 (split) | ✅ Separation of concerns |
| Artifact Complexity | Medium | Low | ✅ Simplified artifacts |
| Cloud Readiness | Limited | High | ✅ Cloud-native |

## Known Limitations

1. **Network Dependency**: Workers require external network created by core
   - **Mitigation**: Start core infrastructure first
   - **Alternative**: Use Docker overlay network for Swarm deployment

2. **Environment File Management**: Two .env files to maintain
   - **Mitigation**: Use .env for core, .env.workers for workers
   - **Alternative**: Use Docker secrets for sensitive values

3. **Service Discovery**: Workers must know Hub URL
   - **Current**: Use environment variable (AGENIX_HUB_URL)
   - **Future**: Use service mesh or DNS-based discovery

## Rollback to Phase 2

If issues occur, revert to single docker-compose.yml:

```bash
# Stop Phase 3 setup
./scripts/stop-workers.sh
docker-compose down

# Restore Phase 2 configuration
git checkout docker-compose.yml

# Start Phase 2 setup
docker-compose up -d
```

## Next Steps

1. **Implement Phase 3**: Create docker-compose.workers.yml and scripts
2. **Test Decoupling**: Verify core-worker independence
3. **Test Multi-Host**: Deploy workers on separate Docker host
4. **Document Deployment**: Create operations runbook
5. **Plan Kubernetes**: Design Kubernetes deployment architecture

## Future Enhancements

### Kubernetes Deployment

```yaml
# kubernetes/workers-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: worker-chromium
spec:
  replicas: 10
  template:
    spec:
      containers:
      - name: worker
        image: agenix/worker:latest
        env:
        - name: AGENIX_HUB_URL
          value: http://hub-service:5000
```

### Docker Swarm

```bash
# Deploy workers as Swarm service
docker service create \
  --name worker-chromium \
  --replicas 20 \
  --env AGENIX_HUB_URL=http://hub:5000 \
  agenix/worker:latest
```

### Auto-Scaling

```bash
# Scale workers based on queue depth
while true; do
  QUEUE_DEPTH=$(redis-cli LLEN test-queue)
  DESIRED_WORKERS=$((QUEUE_DEPTH / 10))
  ./scripts/scale-workers.sh chromium $DESIRED_WORKERS
  sleep 60
done
```

## Related Documentation

- [README: Dynamic Worker Registration](./README.md)
- [Phase 1: Single Worker Service](./phase-1-single-worker-service.md)
- [Phase 2: Heterogeneous Pools](./phase-2-heterogeneous-pools.md)
- [Multi-Host Deployment Guide](../../docs/multi-host-deployment.md)
- [Kubernetes Deployment Guide](../../docs/kubernetes-deployment.md)
