#!/bin/bash
set -e

# ==============================================================================
# Complete Docker & Local Process Cleanup Script
# ==============================================================================
# Performs a complete cleanup of ALL Docker resources AND local processes:
#   - Stops all Docker services (infrastructure, core, monitoring, workers)
#   - Stops all local .NET processes (hub, dashboard, workers, ingestion, housekeeping)
#   - Removes all containers
#   - Removes all volumes (including persistent data)
#   - Removes all networks
#   - Removes all images built for this project
#   - Cleans up dangling resources
#
# ⚠️  WARNING: This is a DESTRUCTIVE operation!
#     - All data will be lost (database, artifacts, logs)
#     - All built images will be deleted
#     - All running processes will be killed
#     - You will need to rebuild everything from scratch
#
# Usage:
#   ./scripts/compose-clean.sh
#
# To rebuild after cleanup:
#   ./scripts/start-infrastructure.sh
#   docker compose --profile core up -d --build
#   ./scripts/start-workers.sh
# ==============================================================================

echo "⚠️  WARNING: Complete Docker & Process Cleanup"
echo "=============================================="
echo "This will DELETE/STOP:"
echo "  - All Docker containers (infrastructure, core, monitoring, workers)"
echo "  - All local .NET processes (hub, dashboard, workers, ingestion, housekeeping)"
echo "  - All volumes (database, artifacts, logs, etc.)"
echo "  - All networks"
echo "  - All project images"
echo ""
echo "Press Ctrl+C to cancel, or Enter to continue..."
read

echo ""
echo "🧹 Starting complete cleanup..."
echo ""

# Stop all services (both Docker and local processes)
echo "📍 Step 1/7: Stopping all services..."
# Stop Docker services
docker compose --profile infrastructure --profile core --profile monitoring stop 2>/dev/null || true
# Stop local .NET processes (if started by run-local-dev-inline.sh)
echo "  - Stopping local .NET processes..."
pkill -f "dotnet.*hub.*--no-build" 2>/dev/null || true
pkill -f "dotnet.*dashboard.*--no-build" 2>/dev/null || true
pkill -f "dotnet.*ingestion.*--no-build" 2>/dev/null || true
pkill -f "dotnet.*housekeeping.*--no-build" 2>/dev/null || true
pkill -f "hub/bin/Debug" 2>/dev/null || true
pkill -f "dashboard/bin/Debug" 2>/dev/null || true
pkill -f "ingestion/bin/Debug" 2>/dev/null || true
pkill -f "housekeeping-service/bin/Debug" 2>/dev/null || true

# Stop workers (both Docker and local processes)
echo "📍 Step 2/7: Stopping workers..."
# Stop Docker workers (if using docker-compose.workers.yml)
# Note: Using -p flag to match the project name used when containers were started
docker compose -p agenix-reportportal -f docker-compose.workers.yml stop 2>/dev/null || true
# Stop local worker processes (if started by run-local-dev-inline.sh)
echo "  - Stopping local worker processes..."
pkill -f "dotnet.*worker.*--no-build" 2>/dev/null || true
pkill -f "worker/bin/Debug" 2>/dev/null || true

# Remove all containers
echo "📍 Step 3/7: Removing all containers..."
docker compose --profile infrastructure --profile core --profile monitoring down 2>/dev/null || true
docker compose -p agenix-reportportal -f docker-compose.workers.yml down 2>/dev/null || true

# Remove all volumes (⚠️  DATA LOSS!)
echo "📍 Step 4/7: Removing all volumes (⚠️  data loss)..."
docker compose --profile infrastructure --profile core --profile monitoring down -v 2>/dev/null || true
docker compose -p agenix-reportportal -f docker-compose.workers.yml down -v 2>/dev/null || true

# Remove all networks
echo "📍 Step 5/7: Removing networks..."
docker network rm agenix-reportportal-network 2>/dev/null || true
docker network rm agenix-reportportal_default 2>/dev/null || true

# Remove project images
echo "📍 Step 6/7: Removing project images..."
echo "  - Hub image..."
docker rmi agenix-reportportal-hub:latest 2>/dev/null || true
echo "  - Dashboard image..."
docker rmi agenix-reportportal-dashboard:latest 2>/dev/null || true
echo "  - Ingestion image..."
docker rmi agenix-reportportal-ingestion:latest 2>/dev/null || true
echo "  - Housekeeping image..."
docker rmi agenix-reportportal-housekeeping:latest 2>/dev/null || true
echo "  - Worker images..."
# Remove all worker image variations (multiple naming patterns exist)
docker rmi agenix-reportportal-worker-chromium:latest 2>/dev/null || true
docker rmi agenix-reportportal-worker-firefox:latest 2>/dev/null || true
docker rmi agenix-reportportal-worker-webkit:latest 2>/dev/null || true
docker rmi agenix-reportportal-workers-worker-chromium:latest 2>/dev/null || true
docker rmi agenix-reportportal-workers-worker-firefox:latest 2>/dev/null || true
docker rmi agenix-reportportal-workers-worker-webkit:latest 2>/dev/null || true
docker rmi agenix-reportportal-worker:latest 2>/dev/null || true
docker rmi agenix-reportportal-worker1:latest 2>/dev/null || true
docker rmi agenix-reportportal-worker2:latest 2>/dev/null || true
docker rmi agenix-reportportal-worker3:latest 2>/dev/null || true
docker rmi agenix-playwright-grid-worker-chromium:latest 2>/dev/null || true
docker rmi agenix-playwright-grid-worker-firefox:latest 2>/dev/null || true
docker rmi agenix-playwright-grid-worker-webkit:latest 2>/dev/null || true
docker rmi agenix-playwright-grid-worker1:latest 2>/dev/null || true
docker rmi agenix-playwright-grid-worker2:latest 2>/dev/null || true
docker rmi agenix-playwright-grid-worker3:latest 2>/dev/null || true

# Clean up dangling resources
echo "📍 Step 7/7: Cleaning up dangling resources..."
docker system prune -f --volumes 2>/dev/null || true

echo ""
echo "✅ Complete cleanup finished!"
echo ""
echo "Removed:"
echo "  ✅ All containers (Docker)"
echo "  ✅ All local .NET processes (hub, dashboard, workers, ingestion, housekeeping)"
echo "  ✅ All volumes (pgdata, artifacts, dashboardkeys, rabbitmq_data, minio-data)"
echo "  ✅ All networks"
echo "  ✅ All project images"
echo "  ✅ Dangling resources"
echo ""
echo "To rebuild the environment:"
echo "  1. Start infrastructure:   ./scripts/start-infrastructure.sh"
echo "  2. Build and start core:   docker compose --profile core up -d --build"
echo "  3. Start workers:          ./scripts/start-workers.sh"
echo "  4. Start monitoring (opt): docker compose --profile monitoring up -d"
echo ""
echo "Or use the all-in-one script:"
echo "  ./scripts/run-local-dev-inline.sh"
