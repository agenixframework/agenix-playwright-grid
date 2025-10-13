# Ingestion Service - Quick Start

## 5-Minute Setup

### 1. Start Services
```bash
# Start RabbitMQ and Ingestion
docker-compose -f docker-compose.yml -f docker-compose.ingestion.yml up -d rabbitmq ingestion

# Verify running
docker-compose ps
```

### 2. Check Health
```bash
# Ingestion health
curl http://localhost:8080/health
# Expected: Healthy

# RabbitMQ health
docker exec rabbitmq rabbitmq-diagnostics ping
# Expected: Ping succeeded
```

### 3. View Logs
```bash
# Ingestion logs
docker logs -f ingestion

# Expected (Stage 1):
# [INFO] Consumer disabled (ENABLE_CONSUMER=false), not processing messages
```

### 4. Access RabbitMQ UI
```bash
open http://localhost:15672
# Username: guest
# Password: guest
```

## Configuration

### Enable Consumer (Stage 3)
```bash
# Edit docker-compose.ingestion.yml:
ENABLE_CONSUMER=true

# Restart
docker-compose -f docker-compose.yml -f docker-compose.ingestion.yml restart ingestion

# Verify consuming
docker logs ingestion | grep "Started consuming"
```

### Tune Performance
```bash
# High throughput (edit .env or docker-compose):
BATCH_SIZE_COMMANDS=1000
CONSUMER_CONCURRENCY=8

# Low latency:
BATCH_TIMEOUT_MS=200
BATCH_SIZE_COMMANDS=100
```

## Monitoring

```bash
# Metrics
curl http://localhost:9091/metrics

# Queue depth
docker exec rabbitmq rabbitmqctl list_queues

# Database check
docker exec postgres psql -U postgres -d playwrightgrid \
  -c "SELECT COUNT(*) FROM test_items;"
```

## Troubleshooting

### Service Won't Start
```bash
# Check logs
docker logs ingestion

# Common issues:
# - RabbitMQ not ready (wait 10s)
# - Postgres not ready (wait 10s)
# - Port conflict (8080 already used)
```

### Queue Building Up
```bash
# Scale up
docker-compose -f docker-compose.yml -f docker-compose.ingestion.yml up -d --scale ingestion=4

# Increase batch size
BATCH_SIZE_COMMANDS=1000
```

### Stop Services
```bash
# Stop ingestion only
docker-compose -f docker-compose.yml -f docker-compose.ingestion.yml stop ingestion

# Stop all
docker-compose -f docker-compose.yml -f docker-compose.ingestion.yml down
```

## Next Steps

1. Read `README.md` for features
2. Read `IMPLEMENTATION-SUMMARY.md` for architecture
3. Read `docs/ingestion-service-deployment.md` for production deployment
