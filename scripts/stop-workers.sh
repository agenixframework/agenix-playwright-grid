#!/usr/bin/env bash

# ==============================================================================
# Agenix Playwright Grid - Stop Worker Pools
# ==============================================================================
# This script stops all worker pools while keeping core infrastructure running.
# This is useful for resource conservation or maintenance.
#
# Usage:
#   ./scripts/stop-workers.sh
#
# What happens:
#   - All workers are stopped and removed
#   - Core infrastructure continues running
#   - Network connections are cleaned up
#
# To restart workers:
#   ./scripts/start-workers.sh
#
# Documentation: specs/dynamic_worker_registration/phase-3-decoupled-deployment.md
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🛑 Stopping Agenix Playwright Grid - Worker Pools"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Show current worker counts before stopping
CHROMIUM_COUNT=$(docker ps --filter "name=worker-chromium" --format "{{.Names}}" | wc -l | tr -d ' ')
FIREFOX_COUNT=$(docker ps --filter "name=worker-firefox" --format "{{.Names}}" | wc -l | tr -d ' ')
WEBKIT_COUNT=$(docker ps --filter "name=worker-webkit" --format "{{.Names}}" | wc -l | tr -d ' ')
TOTAL_COUNT=$((CHROMIUM_COUNT + FIREFOX_COUNT + WEBKIT_COUNT))

if [ "$TOTAL_COUNT" -eq 0 ]; then
    echo "ℹ️  No workers are currently running"
    echo ""
    exit 0
fi

echo "Current worker counts:"
echo "  Chromium workers: $CHROMIUM_COUNT"
echo "  Firefox workers:  $FIREFOX_COUNT"
echo "  WebKit workers:   $WEBKIT_COUNT"
echo "  ────────────────────"
echo "  Total workers:    $TOTAL_COUNT"
echo ""

# Stop workers
echo "Stopping worker pools..."
docker compose -f docker-compose.workers.yml down

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Failed to stop worker pools"
    exit 1
fi

echo ""
echo "✅ Workers stopped successfully!"
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📊 Status"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  ✅ All workers stopped"
echo "  ✅ Core infrastructure still running"
echo "  ✅ Network cleaned up"
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Next Steps"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  1. Restart workers:   ./scripts/start-workers.sh"
echo "  2. Check core status: docker compose ps"
echo "  3. Stop everything:   docker compose --profile infrastructure --profile core down"
echo ""
