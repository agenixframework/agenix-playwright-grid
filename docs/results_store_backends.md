# Results Store Backends — InMemory vs Redis (Hub persistence)

This guide explains how the Playwright Hub stores test run results, command logs, and test cases, and how to switch between the default in‑memory storage and the Redis‑backed durable store.

## What is stored
The Hub aggregates and exposes the following artifacts:
- Run summaries (per execution/run): status, counts, timestamps, metadata (App, Browser, Env, etc.)
- Command logs (append‑only timeline of events from worker and optional runner API logs)

These are consumed by the Hub HTTP endpoints and the Dashboard’s live/summary views.

## Backend choices
- InMemory (default)
  - Volatile, process‑local memory; data is lost on Hub restart or when scaling out.
  - Great for development and local testing.
- Redis (durable)
  - Persists across Hub restarts and supports TTL‑based retention.
  - Minimal ops overhead if you already run Redis (the grid composes a Redis service by default).

## Enabling Redis persistence
Set an environment variable on the Hub:
- HUB_RESULTS_BACKEND=redis
- Keep REDIS_URL pointing at your Redis instance (default in compose is `redis:6379`).
- Optionally set HUB_RESULTS_RETENTION_DAYS to automatically expire old runs.

Example (docker‑compose excerpt):

```yaml
aigenix-playwright-grid:
  services:
    hub:
      build: ./hub
      ports: [ "5100:5000" ]
      environment:
        - REDIS_URL=redis:6379
        - HUB_NODE_SECRET=node-secret
        - HUB_RUNNER_SECRET=runner-secret
        - HUB_RESULTS_BACKEND=redis
        - HUB_RESULTS_RETENTION_DAYS=30   # optional; keep data for ~30 days
      depends_on:
        redis:
          condition: service_healthy
```

When the Hub starts, it logs which backend is active, for example:
- "[hub] ResultsStore backend: redis"
- or "[hub] ResultsStore backend: memory (default)"

## Retention (TTL)
If you set HUB_RESULTS_RETENTION_DAYS to a positive integer, the Hub assigns a TTL to per‑run keys:
- results:run:{runId}
- results:cmd:{runId}
- results:cmdcount:{runId}

The primary index `results:runs:byStart` also receives a TTL to keep it aligned within the retention window. If HUB_RESULTS_RETENTION_DAYS is not set (or <= 0), no TTL is applied.

Tip: For production, values like 7, 30, or 90 days are common depending on data volume and compliance needs.

## Redis key model (implementation details)
- results:run:{runId} → JSON string (camelCase) with the full run summary
- results:runs:byStart → Sorted Set (ZSET) with score = StartedAtUtc ticks, member = runId
- results:tests:{runId} → Hash where field = testId, value = JSON test case
- results:cmd:{runId} → List of JSON command events (append‑only via RPUSH)
- results:cmdcount:{runId} → String counter (INCR on append)

Behavioral notes:
- Commands hard cap: lists are trimmed to keep the last 5000 entries per run to prevent unbounded growth.
- Pagination: all list/hash reads support `skip`/`take` paging in the Hub APIs.
- Run listing: runs are paged from `results:runs:byStart` and filtered client‑side by status/app/browser/env (simple and reliable; advanced Redis server‑side indexes can be added later if needed).
- Command order: returned in ascending order by their `timestampUtc`.

## Validation and usage
- Dashboard continues to work transparently; with Redis enabled, data persists across Hub restarts.
- You can validate quickly by starting the stack, opening the Dashboard, launching a few runs, then restarting only the Hub container: with Redis enabled the runs/commands/tests remain visible after restart.

## Environment variable summary (Hub)
- REDIS_URL=redis:6379 (or your Redis endpoint)
- HUB_RESULTS_BACKEND=memory | redis (default: memory)
- HUB_RESULTS_RETENTION_DAYS=<int days> (optional; enables TTL‐based cleanup)

Related Hub routing/matching envs (unchanged by the backend):
- HUB_BORROW_TRAILING_FALLBACK=true
- HUB_BORROW_PREFIX_EXPAND=true
- HUB_BORROW_WILDCARDS=false

## Troubleshooting
- No persistence after restart
  - Ensure HUB_RESULTS_BACKEND=redis is set on the Hub.
  - Check Hub logs for the backend line ("ResultsStore backend: redis").
  - Verify Redis connectivity and that REDIS_URL points to a reachable instance.
- Redis errors or timeouts
  - Check the `redis` service health (docker logs/healthcheck).
  - Confirm firewall rules/ports if using a remote Redis.
- Fewer command logs than expected
  - The Hub enforces a per‑run cap of 5000 commands to bound storage; older entries are trimmed.
- Data vanishes sooner than expected
  - Review HUB_RESULTS_RETENTION_DAYS; unset it or increase the value to retain longer.

## Migration and compatibility
- Switching from InMemory to Redis affects only newly recorded runs; existing in‑memory data is not migrated.
- No changes are required in clients or workers; the backend is purely a Hub concern.

## FAQ
- Can I increase the command cap beyond 5000?
  - Not currently; the cap is a fixed safety limit to avoid unbounded growth. If you need a higher limit, open an issue to discuss configuration trade‑offs.
- Can I add more Redis indexes for server‑side filtering?
  - The current implementation favors simplicity. If your deployment needs heavy server‑side filtering, we can extend the schema using ZSETs and ZINTERSTORE.
