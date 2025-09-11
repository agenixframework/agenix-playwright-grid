# Capacity Queue, Fair Sharing, and Per‑Label Concurrency Caps

This page explains how the Hub protects capacity and distributes pending borrow requests fairly across labels. It also documents the configuration flags you can tune at runtime.

Overview
- When a borrower requests a label with no immediate capacity, the Hub can place the request in a fair, in‑memory pending queue.
- The queue enforces per‑label and per‑run limits to prevent unbounded backlogs and single‑run overloads.
- A round‑robin scheduler wakes pending requests across labels to avoid starving smaller pools.
- Optional per‑label concurrency caps limit simultaneous in‑flight sessions for specific labels.

Features in detail
1) Pending queue with limits
- Per‑label queue cap: maximum number of pending waiters per label.
- Per‑run cap: maximum number of pending waiters attributed to the same runId across all labels.
- Queue timeout: each waiter has a bounded wait window; upon timeout, the Hub returns 503 with a message.

2) Fair sharing across labels
- The Hub keeps a list of labels that currently have pending waiters and iterates round‑robin.
- When capacity becomes available (e.g., someone returns, a worker replenishes, TTL expires), the Hub signals at most one waiter from the next eligible label under its concurrency cap.
- Canceled or timed‑out waiters are skipped.

3) Per‑label concurrency caps (active grants)
- Independently from the pending queue size, you can constrain the number of simultaneous in‑flight sessions for a label.
- This avoids one hot label consuming all runtime capacity and gives room for others to make progress.

Configuration (environment variables)
- HUB_BORROW_QUEUE_TIMEOUT_SECONDS
  - Default: 30
  - Max time a queued borrow waits before returning 503.
- HUB_BORROW_QUEUE_MAX_PER_LABEL
  - Default: 100
  - Maximum queued waiters per requested label.
- HUB_BORROW_QUEUE_MAX_PER_RUN
  - Default: 5
  - Maximum queued waiters attributed to the same runId across all labels.
- HUB_BORROW_CONCURRENCY_DEFAULT
  - Default: 0 (unlimited)
  - Default active concurrency cap applied to any label not explicitly listed in caps.
- HUB_BORROW_CONCURRENCY_CAPS
  - Format: comma‑separated label=cap pairs.
  - Example: "AppA:Chromium:UAT=2,AppB:Firefox:UAT=1".
  - Label keys are normalized by the Hub’s LabelKey parser.

Related matching toggles (influence candidate pools, not queueing itself)
- HUB_BORROW_TRAILING_FALLBACK (default true)
- HUB_BORROW_PREFIX_EXPAND (default true)
- HUB_BORROW_WILDCARDS (default false)

How it works (technical)
- The Hub maintains per‑label queues and counters keyed by runId (for per‑run caps).
- On successful borrow, the Hub increments an in‑flight counter for the requested label; on return (or auto‑return via TTL/sweepers), it decrements and fairly signals the queue if present.
- Round‑robin cursor moves to the label just served to avoid bias.

Operational tips
- Start with small but non‑zero concurrency caps for hot labels while you size worker pools.
- If you see frequent timeouts, either increase queue timeout/caps or scale workers for those labels.
- Prefer explicit labels over wide wildcards to keep utilization predictable.

Examples
- Limit AppB:Chromium:UAT to 3 in‑flight sessions and AppB:Firefox:UAT to 1; default unlimited elsewhere:
  - HUB_BORROW_CONCURRENCY_DEFAULT=0
  - HUB_BORROW_CONCURRENCY_CAPS="AppB:Chromium:UAT=3,AppB:Firefox:UAT=1"
- Tighten queue to reduce buildup and set a 20s timeout:
  - HUB_BORROW_QUEUE_MAX_PER_LABEL=20
  - HUB_BORROW_QUEUE_MAX_PER_RUN=3
  - HUB_BORROW_QUEUE_TIMEOUT_SECONDS=20

Prometheus metrics
- hub_borrow_requests_total{label}
- hub_borrow_latency_seconds{label}
- hub_borrow_outcomes_total{label, outcome="success|timeout|denied"}
- hub_borrow_queue_length{label}
- hub_pool_available_total{label}
- hub_pool_utilization_ratio{label}

Troubleshooting
- Borrow keeps timing out for a label:
  - Verify workers expose sufficient capacity for that label; check Dashboard and metrics.
  - Increase queue timeout or scale workers.
  - Check concurrency caps: Endpoint count may be available, but active cap may be reached.
- One label seems stuck behind others:
  - Ensure per‑label concurrency caps are not too restrictive for that label.
  - Verify that returns/auto‑returns happen; if not, inspect TTL settings and sweeper logs.

Notes
- The queue is in‑memory within the Hub process; multiple Hubs require external coordination to provide a global queue (out of scope for this version).
- All secrets and PII are excluded from logs. Only label names and run ids are used in counters and audit events.
