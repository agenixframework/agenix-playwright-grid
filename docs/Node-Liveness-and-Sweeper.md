# Node Liveness and Sweeper (Hub)

This document explains how the Hub tracks worker node liveness, the configuration knobs, and what happens when a node becomes stale or disappears. It also outlines how orphaned sessions are reclaimed to avoid capacity leaks.

Overview
- Workers periodically emit heartbeats to Redis updating:
  - node:{nodeId} hash fields: LastSeen (ISO-8601 UTC), Labels (JSON), Capacity
  - nodes set membership (nodeId)
  - node_alive:{nodeId} key with TTL (default 90s in Worker)
- The Hub runs a background NodeSweeperService that periodically scans for stale nodes and prunes associated capacity entries. It complements the Worker heartbeats by performing garbage collection when nodes are dead or unreachable.

Configuration (environment variables)
- HUB_NODE_TIMEOUT: seconds of inactivity before a node is considered stale. Default: 60.
- HUB_SWEEPER_EXPIRE: if true, the sweeper will actually expire nodes and prune data. If false, it will refresh a short TTL on node_alive:{nodeId} and log what would happen. Default: false (dry-run).

How the sweeper works
1) Tick interval: every ~20 seconds the service performs a pass.
2) For each nodeId in Redis set "nodes":
   - If node_alive:{nodeId} exists → node is healthy, skip.
   - Else, parse node:{nodeId} LastSeen (strict ISO-8601 Roundtrip). If missing/invalid or older than HUB_NODE_TIMEOUT → candidate for expiration.
   - Small tolerance: if LastSeen is in the future by >5s (clock skew), do not expire.
   - Double check: if node_alive:{nodeId} re-appears during the pass, skip to avoid race with a fresh heartbeat.
   - If there are still available:* entries that reference this node, we treat the node as alive and refresh node_alive TTL to 30s, skipping expiration for this tick. This avoids evicting a node that is actively serving capacity but briefly missed heartbeat.
3) When expiring (HUB_SWEEPER_EXPIRE=true):
   - Remove nodeId from set "nodes" and delete hash key node:{nodeId}.
   - Prune available:* lists: remove entries containing this nodeId.
   - Prune inuse:* lists (new): remove entries containing this nodeId, and best-effort delete lightweight mappings browser_run:{browserId} and browser_test:{browserId} if browserId is present. This reclaims capacity that would otherwise be stuck.
4) Logs include per-tick stats: scanned, expired, errors, and tick duration.

Why prune inuse:* too?
Previously, only available:* lists were pruned. If a node died while a browser was borrowed (inuse:*), capacity would remain stuck. The sweeper now removes those orphaned records and clears run/test mappings so new borrows are not blocked by phantom in-use entries.

Related components
- Worker HeartbeatService: updates LastSeen and sets node_alive TTL so healthy nodes are never swept.
- RunCleanupService: a separate hub background service that can auto-return outstanding browsers when runs become inactive or exceed max duration. This operates at run level, whereas NodeSweeperService operates at node level.

Operational tips
- If you are testing locally and want to observe sweeper behavior quickly:
  - Set HUB_NODE_TIMEOUT=5 and HUB_SWEEPER_EXPIRE=true on the hub.
  - Stop a worker to simulate a dead node.
  - Watch hub logs for "[Sweeper] Expiring node=..." and pruning messages.
- In CI or during cautious rollouts, set HUB_SWEEPER_EXPIRE=false to dry-run. The sweeper will log and refresh a short node_alive TTL instead of deleting anything.

Metrics
- While the sweeper itself does not currently expose Prometheus metrics, overall pool gauges (available counts per label) are updated elsewhere. Consider adding sweeper-specific counters if needed for ops visibility.

Security considerations
- The sweeper only reads/writes keys used by the grid. Keys deleted are specific to the expired node or to browserId mappings captured from the in-use entries.

Version
- Introduced orphaned in-use pruning in this repository session (2025-08-31).

Interpreting Sweeper logs
- The service logs a summary at the end of each pass, e.g.: [Sweeper] Tick done: scanned=3 expired=0 errors=0 took=2ms
  - scanned=N: number of nodeIds in the Redis set "nodes" that were evaluated this tick.
  - expired=N: how many nodes were actually expired (removed and pruned) in this tick. This remains 0 when:
    - Nodes are healthy (node_alive:{nodeId} TTL present), or
    - LastSeen is within HUB_NODE_TIMEOUT, or
    - HUB_SWEEPER_EXPIRE=false (dry-run mode), or
    - The sweeper detected active available:* entries for the node and refreshed a short TTL instead of expiring.
  - errors=N: number of caught exceptions during processing (per-node or loop-level). Non-zero suggests Redis or parsing issues.
  - took=Xms: how long the entire sweep iteration took in milliseconds.
- If you see scanned>0 with expired=0 consistently, it typically means heartbeats are healthy and no nodes are stale.
