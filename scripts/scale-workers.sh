#!/usr/bin/env bash

# ==============================================================================
# Agenix Playwright Grid - Scale Worker Pools
# ==============================================================================
# This script dynamically scales worker pools up or down without affecting
# core infrastructure or other worker types.
#
# Usage:
#   ./scripts/scale-workers.sh chromium 10    # Scale Chromium to 10 workers
#   ./scripts/scale-workers.sh firefox 5      # Scale Firefox to 5 workers
#   ./scripts/scale-workers.sh webkit 3       # Scale WebKit to 3 workers
#   ./scripts/scale-workers.sh all 5          # Scale all browsers to 5 workers
#
# Examples:
#   ./scripts/scale-workers.sh chromium 0     # Scale down to zero (stop all Chromium)
#   ./scripts/scale-workers.sh all 20         # Scale all browsers to 20 workers each
#
# Documentation: specs/dynamic_worker_registration/phase-3-decoupled-deployment.md
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

BROWSER_TYPE=$1
REPLICAS=$2

# Validate arguments
if [ -z "$BROWSER_TYPE" ] || [ -z "$REPLICAS" ]; then
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "⚠️  Usage Error"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "Usage: $0 <browser-type> <replicas>"
    echo ""
    echo "Browser types:"
    echo "  chromium   - Scale Chromium workers"
    echo "  firefox    - Scale Firefox workers"
    echo "  webkit     - Scale WebKit workers"
    echo "  all        - Scale all browser types"
    echo ""
    echo "Examples:"
    echo "  $0 chromium 10"
    echo "  $0 firefox 5"
    echo "  $0 webkit 3"
    echo "  $0 all 5"
    echo "  $0 chromium 0    # Scale to zero (stop all)"
    echo ""
    exit 1
fi

# Validate replicas is a number
if ! [[ "$REPLICAS" =~ ^[0-9]+$ ]]; then
    echo "❌ Error: Replicas must be a positive number (got: $REPLICAS)"
    exit 1
fi

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📈 Scaling Worker Pools"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Target:"
echo "  Browser type: $BROWSER_TYPE"
echo "  Replicas:     $REPLICAS"
echo ""

# Scale workers
case $BROWSER_TYPE in
    chromium)
        echo "Scaling Chromium workers to $REPLICAS..."
        docker compose -f docker-compose.workers.yml up --scale worker-chromium=$REPLICAS -d --no-recreate
        ;;
    firefox)
        echo "Scaling Firefox workers to $REPLICAS..."
        docker compose -f docker-compose.workers.yml up --scale worker-firefox=$REPLICAS -d --no-recreate
        ;;
    webkit)
        echo "Scaling WebKit workers to $REPLICAS..."
        docker compose -f docker-compose.workers.yml up --scale worker-webkit=$REPLICAS -d --no-recreate
        ;;
    all)
        echo "Scaling all browser types to $REPLICAS..."
        docker compose -f docker-compose.workers.yml up \
            --scale worker-chromium=$REPLICAS \
            --scale worker-firefox=$REPLICAS \
            --scale worker-webkit=$REPLICAS \
            -d --no-recreate
        ;;
    *)
        echo "❌ Error: Unknown browser type '$BROWSER_TYPE'"
        echo ""
        echo "Valid browser types: chromium, firefox, webkit, all"
        exit 1
        ;;
esac

if [ $? -ne 0 ]; then
    echo ""
    echo "❌ Failed to scale workers"
    exit 1
fi

# Wait a moment for changes to apply
sleep 2

echo ""
echo "✅ Scaling complete!"
echo ""

# Show updated worker status
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📊 Updated Worker Status"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
docker compose -f docker-compose.workers.yml ps
echo ""

# Count workers
CHROMIUM_COUNT=$(docker ps --filter "name=worker-chromium" --format "{{.Names}}" | wc -l | tr -d ' ')
FIREFOX_COUNT=$(docker ps --filter "name=worker-firefox" --format "{{.Names}}" | wc -l | tr -d ' ')
WEBKIT_COUNT=$(docker ps --filter "name=worker-webkit" --format "{{.Names}}" | wc -l | tr -d ' ')
TOTAL_COUNT=$((CHROMIUM_COUNT + FIREFOX_COUNT + WEBKIT_COUNT))

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📈 Current Worker Counts"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Chromium workers: $CHROMIUM_COUNT"
echo "  Firefox workers:  $FIREFOX_COUNT"
echo "  WebKit workers:   $WEBKIT_COUNT"
echo "  ────────────────────"
echo "  Total workers:    $TOTAL_COUNT"
echo ""

if [ "$REPLICAS" -eq 0 ]; then
    echo "💡 Tip: Workers scaled to zero. Core infrastructure is still running."
    echo "   Restart workers: ./scripts/start-workers.sh"
    echo ""
fi
