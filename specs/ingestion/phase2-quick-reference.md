# Phase 2: Hub Integration - Quick Reference

## What is Phase 2?
Add RabbitMQ event publishing to hub. Test items, commands, and log items publish to queues instead of direct DB writes. Launches/suites remain unchanged.

## Files to Create (4 files, ~235 lines)

1. **IEventPublisher.cs** (15 lines) - Interface with 3 methods
2. **RabbitMqEventPublisher.cs** (120 lines) - RabbitMQ client, queue declaration, publishing
3. **ResilientEventPublisher.cs** (80 lines) - Circuit breaker wrapper with DB fallback
4. **NoOpEventPublisher.cs** (20 lines) - Used when publishing disabled

## Files to Modify (5 files, ~70 lines changed)

1. **PlaywrightHub.csproj** - Add RabbitMQ.Client package
2. **TestItemsEndpoints.cs** - Publish events instead of DB writes
3. **EndpointMappingExtensions.cs** - Publish command events
4. **LogItemsEndpoints.cs** - Publish log events
5. **HubServiceRunner.cs** - Register publishers in DI

## Key Configuration

```bash
# Stage 1: Disabled (default)
ENABLE_EVENT_PUBLISHER=false

# Stage 2: Validation
ENABLE_EVENT_PUBLISHER=true
ENABLE_DIRECT_DB_FALLBACK=true

# Stage 3: Production
ENABLE_EVENT_PUBLISHER=true
ENABLE_DIRECT_DB_FALLBACK=false
```

## Code Pattern

**Endpoint Change**:
```csharp
// Before
await resultsStore.UpsertTestItemAsync(testItem);
return Results.Ok(testItem);

// After
var evt = new TestItemEvent { /* populate */ };
await _eventPublisher.PublishTestItemEventAsync(evt);
return Results.Accepted($"/api/test-items/{testItem.Id}", testItem);
```

**DI Registration**:
```csharp
if (enablePublisher)
    builder.Services.AddSingleton<IEventPublisher, ResilientEventPublisher>();
else
    builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
```

## Testing Stages

| Stage | Config | Duration | Purpose |
|-------|--------|----------|---------|
| 1 | ENABLE_EVENT_PUBLISHER=false | 1 day | Verify build works |
| 2 | ENABLE_EVENT_PUBLISHER=true | 2 days | Validate publishing |
| 3 | + ENABLE_CONSUMER=true | Ongoing | Full event-driven |

## Verification

```bash
# Hub publishing?
docker logs hub | grep "Published"

# Queue depth growing?
docker exec rabbitmq rabbitmqctl list_queues

# Circuit breaker working?
docker logs hub | grep "Circuit breaker"
```

## Rollback

```bash
ENABLE_EVENT_PUBLISHER=false
docker-compose restart hub
# Instant rollback to direct DB writes
```

## Effort

**Implementation**: 4 hours
**Testing**: 2 hours
**Validation**: 3 days (staged)
**Total**: ~3 days

## Expected Results

- **Throughput**: 20x improvement (10,000+ events/sec)
- **Hub CPU**: 50%+ reduction (no DB I/O wait)
- **Latency**: P99 from 50-100ms → 1-5ms
- **Connections**: 10x fewer DB connections

## Quick Commands

```bash
# Add package
dotnet add hub/PlaywrightHub.csproj package RabbitMQ.Client --version 6.8.1

# Build
dotnet build hub

# Deploy
docker-compose restart hub

# Check health
curl http://localhost:5001/health
```

## Next Steps

See detailed guides:
- **Implementation**: `phase2-implementation-checklist.md`
- **Architecture**: `phase2-hub-integration-plan.md`
- **Deployment**: `ingestion-service-deployment.md`
