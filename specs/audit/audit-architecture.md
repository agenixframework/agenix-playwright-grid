# Audit Architecture

## Overview

The Playwright Grid Hub uses an event-driven audit architecture where audit events are published to RabbitMQ and consumed asynchronously by the Ingestion service for batch persistence to PostgreSQL. This architecture provides durability, high throughput, and complete decoupling of audit processing from the Hub's critical path.

## Data Flow

```
Hub Endpoints (API operations)
    ↓
AuditEventPublisher (IAuditStore)
    ↓ (Fire-and-forget publish)
RabbitMQ Queue (playwright-grid.audit-entries)
    ↓ (Async consumption)
Ingestion Service / AuditConsumerWorker
    ↓ (Batch writes with COPY BINARY)
PostgreSQL (audit_entries table)
```

## Architecture Components

### 1. AuditEventPublisher (Hub)

**Location**: `hub/Infrastructure/Adapters/Audit/AuditEventPublisher.cs`

**Responsibilities**:
- Implements `IAuditStore` interface
- Converts `AuditEntryDto` to `AuditEvent` domain event
- Publishes events to RabbitMQ via `IEventPublisher`
- Fire-and-forget pattern (returns immediately, no blocking)
- Swallows exceptions (audit logging never crashes main execution)

**Usage**:
```csharp
// Registered in HubServiceRunner.cs
builder.Services.AddSingleton<IAuditStore, AuditEventPublisher>();

// Used throughout Hub endpoints
await auditStore.AppendAsync(new AuditEntryDto
{
    Timestamp = DateTime.UtcNow,
    Category = "Launch",
    Action = "Started",
    Actor = userId,
    RemoteIp = ipAddress,
    Severity = "Info",
    Details = new { launchId, projectKey }
});
```

### 2. RabbitMQ Queue

**Queue Name**: `playwright-grid.audit-entries`

**Characteristics**:
- Durable queue (survives RabbitMQ restarts)
- Persisted messages (survives Hub crashes)
- Single consumer (Ingestion service)
- Prefetch count: 100 (configurable)

### 3. AuditConsumerWorker (Ingestion Service)

**Location**: `ingestion/Workers/AuditConsumerWorker.cs`

**Responsibilities**:
- Background service (IHostedService) registered in ingestion service
- Consumes `AuditEvent` messages from RabbitMQ
- Batches events using `BatchWriter<AuditEvent>` helper
- Delegates batch writes to `PostgresBatchWriter.WriteAuditEntriesAsync()`
- Graceful shutdown with batch flushing

**Configuration**:
```bash
# .env
AUDIT_BATCH_SIZE=500        # Max entries per batch
AUDIT_BATCH_TIMEOUT=750     # Max milliseconds before flushing partial batch
RABBITMQ_URL=amqp://localhost:5672
```

### 4. Database Schema

```sql
CREATE TABLE audit_entries (
    id BIGSERIAL PRIMARY KEY,
    timestamp TIMESTAMPTZ NOT NULL,
    category TEXT NOT NULL,
    action TEXT NOT NULL,
    actor TEXT,
    remote_ip TEXT,
    correlation_id TEXT,
    severity TEXT NOT NULL DEFAULT 'Info',
    details JSONB NOT NULL DEFAULT '{}'
);
```

## Benefits

| Benefit | Description |
|---------|-------------|
| **Decoupled** | Audit processing runs in separate Ingestion service, never blocks Hub operations |
| **Durable** | RabbitMQ persists messages to disk, survives Hub crashes without data loss |
| **High Throughput** | COPY BINARY with 500-entry batches achieves ~2,000 events/second |
| **Non-blocking** | Fire-and-forget publish returns in <5ms, Hub endpoints remain fast |
| **Scalable** | Multiple Ingestion instances can consume in parallel for horizontal scaling |
| **Resilient** | Retry logic and graceful shutdown ensure no data loss during failures |

## Configuration

### .env File

```bash
# Audit Architecture
AUDIT_BATCH_SIZE=500          # Max entries per batch (default: 500)
AUDIT_BATCH_TIMEOUT=750       # Flush timeout in ms (default: 750)

# RabbitMQ (shared with other event types)
RABBITMQ_URL=amqp://localhost:5672
RABBITMQ_PREFETCH_COUNT=100
```

## Monitoring

### Metrics

**Ingestion (OpenTelemetry):**
- `audit_persisted` (counter) - Total entries persisted to PostgreSQL
- `audit_failed_batches` (counter) - Failed batch write attempts
- `audit_batch_size` (histogram) - Batch size distribution

**RabbitMQ Management UI** (http://localhost:15672):
- Queue depth: `playwright-grid.audit-entries` message count
- Consumer lag: Message publish rate vs. consumption rate
- Message throughput: Messages/second processed

### Health Checks

**Verify Hub Publishing:**
```bash
docker logs hub 2>&1 | grep "Audit configured"
# Expected: [Hub] Audit configured: Event-driven (RabbitMQ → Ingestion service)
```

**Verify Ingestion Consumption:**
```bash
docker logs ingestion 2>&1 | grep "AuditConsumer"
# Expected: [AuditConsumer] Started (batchSize=500, timeout=750ms)
```

**Verify RabbitMQ Queue:**
```bash
curl -s http://guest:guest@localhost:15672/api/queues/%2F/playwright-grid.audit-entries | jq '.messages'
```

## Troubleshooting

### Issue: Audit events not appearing in database

**Diagnosis:**
```bash
# Check if Ingestion service is running
docker-compose ps ingestion

# Check RabbitMQ queue depth
curl -s http://guest:guest@localhost:15672/api/queues/%2F/playwright-grid.audit-entries | jq '.messages'

# Check Ingestion logs for errors
docker logs ingestion 2>&1 | grep ERROR
```

**Solutions:**
1. Verify Ingestion service is running: `docker-compose restart ingestion`
2. Check RabbitMQ connectivity: Ensure `RABBITMQ_URL` is correct
3. Check PostgreSQL connectivity: Ensure `POSTGRES_CONNECTION_STRING` is correct

---

### Issue: RabbitMQ queue depth growing

**Solutions:**
1. Increase batch size: Set `AUDIT_BATCH_SIZE=1000`
2. Scale Ingestion horizontally: Deploy multiple Ingestion instances
3. Optimize PostgreSQL: Increase `shared_buffers`, `work_mem`

---

## FAQ

### Q: What happens if RabbitMQ goes down?

**A:** Hub's `AuditEventPublisher` swallows publish exceptions, so Hub continues operating normally. Audit events are lost during the outage. When RabbitMQ recovers, audit events resume immediately.

### Q: Can audit events be replayed?

**A:** Yes, RabbitMQ persists messages to disk. If Ingestion service crashes before ACKing a batch, RabbitMQ will redeliver the messages.

### Q: How long are audit events retained?

**A:** Indefinitely in PostgreSQL. Implement a retention policy based on compliance requirements:

```sql
-- Delete audit entries older than 90 days
DELETE FROM audit_entries
WHERE timestamp < NOW() - INTERVAL '90 days';
```

---

## Related Documentation

- [Ingestion Service Architecture](../ingestion/IMPLEMENTATION-SUMMARY.md)
- [RabbitMQ Event Publishing](../ingestion/event-driven-architecture-proposal.md)

---

**Last Updated**: 2025-12-05
**Version**: 2.0 (Simplified - Event-driven architecture only)
