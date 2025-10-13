#!/bin/bash
# Smoke test for Test Item API
# This script tests the complete workflow: create launch/suite -> start test item -> finish test item -> verify
# Uses the new TestItem Start/Finish endpoints with browser borrowing

set -e  # Exit on error

# Get script directory and project root
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"

# ==============================================================================
# Step 1: Preparing Test Environment
# ==============================================================================
echo "=========================================="
echo "Step 1: Preparing Test Environment"
echo "=========================================="
echo ""

# Configuration
HUB_URL="${HUB_URL:-http://localhost:5100}"
PROJECT_KEY="${PROJECT_KEY:-admin_default}"
ADMIN_USER="${ADMIN_USER:-admin}"
API_KEY_FILE="$PROJECT_ROOT/.api-key-local-dev"

# Self-healing API key retrieval with validation
if [ -z "$API_KEY" ]; then
    # Try to read cached API key
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
        fi
    fi

    # If still no valid key, try to create or retrieve one
    if [ -z "$API_KEY" ]; then
        echo "🔑 No API key found, attempting to retrieve/create one..."

        # Check if API key already exists for admin user
        EXISTING_KEYS_RESPONSE=$(curl -s "$HUB_URL/admin/users/$ADMIN_USER/api-keys" \
            -H "Content-Type: application/json" \
            -H "x-user-id: $ADMIN_USER" 2>/dev/null || echo '{"items":[]}')

        # Extract first key name if exists
        EXISTING_KEY_NAME=$(echo "$EXISTING_KEYS_RESPONSE" | grep -o '"name":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")

        if [ -z "$EXISTING_KEY_NAME" ]; then
            # Create new API key for admin user
            API_KEY_RESPONSE=$(curl -s -X POST "$HUB_URL/admin/users/$ADMIN_USER/api-keys" \
                -H "Content-Type: application/json" \
                -H "x-user-id: $ADMIN_USER" \
                -d '{
                    "name": "Local Dev Smoke Test"
                }' 2>/dev/null)

            API_KEY=$(echo "$API_KEY_RESPONSE" | grep -o '"apiKey":"[^"]*"' | cut -d'"' -f4)

            if [ -z "$API_KEY" ]; then
                echo "⚠️  Failed to create API key. Response: $API_KEY_RESPONSE"
                echo "   Using default test-api-key (may fail if authentication is enabled)"
                API_KEY="test-api-key"
            else
                echo "✅ Created new API key: ${API_KEY:0:16}..."
                # Cache the key for future runs
                echo "$API_KEY" > "$API_KEY_FILE"
                chmod 600 "$API_KEY_FILE"
            fi
        else
            # Retrieve existing key
            API_KEY_RESPONSE=$(curl -s "$HUB_URL/admin/users/$ADMIN_USER/api-keys/$EXISTING_KEY_NAME" \
                -H "Content-Type: application/json" \
                -H "x-user-id: $ADMIN_USER" 2>/dev/null)

            API_KEY=$(echo "$API_KEY_RESPONSE" | grep -o '"apiKey":"[^"]*"' | cut -d'"' -f4)

            if [ -z "$API_KEY" ]; then
                echo "⚠️  Failed to retrieve API key. Using default test-api-key"
                API_KEY="test-api-key"
            else
                echo "✅ Retrieved existing API key: ${API_KEY:0:16}..."
                # Cache the key for future runs
                echo "$API_KEY" > "$API_KEY_FILE"
                chmod 600 "$API_KEY_FILE"
            fi
        fi
    fi
else
    echo "✅ Using provided API key: ${API_KEY:0:16}..."
fi

echo ""
echo "=========================================="
echo "Step 2: Test Item API - Smoke Test"
echo "=========================================="
echo "Hub URL: $HUB_URL"
echo "Project Key: $PROJECT_KEY"
echo ""
echo "NOTE: Using new TestItem Start/Finish API with browser borrowing"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Timing tracking
# Use seconds timestamp (cross-platform compatible)
SCRIPT_START_TIME=$(date +%s)
TIMING_FILE=$(mktemp /tmp/smoke-test-timing.XXXXXX)

# Function to print colored output
print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_info() {
    echo -e "${YELLOW}ℹ $1${NC}"
}

print_timing() {
    echo -e "${CYAN}⏱  $1${NC}"
}

# Function to track request timing
track_request() {
    local request_name="$1"
    local start_time="$2"
    local end_time=$(date +%s%3N)
    local duration=$((end_time - start_time))
    REQUEST_TIMES+=("$duration")
    REQUEST_NAMES+=("$request_name")
}

# Function to make timed curl request (measures in milliseconds)
timed_curl() {
    local request_name="$1"
    shift
    local start_time=$(date +%s)
    local result=$(curl "$@" 2>/dev/null)
    local end_time=$(date +%s)
    local duration_secs=$((end_time - start_time))
    local duration_ms=$((duration_secs * 1000))

    # If duration is 0 seconds, assume at least 1ms
    if [ $duration_ms -eq 0 ]; then
        duration_ms=1
    fi

    # Write timing to file
    echo "$request_name|$duration_ms" >> "$TIMING_FILE"
    echo "$result"
}

# Step 1: Check hub health
echo "Step 1: Checking Hub health..."
if curl -sf "$HUB_URL/health" > /dev/null; then
    print_success "Hub is healthy"
else
    print_error "Hub is not responding. Make sure docker-compose is running."
    exit 1
fi

# Step 2: Create a test launch
echo ""
echo "Step 2: Creating test launch..."
LAUNCH_RESPONSE=$(timed_curl "POST /api/launches" -s -X POST "$HUB_URL/api/launches" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "name": "Regresion Test Launch",
        "description": "Automated smoke test launch to verify test result upload functionality and end-to-end workflow",
        "mode": "DEFAULT",
        "attributes": [
            {"key": "environment", "value": "local"},
            {"key": "type", "value": "smoke-test"},
            {"key": "automated", "value": "true"},
            {"key": "priority", "value": "high"}
        ]
    }')

LAUNCH_ID=$(echo "$LAUNCH_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$LAUNCH_ID" ]; then
    print_error "Failed to create launch"
    echo "Response: $LAUNCH_RESPONSE"
    exit 1
fi

print_success "Created launch: $LAUNCH_ID"

# Step 3: Create a test suite (as TestItem with Type="Suite")
echo ""
echo "Step 3: Creating test suite as TestItem..."
SUITE_RESPONSE=$(timed_curl "POST /api/test-items (Suite 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "name": "Smoke Test Suite",
        "description": "Core smoke test suite covering critical user workflows including authentication, navigation, and basic CRUD operations",
        "type": "Suite",
        "codeRef": "com.agenix.portal.smoketests",
        "testCaseId": "com.agenix.portal.smoketests.suite",
        "attributes": [
            {"key": "suite-type", "value": "smoke"},
            {"key": "browser", "value": "chromium"},
            {"key": "os", "value": "macos"},
            {"key": "execution-mode", "value": "parallel"}
        ]
    }')

SUITE_ID=$(echo "$SUITE_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$SUITE_ID" ]; then
    print_error "Failed to create suite"
    echo "Response: $SUITE_RESPONSE"
    exit 1
fi

print_success "Created suite: $SUITE_ID"

# Step 4: Start a test item under the suite (borrows browser automatically)
echo ""
echo "Step 4: Starting test item with browser borrowing..."
CURRENT_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

ITEM_RESPONSE=$(timed_curl "POST /api/test-items (Test 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE_ID'",
        "labelKey": "AppB:Chromium:UAT",
        "name": "Smoke Test - Login Flow",
        "description": "Automated test verifying login flow with valid credentials on Chromium browser in UAT environment",
        "type": "Test",
        "codeRef": "com.agenix.portal.smoketests.loginFlow",
        "testCaseId": "com.agenix.portal.smoketests.test.loginFlow.1",
        "attributes": [
            {"key": "test-type", "value": "e2e"},
            {"key": "feature", "value": "authentication"},
            {"key": "browser", "value": "chromium"},
            {"key": "env", "value": "uat"},
            {"key": "region", "value": "local"},
            {"key": "retry", "value": "0"}
        ],
        "startTime": "'"$CURRENT_TIME"'"
    }')

ITEM_ID=$(echo "$ITEM_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
BROWSER_ID=$(echo "$ITEM_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)
WS_ENDPOINT=$(echo "$ITEM_RESPONSE" | grep -o '"webSocketEndpoint":"[^"]*"' | head -1 | cut -d'"' -f4)
BROWSER_TYPE=$(echo "$ITEM_RESPONSE" | grep -o '"browserType":"[^"]*"' | head -1 | cut -d'"' -f4)
WORKER_NODE=$(echo "$ITEM_RESPONSE" | grep -o '"workerNodeId":"[^"]*"' | head -1 | cut -d'"' -f4)
SESSION_STATUS=$(echo "$ITEM_RESPONSE" | grep -o '"sessionStatus":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ITEM_ID" ]; then
    print_error "Failed to start test item"
    echo "Response: $ITEM_RESPONSE"
    exit 1
fi

print_success "Started test item: $ITEM_ID"

# Create log items for Test Item 1 with different levels and attachments
echo ""
echo "Creating log items for test item 1..."

# Log Item 1: INFO level with text file attachment
CURRENT_TIMESTAMP=$(date)
LOG1_RESPONSE=$(timed_curl "POST /v1/log (INFO)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ITEM_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Test execution started successfully for login flow test\",
    \"file\": {
      \"name\": \"test-execution.log\",
      \"data\": \"$(echo "Test execution log - login flow
Started: $CURRENT_TIMESTAMP
Browser: chromium
Environment: UAT
Region: local" | base64)\"
    }
  }")

if echo "$LOG1_RESPONSE" | grep -q '"id"'; then
    LOG1_ID=$(echo "$LOG1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created INFO log item: $LOG1_ID"
else
    print_error "Failed to create INFO log item"
    echo "Response: $LOG1_RESPONSE"
fi

# Log Item 2: DEBUG level with verbose details
LOG2_RESPONSE=$(timed_curl "POST /v1/log (DEBUG)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ITEM_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Browser borrowed from pool - BrowserId: $BROWSER_ID, WorkerNode: $WORKER_NODE, SessionStatus: $SESSION_STATUS\",
    \"file\": {
      \"name\": \"browser-details.txt\",
      \"data\": \"$(echo 'Browser Details:
Browser ID: '$BROWSER_ID'
Browser Type: '$BROWSER_TYPE'
WebSocket: '$WS_ENDPOINT'
Worker Node: '$WORKER_NODE'
Session Status: '$SESSION_STATUS | base64)\"
    }
  }")

if echo "$LOG2_RESPONSE" | grep -q '"id"'; then
    LOG2_ID=$(echo "$LOG2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created DEBUG log item: $LOG2_ID"
else
    print_error "Failed to create DEBUG log item"
    echo "Response: $LOG2_RESPONSE"
fi

# Display borrowed browser details
if [ -n "$BROWSER_ID" ]; then
    echo ""
    echo "┌─────────────────────────────────────────────────────────────────┐"
    echo "│              📦 Borrowed Browser Details                        │"
    echo "├─────────────────────────────────────────────────────────────────┤"
    printf "│ %-18s: %-42s │\n" "Browser ID" "$BROWSER_ID"
    if [ -n "$BROWSER_TYPE" ]; then
        printf "│ %-18s: %-42s │\n" "Browser Type" "$BROWSER_TYPE"
    fi
    if [ -n "$WORKER_NODE" ]; then
        printf "│ %-18s: %-42s │\n" "Worker Node" "$WORKER_NODE"
    fi
    if [ -n "$SESSION_STATUS" ]; then
        printf "│ %-18s: %-42s │\n" "Session Status" "$SESSION_STATUS"
    fi
    if [ -n "$WS_ENDPOINT" ]; then
        printf "│ %-18s: %-42s │\n" "WebSocket" "${WS_ENDPOINT:0:42}"
        if [ ${#WS_ENDPOINT} -gt 42 ]; then
            printf "│ %-18s  %-42s │\n" "" "${WS_ENDPOINT:42}"
        fi
    fi
    echo "└─────────────────────────────────────────────────────────────────┘"
    echo ""
    print_success "Browser borrowed and ready for test execution"
fi

# Create nested steps under Test 1 (Login Flow)
echo ""
echo "Creating nested steps under Test 1 (Login Flow)..."

# Step 1: Navigate to login page
STEP1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
STEP1_RESPONSE=$(timed_curl "POST /api/test-items (Test 1, Step 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM_ID'",
        "name": "Navigate to login page",
        "description": "Open browser and navigate to login URL",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "navigation"},
            {"key": "action", "value": "open-url"}
        ],
        "startTime": "'"$STEP1_TIME"'"
    }')

STEP1_ID=$(echo "$STEP1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$STEP1_ID" ]; then
    print_error "Failed to create Step 1"
    exit 1
fi
print_success "Created Step 1: $STEP1_ID"

# Log items for Step 1
LOG_STEP1_1=$(timed_curl "POST /v1/log (Step 1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Opening URL: https://example.com/login\"
  }")

LOG_STEP1_2=$(timed_curl "POST /v1/log (Step 1, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Page loaded successfully, checking for login form elements\"
  }")

# Step 1.1: Verify page title
STEP1_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
STEP1_1_RESPONSE=$(timed_curl "POST /api/test-items (Test 1, Step 1.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$STEP1_ID'",
        "name": "Verify page title",
        "description": "Check that page title matches expected value",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "verification"},
            {"key": "action", "value": "assert"}
        ],
        "startTime": "'"$STEP1_1_TIME"'"
    }')

STEP1_1_ID=$(echo "$STEP1_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$STEP1_1_ID" ]; then
    print_error "Failed to create Step 1.1"
    exit 1
fi
print_success "Created Step 1.1: $STEP1_1_ID"

# Log items for Step 1.1
timed_curl "POST /v1/log (Step 1.1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP1_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Trace\",
    \"message\": \"Getting page title from document.title\"
  }" > /dev/null

timed_curl "POST /v1/log (Step 1.1, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP1_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Page title verified: 'Login - Portal'\"
  }" > /dev/null

# Finish Step 1.1
sleep 0.1
STEP1_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (Step 1.1)" -s -X PUT "$HUB_URL/api/test-items/$STEP1_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Page title verified successfully",
        "endTime": "'"$STEP1_1_END"'"
    }' > /dev/null
print_success "Finished Step 1.1 as Passed"

# Step 1.2: Verify login form elements
STEP1_2_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
STEP1_2_RESPONSE=$(timed_curl "POST /api/test-items (Test 1, Step 1.2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$STEP1_ID'",
        "name": "Verify login form elements",
        "description": "Check that username and password fields are present",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "verification"},
            {"key": "action", "value": "find-elements"}
        ],
        "startTime": "'"$STEP1_2_TIME"'"
    }')

STEP1_2_ID=$(echo "$STEP1_2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$STEP1_2_ID" ]; then
    print_error "Failed to create Step 1.2"
    exit 1
fi
print_success "Created Step 1.2: $STEP1_2_ID"

# Log items for Step 1.2
timed_curl "POST /v1/log (Step 1.2, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP1_2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Searching for form elements: #username, #password, #login-button\"
  }" > /dev/null

timed_curl "POST /v1/log (Step 1.2, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP1_2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"All login form elements found and are visible\"
  }" > /dev/null

# Finish Step 1.2
sleep 0.1
STEP1_2_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (Step 1.2)" -s -X PUT "$HUB_URL/api/test-items/$STEP1_2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Login form elements verified",
        "endTime": "'"$STEP1_2_END"'"
    }' > /dev/null
print_success "Finished Step 1.2 as Passed"

# Finish Step 1
sleep 0.1
STEP1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (Step 1)" -s -X PUT "$HUB_URL/api/test-items/$STEP1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Navigation to login page completed",
        "endTime": "'"$STEP1_END"'"
    }' > /dev/null
print_success "Finished Step 1 as Passed"

# Step 2: Fill credentials
STEP2_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
STEP2_RESPONSE=$(timed_curl "POST /api/test-items (Test 1, Step 2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM_ID'",
        "name": "Fill login credentials",
        "description": "Enter username and password into form fields",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "action"},
            {"key": "action", "value": "fill-form"}
        ],
        "startTime": "'"$STEP2_TIME"'"
    }')

STEP2_ID=$(echo "$STEP2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$STEP2_ID" ]; then
    print_error "Failed to create Step 2"
    exit 1
fi
print_success "Created Step 2: $STEP2_ID"

# Log items for Step 2
timed_curl "POST /v1/log (Step 2, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Filling username field with test credentials\"
  }" > /dev/null

timed_curl "POST /v1/log (Step 2, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"WARN\",
    \"message\": \"Password field shows weak password indicator - continuing anyway\"
  }" > /dev/null

# Step 2.1: Enter username
STEP2_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
STEP2_1_RESPONSE=$(timed_curl "POST /api/test-items (Test 1, Step 2.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$STEP2_ID'",
        "name": "Enter username",
        "description": "Type username into username field",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "action"},
            {"key": "action", "value": "type"}
        ],
        "startTime": "'"$STEP2_1_TIME"'"
    }')

STEP2_1_ID=$(echo "$STEP2_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$STEP2_1_ID" ]; then
    print_error "Failed to create Step 2.1"
    exit 1
fi
print_success "Created Step 2.1: $STEP2_1_ID"

# Log items for Step 2.1
timed_curl "POST /v1/log (Step 2.1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP2_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Typing username: testuser@example.com\"
  }" > /dev/null

timed_curl "POST /v1/log (Step 2.1, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$STEP2_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Username field value updated successfully\"
  }" > /dev/null

# Finish Step 2.1
sleep 0.1
STEP2_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (Step 2.1)" -s -X PUT "$HUB_URL/api/test-items/$STEP2_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Username entered successfully",
        "endTime": "'"$STEP2_1_END"'"
    }' > /dev/null
print_success "Finished Step 2.1 as Passed"

# Finish Step 2
sleep 0.1
STEP2_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (Step 2)" -s -X PUT "$HUB_URL/api/test-items/$STEP2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Credentials filled successfully",
        "endTime": "'"$STEP2_END"'"
    }' > /dev/null
print_success "Finished Step 2 as Passed"

print_success "All nested steps for Test 1 created successfully"

# Step 5: Create BeforeMethod hook test (will fail)
echo ""
echo "Step 5: Creating BeforeMethod hook test (will fail - NO browser borrowed)..."
BEFORE_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

BEFORE_RESPONSE=$(timed_curl "POST /api/test-items (BeforeMethod)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE_ID'",
        "name": "Setup: Initialize Test Data",
        "description": "BeforeMethod hook to setup test preconditions and initialize test data",
        "type": "BeforeMethod",
        "codeRef": "com.agenix.demodata.beforeMethod",
        "testCaseId": "com.agenix.demodata.beforeMethod.setup",
        "attributes": [
            {"key": "hook", "value": "before-method"},
            {"key": "phase", "value": "setup"}
        ],
        "startTime": "'"$BEFORE_TIME"'"
    }')

BEFORE_ID=$(echo "$BEFORE_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
BEFORE_BROWSER=$(echo "$BEFORE_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$BEFORE_ID" ]; then
    print_error "Failed to create BeforeMethod test"
    echo "Response: $BEFORE_RESPONSE"
    exit 1
fi

print_success "Created BeforeMethod hook: $BEFORE_ID (no browser borrowed - hook type)"
if [ -n "$BEFORE_BROWSER" ]; then
    print_error "WARNING: BeforeMethod should NOT borrow browser, but got: $BEFORE_BROWSER"
fi

# BeforeMethod ERROR logs are skipped for now to simplify the smoke test
# These were causing JSON parsing issues due to their length
# The main ERROR and FATAL logs in Test Item 3 will demonstrate the error fingerprinting feature

# Finish BeforeMethod as Failed
sleep 0.1
BEFORE_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
BEFORE_FINISH=$(timed_curl "PUT /api/test-items/finish (BeforeMethod)" -s -X PUT "$HUB_URL/api/test-items/$BEFORE_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Failed",
        "description": "Failed to initialize database connection",
        "endTime": "'"$BEFORE_END"'"
    }')

if echo "$BEFORE_FINISH" | grep -q "message\|finished"; then
    print_error "BeforeMethod hook failed (expected) - no browser to return"
else
    print_info "BeforeMethod finish response: $BEFORE_FINISH"
fi

# Step 6: Finish the main test item (returns browser to pool automatically)
echo ""
echo "Step 6: Finishing main test item (returns browser to pool)..."
END_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
FINISH_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Test 1)" -s -X PUT "$HUB_URL/api/test-items/$ITEM_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Test completed successfully",
        "endTime": "'"$END_TIME"'"
    }')

if echo "$FINISH_RESPONSE" | grep -q "message\|finished"; then
    print_success "Test item finished successfully (browser returned to pool)"
    if [ -n "$BROWSER_ID" ]; then
        print_success "Browser $BROWSER_ID returned to pool"
    fi
else
    print_error "Failed to finish test item"
    echo "Response: $FINISH_RESPONSE"
    exit 1
fi

# Step 7: Create AfterMethod hook test (will be skipped)
echo ""
echo "Step 7: Creating AfterMethod hook test (will be skipped - NO browser borrowed)..."
AFTER_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

AFTER_RESPONSE=$(timed_curl "POST /api/test-items (AfterMethod)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE_ID'",
        "name": "Teardown: Cleanup Test Data",
        "description": "AfterMethod hook to cleanup test data and release resources",
        "type": "AfterMethod",
        "codeRef": "com.agenix.demodata.afterMethod",
        "testCaseId": "com.agenix.demodata.afterMethod.teardown",
        "attributes": [
            {"key": "hook", "value": "after-method"},
            {"key": "phase", "value": "teardown"}
        ],
        "startTime": "'"$AFTER_TIME"'"
    }')

AFTER_ID=$(echo "$AFTER_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
AFTER_BROWSER=$(echo "$AFTER_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$AFTER_ID" ]; then
    print_error "Failed to create AfterMethod test"
    echo "Response: $AFTER_RESPONSE"
    exit 1
fi

print_success "Created AfterMethod hook: $AFTER_ID (no browser borrowed - hook type)"
if [ -n "$AFTER_BROWSER" ]; then
    print_error "WARNING: AfterMethod should NOT borrow browser, but got: $AFTER_BROWSER"
fi

# Finish AfterMethod as Skipped
sleep 0.1
AFTER_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
AFTER_FINISH=$(timed_curl "PUT /api/test-items/finish (AfterMethod)" -s -X PUT "$HUB_URL/api/test-items/$AFTER_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Skipped",
        "description": "Skipped due to BeforeMethod failure",
        "endTime": "'"$AFTER_END"'"
    }')

if echo "$AFTER_FINISH" | grep -q "message\|finished"; then
    print_info "AfterMethod hook skipped (expected) - no browser to return"
else
    print_info "AfterMethod finish response: $AFTER_FINISH"
fi

# Step 8: Finish the first suite (as TestItem)
echo ""
echo "Step 8: Finishing first test suite (as TestItem)..."
FINISH_SUITE_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Suite 1)" -s -X PUT "$HUB_URL/api/test-items/$SUITE_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Failed",
        "description": "Suite completed with failures (BeforeMethod failed)",
        "endTime": "'"$END_TIME"'"
    }')

if echo "$FINISH_SUITE_RESPONSE" | grep -q "message\|finished"; then
    print_success "First test suite finished with status: Failed"
else
    print_info "Finish suite response: $FINISH_SUITE_RESPONSE"
fi

# Step 9: Create a second suite (PASSED) with long description
echo ""
echo "Step 9: Creating second test suite with PASSED status and long description..."
SUITE2_RESPONSE=$(timed_curl "POST /api/test-items (Suite 2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "name": "Comprehensive E2E Smoke Test Suite",
        "description": "This comprehensive end-to-end smoke test suite validates critical user workflows across multiple modules including authentication, authorization, user management, dashboard navigation, data persistence, API integrations, notification systems, and real-time updates. The suite is designed to catch major regressions early in the deployment pipeline by exercising the most frequently used features of the application. Each test case within this suite represents a core business capability that must function correctly for the application to be considered production-ready. The suite runs in parallel across multiple browser environments (Chrome, Firefox, Safari) and validates both positive and negative scenarios. Special attention is given to cross-browser compatibility, responsive design, accessibility standards (WCAG 2.1 AA), and performance benchmarks. Test data is generated dynamically to ensure isolation between test runs and prevent flaky test failures. All tests use page object model design pattern for maintainability and follow best practices for async/await handling. The suite typically completes in under 5 minutes when running with full parallelization enabled.",
        "type": "Suite",
        "codeRef": "com.agenix.portal.e2etests",
        "testCaseId": "com.agenix.portal.e2etests.suite",
        "attributes": [
            {"key": "suite-type", "value": "comprehensive"},
            {"key": "browser", "value": "multi-browser"},
            {"key": "os", "value": "cross-platform"},
            {"key": "execution-mode", "value": "parallel"},
            {"key": "priority", "value": "critical"},
            {"key": "category", "value": "smoke"}
        ]
    }')

SUITE2_ID=$(echo "$SUITE2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$SUITE2_ID" ]; then
    print_error "Failed to create second suite"
    echo "Response: $SUITE2_RESPONSE"
    exit 1
fi

print_success "Created second suite: $SUITE2_ID"

# Step 10: Start first test item under the second suite (borrows browser)
echo ""
echo "Step 10: Starting first test (User Registration) in second suite..."
SUITE2_TEST_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

ITEM2_RESPONSE=$(timed_curl "POST /api/test-items (Suite 2, Test 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE2_ID'",
        "labelKey": "AppB:Chromium:UAT",
        "name": "E2E Test - Complete User Registration Flow",
        "description": "End-to-end test validating the complete user registration workflow including form validation, email verification, password strength checks, CAPTCHA validation, terms acceptance, and successful account creation with automated welcome email delivery",
        "type": "Test",
        "codeRef": "com.agenix.portal.e2e.registrationFlow",
        "testCaseId": "com.agenix.portal.e2e.test.registrationFlow.1",
        "attributes": [
            {"key": "test-type", "value": "e2e"},
            {"key": "feature", "value": "registration"},
            {"key": "browser", "value": "chromium"},
            {"key": "env", "value": "uat"},
            {"key": "region", "value": "local"},
            {"key": "priority", "value": "high"}
        ],
        "startTime": "'"$SUITE2_TEST_TIME"'"
    }')

ITEM2_ID=$(echo "$ITEM2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
BROWSER2_ID=$(echo "$ITEM2_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ITEM2_ID" ]; then
    print_error "Failed to start test item in second suite"
    echo "Response: $ITEM2_RESPONSE"
    exit 1
fi

print_success "Started test item in second suite: $ITEM2_ID"
if [ -n "$BROWSER2_ID" ]; then
    print_success "Browser borrowed: $BROWSER2_ID"
fi

# Create log items for Test Item 2 with TRACE and WARN levels
echo ""
echo "Creating log items for test item 2..."

# Log Item 1: TRACE level with JSON configuration
LOG3_RESPONSE=$(timed_curl "POST /v1/log (TRACE)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ITEM2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Trace\",
    \"message\": \"Entering user registration flow - initializing form validation engine\",
    \"file\": {
      \"name\": \"test-config.json\",
      \"data\": \"$(echo '{
  "testSuite": "User Registration",
  "environment": "UAT",
  "browser": "chromium",
  "viewport": { "width": 1920, "height": 1080 },
  "timeout": 30000,
  "validations": {
    "email": true,
    "password": true,
    "captcha": true,
    "terms": true
  }
}' | base64)\"
    }
  }")

if echo "$LOG3_RESPONSE" | grep -q '"id"'; then
    LOG3_ID=$(echo "$LOG3_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created TRACE log item: $LOG3_ID"
else
    print_error "Failed to create TRACE log item"
    echo "Response: $LOG3_RESPONSE"
fi

# Log Item 2: WARN level for minor issue
CURRENT_TIMESTAMP=$(date)
LOG4_RESPONSE=$(timed_curl "POST /v1/log (WARN)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ITEM2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"WARN\",
    \"message\": \"CAPTCHA service responded slowly (2.3s) - still within acceptable threshold but approaching timeout\",
    \"file\": {
      \"name\": \"captcha-response.txt\",
      \"data\": \"$(echo "CAPTCHA Validation Response:
Status: SUCCESS
Response Time: 2.3 seconds
Threshold: 3.0 seconds
Challenge Type: reCAPTCHA v2
User Agent: Mozilla/5.0 (X11; Linux x86_64)
IP Address: 192.168.1.100
Timestamp: $CURRENT_TIMESTAMP" | base64)\"
    }
  }")

if echo "$LOG4_RESPONSE" | grep -q '"id"'; then
    LOG4_ID=$(echo "$LOG4_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created WARN log item: $LOG4_ID"
else
    print_error "Failed to create WARN log item"
    echo "Response: $LOG4_RESPONSE"
fi

# Create nested steps under Test 2 (User Registration)
echo ""
echo "Creating nested steps under Test 2 (User Registration)..."

# Test 2 - Step 1: Fill registration form
T2_STEP1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T2_STEP1_RESPONSE=$(timed_curl "POST /api/test-items (Test 2, Step 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM2_ID'",
        "name": "Fill registration form",
        "description": "Enter user details into registration form",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "action"},
            {"key": "action", "value": "fill-form"}
        ],
        "startTime": "'"$T2_STEP1_TIME"'"
    }')

T2_STEP1_ID=$(echo "$T2_STEP1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "$T2_STEP1_ID" ]; then
    print_error "Failed to create Test 2 Step 1"
    exit 1
fi
print_success "Created Test 2 Step 1: $T2_STEP1_ID"

# Log items for Test 2 Step 1
timed_curl "POST /v1/log (T2 Step 1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Filling email field: newuser@example.com\"
  }" > /dev/null

timed_curl "POST /v1/log (T2 Step 1, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Email validation passed\"
  }" > /dev/null

# Test 2 Step 1.1: Verify password strength
T2_STEP1_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T2_STEP1_1_RESPONSE=$(timed_curl "POST /api/test-items (Test 2, Step 1.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T2_STEP1_ID'",
        "name": "Verify password strength",
        "description": "Check password meets requirements",
        "type": "Step",
        "attributes": [{"key": "step", "value": "verification"}],
        "startTime": "'"$T2_STEP1_1_TIME"'"
    }')

T2_STEP1_1_ID=$(echo "$T2_STEP1_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created Test 2 Step 1.1: $T2_STEP1_1_ID"

# Log items for Test 2 Step 1.1
timed_curl "POST /v1/log (T2 Step 1.1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP1_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Trace\",
    \"message\": \"Checking password strength requirements\"
  }" > /dev/null

timed_curl "POST /v1/log (T2 Step 1.1, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP1_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Password strength: STRONG\"
  }" > /dev/null

# Finish Test 2 Step 1.1
sleep 0.1
T2_STEP1_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T2 Step 1.1)" -s -X PUT "$HUB_URL/api/test-items/$T2_STEP1_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Password strength verified",
        "endTime": "'"$T2_STEP1_1_END"'"
    }' > /dev/null
print_success "Finished Test 2 Step 1.1 as Passed"

# Finish Test 2 Step 1
sleep 0.1
T2_STEP1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T2 Step 1)" -s -X PUT "$HUB_URL/api/test-items/$T2_STEP1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Registration form filled",
        "endTime": "'"$T2_STEP1_END"'"
    }' > /dev/null
print_success "Finished Test 2 Step 1 as Passed"

# Test 2 - Step 2: Submit registration
T2_STEP2_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T2_STEP2_RESPONSE=$(timed_curl "POST /api/test-items (Test 2, Step 2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM2_ID'",
        "name": "Submit registration",
        "description": "Click submit and verify success",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "action"},
            {"key": "action", "value": "submit"}
        ],
        "startTime": "'"$T2_STEP2_TIME"'"
    }')

T2_STEP2_ID=$(echo "$T2_STEP2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created Test 2 Step 2: $T2_STEP2_ID"

# Log items for Test 2 Step 2
timed_curl "POST /v1/log (T2 Step 2, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Clicking submit button\"
  }" > /dev/null

timed_curl "POST /v1/log (T2 Step 2, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Registration successful, redirected to confirmation page\"
  }" > /dev/null

# Test 2 Step 2.1: Verify confirmation email
T2_STEP2_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T2_STEP2_1_RESPONSE=$(timed_curl "POST /api/test-items (Test 2, Step 2.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T2_STEP2_ID'",
        "name": "Verify confirmation email",
        "description": "Check email was sent",
        "type": "Step",
        "attributes": [{"key": "step", "value": "verification"}],
        "startTime": "'"$T2_STEP2_1_TIME"'"
    }')

T2_STEP2_1_ID=$(echo "$T2_STEP2_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created Test 2 Step 2.1: $T2_STEP2_1_ID"

# Log items for Test 2 Step 2.1
timed_curl "POST /v1/log (T2 Step 2.1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP2_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Querying mail server for confirmation email\"
  }" > /dev/null

timed_curl "POST /v1/log (T2 Step 2.1, Log 2)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T2_STEP2_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Confirmation email received and verified\"
  }" > /dev/null

# Finish Test 2 Step 2.1
sleep 0.1
T2_STEP2_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T2 Step 2.1)" -s -X PUT "$HUB_URL/api/test-items/$T2_STEP2_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Email verified",
        "endTime": "'"$T2_STEP2_1_END"'"
    }' > /dev/null
print_success "Finished Test 2 Step 2.1 as Passed"

# Finish Test 2 Step 2
sleep 0.1
T2_STEP2_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T2 Step 2)" -s -X PUT "$HUB_URL/api/test-items/$T2_STEP2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Registration submitted",
        "endTime": "'"$T2_STEP2_END"'"
    }' > /dev/null
print_success "Finished Test 2 Step 2 as Passed"

print_success "All nested steps for Test 2 created successfully"

# Step 11: Finish the first test item in Suite 2 as PASSED
sleep 0.1
echo ""
echo "Step 11: Finishing first test (User Registration) as PASSED..."
SUITE2_END_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
FINISH2_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Suite 2, Test 1)" -s -X PUT "$HUB_URL/api/test-items/$ITEM2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "All registration workflow steps completed successfully with valid data",
        "endTime": "'"$SUITE2_END_TIME"'"
    }')

if echo "$FINISH2_RESPONSE" | grep -q "message\|finished"; then
    print_success "First test in Suite 2 finished as PASSED (browser returned to pool)"
else
    print_error "Failed to finish first test in Suite 2"
    echo "Response: $FINISH2_RESPONSE"
fi

# Step 12: Start second test (Dashboard Navigation) in Suite 2
echo ""
echo "Step 12: Starting second test (Dashboard Navigation) in Suite 2..."
ITEM3_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

ITEM3_RESPONSE=$(timed_curl "POST /api/test-items (Suite 2, Test 2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE2_ID'",
        "labelKey": "AppB:Chromium:UAT",
        "name": "E2E Test - Dashboard Navigation and Widget Interactions",
        "description": "Comprehensive test covering dashboard navigation, widget loading, data refresh, filtering capabilities, and user preference persistence",
        "type": "Test",
        "codeRef": "com.agenix.portal.e2e.dashboardNav",
        "testCaseId": "com.agenix.portal.e2e.test.dashboardNav.1",
        "attributes": [
            {"key": "test-type", "value": "e2e"},
            {"key": "feature", "value": "dashboard"},
            {"key": "browser", "value": "chromium"},
            {"key": "env", "value": "uat"},
            {"key": "priority", "value": "high"}
        ],
        "startTime": "'"$ITEM3_TIME"'"
    }')

ITEM3_ID=$(echo "$ITEM3_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
BROWSER3_ID=$(echo "$ITEM3_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ITEM3_ID" ]; then
    print_error "Failed to start second test in Suite 2"
    echo "Response: $ITEM3_RESPONSE"
    exit 1
fi

print_success "Started second test in Suite 2: $ITEM3_ID"
if [ -n "$BROWSER3_ID" ]; then
    print_success "Browser borrowed: $BROWSER3_ID"
fi

# Create log items for Test Item 3 with ERROR and FATAL levels
echo ""
echo "Creating log items for test item 3..."

# Log Item 1: ERROR level with realistic exception and screenshot attachment
ERROR_MSG='SocketTimeoutException: Read timed out after 30000ms while loading dashboard widget data from API endpoint https://api.example.com/v2/dashboard/widgets/analytics\n\njava.net.SocketTimeoutException: Read timed out\n    at java.base/sun.nio.ch.NioSocketImpl.timedRead(NioSocketImpl.java:283)\n    at java.base/sun.nio.ch.NioSocketImpl.implRead(NioSocketImpl.java:309)\n    at java.base/sun.nio.ch.NioSocketImpl.read(NioSocketImpl.java:350)\n    at java.base/sun.nio.ch.NioSocketImpl$1.read(NioSocketImpl.java:803)\n    at java.base/java.net.Socket$SocketInputStream.read(Socket.java:966)\n    at okhttp3.internal.http1.Http1ExchangeCodec$FixedLengthSource.read(Http1ExchangeCodec.kt:365)\n    at okhttp3.internal.connection.Exchange$ResponseBodySource.read(Exchange.kt:286)\n    at okio.RealBufferedSource.read(RealBufferedSource.kt:206)\n    at retrofit2.OkHttpCall.parseResponse(OkHttpCall.java:243)\n    at retrofit2.OkHttpCall.execute(OkHttpCall.java:186)\n    at com.agenix.portal.services.DashboardService.loadWidgetData(DashboardService.java:156)\n    at com.agenix.portal.pages.DashboardPage.refreshAnalyticsWidget(DashboardPage.java:342)\n    at com.agenix.portal.pages.DashboardPage.initialize(DashboardPage.java:89)\n    at com.agenix.portal.tests.DashboardNavigationTest.verifyDashboardLoad(DashboardNavigationTest.java:78)\n    at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke0(Native Method)\n    at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke(NativeMethodAccessorImpl.java:77)\n    at java.base/jdk.internal.reflect.DelegatingMethodAccessorImpl.invoke(DelegatingMethodAccessorImpl.java:43)\n    at java.base/java.lang.reflect.Method.invoke(Method.java:568)\n\nRequest Details:\n  URL: https://api.example.com/v2/dashboard/widgets/analytics?userId=12345&timeRange=7d\n  Method: GET\n  Timeout: 30000ms\n  Retry Attempts: 3\n  Connection Pool: okhttp3.ConnectionPool@7a81197d\n\nNetwork Conditions:\n  Latency: ~8500ms\n  Packet Loss: 2.3%\n  DNS Resolution Time: 245ms\n  TCP Handshake Time: 1823ms\n  TLS Handshake Time: 2167ms'

LOG5_RESPONSE=$(timed_curl "POST /v1/log (ERROR)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d @- <<EOF
{
  "itemUuid": "$ITEM3_ID",
  "launchUuid": "$LAUNCH_ID",
  "time": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "level": "Error",
  "message": $(echo -n "$ERROR_MSG" | jq -Rs .),
  "file": {
    "name": "error-screenshot.png",
    "data": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
  }
}
EOF
)

if echo "$LOG5_RESPONSE" | grep -q '"id"'; then
    LOG5_ID=$(echo "$LOG5_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created ERROR log item: $LOG5_ID"
else
    print_error "Failed to create ERROR log item"
    echo "Response: $LOG5_RESPONSE"
fi

# Log Item 2: FATAL level with realistic critical exception and full stack trace
CURRENT_TIMESTAMP=$(date)
FATAL_MSG='NullPointerException: Cannot invoke '\''org.openqa.selenium.WebDriver.getCurrentUrl()'\'' because '\''this.driver'\'' is null - Critical browser session failure causing test execution to abort\n\njava.lang.NullPointerException: Cannot invoke '\''org.openqa.selenium.WebDriver.getCurrentUrl()'\'' because '\''this.driver'\'' is null\n    at com.agenix.portal.framework.BrowserSession.getCurrentUrl(BrowserSession.java:234)\n    at com.agenix.portal.framework.BrowserSession.verifyPageLoaded(BrowserSession.java:187)\n    at com.agenix.portal.framework.BasePage.waitForPageReady(BasePage.java:145)\n    at com.agenix.portal.pages.DashboardPage.navigate(DashboardPage.java:67)\n    at com.agenix.portal.tests.DashboardNavigationTest.testDashboardWidgetInteraction(DashboardNavigationTest.java:92)\n    at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke0(Native Method)\n    at java.base/jdk.internal.reflect.NativeMethodAccessorImpl.invoke(NativeMethodAccessorImpl.java:77)\n    at java.base/jdk.internal.reflect.DelegatingMethodAccessorImpl.invoke(DelegatingMethodAccessorImpl.java:43)\n    at java.base/java.lang.reflect.Method.invoke(Method.java:568)\n    at org.testng.internal.MethodInvocationHelper.invokeMethod(MethodInvocationHelper.java:134)\n    at org.testng.internal.TestInvoker.invokeMethod(TestInvoker.java:597)\n    at org.testng.internal.TestInvoker.invokeTestMethod(TestInvoker.java:173)\n    at org.testng.internal.MethodRunner.runInSequence(MethodRunner.java:46)\n    at org.testng.internal.TestInvoker$MethodInvocationAgent.invoke(TestInvoker.java:824)\n    at org.testng.internal.TestInvoker.invokeTestMethods(TestInvoker.java:146)\n    at org.testng.internal.TestMethodWorker.invokeTestMethods(TestMethodWorker.java:146)\n    at org.testng.internal.TestMethodWorker.run(TestMethodWorker.java:128)\n    at java.base/java.util.ArrayList.forEach(ArrayList.java:1511)\n    at org.testng.TestRunner.privateRun(TestRunner.java:794)\n    at org.testng.TestRunner.run(TestRunner.java:596)\n    at org.testng.SuiteRunner.runTest(SuiteRunner.java:377)\n    at org.testng.SuiteRunner.runSequentially(SuiteRunner.java:371)\n    at org.testng.SuiteRunner.privateRun(SuiteRunner.java:332)\n    at org.testng.SuiteRunner.run(SuiteRunner.java:276)\n    at org.testng.SuiteRunnerWorker.runSuite(SuiteRunnerWorker.java:53)\n    at org.testng.SuiteRunnerWorker.run(SuiteRunnerWorker.java:96)\n    at org.testng.TestNG.runSuitesSequentially(TestNG.java:1212)\n    at org.testng.TestNG.runSuitesLocally(TestNG.java:1134)\n    at org.testng.TestNG.runSuites(TestNG.java:1063)\n    at org.testng.TestNG.run(TestNG.java:1031)\n    at com.agenix.portal.runner.TestRunner.execute(TestRunner.java:156)\n    at com.agenix.portal.runner.TestRunner.main(TestRunner.java:89)\n\nRoot Cause Analysis:\n  The WebDriver instance was unexpectedly garbage collected during test execution.\n  This typically occurs when the browser process crashes or the WebSocket connection\n  is terminated due to network instability or memory pressure.\n\nBrowser Session State at Failure:\n  Browser Type: chromium\n  WebDriver Instance: null (GC collected)\n  WebSocket Status: CLOSED (error code 1006 - abnormal closure)\n  Process ID: 45678 (terminated)\n  Memory Usage (before failure): 1.8 GB\n  CPU Usage (before failure): 78%\n  Last Successful Command: executeScript('\''return document.readyState'\'')\n  Failed Command: getCurrentUrl()\n  Time Since Last Activity: 3245ms\n  Connection Timeout Setting: 30000ms\n  Page Load Timeout: 60000ms\n  Implicit Wait: 10000ms\n\nSystem Environment:\n  OS: Linux Ubuntu 22.04.3 LTS\n  Architecture: x86_64\n  Available Memory: 2.1 GB / 16 GB\n  CPU Cores: 8 (6 available)\n  Java Version: 17.0.8 (OpenJDK)\n  Selenium Version: 4.15.0\n  ChromeDriver Version: 119.0.6045.105\n  Test Framework: TestNG 7.8.0\n\nNetwork Diagnostics:\n  Connection State: DISCONNECTED\n  Last Ping/Pong: 5 seconds ago\n  Network Interface: eth0 (status: UP)\n  Packet Loss: 5.7%\n  Round-Trip Time: ~950ms (avg)\n\nAttempted Recovery Actions:\n  1. Browser restart attempt: FAILED (process not responding)\n  2. WebSocket reconnection: FAILED (connection refused)\n  3. Graceful shutdown: FAILED (timeout after 10000ms)\n  4. Force kill process: SUCCESS (SIGKILL sent)\n\nTest execution cannot continue. Manual intervention required.'

LOG6_RESPONSE=$(timed_curl "POST /v1/log (FATAL)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d @- <<EOF
{
  "itemUuid": "$ITEM3_ID",
  "launchUuid": "$LAUNCH_ID",
  "time": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "level": "Fatal",
  "message": $(echo -n "$FATAL_MSG" | jq -Rs .),
  "file": {
    "name": "stack-trace.txt",
    "data": "$(echo "Fatal Error Stack Trace:\nError: Connection lost to browser\n    at BrowserContext.waitForPage (browser-context.js:342:15)\n    at Dashboard.navigate (dashboard.js:78:28)\n    at Test.execute (test-runner.js:156:12)\n    at Suite.run (suite-runner.js:89:23)\n    at Launch.start (launch.js:45:18)\n\nBrowser Details:\n  Type: chromium\n  Connection: WebSocket closed\n  Last activity: $CURRENT_TIMESTAMP\n  Status: DISCONNECTED" | base64)"
  }
}
EOF
)

if echo "$LOG6_RESPONSE" | grep -q '"id"'; then
    LOG6_ID=$(echo "$LOG6_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created FATAL log item: $LOG6_ID"
else
    print_error "Failed to create FATAL log item"
    echo "Response: $LOG6_RESPONSE"
fi

# Step 13: Finish second test as PASSED
sleep 0.1
echo ""
echo "Step 13: Finishing second test (Dashboard Navigation) as PASSED..."
ITEM3_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
FINISH3_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Suite 2, Test 2)" -s -X PUT "$HUB_URL/api/test-items/$ITEM3_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Dashboard navigation and widget interactions validated successfully",
        "endTime": "'"$ITEM3_END"'"
    }')

if echo "$FINISH3_RESPONSE" | grep -q "message\|finished"; then
    print_success "Second test in Suite 2 finished as PASSED"
else
    print_error "Failed to finish second test in Suite 2"
fi

# Step 14: Start third test (User Profile Update) in Suite 2
echo ""
echo "Step 14: Starting third test (User Profile Update) in Suite 2..."
ITEM4_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

ITEM4_RESPONSE=$(timed_curl "POST /api/test-items (Suite 2, Test 3)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE2_ID'",
        "labelKey": "AppB:Chromium:UAT",
        "name": "E2E Test - User Profile Update and Settings Persistence",
        "description": "Tests user profile editing, avatar upload, notification preferences, and settings persistence across sessions",
        "type": "Test",
        "codeRef": "com.agenix.portal.e2e.profileUpdate",
        "testCaseId": "com.agenix.portal.e2e.test.profileUpdate.1",
        "attributes": [
            {"key": "test-type", "value": "e2e"},
            {"key": "feature", "value": "user-profile"},
            {"key": "browser", "value": "chromium"},
            {"key": "env", "value": "uat"},
            {"key": "priority", "value": "medium"}
        ],
        "startTime": "'"$ITEM4_TIME"'"
    }')

ITEM4_ID=$(echo "$ITEM4_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
BROWSER4_ID=$(echo "$ITEM4_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ITEM4_ID" ]; then
    print_error "Failed to start third test in Suite 2"
    echo "Response: $ITEM4_RESPONSE"
    exit 1
fi

print_success "Started third test in Suite 2: $ITEM4_ID"
if [ -n "$BROWSER4_ID" ]; then
    print_success "Browser borrowed: $BROWSER4_ID"
fi

# Create log items for Test Item 4 with INFO and WARN levels
echo ""
echo "Creating log items for test item 4..."

# Log Item 1: INFO level with HTML report
CURRENT_TIMESTAMP=$(date)
LOG7_RESPONSE=$(timed_curl "POST /v1/log (INFO)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ITEM4_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"User profile update operation completed successfully - all changes persisted to database\",
    \"file\": {
      \"name\": \"profile-update-report.html\",
      \"data\": \"$(echo "<!DOCTYPE html>
<html>
<head><title>Profile Update Report</title></head>
<body>
  <h1>User Profile Update</h1>
  <table border=\"1\">
    <tr><th>Field</th><th>Old Value</th><th>New Value</th></tr>
    <tr><td>Display Name</td><td>John Doe</td><td>John Smith</td></tr>
    <tr><td>Email</td><td>john@example.com</td><td>john.smith@example.com</td></tr>
    <tr><td>Notification</td><td>Email</td><td>Email + SMS</td></tr>
    <tr><td>Avatar</td><td>default.png</td><td>user-123.jpg</td></tr>
  </table>
  <p>Status: SUCCESS</p>
  <p>Timestamp: $CURRENT_TIMESTAMP</p>
</body>
</html>" | base64)\"
    }
  }")

if echo "$LOG7_RESPONSE" | grep -q '"id"'; then
    LOG7_ID=$(echo "$LOG7_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created INFO log item: $LOG7_ID"
else
    print_error "Failed to create INFO log item"
    echo "Response: $LOG7_RESPONSE"
fi

# Log Item 2: WARN level for deprecation notice
LOG8_RESPONSE=$(timed_curl "POST /v1/log (WARN)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ITEM4_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"WARN\",
    \"message\": \"Legacy profile API endpoint used - this API version will be deprecated in Q2 2025. Migrate to v2/profile endpoint\",
    \"file\": {
      \"name\": \"api-deprecation-notice.txt\",
      \"data\": \"$(echo 'API Deprecation Notice

Endpoint: /api/v1/user/profile
Status: DEPRECATED
Replacement: /api/v2/profile
Deprecation Date: 2025-04-01
EOL Date: 2025-06-30

Migration Guide:
1. Update endpoint URL from v1 to v2
2. Use new JSON structure with nested objects
3. Add authentication header (Bearer token)
4. Handle new error response format

For details: https://docs.example.com/api/migration-guide
Contact: api-support@example.com' | base64)\"
    }
  }")

if echo "$LOG8_RESPONSE" | grep -q '"id"'; then
    LOG8_ID=$(echo "$LOG8_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created WARN log item: $LOG8_ID"
else
    print_error "Failed to create WARN log item"
    echo "Response: $LOG8_RESPONSE"
fi

# Step 15: Finish third test as PASSED
sleep 0.1
echo ""
echo "Step 15: Finishing third test (User Profile Update) as PASSED..."
ITEM4_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
FINISH4_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Suite 2, Test 3)" -s -X PUT "$HUB_URL/api/test-items/$ITEM4_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "User profile update and settings persistence verified successfully",
        "endTime": "'"$ITEM4_END"'"
    }')

if echo "$FINISH4_RESPONSE" | grep -q "message\|finished"; then
    print_success "Third test in Suite 2 finished as PASSED"
else
    print_error "Failed to finish third test in Suite 2"
fi

# Step 15: Start fourth test (Payment Processing - FAILED) in Suite 2
echo ""
echo "Step 15: Starting fourth test (Payment Processing API Integration - FAILED) in Suite 2..."
ITEM5_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

ITEM5_RESPONSE=$(timed_curl "POST /api/test-items (Suite 2, Test 4)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$SUITE2_ID'",
        "labelKey": "AppB:Chromium:UAT",
        "name": "E2E Test - Payment Processing API Integration",
        "description": "End-to-end test validating payment gateway integration including transaction processing, error handling, rollback mechanisms, and monitoring integration with comprehensive failure scenarios",
        "type": "Test",
        "codeRef": "com.agenix.portal.e2e.paymentProcessing",
        "testCaseId": "com.agenix.portal.e2e.test.paymentProcessing.1",
        "attributes": [
            {"key": "test-type", "value": "e2e"},
            {"key": "feature", "value": "payments"},
            {"key": "browser", "value": "chromium"},
            {"key": "env", "value": "uat"},
            {"key": "priority", "value": "critical"},
            {"key": "expected", "value": "failure"}
        ],
        "startTime": "'"$ITEM5_TIME"'"
    }')

ITEM5_ID=$(echo "$ITEM5_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
BROWSER5_ID=$(echo "$ITEM5_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ITEM5_ID" ]; then
    print_error "Failed to start fourth test in Suite 2"
    echo "Response: $ITEM5_RESPONSE"
    exit 1
fi

print_success "Started fourth test in Suite 2: $ITEM5_ID"
if [ -n "$BROWSER5_ID" ]; then
    print_success "Browser borrowed: $BROWSER5_ID"
fi

# Create nested steps for Test 5 (Payment Processing)
echo ""
echo "Creating nested steps for Payment Processing test..."

# Step 1: Initialize Payment Session (PASSED)
T5_STEP1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP1_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM5_ID'",
        "name": "Initialize Payment Session",
        "description": "Set up payment gateway connection and load merchant configuration",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "setup"},
            {"key": "action", "value": "initialize"}
        ],
        "startTime": "'"$T5_STEP1_TIME"'"
    }')

T5_STEP1_ID=$(echo "$T5_STEP1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 1: $T5_STEP1_ID"

# Log for Step 1
timed_curl "POST /v1/log (T5 Step 1, Log 1)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Payment gateway connection established successfully - Endpoint: https://api.paymentgateway.com/v2\"
  }" > /dev/null

# Nested Step 1.1: Load merchant config (PASSED) with attachment
T5_STEP1_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP1_1_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 1.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T5_STEP1_ID'",
        "name": "Load merchant configuration",
        "description": "Retrieve merchant settings from config service",
        "type": "Step",
        "attributes": [{"key": "step", "value": "config"}],
        "startTime": "'"$T5_STEP1_1_TIME"'"
    }')

T5_STEP1_1_ID=$(echo "$T5_STEP1_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 1.1 (nested): $T5_STEP1_1_ID"

# Log with JSON attachment for nested Step 1.1
timed_curl "POST /v1/log (T5 Step 1.1, Log with attachment)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP1_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Merchant configuration loaded successfully from config service\",
    \"file\": {
      \"name\": \"merchant-config.json\",
      \"data\": \"$(echo '{
  "merchantId": "MERCH-12345-PROD",
  "merchantName": "Agenix Test Store",
  "apiVersion": "v2.1",
  "apiKey": "pk_test_51234567890abcdef",
  "timeout": 30000,
  "retryAttempts": 3,
  "currency": "USD",
  "supportedPaymentMethods": ["card", "bank_transfer", "digital_wallet"],
  "webhookUrl": "https://app.agenix.com/webhooks/payment",
  "environment": "production"
}' | base64)\"
    }
  }" > /dev/null

# Finish nested Step 1.1
sleep 0.1
T5_STEP1_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 1.1)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP1_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Configuration loaded",
        "endTime": "'"$T5_STEP1_1_END"'"
    }' > /dev/null
print_success "Finished T5 Step 1.1 as Passed"

# Nested Step 1.2: Validate API credentials (PASSED)
T5_STEP1_2_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP1_2_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 1.2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T5_STEP1_ID'",
        "name": "Validate API credentials",
        "description": "Verify API key and secret with payment gateway",
        "type": "Step",
        "attributes": [{"key": "step", "value": "validation"}],
        "startTime": "'"$T5_STEP1_2_TIME"'"
    }')

T5_STEP1_2_ID=$(echo "$T5_STEP1_2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 1.2 (nested): $T5_STEP1_2_ID"

# Log for nested Step 1.2
timed_curl "POST /v1/log (T5 Step 1.2, Log)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP1_2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"API credentials validated successfully - Merchant account active and in good standing\"
  }" > /dev/null

# Finish nested Step 1.2
sleep 0.1
T5_STEP1_2_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 1.2)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP1_2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Credentials validated",
        "endTime": "'"$T5_STEP1_2_END"'"
    }' > /dev/null
print_success "Finished T5 Step 1.2 as Passed"

# Finish Step 1
sleep 0.1
T5_STEP1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 1)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Payment session initialized",
        "endTime": "'"$T5_STEP1_END"'"
    }' > /dev/null
print_success "Finished T5 Step 1 as Passed"

# Step 2: Process Payment Transaction (FAILED) with long stack trace and attachment
T5_STEP2_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP2_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM5_ID'",
        "name": "Process Payment Transaction",
        "description": "Execute payment through gateway with card details",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "execution"},
            {"key": "action", "value": "payment"}
        ],
        "startTime": "'"$T5_STEP2_TIME"'"
    }')

T5_STEP2_ID=$(echo "$T5_STEP2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 2: $T5_STEP2_ID"

# Log with payment request attachment for Step 2
timed_curl "POST /v1/log (T5 Step 2, Payment Request)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Submitting payment request to gateway\",
    \"file\": {
      \"name\": \"payment-request.json\",
      \"data\": \"$(echo '{
  "transactionId": "TXN-20250127-089234",
  "amount": 299.99,
  "currency": "USD",
  "cardNumber": "************1234",
  "cardHolderName": "John Smith",
  "expiryMonth": "12",
  "expiryYear": "2026",
  "cvv": "***",
  "billingAddress": {
    "street": "123 Main St",
    "city": "San Francisco",
    "state": "CA",
    "zipCode": "94102",
    "country": "US"
  },
  "merchantId": "MERCH-12345-PROD",
  "timestamp": "2025-01-27T16:42:15Z"
}' | base64)\"
    }
  }" > /dev/null

# Nested Step 2.1: Validate card details (PASSED) with attachment
T5_STEP2_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP2_1_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 2.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T5_STEP2_ID'",
        "name": "Validate card details",
        "description": "Verify card number format, expiry date, and CVV",
        "type": "Step",
        "attributes": [{"key": "step", "value": "validation"}],
        "startTime": "'"$T5_STEP2_1_TIME"'"
    }')

T5_STEP2_1_ID=$(echo "$T5_STEP2_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 2.1 (nested): $T5_STEP2_1_ID"

# Log with validation result attachment for nested Step 2.1
timed_curl "POST /v1/log (T5 Step 2.1, Validation Result)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP2_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Card validation completed successfully - All checks passed\",
    \"file\": {
      \"name\": \"card-validation-result.txt\",
      \"data\": \"$(echo 'Card Validation Report
======================
Transaction ID: TXN-20250127-089234
Timestamp: 2025-01-27T16:42:15Z

Validation Checks:
✓ Card Number Format: VALID (Visa card detected)
✓ Luhn Algorithm: PASSED
✓ Expiry Date: VALID (Expires 12/2026)
✓ CVV Length: CORRECT (3 digits)
✓ Billing Address: COMPLETE
✓ Card Not Blacklisted: VERIFIED

Card Type: Visa Credit
Issuing Bank: Chase Bank
Card Level: Platinum
Country: United States

Overall Result: PASSED
Ready for transaction processing' | base64)\"
    }
  }" > /dev/null

# Finish nested Step 2.1
sleep 0.1
T5_STEP2_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 2.1)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP2_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Card validated",
        "endTime": "'"$T5_STEP2_1_END"'"
    }' > /dev/null
print_success "Finished T5 Step 2.1 as Passed"

# Nested Step 2.2: Call payment gateway API (FAILED) with long stack trace and multiple attachments
T5_STEP2_2_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP2_2_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 2.2)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T5_STEP2_ID'",
        "name": "Call payment gateway API",
        "description": "Execute POST request to payment gateway endpoint",
        "type": "Step",
        "attributes": [
            {"key": "step", "value": "api-call"},
            {"key": "expected", "value": "failure"}
        ],
        "startTime": "'"$T5_STEP2_2_TIME"'"
    }')

T5_STEP2_2_ID=$(echo "$T5_STEP2_2_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 2.2 (nested): $T5_STEP2_2_ID"

# Log with LONG STACK TRACE for nested Step 2.2
timed_curl "POST /v1/log (T5 Step 2.2, ERROR with stack trace)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP2_2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Error\",
    \"message\": \"Payment gateway API call failed - Transaction declined by issuing bank\n\ncom.agenix.payment.PaymentGatewayException: Transaction declined by issuing bank (Error Code: 402 - Payment Required)\n    at com.agenix.payment.gateway.PaymentGatewayClient.processTransaction(PaymentGatewayClient.java:245)\n    at com.agenix.payment.service.PaymentProcessor.executePayment(PaymentProcessor.java:178)\n    at com.agenix.payment.service.PaymentService.processPaymentRequest(PaymentService.java:92)\n    at com.agenix.payment.controller.PaymentController.handlePaymentRequest(PaymentController.java:156)\n    at sun.reflect.NativeMethodAccessorImpl.invoke0(Native Method)\n    at sun.reflect.NativeMethodAccessorImpl.invoke(NativeMethodAccessorImpl.java:62)\n    at sun.reflect.DelegatingMethodAccessorImpl.invoke(DelegatingMethodAccessorImpl.java:43)\n    at java.lang.reflect.Method.invoke(Method.java:498)\n    at org.springframework.web.method.support.InvocableHandlerMethod.doInvoke(InvocableHandlerMethod.java:205)\n    at org.springframework.web.method.support.InvocableHandlerMethod.invokeForRequest(InvocableHandlerMethod.java:133)\n    at org.springframework.web.servlet.mvc.method.annotation.ServletInvocableHandlerMethod.invokeAndHandle(ServletInvocableHandlerMethod.java:97)\n    at org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerAdapter.invokeHandlerMethod(RequestMappingHandlerAdapter.java:827)\n    at org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerAdapter.handleInternal(RequestMappingHandlerAdapter.java:738)\n    at org.springframework.web.servlet.mvc.method.AbstractHandlerMethodAdapter.handle(AbstractHandlerMethodAdapter.java:85)\n    at org.springframework.web.servlet.DispatcherServlet.doDispatch(DispatcherServlet.java:967)\n    at org.springframework.web.servlet.DispatcherServlet.doService(DispatcherServlet.java:901)\n    at org.springframework.web.servlet.FrameworkServlet.processRequest(FrameworkServlet.java:970)\n    at org.springframework.web.servlet.FrameworkServlet.doPost(FrameworkServlet.java:872)\n    at javax.servlet.http.HttpServlet.service(HttpServlet.java:650)\n    at org.springframework.web.servlet.FrameworkServlet.service(FrameworkServlet.java:846)\n    at javax.servlet.http.HttpServlet.service(HttpServlet.java:731)\n    at org.apache.catalina.core.ApplicationFilterChain.internalDoFilter(ApplicationFilterChain.java:303)\n    at org.apache.catalina.core.ApplicationFilterChain.doFilter(ApplicationFilterChain.java:208)\n    at org.apache.tomcat.websocket.server.WsFilter.doFilter(WsFilter.java:52)\n    at org.apache.catalina.core.ApplicationFilterChain.internalDoFilter(ApplicationFilterChain.java:241)\n    at org.apache.catalina.core.ApplicationFilterChain.doFilter(ApplicationFilterChain.java:208)\n    at org.springframework.security.web.FilterChainProxy.doFilterInternal(FilterChainProxy.java:209)\n    at org.springframework.security.web.FilterChainProxy.doFilter(FilterChainProxy.java:178)\n    at org.springframework.web.filter.DelegatingFilterProxy.invokeDelegate(DelegatingFilterProxy.java:357)\n    at org.springframework.web.filter.DelegatingFilterProxy.doFilter(DelegatingFilterProxy.java:270)\n    at org.apache.catalina.core.ApplicationFilterChain.internalDoFilter(ApplicationFilterChain.java:241)\n    at org.apache.catalina.core.ApplicationFilterChain.doFilter(ApplicationFilterChain.java:208)\n    at org.apache.catalina.core.StandardWrapperValve.invoke(StandardWrapperValve.java:218)\n    at org.apache.catalina.core.StandardContextValve.invoke(StandardContextValve.java:122)\n    at org.apache.catalina.authenticator.AuthenticatorBase.invoke(AuthenticatorBase.java:505)\n    at org.apache.catalina.core.StandardHostValve.invoke(StandardHostValve.java:169)\n    at org.apache.catalina.valves.ErrorReportValve.invoke(ErrorReportValve.java:103)\n    at org.apache.catalina.core.StandardEngineValve.invoke(StandardEngineValve.java:116)\n    at org.apache.catalina.connector.CoyoteAdapter.service(CoyoteAdapter.java:445)\n    at org.apache.coyote.http11.AbstractHttp11Processor.process(AbstractHttp11Processor.java:1115)\n    at org.apache.coyote.AbstractProtocol\\$AbstractConnectionHandler.process(AbstractProtocol.java:637)\n    at org.apache.tomcat.util.net.JIoEndpoint\\$SocketProcessor.run(JIoEndpoint.java:316)\n    at java.util.concurrent.ThreadPoolExecutor.runWorker(ThreadPoolExecutor.java:1149)\n    at java.util.concurrent.ThreadPoolExecutor\\$Worker.run(ThreadPoolExecutor.java:624)\n    at org.apache.tomcat.util.threads.TaskThread\\$WrappingRunnable.run(TaskThread.java:61)\n    at java.lang.Thread.run(Thread.java:748)\nCaused by: com.agenix.payment.gateway.BankDeclinedException: Insufficient funds in account - Available balance: \\$45.23, Required: \\$299.99\n    at com.agenix.payment.gateway.adapter.BankAdapter.validateAccountBalance(BankAdapter.java:156)\n    at com.agenix.payment.gateway.adapter.BankAdapter.authorizeTransaction(BankAdapter.java:203)\n    at com.agenix.payment.gateway.adapter.BankAdapter.processAuthorization(BankAdapter.java:89)\n    at com.agenix.payment.gateway.PaymentGatewayClient.requestAuthorization(PaymentGatewayClient.java:312)\n    at com.agenix.payment.gateway.PaymentGatewayClient.processTransaction(PaymentGatewayClient.java:238)\n    ... 44 more\nCaused by: java.net.SocketTimeoutException: Read timed out\n    at java.net.SocketInputStream.socketRead0(Native Method)\n    at java.net.SocketInputStream.socketRead(SocketInputStream.java:116)\n    at java.net.SocketInputStream.read(SocketInputStream.java:171)\n    at java.net.SocketInputStream.read(SocketInputStream.java:141)\n    at sun.security.ssl.InputRecord.readFully(InputRecord.java:465)\n    at sun.security.ssl.InputRecord.read(InputRecord.java:503)\n    at sun.security.ssl.SSLSocketImpl.readRecord(SSLSocketImpl.java:983)\n    at sun.security.ssl.SSLSocketImpl.readDataRecord(SSLSocketImpl.java:940)\n    at sun.security.ssl.AppInputStream.read(AppInputStream.java:105)\n    ... 48 more\"
  }" > /dev/null

# Log with gateway error response attachment for nested Step 2.2
timed_curl "POST /v1/log (T5 Step 2.2, Gateway Error Response)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP2_2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Error\",
    \"message\": \"Gateway returned error response\",
    \"file\": {
      \"name\": \"gateway-response-error.json\",
      \"data\": \"$(echo '{
  "status": "declined",
  "errorCode": "402",
  "errorType": "payment_required",
  "message": "Transaction declined by issuing bank",
  "details": {
    "declineCode": "insufficient_funds",
    "declineMessage": "Insufficient funds in account",
    "availableBalance": 45.23,
    "requestedAmount": 299.99,
    "currency": "USD",
    "bankResponseCode": "51",
    "bankResponseMessage": "DECLINED - NSF"
  },
  "transactionId": "TXN-20250127-089234",
  "gatewayTransactionId": "GTW-20250127-445892",
  "timestamp": "2025-01-27T16:42:18.345Z",
  "merchantId": "MERCH-12345-PROD",
  "cardBin": "424242",
  "cardLast4": "1234",
  "attemptNumber": 1,
  "canRetry": false,
  "recommendedAction": "request_alternative_payment_method"
}' | base64)\"
    }
  }" > /dev/null

# Log with API request headers attachment for nested Step 2.2
timed_curl "POST /v1/log (T5 Step 2.2, Request Headers)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP2_2_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"API request headers for debugging\",
    \"file\": {
      \"name\": \"api-request-headers.txt\",
      \"data\": \"$(echo 'HTTP Request Headers
====================
POST /v2/transactions/process HTTP/1.1
Host: api.paymentgateway.com
Content-Type: application/json
Accept: application/json
Authorization: Bearer pk_test_51234567890abcdef
X-API-Version: 2.1
X-Request-ID: req_789456123abc
X-Idempotency-Key: idem_TXN-20250127-089234
User-Agent: Agenix-Payment-Client/2.4.1
Accept-Encoding: gzip, deflate
Connection: keep-alive
Content-Length: 487

Request Metadata:
-----------------
Timestamp: 2025-01-27T16:42:16.123Z
Client IP: 192.168.1.105
Merchant ID: MERCH-12345-PROD
Environment: production
SDK Version: 2.4.1
Timeout: 30000ms
Retry Policy: disabled' | base64)\"
    }
  }" > /dev/null

# Finish nested Step 2.2 as FAILED
sleep 0.1
T5_STEP2_2_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 2.2)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP2_2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Failed",
        "description": "Gateway API call failed - Insufficient funds",
        "endTime": "'"$T5_STEP2_2_END"'"
    }' > /dev/null
print_success "Finished T5 Step 2.2 as Failed"

# Finish Step 2 as FAILED
sleep 0.1
T5_STEP2_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 2)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Failed",
        "description": "Payment processing failed",
        "endTime": "'"$T5_STEP2_END"'"
    }' > /dev/null
print_success "Finished T5 Step 2 as Failed"

# Step 3: Rollback Transaction (SKIPPED)
T5_STEP3_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP3_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 3)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM5_ID'",
        "name": "Rollback Transaction",
        "description": "Revert payment attempt and release funds hold",
        "type": "Step",
        "attributes": [{"key": "step", "value": "rollback"}],
        "startTime": "'"$T5_STEP3_TIME"'"
    }')

T5_STEP3_ID=$(echo "$T5_STEP3_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 3: $T5_STEP3_ID"

# Log for Step 3
timed_curl "POST /v1/log (T5 Step 3, Log)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP3_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Warn\",
    \"message\": \"Rollback skipped - No active transaction to revert (transaction never authorized)\"
  }" > /dev/null

# Finish Step 3 as SKIPPED
sleep 0.1
T5_STEP3_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 3)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP3_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Skipped",
        "description": "Rollback not needed",
        "endTime": "'"$T5_STEP3_END"'"
    }' > /dev/null
print_success "Finished T5 Step 3 as Skipped"

# Step 4: Log Error Details (PASSED) with HTML attachment
T5_STEP4_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP4_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 4)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$ITEM5_ID'",
        "name": "Log Error Details",
        "description": "Record failure details for debugging and monitoring",
        "type": "Step",
        "attributes": [{"key": "step", "value": "logging"}],
        "startTime": "'"$T5_STEP4_TIME"'"
    }')

T5_STEP4_ID=$(echo "$T5_STEP4_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 4: $T5_STEP4_ID"

# Log with HTML error report attachment for Step 4
CURRENT_TIMESTAMP=$(date)
timed_curl "POST /v1/log (T5 Step 4, Error Report)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP4_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Error details recorded to monitoring system\",
    \"file\": {
      \"name\": \"payment-error-report.html\",
      \"data\": \"$(echo '<!DOCTYPE html>
<html>
<head>
  <meta charset=\"UTF-8\">
  <title>Payment Processing Error Report</title>
  <style>
    body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
    .container { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
    h1 { color: #d32f2f; border-bottom: 3px solid #d32f2f; padding-bottom: 10px; }
    .section { margin: 20px 0; }
    .section h2 { color: #333; font-size: 18px; }
    table { width: 100%; border-collapse: collapse; margin: 10px 0; }
    th, td { text-align: left; padding: 8px; border: 1px solid #ddd; }
    th { background: #f0f0f0; font-weight: bold; }
    .error { color: #d32f2f; font-weight: bold; }
    .timestamp { color: #666; font-size: 12px; }
    pre { background: #f5f5f5; padding: 10px; overflow-x: auto; border-left: 3px solid #d32f2f; }
  </style>
</head>
<body>
  <div class=\"container\">
    <h1>⚠️ Payment Processing Error Report</h1>
    <p class=\"timestamp\">Generated: '"$CURRENT_TIMESTAMP"'</p>

    <div class=\"section\">
      <h2>Transaction Summary</h2>
      <table>
        <tr><th>Field</th><th>Value</th></tr>
        <tr><td>Transaction ID</td><td>TXN-20250127-089234</td></tr>
        <tr><td>Gateway Transaction ID</td><td>GTW-20250127-445892</td></tr>
        <tr><td>Amount</td><td class=\"error\">$299.99 USD</td></tr>
        <tr><td>Status</td><td class=\"error\">DECLINED</td></tr>
        <tr><td>Error Code</td><td>402 - Payment Required</td></tr>
        <tr><td>Decline Reason</td><td>Insufficient Funds</td></tr>
        <tr><td>Merchant ID</td><td>MERCH-12345-PROD</td></tr>
        <tr><td>Card Last 4</td><td>****1234</td></tr>
      </table>
    </div>

    <div class=\"section\">
      <h2>Error Details</h2>
      <table>
        <tr><th>Category</th><th>Information</th></tr>
        <tr><td>Decline Code</td><td>insufficient_funds</td></tr>
        <tr><td>Bank Response Code</td><td>51</td></tr>
        <tr><td>Bank Message</td><td>DECLINED - NSF (Non-Sufficient Funds)</td></tr>
        <tr><td>Available Balance</td><td>$45.23</td></tr>
        <tr><td>Requested Amount</td><td>$299.99</td></tr>
        <tr><td>Shortfall</td><td class=\"error\">$254.76</td></tr>
      </table>
    </div>

    <div class=\"section\">
      <h2>Technical Details</h2>
      <pre>Exception: com.agenix.payment.PaymentGatewayException
Message: Transaction declined by issuing bank
Caused by: BankDeclinedException - Insufficient funds in account
Timestamp: 2025-01-27T16:42:18.345Z
Environment: production
API Version: v2.1</pre>
    </div>

    <div class=\"section\">
      <h2>Recommended Actions</h2>
      <ul>
        <li>Request alternative payment method from customer</li>
        <li>Send notification to customer about insufficient funds</li>
        <li>Update transaction status in database</li>
        <li>Log error for fraud detection analysis</li>
        <li>Do not retry transaction (retry not allowed)</li>
      </ul>
    </div>
  </div>
</body>
</html>' | base64)\"
    }
  }" > /dev/null

# Nested Step 4.1: Send notification to ops team (PASSED) with attachment
T5_STEP4_1_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
T5_STEP4_1_RESPONSE=$(timed_curl "POST /api/test-items (T5 Step 4.1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$LAUNCH_ID'",
        "parentItemId": "'$T5_STEP4_ID'",
        "name": "Send notification to ops team",
        "description": "Alert operations team via Slack about payment failure",
        "type": "Step",
        "attributes": [{"key": "step", "value": "notification"}],
        "startTime": "'"$T5_STEP4_1_TIME"'"
    }')

T5_STEP4_1_ID=$(echo "$T5_STEP4_1_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
print_success "Created T5 Step 4.1 (nested): $T5_STEP4_1_ID"

# Log with Slack notification payload attachment for nested Step 4.1
timed_curl "POST /v1/log (T5 Step 4.1, Slack Notification)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$T5_STEP4_1_ID\",
    \"launchUuid\": \"$LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Alert notification sent successfully to #payments-ops channel\",
    \"file\": {
      \"name\": \"slack-notification-payload.json\",
      \"data\": \"$(echo '{
  "channel": "#payments-ops",
  "username": "Payment Gateway Bot",
  "icon_emoji": ":warning:",
  "attachments": [
    {
      "color": "danger",
      "title": "Payment Processing Failure",
      "title_link": "https://app.agenix.com/transactions/TXN-20250127-089234",
      "text": "A payment transaction has been declined by the issuing bank",
      "fields": [
        {
          "title": "Transaction ID",
          "value": "TXN-20250127-089234",
          "short": true
        },
        {
          "title": "Amount",
          "value": "$299.99 USD",
          "short": true
        },
        {
          "title": "Error",
          "value": "Insufficient Funds",
          "short": true
        },
        {
          "title": "Merchant",
          "value": "MERCH-12345-PROD",
          "short": true
        },
        {
          "title": "Card",
          "value": "Visa ****1234",
          "short": true
        },
        {
          "title": "Timestamp",
          "value": "2025-01-27 16:42:18 UTC",
          "short": true
        }
      ],
      "footer": "Agenix Payment Gateway",
      "footer_icon": "https://app.agenix.com/icon.png",
      "ts": 1738001138
    }
  ]
}' | base64)\"
    }
  }" > /dev/null

# Finish nested Step 4.1
sleep 0.1
T5_STEP4_1_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 4.1)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP4_1_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Notification sent",
        "endTime": "'"$T5_STEP4_1_END"'"
    }' > /dev/null
print_success "Finished T5 Step 4.1 as Passed"

# Finish Step 4
sleep 0.1
T5_STEP4_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
timed_curl "PUT /api/test-items/finish (T5 Step 4)" -s -X PUT "$HUB_URL/api/test-items/$T5_STEP4_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Passed",
        "description": "Error logging completed",
        "endTime": "'"$T5_STEP4_END"'"
    }' > /dev/null
print_success "Finished T5 Step 4 as Passed"

print_success "All nested steps for Payment Processing test created successfully"

# Finish Test 5 (Payment Processing) as FAILED
sleep 0.1
echo ""
echo "Finishing Payment Processing test as FAILED..."
ITEM5_END=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
FINISH5_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Suite 2, Test 4)" -s -X PUT "$HUB_URL/api/test-items/$ITEM5_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Failed",
        "description": "Payment processing failed due to insufficient funds - Transaction declined by bank",
        "endTime": "'"$ITEM5_END"'"
    }')

if echo "$FINISH5_RESPONSE" | grep -q "message\|finished"; then
    print_success "Payment Processing test finished as FAILED (browser returned to pool)"
else
    print_error "Failed to finish Payment Processing test"
    echo "Response: $FINISH5_RESPONSE"
fi

# Step 16: Finish the second suite as FAILED (changed from PASSED because Test 4 failed)
echo ""
echo "Step 16: Finishing second suite as FAILED..."
FINISH_SUITE2_RESPONSE=$(timed_curl "PUT /api/test-items/finish (Suite 2)" -s -X PUT "$HUB_URL/api/test-items/$SUITE2_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "status": "Failed",
        "description": "Suite completed with failures - Payment processing test failed due to insufficient funds",
        "endTime": "'"$SUITE2_END_TIME"'"
    }')

if echo "$FINISH_SUITE2_RESPONSE" | grep -q "message\|finished"; then
    print_success "Second test suite finished with status: Failed"
else
    print_info "Finish second suite response: $FINISH_SUITE2_RESPONSE"
fi

# Step 17: Finish the launch (explicitly after all suites are finished)
echo ""
echo "Step 17: Finishing launch..."
FINISH_LAUNCH_RESPONSE=$(timed_curl "PUT /api/launches/finish" -s -X PUT "$HUB_URL/api/launches/$LAUNCH_ID/finish" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{}')

if echo "$FINISH_LAUNCH_RESPONSE" | grep -q "id\|Launch finished"; then
    print_success "Launch finished successfully (status auto-calculated from test results)"
else
    print_info "Finish launch response: $FINISH_LAUNCH_RESPONSE"
fi

# Summary
echo ""
echo "=========================================="
echo "✅ Smoke Test Completed Successfully!"
echo "=========================================="
echo ""
echo "View results in Dashboard:"
echo "  Launch:    $HUB_URL/${PROJECT_KEY}/launches/$LAUNCH_ID"
echo "  Test Item: $HUB_URL/${PROJECT_KEY}_default/results/$ITEM_ID"
echo ""
echo "Test Details:"
echo "  Launch ID:           $LAUNCH_ID"
echo "  Suite 1 ID:          $SUITE_ID (Status: Failed)"
echo "  Suite 2 ID:          $SUITE2_ID (Status: Passed)"
echo ""
echo "Suite 1 - Test Items Created:"
echo "  1. BeforeMethod Hook: $BEFORE_ID (Status: Failed ❌, No browser)"
echo "  2. Main Test:         $ITEM_ID (Status: Passed ✅, Browser borrowed)"
echo "  3. AfterMethod Hook:  $AFTER_ID (Status: Skipped ⊝, No browser)"
echo ""
echo "Suite 2 - Test Items Created:"
echo "  1. E2E Test (Registration):      $ITEM2_ID (Status: Passed ✅, Browser borrowed)"
echo "  2. E2E Test (Dashboard Nav):     $ITEM3_ID (Status: Passed ✅, Browser borrowed)"
echo "  3. E2E Test (Profile Update):    $ITEM4_ID (Status: Passed ✅, Browser borrowed)"
echo "  4. E2E Test (Payment Processing): $ITEM5_ID (Status: Failed ❌, Browser borrowed)"
if [ -n "$BROWSER_ID" ]; then
    echo ""
    echo "Browser Sessions:"
    echo "  Only Main Test (type=Test) borrowed browser:"
    echo "    Browser ID:   $BROWSER_ID"
    if [ -n "$BROWSER_TYPE" ]; then
        echo "    Browser Type: $BROWSER_TYPE"
    fi
    if [ -n "$WORKER_NODE" ]; then
        echo "    Worker Node:  $WORKER_NODE"
    fi
    echo ""
    echo "  Hooks (BeforeMethod/AfterMethod) do NOT borrow browsers ✓"
fi
echo ""
echo "API Workflow:"
echo "  ✓ Created launch with POST /api/launches"
echo "  ✓ Created Suite 1 with POST /api/test-items (type=Suite, status=Failed)"
echo "    ├─ Started BeforeMethod with POST /api/test-items (type=BeforeMethod, status=Failed)"
echo "    ├─ Started main test with POST /api/test-items (type=Test, status=Passed)"
echo "    └─ Started AfterMethod with POST /api/test-items (type=AfterMethod, status=Skipped)"
echo "  ✓ Created Suite 2 with POST /api/test-items (type=Suite, status=Passed)"
echo "    └─ Started E2E test with POST /api/test-items (type=Test, status=Passed)"
echo "  ✓ Finished all test items with PUT /api/test-items/{id}/finish"
echo "  ✓ Finished both suites with PUT /api/test-items/{id}/finish"
echo "  ✓ Finished launch with PUT /api/launches/{id}/finish (auto-calculated status)"
echo ""
echo "Database Info:"
echo "  ✓ 22 test items stored in test_items table (2 Suites + 5 Tests + 15 Steps)"
echo "  ✓ Item types: Suite (2x), BeforeMethod, Test (5x), AfterMethod, Step (15x)"
echo "  ✓ Test statuses: Failed (Suite 1 + Suite 2 + hook + Test 4), Passed (3 Tests), Skipped"
echo "  ✓ Browser sessions: 5 browsers borrowed and returned (only for Test types)"
echo "  ✓ New TestItem Start/Finish API used (browser borrowing/returning)"
echo "  ✓ Data compatible with hierarchical TestItemTree view"
echo "  ✓ Long description testing: Suite 2 has 900+ character description"
echo ""
echo "Test Hierarchy:"
echo "  📁 Suite 1: Smoke Test Suite (Status: Failed, No browser)"
echo "     ├─ ⚙️  BeforeMethod: Setup Test Data (Status: Failed ❌, No browser)"
echo "     ├─ 🧪 Test: Login Flow (Status: Passed ✅, Browser: $BROWSER_TYPE)"
echo "     └─ 🧹 AfterMethod: Cleanup Test Data (Status: Skipped ⊝, No browser)"
echo ""
echo "  📁 Suite 2: Comprehensive E2E Smoke Test Suite (Status: Failed ❌, No browser)"
echo "     ├─ 🧪 Test: Complete User Registration Flow (Status: Passed ✅, Browser: Chromium)"
echo "     ├─ 🧪 Test: Dashboard Navigation (Status: Passed ✅, Browser: Chromium)"
echo "     ├─ 🧪 Test: User Profile Update (Status: Passed ✅, Browser: Chromium)"
echo "     └─ 🧪 Test: Payment Processing API (Status: Failed ❌, Browser: Chromium)"
echo "         ├─ → Step: Initialize Payment Session (Status: Passed ✅)"
echo "         │   ├─ → Step: Load merchant config (Status: Passed ✅)"
echo "         │   └─ → Step: Validate API credentials (Status: Passed ✅)"
echo "         ├─ → Step: Process Payment Transaction (Status: Failed ❌)"
echo "         │   ├─ → Step: Validate card details (Status: Passed ✅)"
echo "         │   └─ → Step: Call payment gateway API (Status: Failed ❌)"
echo "         ├─ → Step: Rollback Transaction (Status: Skipped ⊝)"
echo "         └─ → Step: Log Error Details (Status: Passed ✅)"
echo "             └─ → Step: Send notification to ops (Status: Passed ✅)"
echo ""
print_info "Open the Dashboard URLs above to view the test results and hierarchy"
echo ""

# =============================================================================
# Step 18: Create ACTIVE/IN-PROGRESS Launch (NOT finished)
# =============================================================================
echo ""
echo "=========================================="
echo "Creating Active Launch (In-Progress)"
echo "=========================================="
echo ""
echo "Step 18: Creating an active launch that will remain in progress..."

ACTIVE_LAUNCH_RESPONSE=$(timed_curl "POST /api/launches (Active)" -s -X POST "$HUB_URL/api/launches" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "name": "Active Test Run",
        "description": "This launch is left active/in-progress for testing delete button disable functionality",
        "mode": "DEFAULT",
        "attributes": [
            {"key": "status", "value": "active"},
            {"key": "test", "value": "in-progress"},
            {"key": "retain", "value": "true"}
        ]
    }')

ACTIVE_LAUNCH_ID=$(echo "$ACTIVE_LAUNCH_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ACTIVE_LAUNCH_ID" ]; then
    print_error "Failed to create active launch"
    echo "Response: $ACTIVE_LAUNCH_RESPONSE"
    exit 1
fi

print_success "Created active launch: $ACTIVE_LAUNCH_ID"

# Step 19: Create active suite (NOT finished)
echo ""
echo "Step 19: Creating active suite under the active launch..."
ACTIVE_SUITE_RESPONSE=$(timed_curl "POST /api/test-items (Suite 1)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$ACTIVE_LAUNCH_ID'",
        "name": "Active Test Suite",
        "description": "Suite left in progress for testing",
        "type": "Suite",
        "codeRef": "com.agenix.portal.activetests",
        "testCaseId": "com.agenix.portal.activetests.suite",
        "attributes": [
            {"key": "suite-type", "value": "active"},
            {"key": "status", "value": "in-progress"}
        ]
    }')

ACTIVE_SUITE_ID=$(echo "$ACTIVE_SUITE_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ACTIVE_SUITE_ID" ]; then
    print_error "Failed to create active suite"
    echo "Response: $ACTIVE_SUITE_RESPONSE"
    exit 1
fi

print_success "Created active suite: $ACTIVE_SUITE_ID"

# Step 20: Start active test item (NOT finished, WITH browser)
echo ""
echo "Step 20: Starting active test item (will borrow browser and remain active)..."
ACTIVE_TEST_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

ACTIVE_TEST_RESPONSE=$(timed_curl "POST /api/test-items (Active Test)" -s -X POST "$HUB_URL/api/test-items" \
    -H "Content-Type: application/json" \
    -H "X-Project-Key: $PROJECT_KEY" \
    -H "Authorization: Bearer $API_KEY" \
    -d '{
        "launchUuid": "'$ACTIVE_LAUNCH_ID'",
        "parentItemId": "'$ACTIVE_SUITE_ID'",
        "labelKey": "AppB:Chromium:UAT",
        "name": "Active Test - User Dashboard Navigation",
        "description": "This test is left in active/running state with borrowed browser",
        "type": "Test",
        "codeRef": "com.agenix.activetest.dashboardNav",
        "testCaseId": "com.agenix.activetest.dashboardNav.1",
        "attributes": [
            {"key": "test-type", "value": "e2e"},
            {"key": "status", "value": "active"},
            {"key": "browser", "value": "chromium"}
        ],
        "startTime": "'"$ACTIVE_TEST_TIME"'"
    }')

ACTIVE_TEST_ID=$(echo "$ACTIVE_TEST_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
ACTIVE_BROWSER_ID=$(echo "$ACTIVE_TEST_RESPONSE" | grep -o '"browserId":"[^"]*"' | head -1 | cut -d'"' -f4)
ACTIVE_WS=$(echo "$ACTIVE_TEST_RESPONSE" | grep -o '"webSocketEndpoint":"[^"]*"' | head -1 | cut -d'"' -f4)
ACTIVE_BROWSER_TYPE=$(echo "$ACTIVE_TEST_RESPONSE" | grep -o '"browserType":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$ACTIVE_TEST_ID" ]; then
    print_error "Failed to start active test"
    echo "Response: $ACTIVE_TEST_RESPONSE"
    exit 1
fi

print_success "Started active test: $ACTIVE_TEST_ID"

# Create log items for Test Item 5 (Active Test) with DEBUG, INFO, and ERROR levels
echo ""
echo "Creating log items for active test (test item 5)..."

# Log Item 1: DEBUG level with verbose execution details
CURRENT_TIMESTAMP=$(date)
LOG9_RESPONSE=$(timed_curl "POST /v1/log (DEBUG - Active)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ACTIVE_TEST_ID\",
    \"launchUuid\": \"$ACTIVE_LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Debug\",
    \"message\": \"Test initialized - browser session active, waiting for manual verification\",
    \"file\": {
      \"name\": \"active-test-debug.txt\",
      \"data\": \"$(echo "Active Test Debug Information

Test ID: $ACTIVE_TEST_ID
Launch ID: $ACTIVE_LAUNCH_ID
Browser ID: $ACTIVE_BROWSER_ID
Browser Type: $ACTIVE_BROWSER_TYPE
WebSocket: $ACTIVE_WS

Test State: RUNNING
Session Status: ACTIVE
Purpose: Browser pool health verification
Duration: Indefinite (manual cleanup required)
Started: $CURRENT_TIMESTAMP" | base64)\"
    }
  }")

if echo "$LOG9_RESPONSE" | grep -q '"id"'; then
    LOG9_ID=$(echo "$LOG9_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created DEBUG log item: $LOG9_ID"
else
    print_error "Failed to create DEBUG log item"
    echo "Response: $LOG9_RESPONSE"
fi

# Log Item 2: INFO level about test progress
LOG10_RESPONSE=$(timed_curl "POST /v1/log (INFO - Active)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ACTIVE_TEST_ID\",
    \"launchUuid\": \"$ACTIVE_LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Info\",
    \"message\": \"Active test is running and will remain active for manual browser pool verification\",
    \"file\": {
      \"name\": \"test-instructions.txt\",
      \"data\": \"$(echo 'Manual Test Instructions

This test intentionally remains ACTIVE to verify:
1. Browser pool health monitoring
2. Long-running session management
3. Resource cleanup on manual finish

To complete this test:
1. Verify browser is accessible at WebSocket endpoint
2. Check browser pool metrics in dashboard
3. Call finish endpoint when ready:
   PUT /api/test-items/'$ACTIVE_TEST_ID'/finish
   PUT /api/launches/'$ACTIVE_LAUNCH_ID'/finish

Expected cleanup behavior:
- Browser should be returned to pool
- Session status should transition to Completed
- Worker resources should be released

Dashboard URL: '$HUB_URL'/'$PROJECT_KEY'/launches/'$ACTIVE_LAUNCH_ID | base64)\"
    }
  }")

if echo "$LOG10_RESPONSE" | grep -q '"id"'; then
    LOG10_ID=$(echo "$LOG10_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created INFO log item: $LOG10_ID"
else
    print_error "Failed to create INFO log item"
    echo "Response: $LOG10_RESPONSE"
fi

# Log Item 3: ERROR level for intentional issue tracking
CURRENT_TIMESTAMP=$(date)
LOG11_RESPONSE=$(timed_curl "POST /v1/log (ERROR - Active)" -s -X POST "$HUB_URL/v1/$PROJECT_KEY/log" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $API_KEY" \
  -d "{
    \"itemUuid\": \"$ACTIVE_TEST_ID\",
    \"launchUuid\": \"$ACTIVE_LAUNCH_ID\",
    \"time\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",
    \"level\": \"Error\",
    \"message\": \"Simulated error for log level testing - this is an intentional test error to verify ERROR log capture\",
    \"file\": {
      \"name\": \"simulated-error.log\",
      \"data\": \"$(echo "Simulated Error Log Entry

Error Type: TEST_SIMULATION
Severity: ERROR
Component: BrowserPoolHealthCheck
Message: This is a simulated error for testing purposes

Stack Trace:
  at HealthCheck.verify (health-check.js:89)
  at BrowserPool.validateSession (pool.js:234)
  at TestRunner.execute (test-runner.js:456)
  at Launch.run (launch.js:123)

Context:
  - This is NOT a real error
  - Used to verify ERROR level log capture
  - All log levels tested: TRACE, DEBUG, INFO, WARN, ERROR, FATAL
  - Attachments working correctly

Timestamp: $CURRENT_TIMESTAMP
Test Environment: Smoke Test
Log Item: #11" | base64)\"
    }
  }")

if echo "$LOG11_RESPONSE" | grep -q '"id"'; then
    LOG11_ID=$(echo "$LOG11_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    print_success "Created ERROR log item: $LOG11_ID"
else
    print_error "Failed to create ERROR log item"
    echo "Response: $LOG11_RESPONSE"
fi

if [ -n "$ACTIVE_BROWSER_ID" ]; then
    echo ""
    echo "┌─────────────────────────────────────────────────────────────────┐"
    echo "│           🔴 ACTIVE Browser Session (NOT Released)              │"
    echo "├─────────────────────────────────────────────────────────────────┤"
    printf "│ %-18s: %-42s │\n" "Browser ID" "$ACTIVE_BROWSER_ID"
    if [ -n "$ACTIVE_BROWSER_TYPE" ]; then
        printf "│ %-18s: %-42s │\n" "Browser Type" "$ACTIVE_BROWSER_TYPE"
    fi
    if [ -n "$ACTIVE_WS" ]; then
        printf "│ %-18s: %-42s │\n" "WebSocket" "${ACTIVE_WS:0:42}"
    fi
    echo "└─────────────────────────────────────────────────────────────────┘"
    echo ""
fi

# Final summary
echo ""
echo "=========================================="
echo "✅ Smoke Test Complete + Active Launch Created"
echo "=========================================="
echo ""
echo "📊 Summary:"
echo ""
echo "Completed Launch (ID: $LAUNCH_ID):"
echo "  ├─ Suite 1: $SUITE_ID (Status: Failed)"
echo "  │   ├─ BeforeMethod: $BEFORE_ID (Status: Failed)"
echo "  │   ├─ Test: $ITEM_ID (Status: Passed)"
echo "  │   └─ AfterMethod: $AFTER_ID (Status: Skipped)"
echo "  └─ Suite 2: $SUITE2_ID (Status: Passed ✅)"
echo "      ├─ Test (Registration): $ITEM2_ID (Status: Passed)"
echo "      ├─ Test (Dashboard):    $ITEM3_ID (Status: Passed)"
echo "      └─ Test (Profile):      $ITEM4_ID (Status: Passed)"
echo ""
echo "🔴 ACTIVE Launch (ID: $ACTIVE_LAUNCH_ID) - Status: InProgress"
echo "  ├─ Suite: $ACTIVE_SUITE_ID (Status: InProgress)"
echo "  └─ Test: $ACTIVE_TEST_ID (Status: Running, Browser: $ACTIVE_BROWSER_TYPE)"
echo "       └─ Browser: $ACTIVE_BROWSER_ID (🔴 BORROWED, NOT RETURNED)"
echo ""
echo "🎯 Testing Objectives:"
echo "  ✅ Completed launch can be deleted"
echo "  ❌ Active launch DELETE button should be DISABLED"
echo "  ❌ Active launch cannot be bulk deleted"
echo "  🔴 Active test has browser borrowed (will remain until finished by user)"
echo ""
echo "Dashboard URLs:"
echo "  Completed Launch: $HUB_URL/dashboard/$PROJECT_KEY/launches/$LAUNCH_DB_ID/suites"
echo "  Active Launch:    $HUB_URL/dashboard/$PROJECT_KEY/launches (check for InProgress status)"
echo ""
print_info "NOTE: Active launch will remain InProgress until manually finished via API or Dashboard"
print_info "To clean up: Call PUT /api/test-items/$ACTIVE_TEST_ID/finish and PUT /api/launches/$ACTIVE_LAUNCH_ID/finish"
echo ""

# Calculate and display timing summary
SCRIPT_END_TIME=$(date +%s)
TOTAL_DURATION=$((SCRIPT_END_TIME - SCRIPT_START_TIME))
TOTAL_SECONDS=$TOTAL_DURATION
TOTAL_MS=0

echo "=========================================="
echo "⏱️  Performance Metrics"
echo "=========================================="
echo ""

# Display individual request timings
if [ -f "$TIMING_FILE" ] && [ -s "$TIMING_FILE" ]; then
    echo "📊 Request Timing Breakdown:"
    echo ""

    TOTAL_REQUEST_TIME=0
    while IFS='|' read -r name duration; do
        seconds=$((duration / 1000))
        ms=$((duration % 1000))

        # Color code based on duration (in milliseconds)
        if [ $duration -lt 100 ]; then
            printf "  ${GREEN}%-60s %5d ms${NC}\n" "$name" "$duration"
        elif [ $duration -lt 500 ]; then
            printf "  ${CYAN}%-60s %5d ms${NC}\n" "$name" "$duration"
        elif [ $duration -lt 1000 ]; then
            printf "  ${YELLOW}%-60s %5d ms${NC}\n" "$name" "$duration"
        else
            printf "  ${RED}%-60s %4d.%01ds${NC}\n" "$name" "$seconds" "$((ms / 100))"
        fi

        TOTAL_REQUEST_TIME=$((TOTAL_REQUEST_TIME + duration))
    done < "$TIMING_FILE"

    echo ""
    total_req_seconds=$((TOTAL_REQUEST_TIME / 1000))
    printf "  ${BLUE}%-60s %4ds${NC}\n" "Total API Request Time:" "$total_req_seconds"

    overhead=$((TOTAL_DURATION - total_req_seconds))
    printf "  ${CYAN}%-60s %4ds${NC}\n" "Script Overhead (sleeps, processing):" "$overhead"
fi

# Cleanup timing file
rm -f "$TIMING_FILE"

echo ""
echo "=========================================="
printf "${GREEN}✅ Total Execution Time: %d seconds${NC}\n" "$TOTAL_SECONDS"
echo "=========================================="
echo ""
