# Environment Variables Reference

**Agenix Playwright Grid** - Complete environment variable documentation

---

## Table of Contents

- [Naming Convention](#naming-convention)
- [Infrastructure Variables](#infrastructure-variables)
  - [PostgreSQL](#postgresql)
  - [Redis](#redis)
  - [RabbitMQ](#rabbitmq)
  - [MinIO](#minio-s3-compatible-object-storage)
  - [Traefik](#traefik-reverse-proxy--load-balancer)
  - [Playwright](#playwright)
- [Hub Service](#hub-service)
- [Worker Service](#worker-service)
- [Dashboard Service](#dashboard-service)
- [Ingestion Service](#ingestion-service)
- [Housekeeping Service](#housekeeping-service)
- [Artifact Storage](#artifact-storage)
- [Email/SMTP](#emailsmtp)
- [Logging & Observability](#logging--observability)
- [Integration Tests](#integration-tests)

---

## Naming Convention

### Service-Specific Variables
All service-specific variables use the pattern: `AGENIX_<SERVICE>_<CATEGORY>_<NAME>`

- **Hub**: `AGENIX_HUB_*`
- **Worker**: `AGENIX_WORKER_*`
- **Dashboard**: `AGENIX_DASHBOARD_*`
- **Ingestion**: `AGENIX_INGESTION_*`
- **Housekeeping**: `AGENIX_HOUSEKEEPING_*`
- **Artifacts**: `AGENIX_ARTIFACTS_*`

### Infrastructure Variables (No Prefix)
Infrastructure variables use standard naming conventions without the `AGENIX_` prefix:

- **PostgreSQL**: `POSTGRES_*`
- **Redis**: `REDIS_*`
- **RabbitMQ**: `RABBITMQ_*`
- **MinIO**: `MINIO_*`
- **SMTP**: `SMTP_*`
- **Playwright**: `PLAYWRIGHT_VERSION`
- **Logging**: `LOG_LEVEL`, `OTEL_*`

---

## Infrastructure Variables

### PostgreSQL

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `POSTGRES_CONNECTION_STRING` | PostgreSQL connection string (used by Hub and Ingestion) | Required | `Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid;Pooling=true;Maximum Pool Size=100` |
| `POSTGRES_USER` | PostgreSQL username (docker-compose only) | `postgres` | `postgres` |
| `POSTGRES_PASSWORD` | PostgreSQL password (docker-compose only) | `postgres` | `postgres` |
| `POSTGRES_DB` | PostgreSQL database name (docker-compose only) | `playwrightgrid` | `playwrightgrid` |

**Notes:**
- `POSTGRES_CONNECTION_STRING` is used by Hub and Ingestion services
- `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` are only used by docker-compose to initialize the PostgreSQL container
- Always enable connection pooling for production: `Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100`

---

### Redis

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `REDIS_URL` | Redis connection string (host:port) | Required | `localhost:6379` or `redis:6379` |

**Notes:**
- Used by Hub for pool state and artifact caching
- Used by Ingestion for log/command token deduplication
- Used by Workers for registration and heartbeat

---

### RabbitMQ

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `RABBITMQ_URL` | RabbitMQ AMQP connection URL | Required | `amqp://localhost:5672` or `amqp://rabbitmq:5672` |
| `RABBITMQ_USERNAME` | RabbitMQ username | `guest` | `guest` |
| `RABBITMQ_PASSWORD` | RabbitMQ password | `guest` | `guest` |
| `RABBITMQ_PREFETCH_COUNT` | Consumer prefetch count (QoS) | `100` | `100` |

**Notes:**
- Hub publishes events (logs, commands, audit) to RabbitMQ
- Ingestion service consumes events from RabbitMQ
- Prefetch count controls how many messages a consumer can process concurrently

---

### MinIO (S3-Compatible Object Storage)

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `MINIO_ENDPOINT` | MinIO server endpoint (host:port) | `localhost:9000` | `localhost:9000` or `minio:9000` |
| `MINIO_ACCESS_KEY` | MinIO access key (S3 Access Key ID) | `minioadmin` | `minioadmin` |
| `MINIO_SECRET_KEY` | MinIO secret key (S3 Secret Access Key) | `minioadmin` | `minioadmin` |
| `MINIO_USE_SSL` | Use HTTPS for MinIO connections | `false` | `false` or `true` |
| `MINIO_REGION` | MinIO region | `eu-central-1` | `eu-central-1` or `us-east-1` |
| `MINIO_BUCKET_NAME` | S3 bucket name for artifacts | `playwright-artifacts` | `playwright-artifacts` |
| `MINIO_PUBLIC_URL` | Public URL for pre-signed URLs | `http://localhost:9000` | `http://localhost:9000` |

**Notes:**
- MinIO is optional - set `AGENIX_ARTIFACTS_STORAGE_BACKEND=minio` to enable
- Default storage backend is `local` (filesystem)
- Web Console: http://localhost:9001 (login: minioadmin/minioadmin)

---

### Traefik (Reverse Proxy & Load Balancer)

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_TRAEFIK_ENABLED` | Enable Traefik integration | `true` | `true` or `false` |
| `AGENIX_TRAEFIK_HTTP_PORT` | HTTP entrypoint port | `80` | `80` or `8000` |
| `AGENIX_TRAEFIK_HTTPS_PORT` | HTTPS entrypoint port (production) | `443` | `443` or `8443` |
| `AGENIX_TRAEFIK_DASHBOARD_PORT` | Traefik dashboard port | `8080` | `8080` |
| `AGENIX_TRAEFIK_DASHBOARD_INSECURE` | Allow dashboard without auth | `true` | `true` (dev only!) |
| `AGENIX_TRAEFIK_LOG_LEVEL` | Traefik log level | `INFO` | `DEBUG`, `INFO`, `WARN`, `ERROR` |
| `AGENIX_TRAEFIK_ACCESS_LOGS` | Enable HTTP access logging | `false` | `true` or `false` |

**Domain Configuration (Local Development):**

| Variable | Description | Default |
|----------|-------------|---------|
| `AGENIX_TRAEFIK_DOMAIN_HUB` | Hub domain | `hub.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_DASHBOARD` | Dashboard domain | `dashboard.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_GRAFANA` | Grafana domain | `grafana.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_PROMETHEUS` | Prometheus domain | `prometheus.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_MINIO` | MinIO console domain | `minio.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_RABBITMQ` | RabbitMQ management domain | `rabbitmq.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_MAILPIT` | Mailpit UI domain | `mailpit.localhost` |
| `AGENIX_TRAEFIK_DOMAIN_INGESTION` | Ingestion service domain | `ingestion.localhost` |

**Production HTTPS (Let's Encrypt):**

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_TRAEFIK_ACME_EMAIL` | Let's Encrypt email address | - | `admin@your-domain.com` |
| `AGENIX_TRAEFIK_ACME_STAGING` | Use Let's Encrypt staging | `false` | `true` (for testing) |

**Notes:**
- Traefik provides domain-based routing instead of port-based access
- Docker Compose profile: `--profile infrastructure` (includes Traefik)
- Startup script automatically checks Traefik health when `AGENIX_TRAEFIK_ENABLED=true`
- Local development uses `*.localhost` domains (auto-resolves to 127.0.0.1)
- Production requires DNS configuration and `AGENIX_TRAEFIK_ACME_EMAIL` for HTTPS
- Dashboard: http://traefik.localhost:8080 (or http://localhost:8080)
- Documentation: `docs/traefik/README.md`

**Access URLs (with Traefik enabled):**
```bash
# Application Services
http://hub.localhost           # Hub API
http://dashboard.localhost     # Dashboard UI
http://ingestion.localhost     # Ingestion service

# Infrastructure Services
http://grafana.localhost       # Grafana dashboards
http://prometheus.localhost    # Prometheus metrics
http://minio.localhost         # MinIO console
http://rabbitmq.localhost      # RabbitMQ management
http://mailpit.localhost       # Mailpit email UI

# Traefik
http://traefik.localhost:8080  # Traefik dashboard
```

---

### Playwright

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `PLAYWRIGHT_VERSION` | Playwright version for workers | `1.54.2` | `1.54.2` |

**Notes:**
- Used by worker Docker builds to install specific Playwright version
- All workers must use the same version

---

### Logging & Observability

#### Chunked Logging (All Services)

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_LOGGING_CHUNKED_ENABLED` | Enable operation-based chunked logging with event codes | `true` | `true` or `false` |
| `AGENIX_LOGGING_CHUNK_MAX_EVENTS` | Maximum events per operation chunk before auto-flush | `1000` | `500`, `1000`, `2000` |
| `AGENIX_LOGGING_CHUNK_MAX_AGE_SECONDS` | Maximum age of operation chunk buffer before auto-flush | `60` | `30`, `60`, `120` |
| `AGENIX_LOGGING_EVENT_CODE_PREFIX` | Include event code prefix in log messages (e.g., [ITEM01]) | `true` | `true` or `false` |
| `AGENIX_LOGGING_INCLUDE_SOURCE_LOCATION` | Include source file/line information in logs | `false` | `true` or `false` |

**Notes:**
- Chunked logging provides operation-scoped logging with visual chunks and event codes
- When `AGENIX_LOGGING_CHUNKED_ENABLED=false`, chunked logger becomes a no-op wrapper
- Event codes follow the pattern: `ITEM01`, `POOL10`, `BROW15` (4 char prefix + 2 digit number)
- Auto-flush triggers when max events OR max age threshold is reached
- Source location adds overhead - only enable for debugging

**Example Log Output (Chunked Logging Enabled):**
```
╔═ Operation: StartTestItem  OperationId=a1b2c3d4
║ [INF][ITEM01] Test Item Started (PlaywrightHub) - launchId=123, name=Login Test
║ [DBG][ITEM02] Validating Request (PlaywrightHub) - itemType=Test
║ [INF][POOL10] Browser Borrowed (PlaywrightHub) - labelKey=app:chromium:prod, browserId=abc123
╚═ Operation: StartTestItem completed in 245ms - Success [KeyEvents: ITEM01, ITEM02, POOL10]
```

---

## Hub Service

### Core Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_URL` | Hub HTTP URL (for workers/dashboard) | Required | `http://localhost:5100` or `http://hub:5000` |
| `AGENIX_HUB_NODE_SECRET` | Shared secret for worker authentication | Required | `node-secret` |

**Notes:**
- `AGENIX_HUB_NODE_SECRET` must match `AGENIX_WORKER_NODE_SECRET` on all workers
- Hub listens on port 5100 in local dev (5000 conflicts with macOS AirPlay)

---

### Bootstrap Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_BOOTSTRAP_ENABLED` | Enable automatic admin user creation | `1` | `1` (enabled) or `0` (disabled) |
| `AGENIX_HUB_BOOTSTRAP_ADMIN_USER` | Bootstrap admin username | `admin` | `admin` |
| `AGENIX_HUB_BOOTSTRAP_ADMIN_PASSWORD` | Bootstrap admin password | `agenix-admin` | `agenix-admin` |
| `AGENIX_HUB_BOOTSTRAP_ADMIN_EMAIL` | Bootstrap admin email | Required | `agenix.admin@domain.com` |
| `AGENIX_HUB_BOOTSTRAP_DEFAULT_PROJECT` | Default project name | `admin_default` | `admin_default` |

**Notes:**
- Bootstrap runs once on first startup to create admin user
- Disabled automatically after successful bootstrap
- Change default password in production!

---

### Background Services Configuration

#### NodeSweeperService
Sweeps stale nodes and prunes stale available entries from Redis.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_NODE_SWEEP_INTERVAL_SECONDS` | Sweep interval | `20` | `20` |

---

#### RedisPoolStateBroadcastService
Broadcasts browser pool state to SignalR clients for real-time dashboard updates.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_POOL_BROADCAST_INTERVAL_SECONDS` | Broadcast interval | `2` | `2` |

---

#### BorrowTtlSweeperService
Auto-returns borrowed browser sessions whose TTL/lease expired.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_BORROW_TTL_SWEEP_SECONDS` | Sweep interval | `10` | `10` |

---

#### BrowserAutoStopService
Auto-stops inactive test items and releases browsers.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_CLEANUP_INTERVAL_MINUTES` | Cleanup interval | `5` | `5` |
| `AGENIX_HUB_INACTIVITY_THRESHOLD_MINUTES` | Inactivity timeout before auto-stop | `30` | `30` |
| `AGENIX_HUB_MAX_RUN_DURATION_HOURS` | Maximum test run duration before force-stop | `3` | `3` |
| `AGENIX_HUB_CLEANUP_BATCH_SIZE` | Batch size for cleanup operations | `50` | `50` |

**Notes:**
- Test items inactive for `INACTIVITY_THRESHOLD` minutes are auto-stopped
- Test items running longer than `MAX_RUN_DURATION` hours are force-stopped
- Released browsers return to the pool for reuse

---

#### LaunchAutoStopService
Auto-stops inactive launches when all test items complete.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HUB_LAUNCH_CLEANUP_INTERVAL_MINUTES` | Cleanup interval | `1` | `1` |
| `AGENIX_HUB_LAUNCH_CLEANUP_BATCH_SIZE` | Batch size for cleanup operations | `20` | `20` |
| `AGENIX_HUB_LAUNCH_CLEANUP_DEBUG` | Enable debug logging | `false` | `false` or `true` |

**Notes:**
- Automatically finishes launches when all test items complete
- Debug mode logs detailed information about cleanup decisions

---

## Worker Service

### Core Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_WORKER_NODE_ID` | Unique worker identifier | Required | `worker1`, `worker2`, `worker3` |
| `AGENIX_WORKER_NODE_SECRET` | Shared secret for Hub authentication | Required | `node-secret` |
| `AGENIX_WORKER_NODE_NODE_SECRET` | Secret for node-to-node communication | Required | `node-node-secret` |
| `AGENIX_WORKER_POOL_CONFIG` | Browser pool configuration | Required | `AppB:Chromium:UAT=3` or `AppB:Firefox:UAT=2,AppB:Webkit:UAT=2` |

**Notes:**
- `AGENIX_WORKER_NODE_SECRET` must match `AGENIX_HUB_NODE_SECRET`
- Pool config format: `App:Browser:Env=Count[,App:Browser:Env=Count]`
- Each worker can manage multiple browser pools

---

### Registration Verification

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_WORKER_REGISTRATION_VERIFICATION_INTERVAL_SECONDS` | Interval (seconds) for periodic worker registration verification | `300` | `300` (5 minutes), `600` (10 minutes) |

**Notes:**
- Detects if worker was removed from hub's worker list (e.g., after hub restart or laptop sleep/wake)
- Works alongside fast-path gap detection in heartbeat service
- Gap detection triggers immediately on timer discrepancies (e.g., system sleep)
- Periodic verification provides redundant safety net for missed gap detections
- Default of 300 seconds (5 minutes) balances responsiveness with API load
- Prometheus metrics track both detection paths: `gap_detection` and `periodic_verification`

---

### Browser Health Checks

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_WORKER_HEALTH_CHECK_ENABLED` | Enable browser health checks via Playwright protocol | `false` | `true` (enabled), `false` (disabled) |
| `AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS` | Interval (seconds) between health check runs | `30` | `30` (30 seconds), `60` (1 minute) |
| `AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS` | Timeout (seconds) for individual health check | `5` | `5`, `10` |
| `AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD` | Consecutive failures before triggering recycle | `3` | `3`, `5` |

**Notes:**
- Health checks are **opt-in** (disabled by default) to avoid performance impact
- When enabled, worker periodically sends `Browser.version` protocol commands via WebSocket
- Detects hung/unresponsive browsers before client requests fail
- Skips browsers with active client connections (avoids interrupting active tests)
- Tracks consecutive failures per browserId to avoid false positives from transient errors
- Triggers browser recycling by setting Redis recycle flag: `recycle:{browserId}` → `"health_check_failed"`
- ReconcileLoop picks up the flag and replaces the browser
- Prometheus metrics exported:
  - `worker_browser_health_check_total{node, label, browser, result}` - Counter (success/failure)
  - `worker_browser_health_check_duration_seconds{node}` - Histogram (latency buckets: 0.05s to 10s)
- Interval should be >= 10 seconds to avoid excessive overhead
- Timeout should be < interval to prevent overlapping checks
- Failure threshold of 3 (default) requires 3 consecutive failures before recycling
- Higher thresholds reduce false positives but increase detection time

**Use Cases:**
- **Browser Hangs**: Detect when browser process is alive but unresponsive (e.g., deadlock, memory leak)
- **WebSocket Issues**: Detect when WebSocket connection is open but browser isn't responding
- **Proactive Recycling**: Replace unhealthy browsers before client requests timeout

**Example Configuration (Production):**
```bash
AGENIX_WORKER_HEALTH_CHECK_ENABLED=true
AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS=30
AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS=5
AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD=3
```

**Example Configuration (Development/Testing):**
```bash
# More aggressive health checking for faster detection during testing
AGENIX_WORKER_HEALTH_CHECK_ENABLED=true
AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS=10
AGENIX_WORKER_HEALTH_CHECK_TIMEOUT_SECONDS=3
AGENIX_WORKER_HEALTH_CHECK_FAILURE_THRESHOLD=2
```

**Log Messages (in `/tmp/pg-worker-background-*.log`):**
```
# Startup (once per worker)
[HealthCheck][worker1] Browser health checks enabled (interval=30s, timeout=5s, threshold=3)

# Every interval (e.g., every 30 seconds)
[HealthCheck][worker1] Checked 3 browsers: 3 healthy, 0 unhealthy, 0 recycled

# On failure
[HealthCheck][worker2] Browser abc123 (chromium, myapp:chromium:staging) health check failed (failures=1/3)
[HealthCheck][worker2] Browser abc123 (chromium, myapp:chromium:staging) health check failed (failures=2/3)
[HealthCheck][worker2] Browser abc123 (chromium, myapp:chromium:staging) failed 3 consecutive health checks - triggering recycle
```

**Viewing Logs:**
```bash
# Watch health check activity in real-time (all workers)
tail -f /tmp/pg-worker-background-*.log | grep '\[HealthCheck\]'

# Watch specific worker (e.g., worker1)
tail -f /tmp/pg-worker-background-*.log | grep '\[HealthCheck\]\[worker1\]'
```

---

### ReconcileLoop Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS` | Interval (seconds) between ReconcileLoop checks for browser process health and recycle flags | `5` | `1` (aggressive), `5` (default), `10` (low-priority) |

**Notes:**
- ReconcileLoop is the core background service that maintains browser pool health
- Runs continuously on each worker, checking for:
  - Browser process crashes (via process ID validation)
  - Recycle flags set by health checks (Redis key `recycle:{browserId}`)
  - Stale browser sessions exceeding idle timeout
- Lower intervals provide faster response to failures but increase CPU usage
- Clamped to 1-60 seconds range for safety
- Default of 5 seconds balances responsiveness with resource usage
- Interacts with adaptive polling (Phase 3):
  - This value sets the **base interval** for normal operation
  - Adaptive polling dynamically adjusts interval based on detected issues
  - When issues detected, interval temporarily decreases for faster response
  - When stable, interval increases (up to configured maximum) to reduce overhead
- Prometheus metrics track ReconcileLoop activity:
  - `worker_browser_recycled_total{node, label, reason}` - Counter of recycled browsers by reason

**Use Cases:**
- **Aggressive (1s)**: Development/testing environments with frequent browser recycling
- **Default (5s)**: Production environments with balanced responsiveness
- **Conservative (10s+)**: Low-priority environments or resource-constrained systems

**Example Configuration:**
```bash
# Production (balanced)
AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS=5

# Development (fast recovery)
AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS=1

# Low-priority (resource-constrained)
AGENIX_WORKER_RECONCILE_LOOP_INTERVAL_SECONDS=10
```

**Log Messages (in `/tmp/pg-worker-background-*.log`):**
```
# Startup (once per worker)
[ReconcileLoop][worker1] Starting browser pool reconciliation loop (interval=5s)

# Every interval (e.g., every 5 seconds)
[ReconcileLoop][worker1] Checked 3 browsers: 3 healthy, 0 replaced

# On recycle flag detection
[ReconcileLoop][worker2] Browser abc123 marked for recycle (reason=health_check_failed)
[ReconcileLoop][worker2] Recycled browser abc123 (chromium, myapp:chromium:staging) successfully
```

**Viewing Logs:**
```bash
# Watch ReconcileLoop activity in real-time (all workers)
tail -f /tmp/pg-worker-background-*.log | grep '\[ReconcileLoop\]'

# Watch specific worker (e.g., worker1)
tail -f /tmp/pg-worker-background-*.log | grep '\[ReconcileLoop\]\[worker1\]'
```

---

### WebSocket Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_WORKER_PUBLIC_WS_SCHEME` | WebSocket scheme (ws or wss) | `ws` | `ws` or `wss` |
| `AGENIX_WORKER_PUBLIC_WS_HOST` | WebSocket public host/IP | Required | `127.0.0.1` or `worker1.example.com` |
| `AGENIX_WORKER_PUBLIC_WS_PORT` | WebSocket public port | Required | `5200`, `5201`, `5202` |

**Notes:**
- Public WebSocket URL is advertised to clients for browser connections
- Use `wss` scheme for production with SSL/TLS

---

### Browser Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_WORKER_CHROMIUM_ARGS` | Chromium launch arguments | See below | `--disable-dev-shm-usage --no-sandbox` |
| `AGENIX_WORKER_FIREFOX_ARGS` | Firefox launch arguments | None | `--headless` |
| `AGENIX_WORKER_FIREFOX_PREFS` | Firefox preferences (JSON) | None | `{"network.dns.disablePrefetch":true}` |

**Default Chromium Args:**
```
--disable-dev-shm-usage --no-sandbox --disable-setuid-sandbox --no-proxy-server --disable-ipv6 --disable-quic --disable-http2 --disable-features=UseDNSHttpsSvcb
```

**Notes:**
- `--disable-dev-shm-usage` required in Docker (limited shared memory)
- `--no-sandbox` required in Docker (no privilege escalation)
- Firefox prefs must be valid JSON

---

## Dashboard Service

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_DASHBOARD_HUB_SIGNALR_URL` | Hub SignalR WebSocket URL | Required | `http://localhost:5100/ws` or `http://hub:5000/ws` |
| `AGENIX_DASHBOARD_PUBLIC_URL` | Dashboard public URL (for emails) | Required | `http://localhost:3001` or `https://dashboard.example.com` |

**Notes:**
- SignalR URL used for real-time updates (pool state, launches, test runs)
- Public URL used in password reset emails

---

## Ingestion Service

### Core Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_INGESTION_PORT` | HTTP port for health checks | `8082` | `8082` |

---

### Log Token Optimization
Reduces log storage by 90%+ through SHA256-based message deduplication.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_INGESTION_LOG_TOKEN_OPTIMIZATION_ENABLED` | Enable log deduplication | `true` | `true` or `false` |
| `AGENIX_INGESTION_LOG_TOKEN_TTL_SECONDS` | Redis TTL for tokens | `604800` (7 days) | `604800` |
| `AGENIX_INGESTION_LOG_TOKEN_CACHE_ENABLED` | Enable in-memory LRU cache | `false` | `false` or `true` |
| `AGENIX_INGESTION_LOG_TOKEN_CACHE_MAX_SIZE` | Max in-memory cache entries | `10000` | `10000` |

**Notes:**
- Duplicate log messages reference shared token instead of storing full text
- 7-day TTL ensures tokens expire after test retention period
- In-memory cache optional (reduces Redis queries but uses RAM)

---

### Command Token Optimization
Reduces command storage by 90%+ through SHA256-based command deduplication.

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_INGESTION_COMMAND_TOKEN_OPTIMIZATION_ENABLED` | Enable command deduplication | `true` | `true` or `false` |
| `AGENIX_INGESTION_COMMAND_TOKEN_TTL_SECONDS` | Redis TTL for tokens | `604800` (7 days) | `604800` |
| `AGENIX_INGESTION_COMMAND_TOKEN_CACHE_ENABLED` | Enable in-memory LRU cache | `false` | `false` or `true` |
| `AGENIX_INGESTION_COMMAND_TOKEN_CACHE_MAX_SIZE` | Max in-memory cache entries | `10000` | `10000` |

**Notes:**
- Similar to log tokens but for Playwright commands (click, fill, goto, etc.)

---

### Audit Processing

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_INGESTION_AUDIT_BATCH_SIZE` | Audit events per batch | `500` | `500` |
| `AGENIX_INGESTION_AUDIT_BATCH_TIMEOUT_MS` | Batch timeout (milliseconds) | `750` | `750` |

**Notes:**
- Audit events consumed from RabbitMQ and batch-written to PostgreSQL via COPY BINARY
- Larger batches = higher throughput but more memory usage

---

### Batching Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_INGESTION_BATCH_SIZE_TEST_ITEMS` | Test items per batch | `200` | `200` |
| `AGENIX_INGESTION_BATCH_SIZE_COMMANDS` | Commands per batch | `500` | `500` |
| `AGENIX_INGESTION_BATCH_SIZE_LOG_ITEMS` | Log items per batch | `300` | `300` |
| `AGENIX_INGESTION_BATCH_TIMEOUT_MS` | Batch timeout (milliseconds) | `1000` | `1000` |

**Notes:**
- PostgreSQL COPY BINARY used for high-throughput writes
- Timeout ensures batches flush even if size not reached

---

### Consumer Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_INGESTION_CONSUMER_CONCURRENCY` | Concurrent consumers per queue | `4` | `4` |
| `AGENIX_INGESTION_MAX_RETRY_ATTEMPTS` | Max retries for failed operations | `3` | `3` |
| `AGENIX_INGESTION_RETRY_DELAY_MS` | Delay between retries (milliseconds) | `1000` | `1000` |

**Notes:**
- Each consumer processes one queue (test-items, commands, log-items, audit)
- Total consumers = `CONCURRENCY * 4 queues` (e.g., 4 * 4 = 16 consumers)
- Retry with exponential backoff for transient errors

---

## Housekeeping Service

### Core Configuration

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HOUSEKEEPING_PORT` | HTTP port for health checks | `8083` | `8083` |

**Notes:**
- Standalone microservice for retention cleanup
- Handles launches, logs, artifacts, and audit entries
- Uses Redis leadership election for multi-instance deployment

---

### Retention Check Intervals

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HOUSEKEEPING_LAUNCH_RETENTION_CHECK_INTERVAL_HOURS` | Launch cleanup check interval (hours) | `6` | `6` |
| `AGENIX_HOUSEKEEPING_LOG_RETENTION_CHECK_INTERVAL_HOURS` | Log cleanup check interval (hours) | `1` | `1` |
| `AGENIX_HOUSEKEEPING_ATTACHMENT_RETENTION_CHECK_INTERVAL_HOURS` | Attachment cleanup check interval (hours) | `1` | `1` |
| `AGENIX_HOUSEKEEPING_AUDIT_RETENTION_CHECK_INTERVAL_HOURS` | Audit cleanup check interval (hours) | `24` | `24` |

**Notes:**
- **Launch retention**: Deletes complete launches with ALL descendants (test items, logs, artifacts) via CASCADE
- **Log retention**: Deletes log items + orphaned log_tokens + orphaned command_tokens
- **Attachment retention**: HARD DELETE from database + physical files (MinIO or local filesystem)
- **Audit retention**: Deletes audit entries older than retention period
- Per-project retention policies stored in Redis: `project:{key}:settings`
- Intervals control how frequently workers scan for old data

**Redis Settings Example:**
```bash
redis-cli SET "project:admin_default:settings" '{"keepLaunches":30,"keepLogs":7,"keepAttachments":7,"keepAudit":90}'
```

**Retention Hierarchy:**
- Attachments ≤ Logs ≤ Launches ≤ Audit
- Shorter intervals for frequently changing data (logs, attachments)
- Longer intervals for infrequent operations (launches, audit)

---

### Leadership Election

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_HOUSEKEEPING_LEADERSHIP` | Enable Redis-based leadership election | `true` | `true` or `false` |
| `AGENIX_HOUSEKEEPING_LEASE_SECONDS` | Leadership lease duration (seconds) | `30` | `30` |
| `AGENIX_HOUSEKEEPING_INSTANCE_ID` | Unique instance identifier | `housekeeping-1` | `housekeeping-1` or `{hostname}:{pid}` |

**Notes:**
- **Multi-instance deployment**: Only one instance performs cleanup at a time (prevents duplicate deletions)
- **Lease-based**: Leader acquires lease in Redis with TTL (auto-recovery if leader crashes)
- **Leader keys**:
  - `housekeeping:leader:launch_retention`
  - `housekeeping:leader:log_retention`
  - `housekeeping:leader:attachment_retention`
  - `housekeeping:leader:audit_retention`
- **Automatic failover**: If leader crashes, lease expires, new leader elected within lease duration
- **Single-instance mode**: Set `AGENIX_HOUSEKEEPING_LEADERSHIP=false` to disable (all instances run cleanup)

---

### Deletion Scope

**Launch Deletion:**
- Deletes launches with `finish_time < (NOW - keepLaunchesDays)`
- Cascade deletes: test_items (all types), log_items, test_artifacts, commands
- Database function: `delete_old_launches(project_key, cutoff_date)`
- Returns: Integer count of launches deleted

**Log Items Deletion:**
- Deletes log_items with `created_at < (NOW - keepLogsDays)`
- Orphaned token cleanup:
  - `log_tokens` with no remaining log_items references
  - `command_tokens` with no remaining command references
- Database function: `delete_old_log_items(project_key, cutoff_date)`
- Returns: JSONB with 3 counts: `{"log_items_deleted": N, "log_tokens_deleted": N, "command_tokens_deleted": N}`

**Attachments Deletion (Hard Delete):**
- Deletes test_artifacts with `created_at < (NOW - keepAttachmentsDays)`
- Database function returns artifact details (id, storage_path, file_name, file_size)
- Worker deletes physical files:
  - **MinIO/S3**: `s3://bucket/path` or `minio://bucket/path`
  - **Local**: Absolute file path (e.g., `/app/artifacts/...`)
- Graceful error handling (logs warning if file deletion fails, continues)
- Returns: JSONB array with deleted artifact details

**Audit Deletion:**
- Deletes audit_entries with `created_at < (NOW - keepAuditDays)`
- Database function: `delete_old_audit_entries(project_key, cutoff_date)`
- Returns: Integer count of audit entries deleted

---

## Artifact Storage

### Storage Backend

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_ARTIFACTS_STORAGE_BACKEND` | Storage backend (local or minio) | `local` | `local` or `minio` |
| `AGENIX_ARTIFACTS_STORAGE_PATH` | Base directory for local storage | Required | `/app/data/artifacts` or `/shared/artifacts` |
| `AGENIX_ARTIFACTS_MAX_SIZE_MB` | Max artifact file size | `100` | `100` |

**Notes:**
- **Local backend**: Stores artifacts on filesystem (use shared volume for multi-instance)
- **MinIO backend**: Stores artifacts in S3-compatible object storage (scalable, distributed)
- **Retention policy**: Configured per-project in Redis (`project:{key}:settings`), not via environment variables
- See Housekeeping Service section below for retention configuration

---

### Artifact Caching (Redis)

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_ARTIFACTS_CACHE_ENABLED` | Enable Redis-based artifact caching | `true` | `true` or `false` |
| `AGENIX_ARTIFACTS_CACHE_COMPRESSION_ENABLED` | Enable Gzip compression | `true` | `true` or `false` |
| `AGENIX_ARTIFACTS_CACHE_MAX_CONTENT_SIZE_MB` | Max content size to cache (MB) | `5` | `5` |
| `AGENIX_ARTIFACTS_CACHE_CONTENT_TTL_SECONDS` | Cache TTL for content | `3600` (1 hour) | `3600` |
| `AGENIX_ARTIFACTS_CACHE_METADATA_TTL_SECONDS` | Cache TTL for metadata | `3600` (1 hour) | `3600` |
| `AGENIX_ARTIFACTS_CACHE_PRESIGNED_URL_TTL_SECONDS` | Cache TTL for pre-signed URLs | `3000` (50 min) | `3000` |

**Notes:**
- Caches frequently accessed artifacts in Redis to reduce disk I/O
- Compression provides 2-5x size reduction (more for text, less for images)
- Artifacts larger than max size use streaming/pre-signed URLs instead

---

### Browser-Level Caching

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_ARTIFACTS_RESPONSE_CACHE_ENABLED` | Enable Cache-Control headers | `true` | `true` or `false` |

**Notes:**
- Enables 304 Not Modified responses for repeated requests
- Browser caches artifacts locally (reduces bandwidth)

---

### Artifact Prefetching

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `AGENIX_ARTIFACTS_PREFETCH_ENABLED` | Enable proactive cache warming | `true` | `true` or `false` |
| `AGENIX_ARTIFACTS_PREFETCH_MAX_CONCURRENCY` | Max concurrent prefetch operations | `5` | `5` |
| `AGENIX_ARTIFACTS_PREFETCH_MAX_PER_ITEM` | Max artifacts to prefetch per test item | `10` | `10` |

**Notes:**
- Prefetches artifacts when test items load (eliminates first-access cache miss)
- Runs in background, doesn't block API responses

---

## Email/SMTP

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `SMTP_HOST` | SMTP server hostname | `localhost` | `localhost` or `smtp.gmail.com` |
| `SMTP_PORT` | SMTP server port | `1025` | `1025` (Mailpit) or `587` (TLS) |
| `SMTP_USERNAME` | SMTP username | `test@localhost` | `test@localhost` or `your-email@gmail.com` |
| `SMTP_PASSWORD` | SMTP password | `test` | `test` or `app-specific-password` |
| `SMTP_FROM_EMAIL` | Sender email address | Required | `noreply@playwrightgrid.local` |
| `SMTP_FROM_NAME` | Sender display name | `Agenix Playwright Grid` | `Agenix Playwright Grid` |
| `SMTP_USE_SSL` | Use SSL/TLS encryption | `false` | `false` or `true` |

**Notes:**
- Local dev: Use Mailpit (`docker compose up mailpit -d`) - view emails at http://localhost:8025
- Gmail: Requires 2FA + App Password - https://myaccount.google.com/apppasswords
- SendGrid: Use `apikey` as username, API key as password

---

## Logging & Observability

| Variable | Description | Default | Example |
|----------|-------------|---------|---------|
| `LOG_LEVEL` | Global log level | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error` |
| `LOG_LEVEL_OVERRIDES` | Per-namespace log level overrides | See below | `Microsoft.AspNetCore=Warning` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry OTLP endpoint | None | `http://localhost:4318` |
| `OTEL_SERVICE_NAME` | Service name for telemetry | Service-specific | `hub`, `worker1`, `ingestion` |

**Default Log Level Overrides:**
```
Microsoft.AspNetCore=Warning,System.Net.Http.HttpClient=Warning
```

**Notes:**
- OpenTelemetry optional (for Prometheus/Grafana integration)
- Service name auto-set per service in docker-compose

---

## Complete Example: Local Development

```bash
# Infrastructure
POSTGRES_CONNECTION_STRING=Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid;Pooling=true;Maximum Pool Size=100
REDIS_URL=localhost:6379
RABBITMQ_URL=amqp://localhost:5672
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
PLAYWRIGHT_VERSION=1.54.2

# Hub
AGENIX_HUB_URL=http://localhost:5100
AGENIX_HUB_NODE_SECRET=node-secret
AGENIX_HUB_BOOTSTRAP_ENABLED=1
AGENIX_HUB_BOOTSTRAP_ADMIN_USER=admin
AGENIX_HUB_BOOTSTRAP_ADMIN_PASSWORD=agenix-admin
AGENIX_HUB_BOOTSTRAP_ADMIN_EMAIL=admin@example.com
AGENIX_HUB_BOOTSTRAP_DEFAULT_PROJECT=admin_default

# Workers
AGENIX_WORKER_NODE_SECRET=node-secret
AGENIX_WORKER_NODE_NODE_SECRET=node-node-secret
AGENIX_WORKER_PUBLIC_WS_SCHEME=ws
AGENIX_WORKER_PUBLIC_WS_HOST=127.0.0.1
AGENIX_WORKER_CHROMIUM_ARGS=--disable-dev-shm-usage --no-sandbox

# Dashboard
AGENIX_DASHBOARD_HUB_SIGNALR_URL=http://localhost:5100/ws
AGENIX_DASHBOARD_PUBLIC_URL=http://localhost:3001

# Ingestion
AGENIX_INGESTION_PORT=8082
AGENIX_INGESTION_LOG_TOKEN_OPTIMIZATION_ENABLED=true
AGENIX_INGESTION_COMMAND_TOKEN_OPTIMIZATION_ENABLED=true

# Artifacts
AGENIX_ARTIFACTS_STORAGE_BACKEND=local
AGENIX_ARTIFACTS_STORAGE_PATH=/app/data/artifacts
AGENIX_ARTIFACTS_CACHE_ENABLED=true

# SMTP
SMTP_HOST=localhost
SMTP_PORT=1025
SMTP_USERNAME=test@localhost
SMTP_PASSWORD=test
SMTP_FROM_EMAIL=noreply@playwrightgrid.local
SMTP_USE_SSL=false
```

---

## Complete Example: Production (Docker)

```bash
# Infrastructure
POSTGRES_CONNECTION_STRING=Host=postgres.prod.example.com;Port=5432;Username=pguser;Password=secure-password;Database=playwrightgrid;SSL Mode=Require;Pooling=true;Maximum Pool Size=200
REDIS_URL=redis.prod.example.com:6379
RABBITMQ_URL=amqps://rabbitmq.prod.example.com:5671
RABBITMQ_USERNAME=griduser
RABBITMQ_PASSWORD=secure-password
PLAYWRIGHT_VERSION=1.54.2

# Hub
AGENIX_HUB_URL=https://hub.playwrightgrid.example.com
AGENIX_HUB_NODE_SECRET=<generate-random-secret>
AGENIX_HUB_BOOTSTRAP_ENABLED=1
AGENIX_HUB_BOOTSTRAP_ADMIN_USER=admin
AGENIX_HUB_BOOTSTRAP_ADMIN_PASSWORD=<generate-strong-password>
AGENIX_HUB_BOOTSTRAP_ADMIN_EMAIL=admin@example.com

# Workers
AGENIX_WORKER_NODE_SECRET=<same-as-hub>
AGENIX_WORKER_PUBLIC_WS_SCHEME=wss
AGENIX_WORKER_PUBLIC_WS_HOST=worker1.playwrightgrid.example.com

# Dashboard
AGENIX_DASHBOARD_HUB_SIGNALR_URL=wss://hub.playwrightgrid.example.com/ws
AGENIX_DASHBOARD_PUBLIC_URL=https://dashboard.playwrightgrid.example.com

# Ingestion (horizontal scaling)
AGENIX_INGESTION_CONSUMER_CONCURRENCY=8

# Artifacts (MinIO)
AGENIX_ARTIFACTS_STORAGE_BACKEND=minio
MINIO_ENDPOINT=minio.prod.example.com:443
MINIO_USE_SSL=true
MINIO_ACCESS_KEY=<minio-access-key>
MINIO_SECRET_KEY=<minio-secret-key>

# SMTP (SendGrid)
SMTP_HOST=smtp.sendgrid.net
SMTP_PORT=587
SMTP_USERNAME=apikey
SMTP_PASSWORD=<sendgrid-api-key>
SMTP_FROM_EMAIL=noreply@example.com
SMTP_USE_SSL=true
```

---

## Migration from Old Variable Names

If migrating from pre-standardization codebase, use this mapping:

| Old Variable | New Variable |
|-------------|--------------|
| `HUB_NODE_SECRET` | `AGENIX_HUB_NODE_SECRET` |
| `HUB_RESULTS_BACKEND` | **DELETED** (always postgres) |
| `HUB_RESULTS_POSTGRES` | `POSTGRES_CONNECTION_STRING` |
| `HUB_URL` | `AGENIX_HUB_URL` |
| `HUB_SIGNALR` | `AGENIX_DASHBOARD_HUB_SIGNALR_URL` |
| `NODE_SECRET` | `AGENIX_WORKER_NODE_SECRET` |
| `NODE_ID` | `AGENIX_WORKER_NODE_ID` |
| `POOL_CONFIG` | `AGENIX_WORKER_POOL_CONFIG` |
| `DASHBOARD_URL` | `AGENIX_DASHBOARD_PUBLIC_URL` |
| `ARTIFACTS_STORAGE_PATH` | `AGENIX_ARTIFACTS_STORAGE_PATH` |
| `USE_LOG_TOKEN_OPTIMIZATION` | `AGENIX_INGESTION_LOG_TOKEN_OPTIMIZATION_ENABLED` |

**Note:** All service-specific variables now have `AGENIX_<SERVICE>_` prefix. Infrastructure variables (PostgreSQL, Redis, RabbitMQ, MinIO, SMTP) remain unprefixed.

---

## Security Best Practices

1. **Secrets Management**: Never commit secrets to version control
   - Use environment variables or secret management tools (AWS Secrets Manager, HashiCorp Vault)
   - Generate strong random values for `AGENIX_HUB_NODE_SECRET`, `AGENIX_WORKER_NODE_NODE_SECRET`

2. **PostgreSQL**:
   - Use SSL/TLS in production: `SSL Mode=Require`
   - Create dedicated database user with minimal privileges
   - Enable connection pooling: `Pooling=true;Maximum Pool Size=100`

3. **RabbitMQ**:
   - Use TLS in production: `amqps://`
   - Create dedicated vhost and user with minimal privileges
   - Enable authentication: never use guest/guest in production

4. **MinIO**:
   - Generate strong access/secret keys
   - Use HTTPS in production: `MINIO_USE_SSL=true`
   - Configure bucket policies for least privilege

5. **SMTP**:
   - Use app-specific passwords (Gmail) or API keys (SendGrid)
   - Enable SSL/TLS: `SMTP_USE_SSL=true`
   - Validate sender domain (SPF, DKIM, DMARC)

---

## Troubleshooting

### Issue: "RABBITMQ_URL environment variable is required"
**Solution**: Ensure `RABBITMQ_URL` is set. RabbitMQ is now mandatory for event-driven architecture.

### Issue: "Failed to connect to PostgreSQL"
**Solution**:
- Verify `POSTGRES_CONNECTION_STRING` is correct
- Check PostgreSQL is running and accessible
- Test connection: `psql "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=playwrightgrid"`

### Issue: Workers not registering with Hub
**Solution**:
- Verify `AGENIX_HUB_NODE_SECRET` matches `AGENIX_WORKER_NODE_SECRET`
- Check `AGENIX_HUB_URL` is accessible from workers
- Review worker logs for connection errors

### Issue: Dashboard not updating in real-time
**Solution**:
- Verify `AGENIX_DASHBOARD_HUB_SIGNALR_URL` is correct (e.g., `http://hub:5000/ws`)
- Check browser console for SignalR connection errors
- Ensure Hub SignalR endpoint is accessible

### Issue: High Redis memory usage
**Solution**:
- Reduce `AGENIX_ARTIFACTS_CACHE_MAX_CONTENT_SIZE_MB` (cache smaller artifacts)
- Reduce `AGENIX_ARTIFACTS_CACHE_CONTENT_TTL_SECONDS` (expire cached content faster)
- Disable prefetching: `AGENIX_ARTIFACTS_PREFETCH_ENABLED=false`
- Monitor with: `GET /api/admin/cache/artifacts/health`

---

## Integration Tests

Configuration for the integration test suite (`Agenix.PlaywrightGrid.Integration.Tests`).

**Prerequisites:**
- Docker Compose infrastructure must be running before tests start
- Use: `docker compose --profile infrastructure --profile core up -d`
- Or use the automated script: `./scripts/run-docker-compose-test.sh`

**Important Notes:**
- Tests use docker-compose for service lifecycle management (Testcontainers removed)
- Integration tests connect to existing Hub/Workers instead of starting containers programmatically
- Hub always uses PostgreSQL backend (Redis backend support removed)
- Tests reuse existing infrastructure variables (POSTGRES_*, REDIS_URL, etc.)

### Hub Health Check

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS` | Maximum time to wait for Hub health endpoint | `120` | No |
| `AGENIX_TESTS_HEALTH_POLL_INTERVAL_SECONDS` | Poll interval for health checks (seconds) | `1.0` | No |

**Example:**
```bash
# Wait up to 2 minutes for Hub to become healthy, polling every second
AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS=120
AGENIX_TESTS_HEALTH_POLL_INTERVAL_SECONDS=1.0
```

### Worker Pool Readiness

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `AGENIX_TESTS_WORKER_TIMEOUT_SECONDS` | Maximum time to wait for workers to register | `60` | No |
| `AGENIX_TESTS_EXPECTED_WORKERS` | Number of workers expected to register | `3` | No |

**Example:**
```bash
# Wait up to 1 minute for 3 workers to join the pool
AGENIX_TESTS_WORKER_TIMEOUT_SECONDS=60
AGENIX_TESTS_EXPECTED_WORKERS=3
```

### Running Integration Tests

**Option 1: Automated Script (Recommended)**
```bash
# Run tests with automatic service startup and cleanup
./scripts/run-docker-compose-test.sh

# Keep services running after tests (for debugging)
./scripts/run-docker-compose-test.sh --keep-running

# Run specific tests
./scripts/run-docker-compose-test.sh --filter "Name~History"

# Skip service startup (services already running)
./scripts/run-docker-compose-test.sh --skip-startup
```

**Option 2: Manual Approach**
```bash
# 1. Start infrastructure and core services
docker compose --profile infrastructure --profile core up -d

# 2. Wait for services to be ready (script does this automatically)
curl -f http://localhost:5100/health

# 3. Run tests
dotnet test Agenix.PlaywrightGrid.Integration.Tests/Agenix.PlaywrightGrid.Integration.Tests.csproj

# 4. Stop services (optional)
docker compose --profile infrastructure --profile core down
```

### Test Environment Workflow

The `TestEnvironment.cs` NUnit SetUpFixture performs the following setup before all tests:

1. **Read Configuration**: Load timeout/polling values from `AGENIX_TESTS_*` environment variables
2. **Wait for Hub Health**: Poll `http://localhost:5100/health` until successful or timeout
3. **Wait for Workers**: Poll `/diagnostics` endpoint to count registered workers
4. **Verify Browser Capacity**: Check that browser pools have capacity available
5. **Set HUB_URL**: Export `HUB_URL` environment variable for tests to use

**No Cleanup Required**: Docker Compose manages container lifecycle, no programmatic cleanup needed.

### Troubleshooting

**Tests fail with "Hub not ready":**
- Check Hub logs: `docker compose logs hub`
- Verify Hub is running: `docker compose ps`
- Increase timeout: `AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS=180`

**Tests fail with "Workers not ready":**
- Check worker logs: `docker compose logs worker1 worker2 worker3`
- Verify workers are running: `docker compose ps`
- Increase timeout: `AGENIX_TESTS_WORKER_TIMEOUT_SECONDS=90`
- Reduce expected workers: `AGENIX_TESTS_EXPECTED_WORKERS=2`

**Tests fail with "No browser capacity":**
- Wait longer for browser pools to initialize
- Check worker configuration: `WORKER1_POOL_CONFIG`, `WORKER2_POOL_CONFIG`, etc.
- Verify browser types match test requirements

---

## Support

For issues, questions, or contributions:
- **GitHub Issues**: https://github.com/agenixframework/agenix-playwright-grid/issues
- **Documentation**: https://docs.agenix.io/playwright-grid
- **License**: Apache-2.0
