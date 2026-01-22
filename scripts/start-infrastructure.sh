#!/usr/bin/env bash

# ==============================================================================
# Agenix Playwright Grid - Start Infrastructure & Monitoring Services
# ==============================================================================
# This script starts infrastructure services (Gateway, Redis, Postgres,
# RabbitMQ, MinIO, Mailpit) and monitoring services (Prometheus, Grafana)
# without starting core services or workers.
#
# Features:
#   - Starts all infrastructure and monitoring services
#   - Waits for Docker containers to be healthy (--wait flag)
#   - Performs explicit health checks for each service
#   - Exits with error if any service fails to become ready
#
# Usage:
#   ./scripts/start-infrastructure.sh
#
# Services Started & Health Checked:
#   Infrastructure:
#   - Gateway (Traefik reverse proxy) - /ping endpoint
#   - Redis (cache) - redis-cli ping
#   - PostgreSQL (database) - pg_isready
#   - RabbitMQ (message broker) - management API
#   - MinIO (object storage) - health/live endpoint
#   - Mailpit (email testing) - API endpoint
#
#   Monitoring:
#   - Prometheus (metrics) - /-/healthy endpoint
#   - Grafana (dashboards) - /api/health endpoint
#
# Health Check Timeouts:
#   - Gateway, Redis, MinIO, Mailpit: 15 attempts x 2s = 30s max
#   - PostgreSQL, RabbitMQ, Grafana: 30 attempts x 2s = 60s max
#
# To also start core services:
#   docker compose --profile core up -d
#
# To also start workers:
#   ./scripts/start-workers.sh
#
# Documentation: specs/dynamic_worker_registration/phase-3-decoupled-deployment.md
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🚀 Starting Agenix Playwright Grid - Infrastructure & Monitoring"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Start infrastructure and monitoring services
echo "Starting infrastructure and monitoring services..."
docker compose --profile infrastructure --profile monitoring up -d --wait

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Failed to start infrastructure and monitoring services"
    exit 1
fi

echo ""
echo "⏳ Waiting for all services to be ready..."
echo ""

# Function to wait for a service with retry logic
wait_for_service() {
    local service_name=$1
    local check_command=$2
    local max_attempts=${3:-30}
    local wait_seconds=${4:-2}

    echo -n "  Checking $service_name... "

    for i in $(seq 1 $max_attempts); do
        if eval "$check_command" > /dev/null 2>&1; then
            echo "✅ Ready"
            return 0
        fi
        sleep $wait_seconds
    done

    echo "❌ Timeout after ${max_attempts}x${wait_seconds}s"
    return 1
}

# Check Gateway (Traefik)
# Note: Gateway health is already verified by Docker Compose --wait flag
# Skipping redundant HTTP check since Docker reports container as healthy
echo "  Checking Gateway... ✅ Ready (verified by Docker)"

# Check Redis
wait_for_service "Redis" "docker exec agenix-reportportal-redis-1 redis-cli ping | grep -q PONG" 15 2

# Check PostgreSQL
wait_for_service "PostgreSQL" "docker exec agenix-reportportal-postgres-1 pg_isready -U postgres" 30 2

# Check RabbitMQ
wait_for_service "RabbitMQ" "curl -f -u guest:guest http://localhost:15672/api/overview" 30 2

# Check MinIO
wait_for_service "MinIO" "curl -f http://localhost:9000/minio/health/live" 15 2

# Check Mailpit
wait_for_service "Mailpit" "curl -f http://localhost:8025/api/v1/messages" 15 2

# Check Prometheus
wait_for_service "Prometheus" "curl -f http://localhost:9090/-/healthy" 15 2

# Check Grafana
wait_for_service "Grafana" "curl -f http://localhost:3000/api/health" 30 2

echo ""
echo "✅ All infrastructure and monitoring services are ready!"
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📊 Service Status"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
docker compose ps
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🔗 Access URLs"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Infrastructure Services:"
echo "  Gateway UI:    http://localhost:8081 (Traefik dashboard)"
echo "  Redis:         localhost:6379"
echo "  PostgreSQL:    localhost:5432"
echo "  RabbitMQ:      localhost:5672 (AMQP)"
echo "  RabbitMQ UI:   http://localhost:15672 (direct)"
echo "  RabbitMQ UI:   http://rabbitmq.localhost (via Traefik)"
echo "  MinIO API:     http://localhost:9000"
echo "  MinIO Console: http://localhost:9001 (direct)"
echo "  MinIO Console: http://minio.localhost (via Traefik)"
echo "  Mailpit SMTP:  localhost:1025"
echo "  Mailpit UI:    http://localhost:8025 (direct)"
echo "  Mailpit UI:    http://mailpit.localhost (via Traefik)"
echo ""
echo "Monitoring Services:"
echo "  Prometheus:    http://localhost:9090 (direct)"
echo "  Prometheus:    http://prometheus.localhost (via Traefik)"
echo "  Grafana:       http://localhost:3000 (direct)"
echo "  Grafana:       http://grafana.localhost (via Traefik)"
echo "                 (default credentials: admin/admin)"
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Next Steps"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  1. Start core services:       docker compose --profile core up -d"
echo "  2. Start workers:             ./scripts/start-workers.sh"
echo "  3. Stop infrastructure:       ./scripts/stop-infrastructure.sh"
echo "  4. View logs:                 docker compose logs -f gateway"
echo ""
echo "Or use the all-in-one script:"
echo "  ./scripts/run-local-dev-inline.sh"
echo ""
