#!/bin/bash

# start-frontail.sh
# Starts nine frontail instances for log viewing:
# - Port 9101: Hub logs
# - Port 9102: Worker 1 logs
# - Port 9103: Worker 2 logs
# - Port 9104: Worker 3 logs
# - Port 9105: Worker 4 logs
# - Port 9106: Ingestion logs
# - Port 9107: Housekeeping logs
# - Port 9108: Dashboard logs
# - Port 9109: Hub Background logs

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📊 Starting Frontail Log Viewers"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Check if frontail is installed
if ! command -v frontail &> /dev/null; then
    echo -e "${YELLOW}⚠️  Frontail not found. Installing via npm...${NC}"

    # Check if npm is available
    if ! command -v npm &> /dev/null; then
        echo -e "${RED}❌ Error: npm is not installed. Please install Node.js and npm first.${NC}"
        exit 1
    fi

    # Install frontail globally
    if npm install -g frontail; then
        echo -e "${GREEN}✅ Frontail installed successfully${NC}"
    else
        echo -e "${RED}❌ Error: Failed to install frontail${NC}"
        exit 1
    fi
else
    echo -e "${GREEN}✅ Frontail is already installed${NC}"
fi

# Kill any existing frontail processes on ports 9101-9109
echo ""
echo "🔍 Checking for existing frontail processes..."

for port in {9101..9109}; do
    pids=$(lsof -ti:$port 2>/dev/null || true)
    if [ -n "$pids" ]; then
        echo -e "${YELLOW}⚠️  Killing existing process(es) on port $port: $pids${NC}"
        echo "$pids" | xargs kill 2>/dev/null || true
        sleep 1
    fi
done

echo -e "${GREEN}✅ Ports 9101-9107 are clear${NC}"

# Ensure all log files exist (even if empty)
touch /tmp/pg-hub.log /tmp/pg-dashboard.log /tmp/pg-worker1.log /tmp/pg-worker2.log /tmp/pg-worker3.log /tmp/pg-worker4.log /tmp/pg-ingestion.log /tmp/pg-housekeeping.log /tmp/pg-hub-background.log

echo ""
echo "🚀 Starting Frontail Instances..."

# Define instances: port|name|pattern|exclude
INSTANCES=(
    "9101|Hub Main|/tmp/pg-hub.log|"
    "9102|Worker 1|/tmp/pg-worker1.log|"
    "9103|Worker 2|/tmp/pg-worker2.log|"
    "9104|Worker 3|/tmp/pg-worker3.log|"
    "9105|Worker 4|/tmp/pg-worker4.log|"
    "9106|Ingestion|/tmp/pg-ingestion*.log|"
    "9107|Housekeeping|/tmp/pg-housekeeping.log|"
    "9108|Dashboard|/tmp/pg-dashboard.log|"
    "9109|Hub Background|/tmp/pg-hub-background.log|"
)

for inst in "${INSTANCES[@]}"; do
    IFS="|" read -r port name pattern exclude <<< "$inst"

    # Discover log files excluding those with date patterns (e.g., pg-hub-20240101.log)
    logs=$(ls $pattern 2>/dev/null | grep -vE -- '-[0-9]{8}\.log$' || true)

    if [ -n "$exclude" ]; then
        logs=$(echo "$logs" | grep -vE -- "$exclude" || true)
    fi

    if [ -z "$logs" ]; then
        # Fallback to pattern if no files found and not a wildcard
        if [[ $pattern != *"*"* ]]; then
            logs=$pattern
        else
            # For workers, fallback to worker1 if nothing found
            if [[ $pattern == *"-worker*"* ]]; then
                logs="/tmp/pg-worker1.log"
            else
                logs=$pattern
            fi
        fi
    fi

    nohup frontail -h 127.0.0.1 -p $port -n 2000 -l 5000 $logs > /dev/null 2>&1 &
    disown
    echo -e "  ✅ Port $port: $name logs initialized"
done

# Verify all instances are running
echo ""
echo "🔍 Verifying frontail instances..."
sleep 2

all_running=true
for inst in "${INSTANCES[@]}"; do
    IFS="|" read -r port name pattern exclude <<< "$inst"
    if lsof -ti:$port &> /dev/null; then
        echo -e "${GREEN}✅ $name (Port $port) is running${NC}"
    else
        echo -e "${RED}❌ $name (Port $port) failed to start${NC}"
        all_running=false
    fi
done

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [ "$all_running" = true ]; then
    echo -e "${GREEN}✅ All Frontail instances started successfully!${NC}"
    echo ""
    echo "📊 Access your logs:"
    echo "   Hub Main:          http://localhost:9101"
    echo "   Worker 1:          http://localhost:9102"
    echo "   Worker 2:          http://localhost:9103"
    echo "   Worker 3:          http://localhost:9104"
    echo "   Worker 4:          http://localhost:9105"
    echo "   Ingestion:         http://localhost:9106"
    echo "   Housekeeping:      http://localhost:9107"
    echo "   Dashboard:         http://localhost:9108"
    echo "   Hub Background:    http://localhost:9109"
    echo ""
    exit 0
else
    echo -e "${RED}❌ One or more frontail instances failed to start${NC}"
    exit 1
fi
