# Hub Capacity Queue

This document explains the capacity queue implemented in the Hub to manage pending /session/borrow requests fairly and to reduce thundering herd effects when capacity is tight.

## Overview

When no browser instance is immediately available for a requested label key (e.g., `AppA:Chromium:UAT`), the Hub can enqueue the borrow request and wait for capacity to free up for a bounded time. The queue enforces:

- Per-label cap: maximum queued waiters per label key.
- Per-run cap: maximum concurrent queued waiters attributed to the same run id across all labels.
- Fairness: canceled or timed-out waiters are skipped; the next active waiter is signaled on capacity.

If capacity becomes available before the timeout, the waiting request proceeds and attempts to borrow again (with the same matching logic used for immediate borrows). Otherwise, the Hub returns 503 with a problem details message indicating timeout.

## Why

- Avoid stampedes when many runners request the same label while capacity is momentarily exhausted.
- Prevent a single run from monopolizing the queue and starving others.
- Provide predictable backoff (bounded waiting time) instead of tight busy retries.

## How it works

1. Immediate attempt:
   - Incoming POST /session/borrow first tries to borrow from exact and configured fallback candidates (trailing fallback, prefix expansion, optional wildcards).
2. Queue fallback:
   - If immediate attempt fails, the Hub enqueues the request in a per-label FIFO queue if caps allow it.
   - The waiter waits up to HUB_BORROW_QUEUE_TIMEOUT_SECONDS for a signal.
3. Signaling on return:
   - When a borrower returns a browser, the Hub signals one waiter for that label (skipping any canceled ones) to proceed.
4. Second-chance borrow:
   - The signaled request attempts to borrow again with the same matching logic.
   - If still no capacity is found (e.g., another consumer raced), it recomputes candidates and tries again once more in the same request path.
5. Timeout / cancellation:
   - If the wait exceeds the timeout or the client cancels the HTTP request, the Hub removes the waiter from the queue and decrements the per-run pending count. The endpoint returns 503 with a clear message.

## Configuration (environment variables)

- HUB_BORROW_QUEUE_TIMEOUT_SECONDS
  - Default: 30 (minimum enforced: 1)
  - Maximum time a borrow request will wait in the queue for capacity before timing out.
  - To minimize waiting, set to 1 for near-immediate denial when no capacity is available.
- HUB_BORROW_QUEUE_MAX_PER_LABEL
  - Default: 100 (minimum enforced: 1)
  - Maximum number of queued waiters per label key.
- HUB_BORROW_QUEUE_MAX_PER_RUN
  - Default: 5 (minimum enforced: 1)
  - Maximum number of queued waiters attributed to the same run id across all labels.

Notes:
- Queue caps and timeouts are enforced in-process in the Hub. They do not require Redis state.
- The queue uses a fair FIFO per label, skipping canceled/timed-out entries.

## Metrics

The following Prometheus metrics are updated by the borrow/return endpoints:

- hub_borrow_requests_total{label}
  - Counter: total borrow attempts (immediate or queued).
- hub_borrow_latency_seconds{label}
  - Histogram: end-to-end borrow latency per requested label.
- hub_borrow_outcomes_total{label, outcome}
  - Counter: outcome in {success, timeout, denied}. "denied" includes authentication failures or queue-cap rejections.
- hub_borrow_queue_length{label}
  - Gauge: current queue length for a label. Increments when enqueued; decrements when signaled/removed or on timeout.
- hub_pool_available_total{label}
  - Gauge: available instances in pool per label.
- hub_pool_utilization_ratio{label}
  - Gauge: in-use / total ratio per label after successful borrows.

These are visible in Prometheus (default http://127.0.0.1:9090) and surfaced by the sample Grafana dashboards under provisioning/.

## Behavior details and fairness

- Per-label cap prevents unbounded memory usage and limits herd size per label.
- Per-run cap avoids a single run from flooding the queue across many labels.
- Cancellation and timeouts are respected: canceled entries are marked and skipped if they reach the head of the queue. Per-run pending counters are decremented appropriately on removal or grant.
- A successful return signals exactly one waiter for the returned label.

## API implications

- Success: 200 OK with the borrowed browser payload.
- Queue timeout: 503 Service Unavailable with message "Borrow timed out after Ns for Label".
- Denied: 401 Unauthorized when x-hub-secret is invalid; contributes to hub_borrow_outcomes_total{outcome="denied"}.

## Minimal examples

- Borrow with run id in header:

  curl -sS -X POST \
    -H "x-hub-secret: $HUB_RUNNER_SECRET" \
    -H "Content-Type: application/json" \
    -H "x-run-id: my-run-123" \
    -d '{"labelKey": "AppB:Chromium:UAT"}' \
    http://127.0.0.1:5100/session/borrow

- Return a browser (id from previous response):

  curl -sS -X POST \
    -H "x-hub-secret: $HUB_RUNNER_SECRET" \
    -H "Content-Type: application/json" \
    -d '{"labelKey":"AppB:Chromium:UAT","browserId":"..."}' \
    http://127.0.0.1:5100/session/return

## Troubleshooting

- Requests denied immediately with outcome=denied:
  - Check x-hub-secret and queue caps (per-label/per-run). If caps reject enqueuing, the Hub records outcome=denied.
- Frequent timeouts (outcome=timeout):
  - Increase HUB_BORROW_QUEUE_TIMEOUT_SECONDS or add worker capacity for the requested labels.
  - Verify that workers expose capacity to the exact labels being requested.
- Queue length not decreasing on returns:
  - Confirm the return endpoint is called with correct labelKey and browserId; the Hub signals based on the label of the returned session.
- Very long waits:
  - Consider reducing timeout to 1s and implementing client-side retry with jitter to spread load.

## Tests

Unit tests under WorkerService.Tests verify the core queue behavior:
- Per-label cap enforcement
- Per-run cap enforcement
- Wait/Signal granting
- Timeout path and per-run slot release
- Cancellation skip on signal

These tests cover the internal queue component used by the endpoints.

## Backward compatibility and configuration tips

- The queue is enabled by default with conservative caps and a 30s timeout. There is no separate on/off switch.
- To effectively avoid waiting, set HUB_BORROW_QUEUE_TIMEOUT_SECONDS=1 so callers get fast feedback when capacity is unavailable.
- Tune per-run cap to strike a balance between parallelism for a single run and fairness among multiple runs.

## Related docs

- Label matching behavior: docs/Label-Matching.md
- Metrics and dashboards: docs/Metrics-and-Grafana.md
