using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

// Background service that sweeps stale nodes and prunes stale available entries
/// <summary>
/// A background service responsible for sweeping stale nodes and pruning stale available entries
/// in the Playwright Hub infrastructure.
/// </summary>
/// <remarks>
/// This service periodically scans and validates node entries stored in Redis. Any stale or
/// invalid nodes are removed as part of its operation. It uses configuration values
/// to determine the node timeout and sweeper execution interval. Optionally, it can also
/// remove expired nodes based on configuration settings. The service runs until explicitly
/// cancelled.
/// </remarks>
public sealed class NodeSweeperService(IDatabase db, IConnectionMultiplexer mux, IConfiguration config)
    : BackgroundService
{
    /// <summary>
    /// Executes the main logic of the NodeSweeperService, which periodically scans and prunes
    /// stale or expired node entries from the Redis database.
    /// </summary>
    /// <param name="stoppingToken">A token that signals the task to stop execution.</param>
    /// <returns>A task that represents the asynchronous execution operation of the service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nodeTimeoutSeconds = int.TryParse(config["HUB_NODE_TIMEOUT"], out var t) ? t : 60;
        var sweeperExpire = string.Equals(config["HUB_SWEEPER_EXPIRE"], "true", StringComparison.OrdinalIgnoreCase);

        var nodeTimeout = TimeSpan.FromSeconds(nodeTimeoutSeconds);
        var sweeperInterval = TimeSpan.FromSeconds(20);

        Console.WriteLine(
            $"[Sweeper] Starting. interval={sweeperInterval.TotalSeconds:F0}s timeout={nodeTimeout.TotalSeconds:F0}s expire={(sweeperExpire ? "on" : "off")}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTime.UtcNow;
            var scanned = 0;
            var expired = 0;
            var errors = 0;

            try
            {
                var nodeIds = db.SetMembers("nodes").Select(x => x.ToString()).ToArray();
                scanned = nodeIds.Length;

                foreach (var nodeId in nodeIds)
                {
                    try
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // If an alive TTL key is present, consider the node healthy.
                        var aliveKey = $"node_alive:{nodeId}";
                        if (await db.KeyExistsAsync(aliveKey))
                        {
                            continue;
                        }

                        var key = $"node:{nodeId}";
                        var lastSeenVal = await db.HashGetAsync(key, "LastSeen");

                        // Parse using round-trip ISO 8601 to preserve UTC
                        var lastSeenStr = lastSeenVal.ToString();
                        var parsed = DateTimeOffset.TryParseExact(
                            lastSeenStr,
                            "o",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var lastSeenDto);

                        var missingOrStale =
                            lastSeenVal.IsNullOrEmpty ||
                            !parsed ||
                            DateTime.UtcNow - lastSeenDto.UtcDateTime > nodeTimeout;

                        // Small tolerance: if clocks are skewed and lastSeen is in the future, do not expire
                        var clockSkewFuture = parsed && lastSeenDto.UtcDateTime > DateTime.UtcNow.AddSeconds(5);

                        if (missingOrStale && !clockSkewFuture)
                        {
                            // Double-check liveness before deleting to avoid racing a fresh heartbeat
                            if (await db.KeyExistsAsync(aliveKey))
                            {
                                continue;
                            }

                            // If the node still has available entries, treat it as alive and refresh a short TTL
                            if (await HasAvailableEntriesForNodeAsync(nodeId))
                            {
                                await db.StringSetAsync(aliveKey, "1", TimeSpan.FromSeconds(30));
                                Console.WriteLine(
                                    $"[Sweeper] Skipping expiration of node={nodeId} due to active available entries; refreshed TTL=30s");
                                continue;
                            }

                            if (sweeperExpire)
                            {
                                Console.WriteLine(
                                    $"[Sweeper] Expiring node={nodeId} lastSeen={(string.IsNullOrEmpty(lastSeenStr) ? "<missing>" : lastSeenStr)}");

                                await db.SetRemoveAsync("nodes", nodeId);
                                await db.KeyDeleteAsync(key);
                                await PruneAvailableEntriesForNodeAsync(nodeId);
                                await PruneInuseEntriesForNodeAsync(nodeId);

                                expired++;
                            }
                            else
                            {
                                await db.StringSetAsync(aliveKey, "1", TimeSpan.FromSeconds(30));
                                Console.WriteLine(
                                    $"[Sweeper] Would expire node={nodeId} (expiration disabled); refreshed TTL=30s");
                            }
                        }
                    }
                    catch (Exception exNode)
                    {
                        errors++;
                        Console.WriteLine($"[Sweeper] Error while processing node {nodeId}: {exNode.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"[Sweeper] Loop error: {ex}");
            }

            var elapsedMs = (int)(DateTime.UtcNow - tickStart).TotalMilliseconds;
            Console.WriteLine(
                $"[Sweeper] Tick done: scanned={scanned} expired={expired} errors={errors} took={elapsedMs}ms");

            try
            {
                await Task.Delay(sweeperInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // graceful exit
                break;
            }
        }
    }

    /// <summary>
    /// Removes stale available entries associated with the specified node from the database.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node whose stale available entries are to be pruned.</param>
    /// <returns>A task that represents the asynchronous pruning operation.</returns>
    private async Task PruneAvailableEntriesForNodeAsync(string nodeId)
    {
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var nodePattern = $"\"nodeId\":\"{nodeId}\"";
        foreach (var rk in server.Keys(pattern: "available:*"))
        {
            var key = rk.ToString();
            var list = db.ListRange(key);
            foreach (var item in list)
            {
                if (item.ToString().Contains(nodePattern, StringComparison.Ordinal))
                {
                    await db.ListRemoveAsync(key, item);
                    Console.WriteLine($"[Sweeper] Pruned stale entry from {key} for node {nodeId}");
                }
            }
        }
    }

    /// <summary>
    /// Checks if the specified node has available entries in the system.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node to check for available entries.</param>
    /// <returns>A task that represents the asynchronous operation.
    /// The task result contains a boolean indicating whether the node has available entries.</returns>
    private async Task<bool> HasAvailableEntriesForNodeAsync(string nodeId)
    {
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var nodePattern = $"\"nodeId\":\"{nodeId}\"";
        foreach (var rk in server.Keys(pattern: "available:*"))
        {
            var key = rk.ToString();
            var list = await db.ListRangeAsync(key);
            if (list.Any(item => item.ToString().Contains(nodePattern, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes orphaned in-use entries associated with the specified node from Redis and
    /// clears lightweight browser mappings (browser_run:/browser_test:) when encountered.
    /// This prevents capacity from being stuck when a worker disappears.
    /// </summary>
    private async Task PruneInuseEntriesForNodeAsync(string nodeId)
    {
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var nodePattern = $"\"nodeId\":\"{nodeId}\"";
        foreach (var rk in server.Keys(pattern: "inuse:*"))
        {
            var key = rk.ToString();
            var list = await db.ListRangeAsync(key);
            foreach (var item in list)
            {
                var s = item.ToString();
                if (s.Contains(nodePattern, StringComparison.Ordinal))
                {
                    await db.ListRemoveAsync(key, item);

                    // Best-effort: remove browser mappings if browserId is present in the JSON blob
                    try
                    {
                        using var doc = JsonDocument.Parse(s);
                        if (doc.RootElement.TryGetProperty("browserId", out var bidEl) &&
                            bidEl.ValueKind == JsonValueKind.String)
                        {
                            var browserId = bidEl.GetString();
                            if (!string.IsNullOrWhiteSpace(browserId))
                            {
                                try { await db.KeyDeleteAsync($"browser_run:{browserId}"); } catch { }
                                try { await db.KeyDeleteAsync($"browser_test:{browserId}"); } catch { }
                            }
                        }
                    }
                    catch { }

                    Console.WriteLine($"[Sweeper] Pruned orphaned in-use entry from {key} for node {nodeId}");
                }
            }
        }
    }
}
