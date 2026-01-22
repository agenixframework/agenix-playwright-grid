# Dynamic Worker Registration

## Overview

This document describes the dynamic worker registration architecture for Agenix ReportPortal, which allows workers to register themselves with the Hub automatically without requiring hardcoded configuration changes in docker-compose.yml.

## Key Discovery

The worker system **already supports dynamic registration** without any code changes. Workers can:

- Auto-generate unique NodeId from `$HOSTNAME` or GUID
- Read pool configuration from environment variables
- Register themselves with Hub at startup
- Scale horizontally using Docker Compose replicas

## Benefits

- **89% Configuration Reduction**: From 360 lines (3 workers) to ~40 lines (single worker definition)
- **Zero Code Changes**: Leverages existing worker capabilities
- **Elastic Scaling**: Add/remove workers by changing environment variable or using `--scale` command
- **No docker-compose.yml Edits**: Scale workers without modifying the main compose file
- **Future Decoupling**: Workers can be deployed separately from core infrastructure

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ docker-compose.yml                                          │
│                                                             │
│  worker:                                                    │
│    deploy:                                                  │
│      replicas: ${WORKER_REPLICAS:-3}  ◄── Scale via .env   │
│    environment:                                             │
│      - AGENIX_WORKER_POOL_CONFIG=AppB:Chromium:UAT=3      │
│      - AGENIX_WORKER_NODE_ID=  ◄── Empty = auto-generate  │
└─────────────────────────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│ Docker Compose Runtime                                      │
│                                                             │
│  ┌──────────────────┐  ┌──────────────────┐               │
│  │ worker-1         │  │ worker-2         │               │
│  │ HOSTNAME=worker-1│  │ HOSTNAME=worker-2│               │
│  │ NodeId=worker-1  │  │ NodeId=worker-2  │               │
│  └────────┬─────────┘  └────────┬─────────┘               │
│           │                     │                          │
│           └──────────┬──────────┘                          │
└─────────────────────┼─────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│ Hub (Redis)                                                 │
│                                                             │
│  {                                                          │
│    "worker-1": { poolConfig: {...}, baseUrl: "..." }      │
│    "worker-2": { poolConfig: {...}, baseUrl: "..." }      │
│  }                                                          │
└─────────────────────────────────────────────────────────────┘
```

## Code Evidence

The system supports dynamic registration through:

1. **Auto NodeId Generation** (`WorkerOptions.cs:596`):
```csharp
NodeId = Environment.GetEnvironmentVariable("AGENIX_WORKER_NODE_ID")
         ?? $"node-{Guid.NewGuid():N}",
```

2. **Environment-Based Pool Config** (`WorkerOptions.cs:241`):
```csharp
var poolConfigEnv = Environment.GetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG")
                    ?? "AppA:Chromium:Staging=3";
```

3. **Dynamic Hub Registration** (`NodeRegistrar.cs:37-48`):
```csharp
var baseUrl = $"http://{Environment.GetEnvironmentVariable("HOSTNAME") ?? _options.NodeId}:5000";
await _hub.RegisterAsync(
    _options.HubUrl,
    _options.NodeSecret,
    _options.NodeId,
    baseUrl,
    _options.PoolConfig.Keys.ToArray(),
    _options.PoolConfig.Values.Sum(),
    _options.Labels.ToDictionary(k => k.Key, v => v.Value));
```

## Implementation Phases

### Phase 1: Single Worker Service (Immediate)
- Replace worker1/worker2/worker3 with single worker definition
- Use `deploy.replicas` for horizontal scaling
- Workers use Docker's `$HOSTNAME` for unique identification
- **Status**: Ready for implementation (no code changes required)
- **Details**: See [phase-1-single-worker-service.md](./phase-1-single-worker-service.md)

### Phase 2: Heterogeneous Pools (Future Enhancement)
- Support multiple browser types (Chromium, Firefox, WebKit)
- Split into worker-chromium, worker-firefox services
- Each service has its own `deploy.replicas`
- **Status**: Design phase
- **Details**: See [phase-2-heterogeneous-pools.md](./phase-2-heterogeneous-pools.md)

### Phase 3: Decoupled Deployment (Future Architecture)
- Workers deployed independently from core infrastructure
- Separate docker-compose.workers.yml file
- Optional worker deployment for production scenarios
- **Status**: Future planning
- **Details**: See [phase-3-decoupled-deployment.md](./phase-3-decoupled-deployment.md)

## Quick Start

### Scale Workers Immediately

**Option 1: Environment Variable**
```bash
# Edit .env file
WORKER_REPLICAS=5

# Restart services
docker-compose up -d
```

**Option 2: Command Line**
```bash
# Scale without editing files
docker-compose up --scale worker=5 -d
```

**Option 3: Watch Mode**
```bash
# Scale and watch logs
docker-compose up --scale worker=10
```

### Verify Worker Registration

```bash
# Check running workers
docker ps | grep worker

# Expected output:
# agenix-reportportal-worker-1
# agenix-reportportal-worker-2
# agenix-reportportal-worker-3

# Check Hub registration (Redis)
docker exec -it agenix-reportportal-redis redis-cli
> KEYS node:*
> HGETALL node:worker-1
```

## Key Advantages Over Explicit Worker Definitions

| Aspect | Before (Explicit) | After (Dynamic) |
|--------|------------------|-----------------|
| Configuration Lines | 360 lines (3 workers) | 40 lines (unlimited workers) |
| Adding Workers | Edit docker-compose.yml | Change .env or use --scale |
| Removing Workers | Edit docker-compose.yml | Change .env or use --scale |
| Code Changes | None | None |
| Artifact Maintenance | Complex | Simple |
| Worker Decoupling | Difficult | Easy |
| Configuration Drift | High risk | Low risk |

## Design Principles

1. **Environment-Driven Configuration**: All worker settings via environment variables
2. **Auto-Registration**: Workers register themselves with Hub at startup
3. **Unique Identification**: Docker's `$HOSTNAME` provides unique NodeId
4. **Zero-Touch Scaling**: No docker-compose.yml modifications needed
5. **Optional Deployment**: Workers can be deployed separately from core

## Next Steps

1. **Implement Phase 1**: Replace explicit worker definitions with single worker service
2. **Test Scaling**: Verify workers register correctly with different replica counts
3. **Document Usage**: Update user documentation with scaling commands
4. **Plan Phase 2**: Design heterogeneous pool architecture
5. **Evaluate Phase 3**: Assess need for fully decoupled worker deployment

## Related Documentation

- [Phase 1: Single Worker Service](./phase-1-single-worker-service.md)
- [Phase 2: Heterogeneous Pools](./phase-2-heterogeneous-pools.md)
- [Phase 3: Decoupled Deployment](./phase-3-decoupled-deployment.md)
- [Worker Configuration Reference](../../docs/worker-configuration.md)
- [Hub Registration Protocol](../../docs/hub-registration-protocol.md)
