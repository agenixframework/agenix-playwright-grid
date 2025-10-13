#!/usr/bin/env bash

# ==============================================================================
# Agenix Playwright Grid - Start Worker Pools
# ==============================================================================
# This script starts worker pools independently from core infrastructure.
# Workers connect to the Hub via the external network created by infrastructure.
#
# Usage:
#   ./scripts/start-workers.sh                    # Use .env.workers defaults
#   ./scripts/start-workers.sh --env-file .env.us # Use custom env file
#
# Prerequisites:
#   - Core infrastructure must be running (./scripts/start-infrastructure.sh)
#   - External network 'agenix-reportportal-network' must exist
#
# Documentation: specs/dynamic_worker_registration/phase-3-decoupled-deployment.md
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

# Parse command-line arguments
ENV_FILE=".env.workers"
while [[ $# -gt 0 ]]; do
    case $1 in
        --env-file)
            ENV_FILE="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--env-file <file>]"
            exit 1
            ;;
    esac
done

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🚀 Starting Agenix Playwright Grid - Worker Pools"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Configuration:"
echo "  Environment file: $ENV_FILE"
echo ""

# Check if core network exists
if ! docker network inspect agenix-reportportal-network &> /dev/null; then
    echo "❌ Error: Core network 'agenix-reportportal-network' not found"
    echo ""
    echo "Please start core infrastructure first:"
    echo "  ./scripts/start-infrastructure.sh"
    echo ""
    exit 1
fi

echo "✅ Core network found: agenix-reportportal-network"
echo ""

# Check if env file exists
if [ ! -f "$ENV_FILE" ]; then
    echo "⚠️  Warning: Environment file '$ENV_FILE' not found"
    echo "   Using values from .env (if present) or defaults"
    echo ""
fi

# Start workers
echo "Starting worker pools..."
if [ -f "$ENV_FILE" ]; then
    docker compose -f docker-compose.workers.yml --env-file "$ENV_FILE" up -d
else
    docker compose -f docker-compose.workers.yml up -d
fi

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Failed to start worker pools"
    exit 1
fi

# Wait a moment for workers to start
sleep 2

echo ""
echo "✅ Worker pools started successfully!"
echo ""

# Show worker status
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📊 Worker Status"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
docker compose -f docker-compose.workers.yml ps
echo ""

# Count workers
CHROMIUM_COUNT=$(docker ps --filter "name=worker-chromium" --format "{{.Names}}" | wc -l | tr -d ' ')
FIREFOX_COUNT=$(docker ps --filter "name=worker-firefox" --format "{{.Names}}" | wc -l | tr -d ' ')
WEBKIT_COUNT=$(docker ps --filter "name=worker-webkit" --format "{{.Names}}" | wc -l | tr -d ' ')
TOTAL_COUNT=$((CHROMIUM_COUNT + FIREFOX_COUNT + WEBKIT_COUNT))

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📈 Worker Counts"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Chromium workers: $CHROMIUM_COUNT"
echo "  Firefox workers:  $FIREFOX_COUNT"
echo "  WebKit workers:   $WEBKIT_COUNT"
echo "  ────────────────────"
echo "  Total workers:    $TOTAL_COUNT"
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Next Steps"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  1. Check registration:  curl http://localhost:5100/diagnostics | jq '.workers'"
echo "  2. Scale workers:       ./scripts/scale-workers.sh chromium 10"
echo "  3. View logs:           docker compose -f docker-compose.workers.yml logs -f"
echo "  4. Stop workers:        ./scripts/stop-workers.sh"
echo ""

# Wait for workers to register (give them 10 seconds)
echo "⏳ Waiting 10 seconds for workers to register with Hub..."
sleep 10

# Check registration
echo ""
echo "🔍 Checking worker registration..."
REGISTERED_COUNT=$(curl -sf http://localhost:5100/diagnostics 2>/dev/null | grep -o '"id":"worker[0-9_-]*"' | wc -l | tr -d ' ')

if [ -z "$REGISTERED_COUNT" ]; then
    REGISTERED_COUNT=0
fi

if [ "$REGISTERED_COUNT" -gt 0 ]; then
    echo "✅ $REGISTERED_COUNT worker(s) registered with Hub"
else
    echo "⚠️  No workers registered yet (they may still be starting)"
    echo "   Check registration: curl http://localhost:5100/diagnostics | jq '.workers'"
fi

echo ""
