#!/usr/bin/env bash
set -euo pipefail
# Validate Worker POOL_CONFIG and compute effective capacity
# Usage: scripts/validate-pool-config.sh [--pool "App:Browser:Env=3,..."] [--json]
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${SCRIPT_DIR%/scripts}"
cd "$REPO_ROOT"
export DOTNET_NOLOGO=1
exec dotnet run --project worker/WorkerService.csproj -- validate-pool-config "$@"
