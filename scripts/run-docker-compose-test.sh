#!/usr/bin/env bash

# ==============================================================================
# Agenix Playwright Grid - Docker Compose Integration Test Runner
# ==============================================================================
# This script:
# 1. Starts all services via docker-compose (infrastructure + core profiles)
# 2. Waits for Hub health check
# 3. Waits for workers to register and browser pools to be ready
# 4. Runs integration tests
# 5. Stops services (unless --keep-running flag provided)
#
# Usage:
#   ./scripts/run-docker-compose-test.sh                   # Run tests with cleanup
#   ./scripts/run-docker-compose-test.sh --keep-running    # Keep services after tests
#   ./scripts/run-docker-compose-test.sh --skip-startup    # Services already running
#   ./scripts/run-docker-compose-test.sh --skip-tests      # Start services without running tests
#   ./scripts/run-docker-compose-test.sh --filter "Name~History"  # Run specific tests
#   ./scripts/run-docker-compose-test.sh --rebuild-changed # Auto-detect and rebuild changed services
#   ./scripts/run-docker-compose-test.sh --force-rebuild   # Force rebuild all services
#   ./scripts/run-docker-compose-test.sh --no-rebuild      # Skip building, use existing images
#
# Prerequisites:
#   - Docker and docker-compose installed
#   - .env file configured (or environment variables set)
#
# ==============================================================================

set -e  # Exit on error

# ==============================================================================
# PARSE COMMAND-LINE FLAGS
# ==============================================================================

KEEP_RUNNING=false
    SKIP_STARTUP=false
SKIP_TESTS=false
TEST_FILTER=""
REBUILD_MODE="auto"  # auto, force, none

while [[ $# -gt 0 ]]; do
    case $1 in
        --keep-running)
            KEEP_RUNNING=true
            shift
            ;;
        --skip-startup)
            SKIP_STARTUP=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            KEEP_RUNNING=true  # Implies --keep-running
            shift
            ;;
        --filter)
            TEST_FILTER="$2"
            shift 2
            ;;
        --rebuild-changed)
            REBUILD_MODE="auto"
            shift
            ;;
        --force-rebuild)
            REBUILD_MODE="force"
            shift
            ;;
        --no-rebuild)
            REBUILD_MODE="none"
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--keep-running] [--skip-startup] [--skip-tests] [--filter <pattern>]"
            echo "          [--rebuild-changed] [--force-rebuild] [--no-rebuild]"
            exit 1
            ;;
    esac
done

# ==============================================================================
# LOAD ENVIRONMENT VARIABLES FROM .ENV FILE
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# NOTE: For Docker Compose deployment, we do NOT load .env file
# The .env file is designed for local development (services running outside Docker)
# and contains localhost URLs which are incorrect for container-to-container communication.
# Docker Compose will use service names (redis:6379, postgres:5432) from compose file defaults.

echo "📋 Using Docker Compose service discovery for infrastructure connections"
echo "   (Services will connect via Docker network: redis, postgres, hub, etc.)"

# ==============================================================================
# CONFIGURATION
# ==============================================================================

HUB_URL="${AGENIX_HUB_URL:-http://localhost:5100}"
HEALTH_TIMEOUT="${AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS:-120}"
HEALTH_POLL_INTERVAL="${AGENIX_TESTS_HEALTH_POLL_INTERVAL_SECONDS:-1.0}"
WORKER_TIMEOUT="${AGENIX_TESTS_WORKER_TIMEOUT_SECONDS:-60}"
BUILD_TIMEOUT="${BUILD_TIMEOUT:-600}"  # 10 minutes

# Calculate expected workers from .env.workers if it exists
if [ -f "$PROJECT_ROOT/.env.workers" ]; then
    CHROMIUM_REPLICAS=$(grep "^WORKER_CHROMIUM_REPLICAS=" "$PROJECT_ROOT/.env.workers" | cut -d'=' -f2 || echo "5")
    FIREFOX_REPLICAS=$(grep "^WORKER_FIREFOX_REPLICAS=" "$PROJECT_ROOT/.env.workers" | cut -d'=' -f2 || echo "3")
    WEBKIT_REPLICAS=$(grep "^WORKER_WEBKIT_REPLICAS=" "$PROJECT_ROOT/.env.workers" | cut -d'=' -f2 || echo "2")
    EXPECTED_WORKERS=$((CHROMIUM_REPLICAS + FIREFOX_REPLICAS + WEBKIT_REPLICAS))
else
    # Default: 5 chromium + 3 firefox + 2 webkit = 10 (from docker-compose.workers.yml defaults)
    EXPECTED_WORKERS="${AGENIX_TESTS_EXPECTED_WORKERS:-10}"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🧪 Agenix Playwright Grid - Integration Test Runner"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Configuration:"
echo "  Hub URL:              $HUB_URL"
echo "  Health Timeout:       ${HEALTH_TIMEOUT}s"
echo "  Worker Timeout:       ${WORKER_TIMEOUT}s"
if [ -f "$PROJECT_ROOT/.env.workers" ]; then
    echo "  Expected Workers:     $EXPECTED_WORKERS (from .env.workers: ${CHROMIUM_REPLICAS} chromium + ${FIREFOX_REPLICAS} firefox + ${WEBKIT_REPLICAS} webkit)"
else
    echo "  Expected Workers:     $EXPECTED_WORKERS (defaults)"
fi
echo "  Keep Running:         $KEEP_RUNNING"
echo "  Skip Startup:         $SKIP_STARTUP"
echo "  Skip Tests:           $SKIP_TESTS"
echo "  Rebuild Mode:         $REBUILD_MODE"
if [ -n "$TEST_FILTER" ]; then
    echo "  Test Filter:          $TEST_FILTER"
fi
echo ""

# ==============================================================================
# HELPER FUNCTIONS FOR IMAGE BUILDING
# ==============================================================================

# Map changed file to service name
map_file_to_service() {
    local file="$1"

    # Direct service directory matches
    if [[ "$file" == hub/* ]] || [[ "$file" == hub/Dockerfile ]]; then
        echo "hub"
    elif [[ "$file" == dashboard/* ]] || [[ "$file" == dashboard/Dockerfile ]]; then
        echo "dashboard"
    elif [[ "$file" == ingestion/* ]] || [[ "$file" == ingestion/Dockerfile ]]; then
        echo "ingestion"
    elif [[ "$file" == housekeeping-service/* ]] || [[ "$file" == housekeeping-service/Dockerfile ]]; then
        echo "housekeeping"
    elif [[ "$file" == worker/* ]] || [[ "$file" == worker/Dockerfile ]]; then
        echo "worker"
    fi
}

# Detect changed services using git diff
detect_changed_services() {
    local services=()

    # Check if we're in a git repository
    if ! git rev-parse --git-dir > /dev/null 2>&1; then
        echo "⚠️  Not in a git repository - cannot detect changes" >&2
        return 1
    fi

    # Get list of changed files (staged + unstaged + untracked)
    local changed_files=$(git diff --name-only HEAD 2>/dev/null)
    local untracked_files=$(git ls-files --others --exclude-standard 2>/dev/null)
    local all_files="$changed_files"$'\n'"$untracked_files"

    # If no changes detected, check against origin
    if [ -z "$changed_files" ]; then
        changed_files=$(git diff --name-only origin/$(git rev-parse --abbrev-ref HEAD) 2>/dev/null || echo "")
    fi

    # Parse changed files and map to services
    local seen_services=()
    while IFS= read -r file; do
        [ -z "$file" ] && continue
        local service=$(map_file_to_service "$file")
        if [ -n "$service" ] && [[ ! " ${seen_services[@]} " =~ " ${service} " ]]; then
            services+=("$service")
            seen_services+=("$service")
        fi
    done <<< "$all_files"

    # Output services (space-separated)
    echo "${services[@]}"
}

# Build service images
build_service_images() {
    local services=("$@")

    if [ ${#services[@]} -eq 0 ]; then
        echo "✅ No services to rebuild"
        return 0
    fi

    echo "🔨 Building Docker images for changed services..."
    echo "   Services: ${services[*]}"
    echo ""

    local build_failed=false
    local built_services=()

    # Check if timeout command is available
    local use_timeout=false
    if command -v timeout >/dev/null 2>&1 || command -v gtimeout >/dev/null 2>&1; then
        use_timeout=true
        # Use gtimeout on macOS (from coreutils), timeout on Linux
        if command -v gtimeout >/dev/null 2>&1; then
            local timeout_cmd="gtimeout"
        else
            local timeout_cmd="timeout"
        fi
    fi

    for service in "${services[@]}"; do
        echo "📦 Building $service..."

        if [[ "$service" == "worker" ]]; then
            # Build worker image (shared by all worker types)
            local build_cmd="docker compose --progress=plain -p agenix-reportportal -f docker-compose.workers.yml build"

            if [ "$use_timeout" = true ]; then
                if $timeout_cmd $BUILD_TIMEOUT $build_cmd; then
                    echo ""
                    echo "   ✅ Worker image built successfully"
                    built_services+=("worker-chromium" "worker-firefox" "worker-webkit")
                else
                    echo ""
                    echo "   ❌ Worker image build failed"
                    build_failed=true
                fi
            else
                # No timeout available - build without timeout
                if $build_cmd; then
                    echo ""
                    echo "   ✅ Worker image built successfully"
                    built_services+=("worker-chromium" "worker-firefox" "worker-webkit")
                else
                    echo ""
                    echo "   ❌ Worker image build failed"
                    build_failed=true
                fi
            fi
        else
            # Build core service image
            local build_cmd="docker compose --progress=plain -p agenix-reportportal --profile infrastructure --profile core build $service"

            if [ "$use_timeout" = true ]; then
                if $timeout_cmd $BUILD_TIMEOUT $build_cmd; then
                    echo ""
                    echo "   ✅ $service built successfully"
                    built_services+=("$service")
                else
                    echo ""
                    echo "   ❌ $service build failed"
                    build_failed=true
                fi
            else
                # No timeout available - build without timeout
                if $build_cmd; then
                    echo ""
                    echo "   ✅ $service built successfully"
                    built_services+=("$service")
                else
                    echo ""
                    echo "   ❌ $service build failed"
                    build_failed=true
                fi
            fi
        fi
        echo ""
    done

    if [ "$build_failed" = true ]; then
        echo "❌ One or more service builds failed"
        return 1
    fi

    echo "✅ All service images built successfully: ${built_services[*]}"
    echo ""
    return 0
}

# ==============================================================================
# STEP 0: BUILD CHANGED SERVICE IMAGES (IF NEEDED)
# ==============================================================================

if [ "$SKIP_STARTUP" = false ]; then
    cd "$PROJECT_ROOT"

    if [ "$REBUILD_MODE" = "none" ]; then
        echo "⏭️  Skipping image build (--no-rebuild flag)"
        echo ""
    elif [ "$REBUILD_MODE" = "force" ]; then
        echo "🔨 Force rebuilding all service images (--force-rebuild flag)..."
        echo ""

        # Build all services
        ALL_SERVICES=("hub" "dashboard" "ingestion" "housekeeping" "worker")
        if ! build_service_images "${ALL_SERVICES[@]}"; then
            echo "❌ Build failed - aborting"
            exit 1
        fi
    elif [ "$REBUILD_MODE" = "auto" ]; then
        echo "🔍 Detecting changed services..."

        # Detect changed services
        CHANGED_SERVICES=$(detect_changed_services)

        if [ $? -ne 0 ]; then
            echo "⚠️  Change detection failed - skipping auto-rebuild"
            echo "   Use --force-rebuild to rebuild all services"
            echo ""
        elif [ -z "$CHANGED_SERVICES" ]; then
            echo "✅ No service changes detected - using existing images"
            echo ""
        else
            echo "📝 Changed services detected: $CHANGED_SERVICES"
            echo ""

            # Build changed services
            if ! build_service_images $CHANGED_SERVICES; then
                echo "❌ Build failed - aborting"
                exit 1
            fi
        fi
    fi
fi

# ==============================================================================
# STEP 1: START DOCKER-COMPOSE SERVICES
# ==============================================================================

if [ "$SKIP_STARTUP" = false ]; then
    cd "$PROJECT_ROOT"

    echo "🚀 Starting docker-compose services (Decoupled deployment)..."
    echo ""
    echo "Step 1: Starting core infrastructure..."
    docker compose -p agenix-reportportal --profile infrastructure --profile core up -d --remove-orphans --wait

    if [ $? -ne 0 ]; then
        echo "❌ Failed to start core infrastructure"
        exit 1
    fi

    echo ""
    echo "Step 2: Starting worker pools..."

    # Load replica counts from .env.workers if it exists
    if [ -f ".env.workers" ]; then
        set -a
        source .env.workers
        set +a
    fi

    CHROMIUM_REPLICAS="${WORKER_CHROMIUM_REPLICAS:-5}"
    FIREFOX_REPLICAS="${WORKER_FIREFOX_REPLICAS:-3}"
    WEBKIT_REPLICAS="${WORKER_WEBKIT_REPLICAS:-2}"

    if [ -f ".env.workers" ]; then
        docker compose -p agenix-reportportal -f docker-compose.workers.yml --env-file .env.workers up -d \
            --scale worker-chromium=$CHROMIUM_REPLICAS \
            --scale worker-firefox=$FIREFOX_REPLICAS \
            --scale worker-webkit=$WEBKIT_REPLICAS \
            --wait
    else
        docker compose -p agenix-reportportal -f docker-compose.workers.yml up -d \
            --scale worker-chromium=$CHROMIUM_REPLICAS \
            --scale worker-firefox=$FIREFOX_REPLICAS \
            --scale worker-webkit=$WEBKIT_REPLICAS \
            --wait
    fi

    if [ $? -ne 0 ]; then
        echo "❌ Failed to start worker pools"
        docker compose -p agenix-reportportal --profile infrastructure --profile core down
        exit 1
    fi

    echo ""
    echo "✅ Core infrastructure and workers started (all containers healthy)"
    echo ""
else
    echo "⏭️  Skipping docker-compose startup (--skip-startup flag provided)"
    echo "   Assuming services are already running..."
    echo ""
fi

# ==============================================================================
# STEP 2: WAIT FOR HUB HEALTH CHECK
# ==============================================================================

echo "⏳ Waiting for Hub to be ready at $HUB_URL/health..."
echo "   Timeout: ${HEALTH_TIMEOUT}s, Poll Interval: ${HEALTH_POLL_INTERVAL}s"
echo ""

WAIT_COUNT=0
HUB_READY=false

while [ $(echo "$WAIT_COUNT < $HEALTH_TIMEOUT" | bc) -eq 1 ]; do
    if curl -sf "$HUB_URL/health" > /dev/null 2>&1; then
        echo "✅ Hub is ready! (took ${WAIT_COUNT}s)"
        HUB_READY=true
        break
    fi

    # Show progress every 10 seconds
    if [ $((${WAIT_COUNT%.*} % 10)) -eq 0 ] && [ $(echo "$WAIT_COUNT > 0" | bc) -eq 1 ]; then
        echo "   Still waiting... (${WAIT_COUNT}s elapsed)"
    fi

    sleep "$HEALTH_POLL_INTERVAL"
    WAIT_COUNT=$(echo "$WAIT_COUNT + $HEALTH_POLL_INTERVAL" | bc)
done

if [ "$HUB_READY" = false ]; then
    echo ""
    echo "❌ Hub did not become ready within ${HEALTH_TIMEOUT}s timeout"
    echo "   Check Hub logs: docker compose logs hub"

    if [ "$SKIP_STARTUP" = false ] && [ "$KEEP_RUNNING" = false ]; then
        echo ""
        echo "🧹 Cleaning up services..."
        docker compose --profile infrastructure --profile core down
    fi

    exit 1
fi

echo ""

# ==============================================================================
# STEP 3: WAIT FOR WORKERS TO REGISTER
# ==============================================================================

echo "⏳ Waiting for workers to join the pool..."
echo "   Expected: $EXPECTED_WORKERS workers, Timeout: ${WORKER_TIMEOUT}s"
echo ""

WORKER_WAIT_COUNT=0
WORKERS_READY=false

while [ $WORKER_WAIT_COUNT -lt $WORKER_TIMEOUT ]; do
    # Check diagnostics endpoint
    DIAG_RESPONSE=$(curl -sf "$HUB_URL/diagnostics" 2>/dev/null || echo '{"workers":[]}')

    # Count registered workers (matches any worker ID, not just "worker1", "worker2", etc.)
    # Workers can have IDs like "agenix-reportportal-worker-chromium-1" in decoupled deployment
    REGISTERED_NODES=$(echo "$DIAG_RESPONSE" | grep -o '"id":"[^"]*"' | wc -l | tr -d ' ')

    # Default to 0 if empty
    if [ -z "$REGISTERED_NODES" ]; then
        REGISTERED_NODES=0
    fi

    # Check if we have enough workers
    if [ "$REGISTERED_NODES" -ge "$EXPECTED_WORKERS" ] 2>/dev/null; then
        echo "✅ Workers are ready! ($REGISTERED_NODES/$EXPECTED_WORKERS workers registered)"
        WORKERS_READY=true
        break
    fi

    # Show progress every 10 seconds
    if [ $((WORKER_WAIT_COUNT % 10)) -eq 0 ] && [ $WORKER_WAIT_COUNT -gt 0 ]; then
        echo "   Still waiting... ($REGISTERED_NODES/$EXPECTED_WORKERS workers registered, ${WORKER_WAIT_COUNT}s elapsed)"
    fi

    WORKER_WAIT_COUNT=$((WORKER_WAIT_COUNT + 1))
    sleep 1
done

if [ "$WORKERS_READY" = false ]; then
    echo ""
    echo "⚠️  Workers did not join the pool within ${WORKER_TIMEOUT}s timeout"
    echo "   Registered: $REGISTERED_NODES/$EXPECTED_WORKERS workers"
    echo "   Check worker logs:"
    echo "     docker compose -p agenix-reportportal logs worker-chromium"
    echo "     docker compose -p agenix-reportportal logs worker-firefox"
    echo "     docker compose -p agenix-reportportal logs worker-webkit"
    echo ""
    echo "   Tests may fail with '503 No browser capacity' errors"
    echo ""
fi

# ==============================================================================
# STEP 4: WAIT FOR BROWSER POOL CAPACITY
# ==============================================================================

echo "🔍 Verifying browser pool capacity..."

# Give workers time to fully initialize browser pools
echo "   Waiting 5 seconds for browser pools to stabilize..."
sleep 5

# Check browser capacity
CAPACITY_CHECK=$(curl -sf "$HUB_URL/diagnostics" 2>/dev/null | grep -o '"totalBrowsers":[0-9]*' | head -1 | cut -d':' -f2)

# Fallback to "capacity" field if "totalBrowsers" not found
if [ -z "$CAPACITY_CHECK" ]; then
    CAPACITY_CHECK=$(curl -sf "$HUB_URL/diagnostics" 2>/dev/null | grep -o '"capacity":[0-9]*' | head -1 | cut -d':' -f2)
fi

if [ -n "$CAPACITY_CHECK" ] && [ "$CAPACITY_CHECK" -gt 0 ]; then
    echo "✅ Browser capacity available: $CAPACITY_CHECK browser(s)"
else
    echo "⚠️  Warning: Browser capacity may not be ready yet"
    echo "   Tests may fail with '503 No browser capacity' errors"
fi

echo ""

# ==============================================================================
# STEP 5: RUN INTEGRATION TESTS (OPTIONAL)
# ==============================================================================

if [ "$SKIP_TESTS" = true ]; then
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "⏭️  Skipping Integration Tests (--skip-tests flag)"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "✅ Services are running and ready for manual testing"
    echo ""
    echo "Access URLs:"
    echo "  Hub:       $HUB_URL"
    echo "  Dashboard: http://localhost:3001"
    echo ""
    echo "Diagnostics:"
    echo "  curl $HUB_URL/health"
    echo "  curl $HUB_URL/diagnostics"
    echo ""
    echo "Stop services:"
    echo "  docker compose -p agenix-reportportal -f docker-compose.workers.yml down"
    echo "  docker compose -p agenix-reportportal --profile infrastructure --profile core down"
    echo ""
    exit 0
fi

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🧪 Running Integration Tests"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

cd "$PROJECT_ROOT"

# Build test command
TEST_CMD="dotnet test Agenix.PlaywrightGrid.Integration.Tests/Agenix.PlaywrightGrid.Integration.Tests.csproj --logger \"console;verbosity=normal\""

if [ -n "$TEST_FILTER" ]; then
    TEST_CMD="$TEST_CMD --filter \"$TEST_FILTER\""
fi

# Set environment variables for tests
export HUB_URL="$HUB_URL"
export AGENIX_TESTS_EXPECTED_WORKERS="$EXPECTED_WORKERS"

# Create logs directory and test output file
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
TEST_OUTPUT_FILE="/tmp/pg-integration-tests-${TIMESTAMP}.log"
TEST_RESULTS_FILE="/tmp/pg-integration-tests-results-${TIMESTAMP}.txt"

# Run tests and capture output
echo "Running: $TEST_CMD"
echo "📝 Test output: $TEST_OUTPUT_FILE"
echo ""

eval $TEST_CMD 2>&1 | tee "$TEST_OUTPUT_FILE"
TEST_EXIT_CODE=${PIPESTATUS[0]}

echo ""

# ==============================================================================
# STEP 6: PARSE AND DISPLAY TEST RESULTS
# ==============================================================================

# Parse test results from output (dynamic - works with any number of tests)
PASSED_COUNT=$(grep -o "Passed:  *[0-9]*" "$TEST_OUTPUT_FILE" | tail -1 | grep -o "[0-9]*" || echo "0")
FAILED_COUNT=$(grep -o "Failed:  *[0-9]*" "$TEST_OUTPUT_FILE" | tail -1 | grep -o "[0-9]*" || echo "0")
SKIPPED_COUNT=$(grep -o "Skipped: *[0-9]*" "$TEST_OUTPUT_FILE" | tail -1 | grep -o "[0-9]*" || echo "0")
TOTAL_COUNT=$(grep -o "Total:  *[0-9]*" "$TEST_OUTPUT_FILE" | tail -1 | grep -o "[0-9]*" || echo "0")
DURATION=$(grep -o "Duration: [0-9.]* [a-z]*" "$TEST_OUTPUT_FILE" | tail -1 | sed 's/Duration: //' || echo "unknown")

# Extract failed test names and details
if [ "$FAILED_COUNT" -gt 0 ]; then
    echo "Extracting failed test details..."
    grep -A 20 "^\[FAIL\]\|^  Failed " "$TEST_OUTPUT_FILE" > "$TEST_RESULTS_FILE" || true
fi

# Display results summary
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "✅ Integration Tests PASSED"
else
    echo "❌ Integration Tests FAILED"
fi
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "📊 Test Results Summary:"
echo "   ✅ Passed:  $PASSED_COUNT"
echo "   ❌ Failed:  $FAILED_COUNT"
echo "   ⊝ Skipped: $SKIPPED_COUNT"
echo "   📈 Total:   $TOTAL_COUNT"
echo "   ⏱️  Duration: $DURATION"
echo ""

if [ "$FAILED_COUNT" -gt 0 ]; then
    echo "📋 Failed Test Details:"
    echo "   Log file: $TEST_RESULTS_FILE"
    echo ""
    echo "   Failed tests:"
    # Extract and display failed test names
    grep "^\[FAIL\]" "$TEST_OUTPUT_FILE" | sed 's/\[FAIL\]/   ❌/' || echo "   (See full log for details)"
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "🔍 Debugging Commands:"
    echo "   cat $TEST_RESULTS_FILE                                      # View failed test details"
    echo "   docker compose -p agenix-reportportal logs hub              # Hub logs"
    echo "   docker compose -p agenix-reportportal logs worker-chromium  # Chromium worker logs"
    echo "   docker compose -p agenix-reportportal logs worker-firefox   # Firefox worker logs"
    echo "   docker compose -p agenix-reportportal logs worker-webkit    # WebKit worker logs"
    echo "   docker compose -p agenix-reportportal ps                    # Service status"
    echo ""
else
    echo "📁 Test Logs:"
    echo "   Full output: $TEST_OUTPUT_FILE"
    echo ""
fi

# ==============================================================================
# STEP 7: CLEANUP (OPTIONAL)
# ==============================================================================

if [ "$KEEP_RUNNING" = true ]; then
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "🔄 Services kept running (--keep-running flag)"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "Access URLs:"
    echo "  Hub:       $HUB_URL"
    echo "  Dashboard: http://localhost:3001"
    echo ""
    echo "Stop services:"
    echo "  ./scripts/stop-workers.sh                           # Stop workers only"
    echo "  docker compose -p agenix-reportportal --profile infrastructure --profile core down  # Stop core"
    echo "  # Or stop everything:"
    echo "  docker compose -p agenix-reportportal -f docker-compose.workers.yml down && \\"
    echo "    docker compose -p agenix-reportportal --profile infrastructure --profile core down"
    echo ""
elif [ "$SKIP_STARTUP" = false ]; then
    echo ""
    echo "🧹 Stopping docker-compose services..."
    docker compose -p agenix-reportportal -f docker-compose.workers.yml down
    docker compose -p agenix-reportportal --profile infrastructure --profile core down
    echo "✅ Services stopped"
    echo ""
fi

# Exit with test result code
exit $TEST_EXIT_CODE
