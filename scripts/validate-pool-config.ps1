#!/usr/bin/env pwsh
# Validate Worker POOL_CONFIG and compute effective capacity
# Usage: scripts/validate-pool-config.ps1 [--pool "App:Browser:Env=3,..."] [--json]
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$env:DOTNET_NOLOGO = '1'
Push-Location $repoRoot
try {
  dotnet run --project worker/WorkerService.csproj -- validate-pool-config @args
} finally {
  Pop-Location
}
