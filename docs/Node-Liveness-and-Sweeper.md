# Node Liveness and Sweeper

This page documents how the Hub detects stale worker nodes and reclaims capacity safely.

Overview
- Workers register with the Hub and periodically update a liveness footprint in Redis.
- The Hub runs a background sweeper (NodeSweeperService) that scans registered nodes and prunes entries for nodes that have stopped heartbeating.
- When expiring a node, the Hub also removes orphaned capacity from available:/inuse: lists to prevent stuck capacity.

Key configuration (Hub environment)
- HUB_NODE_TIMEOUT
  - Default: 60 (seconds)
  - Maximum tolerated time since the last node heartbeat before a node is considered stale.
- HUB_SWEEPER_EXPIRE
  - Default: false
  - When true, the sweeper actively expires stale nodes and prunes their capacity. When false, it only logs and defers deletion (it will refresh a short alive TTL to avoid tight loops).

How liveness is tracked
- A Redis set "nodes" holds the registered node ids.
- For each node, a Redis hash key node:{nodeId} contains metadata like LastSeen (UTC, ISO 8601).
- A short-lived alive key node_alive:{nodeId} is used as a fast-path heartbeat indicator. If present, the node is considered healthy without further checks.

Sweeper logic (NodeSweeperService)
1) Iterate node ids in the "nodes" set.
2) If node_alive:{nodeId} exists → skip (healthy).
3) Fetch node:{nodeId}:LastSeen and parse as UTC; if missing/invalid or older than HUB_NODE_TIMEOUT → treat as stale (with small tolerance for clock skew into the future).
4) Double-check node_alive:{nodeId} again before mutating, to avoid racing a fresh heartbeat.
5) If HUB_SWEEPER_EXPIRE=true:
   - Remove nodeId from the "nodes" set and delete node:{nodeId}.
   - Prune available entries that reference this node from all available:* lists.
   - Prune in-use entries that reference this node from inuse:* lists and clean up lightweight browser mappings (browser_run:/browser_test:).
   - Decrement in-flight caps per label and signal the fair capacity queue, so queued borrows can proceed if possible.
6) If HUB_SWEEPER_EXPIRE=false:
   - Refresh node_alive:{nodeId} with a short TTL (30s) to avoid repeated processing; log that expiration is disabled.

Capacity cleanup details
- Available entries pruning: remove list items that include the JSON fragment "\"nodeId\":\"{nodeId}\"" from keys matching available:*.
- In-use entries pruning: similarly remove items from inuse:* and perform:
  - EndpointCapacityQueue.OnFinished(labelKey) to decrement in-flight counters for the affected label.
  - Remove browser_run:{browserId} and browser_test:{browserId} mappings if browserId is found in the JSON blob.

Operational recommendations
- Keep HUB_NODE_TIMEOUT slightly above your worker heartbeat interval to reduce false positives.
- Run with HUB_SWEEPER_EXPIRE=true in production to automatically reclaim capacity when a worker disappears.
- Monitor logs from the sweeper: it emits per-interval summaries (scanned, expired, errors, elapsed).

Troubleshooting
- Capacity appears stuck after a worker crash:
  - Ensure HUB_SWEEPER_EXPIRE=true.
  - Verify the sweeper logs show pruning of in-use entries for the crashed node.
- Nodes being expired too aggressively:
  - Increase HUB_NODE_TIMEOUT.
  - Check for clock skew; the sweeper tolerates small future timestamps but large skews can mislead liveness checks.

Related docs
- Borrow TTL & Session Persistence: Borrow-TTL-and-Session-Persistence.md
- Capacity Queue and Concurrency Caps: Capacity-Queue.md
