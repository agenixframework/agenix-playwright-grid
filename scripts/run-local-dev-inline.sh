#!/bin/bash

# ==============================================================================
# Agenix Playwright Grid - Local Development Startup Script (Inline)
# ==============================================================================
# This script starts the complete local development environment:
# 1. Starts infrastructure & monitoring services (via start-infrastructure.sh)
# 2. Builds and starts Hub, Dashboard, Ingestion, Housekeeping, and 3 Workers
# 3. Runs smoke test to verify everything is working
#
# Services run in background and output is multiplexed to the terminal.
# Perfect for running inside IntelliJ IDEA terminal.
#
# Usage:
#   bash scripts/run-local-dev-inline.sh [options]
#
# Options:
#   --skip-test    Skip the smoke test after services start
#
# To stop all services:
#   Press Ctrl+C (will kill all background processes)
# ==============================================================================

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Progress tracking
TOTAL_STEPS=8
CURRENT_STEP=0
STEP_START_TIME=0

# Function to start timing a step
start_step() {
    STEP_START_TIME=$(date +%s)
}

# Function to show progress with elapsed time
show_progress() {
    CURRENT_STEP=$((CURRENT_STEP + 1))
    local percent=$((CURRENT_STEP * 100 / TOTAL_STEPS))
    local filled=$((CURRENT_STEP * 20 / TOTAL_STEPS))
    local empty=$((20 - filled))

    # Calculate elapsed time for this step
    local step_end_time=$(date +%s)
    local elapsed=$((step_end_time - STEP_START_TIME))

    printf "\r["
    printf "%${filled}s" | tr ' ' '█'
    printf "%${empty}s" | tr ' ' '░'
    printf "] %d%% (%d/%d) %s (⏱️  %ds)\n" "$percent" "$CURRENT_STEP" "$TOTAL_STEPS" "$1" "$elapsed"
    echo ""
}

# Cleanup function to kill all background processes
cleanup() {
    echo ""
    echo "🛑 Stopping all services..."

    if [ ! -z "$HUB_PID" ] && kill -0 $HUB_PID 2>/dev/null; then
        kill $HUB_PID 2>/dev/null || true
    fi

    if [ ! -z "$DASHBOARD_PID" ] && kill -0 $DASHBOARD_PID 2>/dev/null; then
        kill $DASHBOARD_PID 2>/dev/null || true
    fi

    if [ ! -z "$WORKER1_PID" ] && kill -0 $WORKER1_PID 2>/dev/null; then
        kill $WORKER1_PID 2>/dev/null || true
    fi

    if [ ! -z "$WORKER2_PID" ] && kill -0 $WORKER2_PID 2>/dev/null; then
        kill $WORKER2_PID 2>/dev/null || true
    fi

    if [ ! -z "$WORKER3_PID" ] && kill -0 $WORKER3_PID 2>/dev/null; then
        kill $WORKER3_PID 2>/dev/null || true
    fi

    if [ ! -z "$WORKER4_PID" ] && kill -0 $WORKER4_PID 2>/dev/null; then
        kill $WORKER4_PID 2>/dev/null || true
    fi

    if [ ! -z "$INGESTION_PID" ] && kill -0 $INGESTION_PID 2>/dev/null; then
        kill $INGESTION_PID 2>/dev/null || true
    fi

    if [ ! -z "$HOUSEKEEPING_PID" ] && kill -0 $HOUSEKEEPING_PID 2>/dev/null; then
        kill $HOUSEKEEPING_PID 2>/dev/null || true
    fi

    # Kill frontail processes if they were started
    if [ "$FRONTAIL_ENABLED" = true ]; then
        for port in {9101..9109}; do
            pids=$(lsof -ti:$port 2>/dev/null || true)
            if [ -n "$pids" ]; then
                echo "$pids" | xargs kill 2>/dev/null || true
            fi
        done
    fi

    # Kill any remaining dotnet run processes
    pkill -f "dotnet run" 2>/dev/null || true

    echo "✅ All services stopped"
    exit 0
}

# Set up trap to cleanup on exit
trap cleanup SIGINT SIGTERM EXIT

# Parse command line arguments
SKIP_SMOKE_TEST=false
FRONTAIL_ENABLED=false
for arg in "$@"; do
    case $arg in
        --skip-test)
            SKIP_SMOKE_TEST=true
            shift
            ;;
        --frontail-enabled)
            FRONTAIL_ENABLED=true
            shift
            ;;
        *)
            # Unknown option
            ;;
    esac
done

echo "======================================================================"
echo "Agenix Playwright Grid - Local Development Startup (Inline Mode)"
echo "======================================================================"
echo ""
echo "ℹ️  Running in inline mode - all output in this terminal"
echo "ℹ️  Press Ctrl+C to stop all services"
if [ "$SKIP_SMOKE_TEST" = true ]; then
    echo "ℹ️  Smoke test will be skipped (--skip-test flag)"
fi
if [ "$FRONTAIL_ENABLED" = true ]; then
    echo "ℹ️  Frontail log viewers will be started (--frontail-enabled flag)"
fi
echo ""

# ==============================================================================
# 1. Load .env file (must be first to use variables in checks)
# ==============================================================================
if [ ! -f "$PROJECT_ROOT/.env" ]; then
    echo "❌ .env file not found at $PROJECT_ROOT/.env"
    exit 1
fi

# Export variables from .env (safe parsing that handles special characters)
while IFS='=' read -r key value; do
    # Skip empty lines and comments
    if [[ -z "$key" ]] || [[ "$key" =~ ^[[:space:]]*# ]]; then
        continue
    fi
    # Remove leading/trailing whitespace from key
    key=$(echo "$key" | xargs)
    # Skip if key is empty after trimming
    if [[ -z "$key" ]]; then
        continue
    fi
    # Export the variable (value may contain spaces, special chars, etc.)
    export "$key=$value"
done < "$PROJECT_ROOT/.env"

echo "✅ Loaded .env configuration"
echo ""

# ==============================================================================
# 2. Start infrastructure & monitoring services
# ==============================================================================
start_step
echo "🚀 Starting infrastructure & monitoring services..."

if [ -f "$SCRIPT_DIR/start-infrastructure.sh" ]; then
    # Run the infrastructure startup script
    if bash "$SCRIPT_DIR/start-infrastructure.sh"; then
        echo "✅ Infrastructure & monitoring started successfully"
    else
        echo "❌ Failed to start infrastructure & monitoring services"
        exit 1
    fi
else
    echo "⚠️  Warning: start-infrastructure.sh not found"
    echo "   Assuming infrastructure is already running..."
fi

show_progress "Infrastructure startup complete"

# ==============================================================================
# 3. Check infrastructure is running
# ==============================================================================
echo "🔍 Verifying infrastructure services..."

if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed or not in PATH"
    exit 1
fi

# Check if docker compose is available
if docker compose version &> /dev/null; then
    COMPOSE_CMD="docker compose"
elif docker-compose version &> /dev/null; then
    COMPOSE_CMD="docker-compose"
else
    echo "❌ Docker Compose is not available"
    exit 1
fi

cd "$PROJECT_ROOT"

# Check Redis
if ! $COMPOSE_CMD ps redis 2>/dev/null | grep -q "Up"; then
    echo "❌ Redis is not running"
    echo ""
    echo "Please start infrastructure services first:"
    echo "  $COMPOSE_CMD --profile infrastructure up -d"
    exit 1
fi

# Check PostgreSQL
if ! $COMPOSE_CMD ps postgres 2>/dev/null | grep -q "Up"; then
    echo "❌ PostgreSQL is not running"
    echo ""
    echo "Please start infrastructure services first:"
    echo "  $COMPOSE_CMD --profile infrastructure up -d"
    exit 1
fi

# Check Gateway (if enabled)
if [ "$AGENIX_GATEWAY_ENABLED" = "true" ]; then
    if ! $COMPOSE_CMD ps gateway 2>/dev/null | grep -q "Up"; then
        echo "❌ Gateway is not running"
        echo ""
        echo "Please start infrastructure services first:"
        echo "  $COMPOSE_CMD --profile infrastructure up -d"
        exit 1
    fi

    # Verify Gateway is actually responding (check /ping endpoint)
    if ! curl -s http://localhost:${AGENIX_GATEWAY_DASHBOARD_PORT:-8081}/ping > /dev/null 2>&1; then
        echo "⚠️  Gateway is running but not responding on port ${AGENIX_GATEWAY_DASHBOARD_PORT:-8081}"
        echo "   Waiting for Gateway to be ready..."
        sleep 3
        if ! curl -s http://localhost:${AGENIX_GATEWAY_DASHBOARD_PORT:-8081}/ping > /dev/null 2>&1; then
            echo "❌ Gateway health check failed"
            exit 1
        fi
    fi

    echo "✅ Gateway is running and healthy"
    echo "✅ Redis, PostgreSQL, and Gateway are running"
else
    echo "ℹ️  Gateway is disabled (AGENIX_GATEWAY_ENABLED != true)"
    echo "✅ Redis and PostgreSQL are running"
fi

show_progress "Infrastructure check complete"

# ==============================================================================
# 4. Check and install Playwright browsers
# ==============================================================================
echo "🎭 Checking Playwright browsers..."

# Check if npx is available
if ! command -v npx &> /dev/null; then
    echo "⚠️  npx not found. Skipping Playwright browser check."
    echo "   If workers fail to start, install Node.js and run: npx playwright install"
else
    # Check if Chromium browser exists
    PLAYWRIGHT_CACHE="$HOME/Library/Caches/ms-playwright"
    if [ "$(uname -s)" = "Linux" ]; then
        PLAYWRIGHT_CACHE="$HOME/.cache/ms-playwright"
    fi

    # Check if any browser directories exist
    if [ -d "$PLAYWRIGHT_CACHE" ] && [ "$(ls -A "$PLAYWRIGHT_CACHE" 2>/dev/null | grep -E 'chromium|firefox|webkit' | wc -l)" -gt 0 ]; then
        echo "✅ Playwright browsers already installed (using cached)"
    else
        echo "📥 Installing Playwright browsers (this may take a few minutes)..."
        if npx playwright install chromium firefox webkit; then
            echo "✅ Playwright browsers installed successfully"
        else
            echo "⚠️  Failed to install Playwright browsers"
            echo "   Workers may fail to start. Try manually: npx playwright install"
        fi
    fi
fi
show_progress "Playwright browsers ready"

# ==============================================================================
# 5. Build projects (only if changed)
# ==============================================================================
start_step
echo "🔨 Building projects (incremental)..."

BUILD_COUNT=0
REBUILD_HUB=false
REBUILD_DASHBOARD=false
REBUILD_WORKER=false
REBUILD_INGESTION=false
REBUILD_HOUSEKEEPING=false

# Function to check if project needs rebuild
needs_rebuild() {
    local proj_file=$1
    local dll_file=$2

    # If DLL doesn't exist, rebuild
    if [ ! -f "$dll_file" ]; then
        return 0
    fi

    # Check if any source files are newer than DLL
    # Exclude bin/, obj/, and other build artifact directories
    local proj_dir=$(dirname "$proj_file")
    if find "$proj_dir" -type f \
        \( -name "*.cs" -o -name "*.razor" -o -name "*.csproj" -o -name "*.json" \) \
        ! -path "*/bin/*" \
        ! -path "*/obj/*" \
        -newer "$dll_file" 2>/dev/null | grep -q .; then
        return 0
    fi

    return 1
}

# Check if shared dependencies exist (Domain, Shared, Client projects)
DOMAIN_DLL="$PROJECT_ROOT/Agenix.PlaywrightGrid.Domain/bin/Debug/net8.0/Agenix.PlaywrightGrid.Domain.dll"
SHARED_DLL="$PROJECT_ROOT/Agenix.PlaywrightGrid.Shared/bin/Debug/net8.0/Agenix.PlaywrightGrid.Shared.dll"
CLIENT_DLL="$PROJECT_ROOT/Agenix.PlaywrightGrid.Client/bin/Debug/net8.0/Agenix.PlaywrightGrid.Client.dll"

DEPENDENCIES_MISSING=false
if [ ! -f "$DOMAIN_DLL" ] || [ ! -f "$SHARED_DLL" ] || [ ! -f "$CLIENT_DLL" ]; then
    DEPENDENCIES_MISSING=true
    echo "  🔨 Building shared dependencies (Domain, Shared, Client)..."
    if ! dotnet build "$PROJECT_ROOT/PlaywrightGrid.sln" -c Debug --nologo -v quiet; then
        echo "❌ Failed to build solution"
        exit 1
    fi
    BUILD_COUNT=$((BUILD_COUNT + 3))
    REBUILD_HUB=true
    REBUILD_DASHBOARD=true
    REBUILD_WORKER=true
    REBUILD_INGESTION=true
    REBUILD_HOUSEKEEPING=true
    echo "  ✅ Solution built successfully"
fi

# Check Hub
HUB_DLL="$PROJECT_ROOT/hub/bin/Debug/net8.0/PlaywrightHub.dll"
if [ "$DEPENDENCIES_MISSING" = false ] && needs_rebuild "$PROJECT_ROOT/hub/PlaywrightHub.csproj" "$HUB_DLL"; then
    echo "  🔨 Building Hub (changed)..."
    if ! dotnet build "$PROJECT_ROOT/hub/PlaywrightHub.csproj" -c Debug --nologo -v quiet; then
        echo "❌ Failed to build Hub"
        exit 1
    fi
    BUILD_COUNT=$((BUILD_COUNT + 1))
    REBUILD_HUB=true
elif [ "$DEPENDENCIES_MISSING" = false ]; then
    echo "  ⏭️  Hub (no changes)"
fi

# Check Dashboard
DASHBOARD_DLL="$PROJECT_ROOT/dashboard/bin/Debug/net8.0/Dashboard.dll"
if [ "$DEPENDENCIES_MISSING" = false ] && needs_rebuild "$PROJECT_ROOT/dashboard/Dashboard.csproj" "$DASHBOARD_DLL"; then
    echo "  🔨 Building Dashboard (changed)..."
    if ! dotnet build "$PROJECT_ROOT/dashboard/Dashboard.csproj" -c Debug --nologo -v quiet; then
        echo "❌ Failed to build Dashboard"
        exit 1
    fi
    BUILD_COUNT=$((BUILD_COUNT + 1))
    REBUILD_DASHBOARD=true
elif [ "$DEPENDENCIES_MISSING" = false ]; then
    echo "  ⏭️  Dashboard (no changes)"
fi

# Check Worker
WORKER_DLL="$PROJECT_ROOT/worker/bin/Debug/net8.0/WorkerService.dll"
if [ "$DEPENDENCIES_MISSING" = false ] && needs_rebuild "$PROJECT_ROOT/worker/WorkerService.csproj" "$WORKER_DLL"; then
    echo "  🔨 Building Worker (changed)..."
    if ! dotnet build "$PROJECT_ROOT/worker/WorkerService.csproj" -c Debug --nologo -v quiet; then
        echo "❌ Failed to build Worker"
        exit 1
    fi
    BUILD_COUNT=$((BUILD_COUNT + 1))
    REBUILD_WORKER=true
elif [ "$DEPENDENCIES_MISSING" = false ]; then
    echo "  ⏭️  Worker (no changes)"
fi

# Check Ingestion
INGESTION_DLL="$PROJECT_ROOT/ingestion/bin/Debug/net8.0/IngestionService.dll"
if [ "$DEPENDENCIES_MISSING" = false ] && needs_rebuild "$PROJECT_ROOT/ingestion/IngestionService.csproj" "$INGESTION_DLL"; then
    echo "  🔨 Building Ingestion (changed)..."
    if ! dotnet build "$PROJECT_ROOT/ingestion/IngestionService.csproj" -c Debug --nologo -v quiet; then
        echo "❌ Failed to build Ingestion"
        exit 1
    fi
    BUILD_COUNT=$((BUILD_COUNT + 1))
    REBUILD_INGESTION=true
elif [ "$DEPENDENCIES_MISSING" = false ]; then
    echo "  ⏭️  Ingestion (no changes)"
fi

# Check Housekeeping
HOUSEKEEPING_DLL="$PROJECT_ROOT/housekeeping-service/bin/Debug/net8.0/HousekeepingService.dll"
if [ "$DEPENDENCIES_MISSING" = false ] && needs_rebuild "$PROJECT_ROOT/housekeeping-service/HousekeepingService.csproj" "$HOUSEKEEPING_DLL"; then
    echo "  🔨 Building Housekeeping (changed)..."
    if ! dotnet build "$PROJECT_ROOT/housekeeping-service/HousekeepingService.csproj" -c Debug --nologo -v quiet; then
        echo "❌ Failed to build Housekeeping"
        exit 1
    fi
    BUILD_COUNT=$((BUILD_COUNT + 1))
    REBUILD_HOUSEKEEPING=true
elif [ "$DEPENDENCIES_MISSING" = false ]; then
    echo "  ⏭️  Housekeeping (no changes)"
fi

if [ $BUILD_COUNT -eq 0 ]; then
    echo "✅ All projects up-to-date (no rebuild needed)"
else
    echo "✅ Built $BUILD_COUNT project(s) successfully"
fi
show_progress "Build complete"

# ==============================================================================
# 6. Clean up old logs
# ==============================================================================
echo "🧹 Cleaning up old logs..."

# Remove ALL log files (including date-suffixed files from Serilog daily rolling)
rm -f /tmp/pg-* 2>/dev/null || true

# Show how many log files were cleaned up
LOG_COUNT=$(ls /tmp/pg-*.log 2>/dev/null | wc -l | tr -d ' ')
if [ "$LOG_COUNT" -eq 0 ]; then
    echo "✅ All old logs cleaned up"
else
    echo "⚠️  Some log files remain: $LOG_COUNT files"
fi
show_progress "Logs cleaned up"

# ==============================================================================
# 7. Check and kill processes on required ports
# ==============================================================================
echo "🔍 Checking for processes on required ports..."

# Function to check if a process is a system process (should not be killed)
is_system_process() {
    local pid=$1
    local process_name=$(ps -p $pid -o comm= 2>/dev/null)

    # Check if it's a macOS system process
    if [[ "$process_name" =~ "ControlCenter" ]] || [[ "$process_name" =~ "AirPlay" ]] || [[ "$process_name" =~ "rapportd" ]]; then
        return 0  # Is system process
    fi
    return 1  # Not system process
}

# Function to kill process on a specific port
kill_port() {
    local port=$1
    local service_name=$2
    local max_attempts=5
    local attempt=0

    if command -v lsof &> /dev/null; then
        # macOS/Linux with lsof
        while [ $attempt -lt $max_attempts ]; do
            local pid=$(lsof -ti:$port 2>/dev/null)
            if [ -z "$pid" ]; then
                # Port is free
                return 0
            fi

            # Check if it's a system process
            if is_system_process "$pid"; then
                local process_name=$(ps -p $pid -o comm= 2>/dev/null)
                echo "❌ Port $port is in use by system process: $process_name (PID $pid)"
                echo "   This is likely macOS AirPlay Receiver or ControlCenter."
                echo "   Please change the port in .env file or disable AirPlay Receiver:"
                echo "   System Settings > General > AirDrop & Handoff > AirPlay Receiver (turn OFF)"
                return 1
            fi

            if [ $attempt -eq 0 ]; then
                echo "⚠️  Port $port is in use by PID $pid ($service_name) - killing it..."
            fi

            kill -9 $pid 2>/dev/null || true
            sleep 0.5
            attempt=$((attempt + 1))
        done

        # Check one final time
        local pid=$(lsof -ti:$port 2>/dev/null)
        if [ -z "$pid" ]; then
            echo "✅ Port $port is now free"
            return 0
        else
            echo "❌ Failed to free port $port after $max_attempts attempts"
            return 1
        fi
    elif command -v netstat &> /dev/null; then
        # Linux with netstat
        while [ $attempt -lt $max_attempts ]; do
            local pid=$(netstat -tlnp 2>/dev/null | grep ":$port " | awk '{print $7}' | cut -d'/' -f1)
            if [ -z "$pid" ]; then
                # Port is free
                return 0
            fi

            if [ $attempt -eq 0 ]; then
                echo "⚠️  Port $port is in use by PID $pid ($service_name) - killing it..."
            fi

            kill -9 $pid 2>/dev/null || true
            sleep 0.5
            attempt=$((attempt + 1))
        done

        # Check one final time
        local pid=$(netstat -tlnp 2>/dev/null | grep ":$port " | awk '{print $7}' | cut -d'/' -f1)
        if [ -z "$pid" ]; then
            echo "✅ Port $port is now free"
            return 0
        else
            echo "❌ Failed to free port $port after $max_attempts attempts"
            return 1
        fi
    else
        # Fallback: try to kill all dotnet run processes
        echo "⚠️  Cannot detect port usage (lsof/netstat not found)"
        echo "   Killing all 'dotnet run' processes as precaution..."
        pkill -9 -f "dotnet run" 2>/dev/null || true
        sleep 2
    fi
}

# Check and free ports
kill_port 5100 "Hub"
kill_port 3001 "Dashboard"
kill_port "${WORKER1_HTTP_PORT}" "Worker1 HTTP"
kill_port "${WORKER2_HTTP_PORT}" "Worker2 HTTP"
kill_port "${WORKER3_HTTP_PORT}" "Worker3 HTTP"
kill_port "${WORKER4_HTTP_PORT}" "Worker4 HTTP"
kill_port "${WORKER1_PUBLIC_WS_PORT}" "Worker1 WS"
kill_port "${WORKER2_PUBLIC_WS_PORT}" "Worker2 WS"
kill_port "${WORKER3_PUBLIC_WS_PORT}" "Worker3 WS"
kill_port "${WORKER4_PUBLIC_WS_PORT}" "Worker4 WS"
kill_port "${AGENIX_INGESTION_PORT:-8082}" "Ingestion"
kill_port "${AGENIX_HOUSEKEEPING_PORT:-8083}" "Housekeeping"

# Additional wait to ensure ports are fully released
sleep 1

echo "✅ All ports are free"
show_progress "Ports checked and cleared"

# ==============================================================================
# 8. Start/Restart services (always restart all services)
# ==============================================================================
start_step

# Function to stop service gracefully
stop_service() {
    local pid=$1
    local name=$2
    if [ ! -z "$pid" ] && kill -0 "$pid" 2>/dev/null; then
        echo "  🛑 Stopping $name (PID: $pid)..."
        kill "$pid" 2>/dev/null || true
        # Wait for graceful shutdown (max 5 seconds)
        local wait_count=0
        while [ $wait_count -lt 5 ] && kill -0 "$pid" 2>/dev/null; do
            sleep 1
            wait_count=$((wait_count + 1))
        done
        # Force kill if still running
        if kill -0 "$pid" 2>/dev/null; then
            kill -9 "$pid" 2>/dev/null || true
        fi
    fi
}

if [ "$BUILD_COUNT" -eq 0 ]; then
    echo "🚀 Starting services (no build changes)..."
else
    echo "🚀 Starting services ($BUILD_COUNT project(s) rebuilt)..."
fi
echo ""

# Stop all existing services
stop_service "$HUB_PID" "Hub"
stop_service "$DASHBOARD_PID" "Dashboard"
stop_service "$WORKER1_PID" "Worker 1"
stop_service "$WORKER2_PID" "Worker 2"
stop_service "$WORKER3_PID" "Worker 3"
stop_service "$WORKER4_PID" "Worker 4"
stop_service "$INGESTION_PID" "Ingestion"
stop_service "$HOUSEKEEPING_PID" "Housekeeping"

# Start Hub (use --no-build since we already built everything)
cd "$PROJECT_ROOT/hub"
ASPNETCORE_URLS="http://localhost:5100" dotnet run --no-build > /tmp/pg-hub.log 2>&1 &
HUB_PID=$!
echo "✅ Hub started (PID: $HUB_PID) - Logs: /tmp/pg-hub.log"

# Start Dashboard
cd "$PROJECT_ROOT/dashboard"
ASPNETCORE_URLS="http://localhost:3001" dotnet run --no-build > /tmp/pg-dashboard.log 2>&1 &
DASHBOARD_PID=$!
echo "✅ Dashboard started (PID: $DASHBOARD_PID) - Logs: /tmp/pg-dashboard.log"

# Start Worker 1 (use --no-build to avoid file locking conflicts)
cd "$PROJECT_ROOT/worker"
export AGENIX_WORKER_NODE_ID="${WORKER1_NODE_ID}"
export AGENIX_WORKER_PUBLIC_WS_PORT="${WORKER1_PUBLIC_WS_PORT}"
export AGENIX_WORKER_POOL_CONFIG="${WORKER1_POOL_CONFIG}"
ASPNETCORE_URLS="http://localhost:${WORKER1_PUBLIC_WS_PORT}" dotnet run --no-build > /tmp/pg-worker1.log 2>&1 &
WORKER1_PID=$!
echo "✅ Worker 1 started (PID: $WORKER1_PID) - Logs: /tmp/pg-worker1.log"

# Start Worker 2
cd "$PROJECT_ROOT/worker"
export AGENIX_WORKER_NODE_ID="${WORKER2_NODE_ID}"
export AGENIX_WORKER_PUBLIC_WS_PORT="${WORKER2_PUBLIC_WS_PORT}"
export AGENIX_WORKER_POOL_CONFIG="${WORKER2_POOL_CONFIG}"
ASPNETCORE_URLS="http://localhost:${WORKER2_PUBLIC_WS_PORT}" dotnet run --no-build > /tmp/pg-worker2.log 2>&1 &
WORKER2_PID=$!
echo "✅ Worker 2 started (PID: $WORKER2_PID) - Logs: /tmp/pg-worker2.log"

# Start Worker 3
cd "$PROJECT_ROOT/worker"
export AGENIX_WORKER_NODE_ID="${WORKER3_NODE_ID}"
export AGENIX_WORKER_PUBLIC_WS_PORT="${WORKER3_PUBLIC_WS_PORT}"
export AGENIX_WORKER_POOL_CONFIG="${WORKER3_POOL_CONFIG}"
export AGENIX_WORKER_FIREFOX_ARGS="${WORKER3_FIREFOX_ARGS}"
export AGENIX_WORKER_FIREFOX_PREFS="${WORKER3_FIREFOX_PREFS}"
ASPNETCORE_URLS="http://localhost:${WORKER3_PUBLIC_WS_PORT}" dotnet run --no-build > /tmp/pg-worker3.log 2>&1 &
WORKER3_PID=$!
echo "✅ Worker 3 started (PID: $WORKER3_PID) - Logs: /tmp/pg-worker3.log"

# Start Worker 4
cd "$PROJECT_ROOT/worker"
export AGENIX_WORKER_NODE_ID="${WORKER4_NODE_ID}"
export AGENIX_WORKER_PUBLIC_WS_PORT="${WORKER4_PUBLIC_WS_PORT}"
export AGENIX_WORKER_POOL_CONFIG="${WORKER4_POOL_CONFIG}"
unset AGENIX_WORKER_FIREFOX_ARGS  # WebKit doesn't need Firefox args
unset AGENIX_WORKER_FIREFOX_PREFS
ASPNETCORE_URLS="http://localhost:${WORKER4_PUBLIC_WS_PORT}" dotnet run --no-build > /tmp/pg-worker4.log 2>&1 &
WORKER4_PID=$!
echo "✅ Worker 4 started (PID: $WORKER4_PID) - Logs: /tmp/pg-worker4.log"

# Start Ingestion
cd "$PROJECT_ROOT/ingestion"
AGENIX_INGESTION_PORT="${AGENIX_INGESTION_PORT:-8082}"  # Default to 8082 if not set
ASPNETCORE_URLS="http://localhost:${AGENIX_INGESTION_PORT}" dotnet run --no-build > /tmp/pg-ingestion.log 2>&1 &
INGESTION_PID=$!
echo "✅ Ingestion started (PID: $INGESTION_PID) - Logs: /tmp/pg-ingestion*.log"

# Start Housekeeping
cd "$PROJECT_ROOT/housekeeping-service"
AGENIX_HOUSEKEEPING_PORT="${AGENIX_HOUSEKEEPING_PORT:-8082}"  # Default to 8082 if not set
ASPNETCORE_URLS="http://localhost:${AGENIX_HOUSEKEEPING_PORT}" dotnet run --no-build > /tmp/pg-housekeeping.log 2>&1 &
HOUSEKEEPING_PID=$!
echo "✅ Housekeeping started (PID: $HOUSEKEEPING_PID) - Logs: /tmp/pg-housekeeping*.log"

show_progress "Services launched/restarted"

# ==============================================================================
# 9. Wait for services to be ready, create API key, and run smoke test
# ==============================================================================

# Always wait for Hub and run smoke test (unless --skip-test flag provided)
if [ "$SKIP_SMOKE_TEST" = false ]; then
    echo "⏳ Waiting for Hub to be ready..."
    PROJECT_KEY="admin_default"
    HUB_URL="http://localhost:5100"
    ADMIN_USER="admin"

    # Wait for Hub health check (max 30 seconds)
    MAX_WAIT=30
    WAIT_COUNT=0
    while [ $WAIT_COUNT -lt $MAX_WAIT ]; do
        if curl -sf "$HUB_URL/health" > /dev/null 2>&1; then
            echo "✅ Hub is ready!"
            break
        fi
        WAIT_COUNT=$((WAIT_COUNT + 1))
        sleep 1
    done

    if [ $WAIT_COUNT -ge $MAX_WAIT ]; then
        echo "⚠️  Hub did not become ready in time, skipping smoke test"
        show_progress "Environment ready (Hub timeout)"
        echo ""
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo "🌐 Hub:       http://localhost:5100"
        echo "📊 Dashboard: http://localhost:3001"
        echo "🔧 Worker 1:  ws://127.0.0.1:${WORKER1_PUBLIC_WS_PORT} (Chromium)"
        echo "🔧 Worker 2:  ws://127.0.0.1:${WORKER2_PUBLIC_WS_PORT} (Chromium)"
        echo "🔧 Worker 3:  ws://127.0.0.1:${WORKER3_PUBLIC_WS_PORT} (Firefox)"
        echo "🔧 Worker 4:  ws://127.0.0.1:${WORKER4_PUBLIC_WS_PORT} (WebKit)"
        echo "📨 Ingestion: http://localhost:${AGENIX_INGESTION_PORT}"
        echo "📨 Housekeeping: http://localhost:${AGENIX_HOUSEKEEPING_PORT}"
        if [ "$FRONTAIL_ENABLED" = true ]; then
            echo "📊 Frontail (Hub Main):     http://localhost:9101"
            echo "📊 Frontail (Worker 1):     http://localhost:9102"
            echo "📊 Frontail (Worker 2):     http://localhost:9103"
            echo "📊 Frontail (Worker 3):     http://localhost:9104"
            echo "📊 Frontail (Worker 4):     http://localhost:9105"
            echo "📊 Frontail (Ingestion):    http://localhost:9106"
            echo "📊 Frontail (Housekeeping): http://localhost:9107"
            echo "📊 Frontail (Dashboard):    http://localhost:9108"
            echo "📊 Frontail (Hub BG):       http://localhost:9109"
        fi
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo ""
        echo "📋 Logs locations (with timestamps):"
        echo "   Hub:                  tail -f /tmp/pg-hub*.log"
        echo "   Hub (Background):     tail -f /tmp/pg-hub-background*.log"
        echo "                         (BrowserCleanup, LaunchCleanup, NodeSweeper, PoolBroadcast)"
        echo "   Dashboard:            tail -f /tmp/pg-dashboard*.log"
        echo "   Worker 1:             tail -f /tmp/pg-worker1*.log"
        echo "   Worker 2:             tail -f /tmp/pg-worker2*.log"
        echo "   Worker 3:             tail -f /tmp/pg-worker3*.log"
        echo "   Worker 4:             tail -f /tmp/pg-worker4*.log"
        echo "   Ingestion:            tail -f /tmp/pg-ingestion*.log"
        echo "   Housekeeping:         tail -f /tmp/pg-housekeeping*.log"
        echo ""
        echo "✨ Local development environment started!"
        echo "⏸️  Press Ctrl+C to stop all services..."
        echo ""
    else
        # Wait for workers to register in the pool
        echo "⏳ Waiting for workers to join the pool..."
        WORKER_WAIT=30
        WORKER_COUNT=0
        EXPECTED_WORKERS=4  # We start 4 workers

        while [ $WORKER_COUNT -lt $WORKER_WAIT ]; do
            # Check diagnostics endpoint to see if workers have registered
            DIAG_RESPONSE=$(curl -sf "$HUB_URL/diagnostics" 2>/dev/null || echo '{"workers":[]}')

            # Count registered workers - use grep to count "id" occurrences in the workers array
            REGISTERED_NODES=$(echo "$DIAG_RESPONSE" | grep -o '"id":"worker[0-9]*"' | wc -l | tr -d ' ')

            # Default to 0 if empty or not found
            if [ -z "$REGISTERED_NODES" ]; then
                REGISTERED_NODES=0
            fi

            if [ "$REGISTERED_NODES" -ge "$EXPECTED_WORKERS" ] 2>/dev/null; then
                echo "✅ Workers are ready! ($REGISTERED_NODES/$EXPECTED_WORKERS nodes registered)"
                # Give workers MORE time to fully initialize their browser pools
                # Workers need time to register + start browsers + report capacity
                echo "   Waiting 5 seconds for browser pools to initialize..."
                sleep 5
                break
            fi

            # Show progress every 5 seconds
            if [ $((WORKER_COUNT % 5)) -eq 0 ] && [ $WORKER_COUNT -gt 0 ]; then
                echo "   Still waiting... ($REGISTERED_NODES/$EXPECTED_WORKERS workers registered)"
            fi

            WORKER_COUNT=$((WORKER_COUNT + 1))
            sleep 1
        done

        if [ $WORKER_COUNT -ge $WORKER_WAIT ]; then
            echo "⚠️  Workers did not join the pool in time, but continuing..."
            echo "   Registered: $REGISTERED_NODES/$EXPECTED_WORKERS workers"
            echo "   You may see '503 No browser capacity' errors in smoke test"
        else
            # Double-check browser capacity is available by checking diagnostics
            echo "🔍 Verifying browser capacity..."
            CAPACITY_CHECK=$(curl -sf "$HUB_URL/diagnostics" 2>/dev/null | grep -o '"capacity":[0-9]*' | head -1 | cut -d':' -f2)
            if [ ! -z "$CAPACITY_CHECK" ] && [ "$CAPACITY_CHECK" -gt 0 ]; then
                echo "✅ Browser capacity available: $CAPACITY_CHECK browser(s)"
            else
                echo "⚠️  Warning: Browser capacity may not be ready yet"
            fi
        fi
        echo "🔑 Getting API key for admin user..."
        API_KEY_FILE="$PROJECT_ROOT/.api-key-local-dev"

        # Check if we have a cached API key and validate it
        if [ -f "$API_KEY_FILE" ]; then
            CACHED_KEY=$(cat "$API_KEY_FILE")

            # Validate cached key by testing actual authentication with a test launch
            if [ ! -z "$CACHED_KEY" ]; then
                echo "   Validating cached API key..."

                # Create a test launch to verify authentication works
                TEST_LAUNCH_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$HUB_URL/api/launches" \
                    -H "Content-Type: application/json" \
                    -H "X-Project-Key: $PROJECT_KEY" \
                    -H "Authorization: Bearer $CACHED_KEY" \
                    -d '{
                        "name": "API-Key-Validation-Test",
                        "mode": "DEFAULT"
                    }' 2>/dev/null)

                # Extract HTTP code (last line) and response body (everything else)
                VALIDATION_HTTP_CODE=$(echo "$TEST_LAUNCH_RESPONSE" | tail -1)
                TEST_LAUNCH_BODY=$(echo "$TEST_LAUNCH_RESPONSE" | sed '$d')

                if [ "$VALIDATION_HTTP_CODE" = "201" ]; then
                    # Key is valid - extract launch ID and delete the test launch
                    TEST_LAUNCH_ID=$(echo "$TEST_LAUNCH_BODY" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
                    if [ ! -z "$TEST_LAUNCH_ID" ]; then
                        # Delete test launch immediately
                        curl -s -X DELETE "$HUB_URL/api/launches/$TEST_LAUNCH_ID" \
                            -H "X-Project-Key: $PROJECT_KEY" \
                            -H "Authorization: Bearer $CACHED_KEY" > /dev/null 2>&1
                    fi

                    API_KEY="$CACHED_KEY"
                    echo "✅ Using cached API key (validated): ${API_KEY:0:16}..."
                else
                    echo "⚠️  Cached API key is invalid (HTTP $VALIDATION_HTTP_CODE), will create new one..."
                    echo "   Error response: $(echo "$TEST_LAUNCH_BODY" | head -c 100)"
                    rm -f "$API_KEY_FILE"
                    API_KEY=""
                fi
            else
                API_KEY=""
            fi
        else
            API_KEY=""
        fi

        # If no valid cached key, try to create or retrieve one
        if [ -z "$API_KEY" ]; then
            # Check if API key already exists for admin user
            EXISTING_KEYS_RESPONSE=$(curl -s "$HUB_URL/admin/users/$ADMIN_USER/api-keys" \
                -H "Content-Type: application/json" \
                -H "x-user-id: $ADMIN_USER" || echo '{"items":[]}')

            # Extract first key name if exists
            EXISTING_KEY_NAME=$(echo "$EXISTING_KEYS_RESPONSE" | grep -o '"name":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")

            if [ -z "$EXISTING_KEY_NAME" ]; then
                # Create new API key for admin user
                echo "   Creating new API key for admin user..."
                API_KEY_RESPONSE=$(curl -s -X POST "$HUB_URL/admin/users/$ADMIN_USER/api-keys" \
                    -H "Content-Type: application/json" \
                    -H "x-user-id: $ADMIN_USER" \
                    -d '{
                        "name": "Local Dev Smoke Test"
                    }')

                API_KEY=$(echo "$API_KEY_RESPONSE" | grep -o '"apiKey":"[^"]*"' | cut -d'"' -f4)

                if [ -z "$API_KEY" ]; then
                    echo "⚠️  Failed to create API key, skipping smoke test"
                    echo "   Response: $API_KEY_RESPONSE"
                else
                    echo "✅ Created API key: ${API_KEY:0:16}..."
                    # Cache the key for future runs
                    echo "$API_KEY" > "$API_KEY_FILE"
                    chmod 600 "$API_KEY_FILE"
                fi
            else
                echo "⚠️  API key '$EXISTING_KEY_NAME' exists but value not cached"
                echo "   Deleting existing key to create a fresh one..."

                # URL encode the key name
                ENCODED_KEY_NAME=$(echo "$EXISTING_KEY_NAME" | sed 's/ /%20/g')

                DELETE_RESPONSE=$(curl -s -X DELETE "$HUB_URL/admin/users/$ADMIN_USER/api-keys/$ENCODED_KEY_NAME" \
                    -H "x-user-id: $ADMIN_USER")

                # Try to create a new key
                echo "   Creating new API key for admin user..."
                API_KEY_RESPONSE=$(curl -s -X POST "$HUB_URL/admin/users/$ADMIN_USER/api-keys" \
                    -H "Content-Type: application/json" \
                    -H "x-user-id: $ADMIN_USER" \
                    -d '{
                        "name": "Local Dev Smoke Test"
                    }')

                API_KEY=$(echo "$API_KEY_RESPONSE" | grep -o '"apiKey":"[^"]*"' | cut -d'"' -f4)

                if [ -z "$API_KEY" ]; then
                    echo "⚠️  Failed to create API key after deletion"
                    echo "   Response: $API_KEY_RESPONSE"
                else
                    echo "✅ Created API key: ${API_KEY:0:16}..."
                    # Cache the key for future runs
                    echo "$API_KEY" > "$API_KEY_FILE"
                    chmod 600 "$API_KEY_FILE"
                fi
            fi
        fi

        # Run smoke test if we have an API key
        if [ ! -z "$API_KEY" ]; then
            echo "🧪 Running smoke test..."
            export HUB_URL="$HUB_URL"
            export PROJECT_KEY="$PROJECT_KEY"
            export API_KEY="$API_KEY"

            if bash "$SCRIPT_DIR/test-result-upload-smoke-test.sh"; then
                echo "✅ Smoke test passed!"
            else
                echo "⚠️  Smoke test failed, but continuing..."
            fi
        else
            echo "⚠️  Skipping smoke test (no API key available)"
        fi

        show_progress "Environment ready and tested"

        # Start frontail log viewers if enabled
        if [ "$FRONTAIL_ENABLED" = true ]; then
            echo ""
            if bash "$SCRIPT_DIR/start-frontail.sh"; then
                echo ""
            else
                echo "⚠️  Frontail startup failed, but continuing..."
                echo ""
            fi
        fi

        echo ""
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo "🌐 Hub:       http://localhost:5100"
        echo "📊 Dashboard: http://localhost:3001"
        echo "🔧 Worker 1:  ws://127.0.0.1:${WORKER1_PUBLIC_WS_PORT} (Chromium)"
        echo "🔧 Worker 2:  ws://127.0.0.1:${WORKER2_PUBLIC_WS_PORT} (Chromium)"
        echo "🔧 Worker 3:  ws://127.0.0.1:${WORKER3_PUBLIC_WS_PORT} (Firefox)"
        echo "🔧 Worker 4:  ws://127.0.0.1:${WORKER4_PUBLIC_WS_PORT} (WebKit)"
        echo "📨 Ingestion: http://localhost:${AGENIX_INGESTION_PORT}"
        echo "📨 Housekeeping: http://localhost:${AGENIX_HOUSEKEEPING_PORT}"
        if [ "$FRONTAIL_ENABLED" = true ]; then
            echo "📊 Frontail (Hub Main):    http://localhost:9101"
            echo "📊 Frontail (Worker 1):    http://localhost:9102"
            echo "📊 Frontail (Worker 2):    http://localhost:9103"
            echo "📊 Frontail (Worker 3):    http://localhost:9104"
            echo "📊 Frontail (Worker 4):    http://localhost:9105"
            echo "📊 Frontail (Ingestion):   http://localhost:9106"
            echo "📊 Frontail (Housekeep):   http://localhost:9107"
            echo "📊 Frontail (Dashboard):   http://localhost:9108"
            echo "📊 Frontail (Hub BG):      http://localhost:9109"
        fi
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo ""
        echo "📋 Logs locations (with timestamps):"
        echo "   Hub:                  tail -f /tmp/pg-hub*.log"
        echo "   Hub (Background):     tail -f /tmp/pg-hub-background*.log"
        echo "                         (BrowserCleanup, LaunchCleanup, NodeSweeper, PoolBroadcast)"
        echo "   Dashboard:            tail -f /tmp/pg-dashboard*.log"
        echo "   Worker 1:             tail -f /tmp/pg-worker1*.log"
        echo "   Worker 2:             tail -f /tmp/pg-worker2*.log"
        echo "   Worker 3:             tail -f /tmp/pg-worker3*.log"
        echo "   Worker 4:             tail -f /tmp/pg-worker4*.log"
        echo "   Ingestion:            tail -f /tmp/pg-ingestion*.log"
        echo "   Housekeep:            tail -f /tmp/pg-housekeeping*.log"
        echo ""
        echo "✨ Local development environment started successfully!"
        echo "⏸️  Press Ctrl+C to stop all services..."
        echo ""
    fi
else
    echo "⏭️  Skipping smoke test (--skip-test flag provided)"
    show_progress "Environment ready"
    echo ""

    # Start frontail log viewers if enabled
    if [ "$FRONTAIL_ENABLED" = true ]; then
        if bash "$SCRIPT_DIR/start-frontail.sh"; then
            echo ""
        else
            echo "⚠️  Frontail startup failed, but continuing..."
            echo ""
        fi
    fi

    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "🌐 Hub:       http://localhost:5100"
    echo "📊 Dashboard: http://localhost:3001"
    echo "🔧 Worker 1:  ws://127.0.0.1:${WORKER1_PUBLIC_WS_PORT} (Chromium)"
    echo "🔧 Worker 2:  ws://127.0.0.1:${WORKER2_PUBLIC_WS_PORT} (Chromium)"
    echo "🔧 Worker 3:  ws://127.0.0.1:${WORKER3_PUBLIC_WS_PORT} (Firefox)"
    echo "🔧 Worker 4:  ws://127.0.0.1:${WORKER4_PUBLIC_WS_PORT} (WebKit)"
    echo "📨 Ingestion: http://localhost:${AGENIX_INGESTION_PORT}"
    echo "📨 Housekeeping: http://localhost:${AGENIX_HOUSEKEEPING_PORT}"
    if [ "$FRONTAIL_ENABLED" = true ]; then
        echo "📊 Frontail (Hub Main):    http://localhost:9101"
        echo "📊 Frontail (Worker 1):    http://localhost:9102"
        echo "📊 Frontail (Worker 2):    http://localhost:9103"
        echo "📊 Frontail (Worker 3):    http://localhost:9104"
        echo "📊 Frontail (Worker 4):    http://localhost:9105"
        echo "📊 Frontail (Ingestion):   http://localhost:9106"
        echo "📊 Frontail (Housekeep):   http://localhost:9107"
        echo "📊 Frontail (Dashboard):   http://localhost:9108"
        echo "📊 Frontail (Hub BG):      http://localhost:9109"
    fi
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "📋 Logs locations (with timestamps):"
    echo "   Hub:                  tail -f /tmp/pg-hub*.log"
    echo "   Hub (Background):     tail -f /tmp/pg-hub-background*.log"
    echo "                         (BrowserCleanup, LaunchCleanup, NodeSweeper, PoolBroadcast)"
    echo "   Dashboard:            tail -f /tmp/pg-dashboard*.log"
    echo "   Worker 1:             tail -f /tmp/pg-worker1*.log"
    echo "   Worker 2:             tail -f /tmp/pg-worker2*.log"
    echo "   Worker 3:             tail -f /tmp/pg-worker3*.log"
    echo "   Worker 4:             tail -f /tmp/pg-worker4*.log"
    echo "   Ingestion:            tail -f /tmp/pg-ingestion*.log"
    echo "   Housekeeping:         tail -f /tmp/pg-housekeeping*.log"
    echo ""
    echo "✨ Local development environment ready!"
    echo "⏸️  Press Ctrl+C to stop all services..."
    echo ""
fi

# Give services a moment to initialize before starting health checks
sleep 2

# Track which services have already been reported as dead to avoid spam
DEAD_HUB_REPORTED=false
DEAD_DASHBOARD_REPORTED=false
DEAD_WORKER1_REPORTED=false
DEAD_WORKER2_REPORTED=false
DEAD_WORKER3_REPORTED=false
DEAD_WORKER4_REPORTED=false
DEAD_INGESTION_REPORTED=false
DEAD_HOUSEKEEPING_REPORTED=false

# Keep script running and wait for Ctrl+C
while true; do
    # Check if any service has died (warn but don't exit - let other services continue)
    if ! kill -0 $HUB_PID 2>/dev/null && [ "$DEAD_HUB_REPORTED" = false ]; then
        echo "⚠️  Hub process died unexpectedly. Check logs: /tmp/pg-hub.log"
        DEAD_HUB_REPORTED=true
    fi
    if ! kill -0 $DASHBOARD_PID 2>/dev/null && [ "$DEAD_DASHBOARD_REPORTED" = false ]; then
        echo "⚠️  Dashboard process died unexpectedly. Check logs: /tmp/pg-dashboard.log"
        DEAD_DASHBOARD_REPORTED=true
    fi
    if ! kill -0 $WORKER1_PID 2>/dev/null && [ "$DEAD_WORKER1_REPORTED" = false ]; then
        echo "⚠️  Worker 1 process died unexpectedly. Check logs: /tmp/pg-worker1.log"
        DEAD_WORKER1_REPORTED=true
    fi
    if ! kill -0 $WORKER2_PID 2>/dev/null && [ "$DEAD_WORKER2_REPORTED" = false ]; then
        echo "⚠️  Worker 2 process died unexpectedly. Check logs: /tmp/pg-worker2.log"
        DEAD_WORKER2_REPORTED=true
    fi
    if ! kill -0 $WORKER3_PID 2>/dev/null && [ "$DEAD_WORKER3_REPORTED" = false ]; then
        echo "⚠️  Worker 3 process died unexpectedly. Check logs: /tmp/pg-worker3.log"
        DEAD_WORKER3_REPORTED=true
    fi
    if ! kill -0 $WORKER4_PID 2>/dev/null && [ "$DEAD_WORKER4_REPORTED" = false ]; then
        echo "⚠️  Worker 4 process died unexpectedly. Check logs: /tmp/pg-worker4.log"
        DEAD_WORKER4_REPORTED=true
    fi
    if ! kill -0 $INGESTION_PID 2>/dev/null && [ "$DEAD_INGESTION_REPORTED" = false ]; then
        echo "⚠️  Ingestion process died unexpectedly. Check logs: /tmp/pg-ingestion*.log"
        DEAD_INGESTION_REPORTED=true
    fi
    if ! kill -0 $HOUSEKEEPING_PID 2>/dev/null && [ "$DEAD_HOUSEKEEPING_REPORTED" = false ]; then
        echo "⚠️  Housekeeping process died unexpectedly. Check logs: /tmp/pg-housekeeping*.log"
        DEAD_HOUSEKEEPING_REPORTED=true
    fi

    sleep 5
done
