# Label Matching Strategy

This document describes how the Hub resolves a requested label key to an available capacity pool using a central, pluggable strategy.

Labels are ordered, ':'-separated keys. We recommend keeping Browser as the second segment for consistency:
- App:Browser:Env[:Region[:OS[:...]]]

## Overview
The matcher evaluates candidates in the following order:

1. Exact match
   - Same number of segments and segment-by-segment equality.
2. Trailing fallback (optional)
   - Progressively drop trailing segments from the request down to a minimum (default 2 → App:Browser).
3. Prefix expansion (optional)
   - If no exact or fallback match is found, accept more specific available labels whose leading segments match the request.
   - Among multiple matches, pick the shortest candidate (fewest extra segments); break ties lexicographically by normalized label.
4. Wildcards (optional)
   - If enabled and the request contains `*`, a `*` in any segment matches any single segment during exact/prefix phases.

If no candidate matches, the Hub returns 503 (capacity unavailable).

## Deterministic tie‑breaking
When multiple candidates match in the same phase, the matcher picks deterministically:
- Prefer the candidate with the fewest segments beyond the requested prefix (shortest).
- If equal length, choose the lexicographically first by normalized label (ordinal comparison).

## Configuration knobs
Hub environment variables controlling the strategy:
- HUB_BORROW_TRAILING_FALLBACK=true | false
- HUB_BORROW_PREFIX_EXPAND=true | false
- HUB_BORROW_WILDCARDS=false | true

Defaults: trailing fallback and prefix expansion enabled; wildcards disabled.

## Domain API
The strategy is centralized in the Domain package:
- LabelKey: value object for parsing/validation/normalization of label keys.
- LabelMatchingOptions: enables/disables trailing fallback, prefix expansion, and wildcards; also sets MinSegmentsForFallback (default 2).
- ILabelMatcher / LabelMatcher: ordered strategy implementation (exact → trailing → prefix, with optional wildcards) and deterministic tie‑breaking.

Example usage:
```csharp
var options = new LabelMatchingOptions
{
    TrailingFallbackEnabled = true,
    PrefixExpansionEnabled = true,
    WildcardsEnabled = false,
    MinSegmentsForFallback = 2
};
var matcher = new LabelMatcher(options);

LabelKey.TryParse("AppA:Chromium:UAT", out var requested);
var available = new[] { "AppA:Chromium", "AppA:Chromium:UAT", "AppA:Chromium:UAT:EU" }
    .Select(s => LabelKey.TryParse(s, out var lk) ? lk! : null)
    .Where(lk => lk is not null)!;

var match = matcher.TryMatch(requested!, available!);
// match.Normalized == "AppA:Chromium:UAT"
```

## Wildcards and parsing
When HUB_BORROW_WILDCARDS is true and the requested label contains `*`, parsing relaxes the default enforcement that the second segment must be a known browser. The Hub uses:
- LabelKey.TryParse(request, new LabelKeyParsingOptions { EnforceBrowserSecond = false })
This allows `*` in any segment, including the Browser position.

Examples (with wildcards enabled):
- Request `App:*:UAT` matches `App:Chromium:UAT` or `App:Firefox:UAT` (exact), before considering prefix expansion.
- Request `*:Firefox:Staging` matches any App with `Firefox:Staging` pools.

## Examples
1. Exact match
   - Requested: `AppA:Chromium:UAT`
   - Available: `AppA:Chromium:UAT`, `AppA:Chromium`
   - Chosen: `AppA:Chromium:UAT` (exact; same number of segments and equality per segment).
2. Trailing fallback (optional)
   - Requested: `AppA:Chromium:UAT:EU`
   - Available: `AppA:Chromium:UAT`, `AppA:Chromium`
   - Chosen: `AppA:Chromium:UAT` (drop trailing `EU` to the first available match; do not drop below App:Browser by default).
3. Prefix expansion (optional)
   - Requested: `AppB:Firefox`
   - Available: `AppB:Firefox:UAT`, `AppB:Firefox:Prod:EU`
   - Chosen: `AppB:Firefox:UAT` (accept more specific labels; pick the shortest candidate; break ties lexicographically).
4. Wildcards (optional)
   - Requested: `AppA:*:UAT` (wildcards enabled)
   - Available: `AppA:Firefox:UAT`, `AppA:Chromium:UAT:EU`
   - Chosen: `AppA:Firefox:UAT` (exact with wildcard beats longer prefix expansion).

## Operational notes
- Keep Browser as second segment across all labels for consistency in routing, metrics, and UI filters.
- Choose a clear case policy and segment vocabulary for your organization; LabelKey supports normalization options.
- Avoid excessive cardinality (too many distinct labels) to keep metrics and dashboards efficient.

## Related docs
- Session distribution across workers: docs/distribution.md
- Testing and environment knobs: see docs/TestClient-Usage.md and tests/TestEnvironment.cs
