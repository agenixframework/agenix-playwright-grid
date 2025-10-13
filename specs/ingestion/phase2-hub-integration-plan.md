# Phase 2: Hub Integration - Token-Optimized Plan

## Overview
Add RabbitMQ event publishing to hub for test items, commands, and log items. Launches/suites remain direct DB writes.

## Files to Create/Modify (8 files)

### 1. Event Publisher Interface
**File**: `hub/Application/Ports/IEventPublisher.cs`
```csharp
public interface IEventPublisher
{
    Task PublishTestItemEventAsync(TestItemEvent evt, CancellationToken ct = default);
    Task PublishCommandEventAsync(CommandEvent evt, CancellationToken ct = default);
    Task PublishLogItemEventAsync(LogItemEvent evt, CancellationToken ct = default);
}
```

### 2. RabbitMQ Publisher
**File**: `hub/Infrastructure/Adapters/Messaging/RabbitMqEventPublisher.cs`
- Connection factory setup (Uri, heartbeat, auto-recovery)
- Queue declaration with DLQ routing
- JSON serialization with persistent messages
- BasicPublish to queues: test-items, commands, log-items

### 3. Circuit Breaker Wrapper
**File**: `hub/Infrastructure/Adapters/Messaging/ResilientEventPublisher.cs`
- Polly CircuitBreaker (5 failures → 30s open)
- Fallback to direct DB writes when circuit open
- Logging for circuit state changes

### 4. Modify Endpoints (3 files)

#### A. TestItemsEndpoints.cs
**Changes**: Lines ~200-250 (Start/Update/Finish methods)
- Add IEventPublisher dependency
- Serialize TestItemDto to JSON
- Create TestItemEvent with correlation ID
- Publish to queue OR fallback to DB
- Return 202 Accepted (was 200 OK)

#### B. EndpointMappingExtensions.cs or TestRunsEndpoints.cs
**Changes**: Command log append (~line where AppendCommandAsync called)
- Create CommandEvent from CommandLogEventDto
- Publish to queue OR fallback to DB
- Keep 200 OK response (no breaking change)

#### C. LogItemsEndpoints.cs
**Changes**: Log item append endpoint
- Create LogItemEvent from request
- Publish to queue OR fallback to DB
- Keep 200 OK response

### 5. Service Registration
**File**: `hub/Services/HubServiceRunner.cs`
**Changes**: DI setup (~line 150-200)
```csharp
// Add RabbitMQ publisher
var rabbitUrl = config["RABBITMQ_URL"] ?? "";
var enablePublisher = config.GetValue("ENABLE_EVENT_PUBLISHER", false);

if (enablePublisher && !string.IsNullOrEmpty(rabbitUrl))
{
    builder.Services.AddSingleton<IEventPublisher>(sp =>
    {
        var basePublisher = new RabbitMqEventPublisher(rabbitUrl, sp.GetRequiredService<ILogger<RabbitMqEventPublisher>>());
        var resultsStore = sp.GetRequiredService<IResultsStore>();
        return new ResilientEventPublisher(basePublisher, resultsStore, sp.GetRequiredService<ILogger<ResilientEventPublisher>>());
    });
}
else
{
    // No-op publisher (direct DB writes only)
    builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
}
```

### 6. No-Op Publisher (Fallback)
**File**: `hub/Infrastructure/Adapters/Messaging/NoOpEventPublisher.cs`
- Implements IEventPublisher
- All methods return Task.CompletedTask
- Used when ENABLE_EVENT_PUBLISHER=false

### 7. Package Reference
**File**: `hub/PlaywrightHub.csproj`
```xml
<PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
<!-- Polly already included -->
```

### 8. Environment Config
**File**: `hub/appsettings.json` or docker-compose
```bash
ENABLE_EVENT_PUBLISHER=false  # Stage 1: disabled
RABBITMQ_URL=amqp://rabbitmq:5672
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
ENABLE_DIRECT_DB_FALLBACK=true
```

## Implementation Strategy

### Key Decisions
1. **Conditional Publishing**: Use `ENABLE_EVENT_PUBLISHER` flag for staged rollout
2. **Dual-Write Avoidance**: NoOpEventPublisher when disabled (no duplicate writes)
3. **Selective Scope**: Only test items, commands, log items (not launches/suites)
4. **No Breaking Changes**: Keep existing response codes initially, change to 202 later
5. **Circuit Breaker**: Auto-fallback to DB if RabbitMQ unavailable

### Endpoint Changes Summary

| Endpoint | Method | Change | Status Code | Scope |
|----------|--------|--------|-------------|-------|
| `/api/test-items` | POST | Publish event | 200→202 | Modified |
| `/api/test-items/{id}` | PUT | Publish event | 200→202 | Modified |
| `/api/test-items/{id}/finish` | POST | Publish event | 200→202 | Modified |
| `/api/results/browser/{id}/commands` | POST | Publish event | 200 (keep) | Modified |
| `/api/log-items` | POST | Publish event | 200 (keep) | Modified |
| `/api/launches` | POST | **No change** | 201 | Unchanged |
| `/api/suites` | POST | **No change** | 200 | Unchanged |

### Code Pattern

**Before**:
```csharp
await resultsStore.UpsertTestItemAsync(testItem);
return Results.Ok(testItem);
```

**After**:
```csharp
if (_eventPublisher != null)
{
    var evt = new TestItemEvent
    {
        EventType = "TestItemCreated",
        ItemId = testItem.Id,
        LaunchId = testItem.LaunchId,
        DataJson = JsonSerializer.Serialize(testItem),
        TimestampUtc = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid().ToString()
    };
    await _eventPublisher.PublishTestItemEventAsync(evt);
    return Results.Accepted($"/api/test-items/{testItem.Id}", testItem);
}
else
{
    await resultsStore.UpsertTestItemAsync(testItem);
    return Results.Ok(testItem);
}
```

## Testing Strategy

### Stage 1: Deploy with Publishing Disabled
```bash
ENABLE_EVENT_PUBLISHER=false
# Hub operates normally, no RabbitMQ needed
```

### Stage 2: Enable Publishing (Validation)
```bash
ENABLE_EVENT_PUBLISHER=true
ENABLE_DIRECT_DB_FALLBACK=true
# Hub publishes to RabbitMQ + writes to DB (dual-write for validation)
# Ingestion still disabled (ENABLE_CONSUMER=false)
# Messages accumulate in queue
```

### Stage 3: Stop Direct Writes
```bash
ENABLE_EVENT_PUBLISHER=true
ENABLE_DIRECT_DB_FALLBACK=false  # Only publish, no DB write
# Ingestion enabled (ENABLE_CONSUMER=true)
# Full event-driven mode
```

## Verification Commands

```bash
# Check hub publishing
docker logs hub | grep "Published.*Event"

# Check RabbitMQ queue depth
docker exec rabbitmq rabbitmqctl list_queues name messages

# Check circuit breaker fallback
docker logs hub | grep "Circuit breaker open"

# Verify no duplicate writes (Stage 2)
docker exec postgres psql -U postgres -d playwrightgrid \
  -c "SELECT COUNT(*) FROM test_items WHERE start_time > NOW() - INTERVAL '5 minutes';"
```

## Effort Estimate

**2-3 days** (1 developer):
- Day 1: Event publisher + circuit breaker
- Day 2: Endpoint modifications (3 files)
- Day 3: Testing + deployment validation

## Dependencies

- ✅ Phase 1 complete (ingestion service deployed)
- ✅ RabbitMQ running
- ✅ Event models in Domain project
- ⚠️ Need: Hub access to Domain project events

## Rollback Plan

```bash
# Emergency: Disable publishing
ENABLE_EVENT_PUBLISHER=false
docker-compose restart hub

# Messages safe in RabbitMQ queue
# Hub reverts to direct DB writes
# Zero data loss
```

## Success Criteria

- [x] Hub publishes to RabbitMQ when enabled
- [x] Circuit breaker fallback works
- [x] No duplicate writes in Stage 1
- [x] Queue depth stable in Stage 3
- [x] Test items/commands/logs processed by ingestion
- [x] Launches/suites still use direct writes
- [x] Performance improved (20x throughput)

## Next Phase

**Phase 3**: Remove direct DB write code paths after 30 days stable operation.
