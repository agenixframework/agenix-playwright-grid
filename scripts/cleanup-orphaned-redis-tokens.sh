#!/usr/bin/env bash

# Cleanup script for orphaned Redis token keys
# This script removes log_token:* and command_token:* keys from Redis that don't have
# corresponding records in PostgreSQL (orphaned after retention cleanup)
#
# Usage:
#   ./scripts/cleanup-orphaned-redis-tokens.sh [--dry-run]
#
# Options:
#   --dry-run   Print what would be deleted without actually deleting

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Load environment variables (only the ones we need, avoid parsing issues with complex connection strings)
if [[ -f "$PROJECT_ROOT/.env" ]]; then
    # Extract specific variables we need
    REDIS_HOST=$(grep -E "^REDIS_HOST=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "localhost")
    REDIS_PORT=$(grep -E "^REDIS_PORT=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "6379")
    POSTGRES_HOST=$(grep -E "^POSTGRES_HOST=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "localhost")
    POSTGRES_PORT=$(grep -E "^POSTGRES_PORT=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "5432")
    POSTGRES_DB=$(grep -E "^POSTGRES_DB=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "agenix_reportportal")
    POSTGRES_USER=$(grep -E "^POSTGRES_USER=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "postgres")
    POSTGRES_PASSWORD=$(grep -E "^POSTGRES_PASSWORD=" "$PROJECT_ROOT/.env" | cut -d '=' -f2- | tr -d '"' || echo "postgres")
fi

# Configuration with fallback defaults
REDIS_HOST="${REDIS_HOST:-localhost}"
REDIS_PORT="${REDIS_PORT:-6379}"
POSTGRES_HOST="${POSTGRES_HOST:-localhost}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_DB="${POSTGRES_DB:-agenix_reportportal}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-postgres}"

DRY_RUN=false
if [[ "${1:-}" == "--dry-run" ]]; then
    DRY_RUN=true
fi

echo "========================================"
echo "Redis Token Cleanup Script"
echo "========================================"
echo "Redis: $REDIS_HOST:$REDIS_PORT"
echo "PostgreSQL: $POSTGRES_HOST:$POSTGRES_PORT/$POSTGRES_DB"
if [[ "$DRY_RUN" == true ]]; then
    echo "Mode: DRY RUN (no changes will be made)"
else
    echo "Mode: LIVE (tokens will be deleted)"
fi
echo "========================================"
echo ""

# Detect if we're using Docker or local installation
USE_DOCKER=false
REDIS_CONTAINER=""
POSTGRES_CONTAINER=""

if command -v docker &> /dev/null; then
    # Try to find redis container (with or without project prefix)
    REDIS_CONTAINER=$(docker ps --format "{{.Names}}" | grep -E "redis" | head -n 1)
    POSTGRES_CONTAINER=$(docker ps --format "{{.Names}}" | grep -E "postgres" | head -n 1)

    if [[ -n "$REDIS_CONTAINER" ]] && [[ -n "$POSTGRES_CONTAINER" ]]; then
        USE_DOCKER=true
        echo "Detected Docker containers - will use docker exec"
        echo "  Redis container: $REDIS_CONTAINER"
        echo "  Postgres container: $POSTGRES_CONTAINER"
    else
        echo "Using local redis-cli and psql"
    fi
else
    echo "Using local redis-cli and psql"
fi

# Check if redis-cli is available (or Docker)
if [[ "$USE_DOCKER" == false ]] && ! command -v redis-cli &> /dev/null; then
    echo "Error: redis-cli not found and no Docker containers detected."
    echo "  Ubuntu/Debian: sudo apt-get install redis-tools"
    echo "  macOS: brew install redis"
    exit 1
fi

# Check if psql is available (or Docker)
if [[ "$USE_DOCKER" == false ]] && ! command -v psql &> /dev/null; then
    echo "Error: psql not found and no Docker containers detected."
    echo "  Ubuntu/Debian: sudo apt-get install postgresql-client"
    echo "  macOS: brew install postgresql"
    exit 1
fi

# Function to get all log_token hashes from Redis
get_redis_log_tokens() {
    if [[ "$USE_DOCKER" == true ]]; then
        docker exec "$REDIS_CONTAINER" redis-cli --scan --pattern "log_token:*" | \
            sed 's/^log_token://'
    else
        redis-cli -h "$REDIS_HOST" -p "$REDIS_PORT" --scan --pattern "log_token:*" | \
            sed 's/^log_token://'
    fi
}

# Function to get all command_token hashes from Redis
get_redis_command_tokens() {
    if [[ "$USE_DOCKER" == true ]]; then
        docker exec "$REDIS_CONTAINER" redis-cli --scan --pattern "command_token:*" | \
            sed 's/^command_token://'
    else
        redis-cli -h "$REDIS_HOST" -p "$REDIS_PORT" --scan --pattern "command_token:*" | \
            sed 's/^command_token://'
    fi
}

# Function to check if token exists in PostgreSQL
token_exists_in_db() {
    local table=$1
    local hash=$2

    if [[ "$USE_DOCKER" == true ]]; then
        docker exec "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -t -c \
            "SELECT EXISTS(SELECT 1 FROM $table WHERE token_hash = '$hash');" 2>/dev/null | tr -d ' '
    else
        PGPASSWORD="$POSTGRES_PASSWORD" psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -t -c \
            "SELECT EXISTS(SELECT 1 FROM $table WHERE token_hash = '$hash');" 2>/dev/null | tr -d ' '
    fi
}

# Function to delete Redis key
delete_redis_key() {
    local key=$1
    if [[ "$USE_DOCKER" == true ]]; then
        docker exec "$REDIS_CONTAINER" redis-cli DEL "$key" > /dev/null
    else
        redis-cli -h "$REDIS_HOST" -p "$REDIS_PORT" DEL "$key" > /dev/null
    fi
}

# Cleanup log_tokens
echo "Step 1: Cleaning up log_tokens..."
echo "  - Scanning Redis for log_token:* keys..."

log_token_count=0
log_token_orphaned=0
log_token_deleted=0

while IFS= read -r hash; do
    if [[ -z "$hash" ]]; then
        continue
    fi

    log_token_count=$((log_token_count + 1))

    # Show progress every 100 tokens
    if (( log_token_count % 100 == 0 )); then
        echo "  - Processed $log_token_count log tokens ($log_token_orphaned orphaned, $log_token_deleted deleted)..."
    fi

    # Check if token exists in PostgreSQL
    exists=$(token_exists_in_db "log_tokens" "$hash")

    if [[ "$exists" == "f" ]]; then
        log_token_orphaned=$((log_token_orphaned + 1))

        if [[ "$DRY_RUN" == true ]]; then
            echo "  [DRY-RUN] Would delete: log_token:$hash"
        else
            delete_redis_key "log_token:$hash"
            log_token_deleted=$((log_token_deleted + 1))
        fi
    fi
done < <(get_redis_log_tokens)

echo "  ✓ Processed $log_token_count log tokens"
echo "  ✓ Found $log_token_orphaned orphaned tokens"
if [[ "$DRY_RUN" == true ]]; then
    echo "  ✓ Would delete $log_token_orphaned Redis keys"
else
    echo "  ✓ Deleted $log_token_deleted Redis keys"
fi
echo ""

# Cleanup command_tokens
echo "Step 2: Cleaning up command_tokens..."
echo "  - Scanning Redis for command_token:* keys..."

command_token_count=0
command_token_orphaned=0
command_token_deleted=0

while IFS= read -r hash; do
    if [[ -z "$hash" ]]; then
        continue
    fi

    command_token_count=$((command_token_count + 1))

    # Show progress every 100 tokens
    if (( command_token_count % 100 == 0 )); then
        echo "  - Processed $command_token_count command tokens ($command_token_orphaned orphaned, $command_token_deleted deleted)..."
    fi

    # Check if token exists in PostgreSQL
    exists=$(token_exists_in_db "command_tokens" "$hash")

    if [[ "$exists" == "f" ]]; then
        command_token_orphaned=$((command_token_orphaned + 1))

        if [[ "$DRY_RUN" == true ]]; then
            echo "  [DRY-RUN] Would delete: command_token:$hash"
        else
            delete_redis_key "command_token:$hash"
            command_token_deleted=$((command_token_deleted + 1))
        fi
    fi
done < <(get_redis_command_tokens)

echo "  ✓ Processed $command_token_count command tokens"
echo "  ✓ Found $command_token_orphaned orphaned tokens"
if [[ "$DRY_RUN" == true ]]; then
    echo "  ✓ Would delete $command_token_orphaned Redis keys"
else
    echo "  ✓ Deleted $command_token_deleted Redis keys"
fi
echo ""

# Summary
echo "========================================"
echo "Summary"
echo "========================================"
echo "Log tokens:"
echo "  - Total scanned: $log_token_count"
echo "  - Orphaned found: $log_token_orphaned"
if [[ "$DRY_RUN" == true ]]; then
    echo "  - Would delete: $log_token_orphaned"
else
    echo "  - Deleted: $log_token_deleted"
fi
echo ""
echo "Command tokens:"
echo "  - Total scanned: $command_token_count"
echo "  - Orphaned found: $command_token_orphaned"
if [[ "$DRY_RUN" == true ]]; then
    echo "  - Would delete: $command_token_orphaned"
else
    echo "  - Deleted: $command_token_deleted"
fi
echo ""
echo "Total orphaned tokens: $((log_token_orphaned + command_token_orphaned))"

if [[ "$DRY_RUN" == true ]]; then
    echo ""
    echo "This was a DRY RUN. No changes were made."
    echo "Run without --dry-run to actually delete the orphaned keys."
else
    echo ""
    echo "✓ Cleanup complete!"
    echo ""
    echo "Note: Future orphaned tokens will be automatically cleaned up"
    echo "      by the LogRetentionWorker using V44 migration."
fi

# Show Redis memory info
echo ""
echo "========================================"
echo "Redis Memory Info"
echo "========================================"
if [[ "$USE_DOCKER" == true ]]; then
    docker exec "$REDIS_CONTAINER" redis-cli INFO memory | grep -E "(used_memory_human|used_memory_peak_human)"
else
    redis-cli -h "$REDIS_HOST" -p "$REDIS_PORT" INFO memory | grep -E "(used_memory_human|used_memory_peak_human)"
fi
