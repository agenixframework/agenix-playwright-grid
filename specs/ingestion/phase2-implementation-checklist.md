# Phase 2: Hub Integration - Implementation Checklist

## Pre-Implementation

- [ ] Phase 1 deployed (ingestion service running)
- [ ] RabbitMQ healthy and accessible
- [ ] Review event models in Domain project
- [ ] Backup database before changes

## Implementation Tasks

### 1. Add Dependencies (5 min)
```bash
cd hub
dotnet add package RabbitMQ.Client --version 6.8.1
dotnet build
```

### 2. Create Event Publisher Interface (10 min)
- [ ] Create `hub/Application/Ports/IEventPublisher.cs`
- [ ] 3 methods: PublishTestItemEventAsync, PublishCommandEventAsync, PublishLogItemEventAsync
- [ ] Add using `Agenix.PlaywrightGrid.Domain.Events`

### 3. Implement RabbitMQ Publisher (30 min)
- [ ] Create `hub/Infrastructure/Adapters/Messaging/RabbitMqEventPublisher.cs`
- [ ] ConnectionFactory with auto-recovery
- [ ] DeclareInfrastructure(): queues + DLQ routing
- [ ] Implement 3 publish methods (serialize JSON, set props, BasicPublish)
- [ ] IDisposable pattern for cleanup

### 4. Implement Circuit Breaker (20 min)
- [ ] Create `hub/Infrastructure/Adapters/Messaging/ResilientEventPublisher.cs`
- [ ] Polly CircuitBreakerPolicy (5 failures, 30s timeout)
- [ ] Fallback to IResultsStore on circuit open
- [ ] Logging for circuit state changes

### 5. Implement No-Op Publisher (10 min)
- [ ] Create `hub/Infrastructure/Adapters/Messaging/NoOpEventPublisher.cs`
- [ ] All methods return Task.CompletedTask
- [ ] Used when ENABLE_EVENT_PUBLISHER=false

### 6. Modify TestItemsEndpoints (45 min)
- [ ] Add IEventPublisher parameter to constructor
- [ ] Modify StartAsync: publish TestItemEvent
- [ ] Modify UpdateAsync: publish TestItemEvent
- [ ] Modify FinishAsync: publish TestItemEvent
- [ ] Add conditional: if publisher != null → publish, else → direct DB
- [ ] Change return: Results.Accepted (was Results.Ok)

### 7. Modify Command Log Endpoint (20 min)
- [ ] Find command append endpoint (EndpointMappingExtensions or TestRunsEndpoints)
- [ ] Add IEventPublisher parameter
- [ ] Create CommandEvent from CommandLogEventDto
- [ ] Publish to queue with conditional fallback
- [ ] Keep 200 OK response (no breaking change)

### 8. Modify Log Items Endpoint (20 min)
- [ ] Open `hub/Infrastructure/Web/LogItemsEndpoints.cs`
- [ ] Add IEventPublisher parameter
- [ ] Create LogItemEvent from request
- [ ] Publish to queue with conditional fallback
- [ ] Keep 200 OK response

### 9. Register Services in DI (30 min)
- [ ] Open `hub/Services/HubServiceRunner.cs`
- [ ] Add configuration reading (ENABLE_EVENT_PUBLISHER, RABBITMQ_URL)
- [ ] Register RabbitMqEventPublisher (singleton)
- [ ] Wrap with ResilientEventPublisher
- [ ] Register NoOpEventPublisher when disabled
- [ ] Add IResultsStore to circuit breaker constructor

### 10. Add Configuration (10 min)
- [ ] Update `hub/appsettings.json` or docker-compose.yml
```bash
ENABLE_EVENT_PUBLISHER=false  # Start disabled
RABBITMQ_URL=amqp://rabbitmq:5672
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
ENABLE_DIRECT_DB_FALLBACK=true
```

## Testing

### Local Build (5 min)
```bash
cd hub
dotnet build
# Expected: 0 errors
```

### Stage 1: Publishing Disabled (1 day)
- [ ] Deploy with ENABLE_EVENT_PUBLISHER=false
- [ ] Verify hub starts normally
- [ ] Run smoke tests
- [ ] Verify no RabbitMQ dependency
- [ ] Monitor for 24 hours

### Stage 2: Enable Publishing (2 days)
- [ ] Set ENABLE_EVENT_PUBLISHER=true
- [ ] Restart hub
- [ ] Check logs: "Published.*Event"
- [ ] Verify queue depth growing
- [ ] Ingestion still disabled (ENABLE_CONSUMER=false)
- [ ] Monitor for 48 hours

### Stage 3: Enable Ingestion (ongoing)
- [ ] Set ENABLE_CONSUMER=true in ingestion
- [ ] Restart ingestion service
- [ ] Verify queue depth stable
- [ ] Check database inserts
- [ ] Monitor metrics
- [ ] Verify 20x performance improvement

## Verification Checklist

- [ ] Hub logs show "Published TestItemEvent"
- [ ] Hub logs show "Published CommandEvent"
- [ ] Hub logs show "Published LogItemEvent"
- [ ] RabbitMQ UI shows messages in queues
- [ ] No "Circuit breaker open" warnings
- [ ] Test items created successfully
- [ ] Commands logged successfully
- [ ] Log items created successfully
- [ ] Launches still work (direct DB)
- [ ] Suites still work (direct DB)

## Performance Verification

```bash
# Before Phase 2
# - Commands: 500/sec
# - Test items: 50/sec

# After Phase 2 + Phase 1
# - Commands: 10,000+/sec (20x)
# - Test items: 1,000+/sec (20x)

# Run load test
# Monitor metrics: http://localhost:9091/metrics
# Check ingestion batch sizes
# Verify no queue buildup
```

## Rollback Procedure

### Quick Rollback
```bash
ENABLE_EVENT_PUBLISHER=false
docker-compose restart hub
# Hub reverts to direct DB writes immediately
```

### Full Rollback
```bash
# 1. Stop publishing
ENABLE_EVENT_PUBLISHER=false
docker-compose restart hub

# 2. Stop ingestion
docker-compose -f docker-compose.yml stop ingestion

# 3. System back to original state
```

## Success Metrics

- [ ] Build succeeds with 0 errors
- [ ] Stage 1: Hub works without RabbitMQ
- [ ] Stage 2: Events published to queues
- [ ] Stage 3: Ingestion processes events
- [ ] Performance: 20x throughput improvement
- [ ] Stability: 7 days without issues
- [ ] No data loss or corruption

## Files Modified Summary

```
hub/PlaywrightHub.csproj                                    (1 line added)
hub/Application/Ports/IEventPublisher.cs                    (NEW, 15 lines)
hub/Infrastructure/Adapters/Messaging/RabbitMqEventPublisher.cs    (NEW, 120 lines)
hub/Infrastructure/Adapters/Messaging/ResilientEventPublisher.cs   (NEW, 80 lines)
hub/Infrastructure/Adapters/Messaging/NoOpEventPublisher.cs        (NEW, 20 lines)
hub/Infrastructure/Web/TestItemsEndpoints.cs                (30 lines modified)
hub/Infrastructure/Web/EndpointMappingExtensions.cs         (10 lines modified)
hub/Infrastructure/Web/LogItemsEndpoints.cs                 (10 lines modified)
hub/Services/HubServiceRunner.cs                            (20 lines added)
docker-compose.yml                                          (5 lines added)

Total: ~320 new lines, ~70 modified lines
```

## Estimated Effort

**Total: 2-3 days** (1 developer)

- Implementation: 4 hours
- Testing: 2 hours
- Stage 1 validation: 1 day
- Stage 2 validation: 2 days
- Documentation: 1 hour

## Next Steps After Completion

1. Monitor production for 30 days
2. Phase 3: Remove direct DB write code paths
3. Add monitoring dashboards
4. Create alert rules (queue depth, DLQ, errors)
5. Update client SDK documentation
