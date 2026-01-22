#!/bin/bash
set -e

# ==============================================================================
# Stop Infrastructure & Monitoring Services Script
# ==============================================================================
# Stops all infrastructure services (Traefik, Redis, PostgreSQL, RabbitMQ,
# MinIO, Mailpit) and monitoring services (Prometheus, Grafana) while keeping
# core services and workers running.
#
# Usage:
#   ./scripts/stop-infrastructure.sh
#
# Services stopped:
#   Infrastructure:
#   - Traefik Gateway (reverse proxy)
#   - Redis (cache)
#   - PostgreSQL (database)
#   - RabbitMQ (message broker)
#   - MinIO (object storage)
#   - Mailpit (email testing)
#
#   Monitoring:
#   - Prometheus (metrics)
#   - Grafana (dashboards)
# ==============================================================================

echo "🛑 Stopping infrastructure and monitoring services..."
echo ""

# Stop infrastructure and monitoring profile services
docker compose --profile infrastructure --profile monitoring stop

echo ""
echo "✅ Infrastructure and monitoring services stopped!"
echo ""
echo "Status:"
echo "  Infrastructure:"
echo "    ✅ Traefik Gateway - stopped"
echo "    ✅ Redis - stopped"
echo "    ✅ PostgreSQL - stopped"
echo "    ✅ RabbitMQ - stopped"
echo "    ✅ MinIO - stopped"
echo "    ✅ Mailpit - stopped"
echo ""
echo "  Monitoring:"
echo "    ✅ Prometheus - stopped"
echo "    ✅ Grafana - stopped"
echo ""
echo "Core services (hub, dashboard, ingestion, housekeeping) remain running."
echo "Workers remain running."
echo ""
echo "To restart infrastructure and monitoring:"
echo "  ./scripts/start-infrastructure.sh"
