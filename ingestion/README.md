# Ingestion Service

High-performance message consumer for Agenix Playwright Grid. Consumes events from RabbitMQ and batch-writes to
PostgreSQL using COPY protocol.

## Architecture

```
RabbitMQ → Consumer (4x) → BatchWriter → PostgreSQL COPY
           ↓
        DLQ (on failure)
```

## Features

- **Batch Processing**: 10-100x faster than individual inserts
- **COPY Protocol**: Npgsql bulk inserts for maximum throughput
- **Dead Letter Queue**: Failed messages preserved for investigation
- **Retry Logic**: Exponential backoff (3 attempts)
- **Health Checks**: HTTP endpoint at /health
- **Metrics**: Prometheus metrics at /metrics
- **Graceful Shutdown**: Completes in-flight batches before exit

## Queues

- `agenix-test-platform.test-items` - Test item operations (batch: 200)
- `agenix-test-platform.commands` - Command logs (batch: 500, the highest volume)
- `agenix-test-platform.log-items` - Log items (batch: 300)
- `agenix-test-platform.dlq` - Dead letter queue

## Configuration

Copy `.env.example` to `.env` and adjust:

```bash
CONSUMER_CONCURRENCY=4
BATCH_SIZE_COMMANDS=500
```

## Running

**Development:**

```bash
dotnet run --project ingestion
```

**Docker:**

```bash
docker-compose -f docker-compose.yml -f docker-compose.ingestion.yml up
```

## Migration Strategy

### Stage 1: Validation (ENABLE_CONSUMER=false)

- Messages accumulate in queue
- No consumption, no writes
- Verify publishing works

### Stage 2: Processing (ENABLE_CONSUMER=true)

- Ingestion processes queue
- Verify batching performance
- Monitor metrics

## Monitoring

- **Health**: http://localhost:8080/health
- **Metrics**: http://localhost:9091/metrics
- **RabbitMQ UI**: http://localhost:15672 (guest/guest)

## Performance

Expected throughput:

- Test items: 1,000+/sec
- Commands: 10,000+/sec
- Log items: 5,000+/sec
