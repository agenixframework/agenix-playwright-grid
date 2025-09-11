# CLI Reference

This page documents the Playwright Grid command-line tooling available in this repository.

Currently available tool(s):
- Pool Config Validator

---

## Pool Config Validator

Validate a Worker's POOL_CONFIG and compute effective capacity before booting the Worker. This helps catch malformed labels and understand how capacity aggregates per browser and in total.

### Why use it?
- Verify label keys conform to the shared schema App:Browser:Env[:...], with Browser as the second segment.
- Normalize duplicates (case/spacing) and sum capacities.
- Get a quick breakdown per browser and total effective capacity.
- Machine-readable JSON output for automation.

### Invocation options
You can run the validator in any of the following ways:

- Bash (macOS/Linux):
  - scripts/validate-pool-config.sh --pool "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"
- PowerShell (Windows):
  - scripts/validate-pool-config.ps1 --pool "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"
- Direct dotnet run:
  - dotnet run --project worker/WorkerService.csproj -- validate-pool-config --pool "AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"

### Arguments
- --pool "..." (optional)
  - Comma-separated list of label-to-count entries.
  - If omitted, the tool reads from the POOL_CONFIG environment variable. If that is empty, it falls back to a default sample.
- --json (optional)
  - Outputs a machine-readable JSON summary.

### Exit codes
- 0 → Validation succeeded; at least one valid pool entry was parsed.
- 1 → Validation failed; input was malformed or produced no valid pools. Errors are printed to stderr (text mode) or included next to the JSON output.

### Output
- Text mode (default):
  - Prints the parsed pools, per-browser totals, and EffectiveTotalCapacity.
- JSON mode (--json):
  - Example shape:
    {
      "EffectiveTotalCapacity": 4,
      "ByBrowser": { "Chromium": 3, "Firefox": 1 },
      "Pools": { "AppA:Chromium:Staging": 3, "AppB:Firefox:UAT": 1 },
      "Region": "local",
      "Os": "linux",
      "Source": "ENV:POOL_CONFIG"
    }

Notes
- Label keys use the shared Domain validator. Non-canonical keys are accepted with a warning and included as-is.
- Counts from duplicate label keys (after normalization) are summed.
- Region is derived from NODE_REGION (default local). OS best-effort detection defaults to linux if NODE_OS is not set.

### Examples
- Validate using an env var:
  - export POOL_CONFIG="AppA:Chromium:Staging=3,AppB:Firefox:UAT=1"
  - scripts/validate-pool-config.sh
- Validate and get JSON:
  - scripts/validate-pool-config.sh --json
- Inline override:
  - scripts/validate-pool-config.sh --pool "AppB:WebKit:UAT=2,AppB:Chromium:UAT=2"

### Related
- Configuration Guide → Pool config validator: configuration.md#pool-config-validator

---

Borrow a session via curl (handy while debugging)
- You can borrow directly from the Hub API using curl and include an optional runName so the Dashboard displays a friendly label.

Example
curl -s -X POST http://127.0.0.1:5100/session/borrow \
  -H 'content-type: application/json' \
  -H 'x-hub-secret: runner-secret' \
  -d '{"labelKey":"AppB:Chromium:UAT","runName":"Smoke UAT #123"}'

Then open the Dashboard filtered to your run:
- http://127.0.0.1:3001/results?runName=Smoke%20UAT%20%23123

Caution
- RunName may be descriptive but should not include secrets or personal data. To suppress RunName in hub logs, set HUB_REDACT_RUNNAME=1.
