#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Globalization;
using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Redis;

/// <summary>
///     Represents a service for reading aggregated pool state data from Redis.
/// </summary>
/// <remarks>
///     This class provides functionality to extract and aggregate pool-related state information
///     such as available/in-use resources and worker nodes by interfacing with a Redis database.
///     The information is collated from specific Redis key patterns representing pool and worker state.
/// </remarks>
public sealed class RedisPoolStateReader(IDatabase db, IConnectionMultiplexer mux) : IPoolStateReader
{
    /// Retrieves the current state of the pool, including information about pools and workers.
    /// The state is aggregated from Redis data and includes details such as available and in-use
    /// resources, node information, and per-worker counts.
    /// <returns>
    ///     A task that resolves to a PoolStateDto representing the aggregated pool state.
    /// </returns>
    public async Task<PoolStateDto> GetStateAsync()
    {
        var pools = new List<PoolEntryDto>();
        var workers = new List<WorkerStatusDto>();
        var browserVersionByLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var server = mux.GetServer(mux.GetEndPoints()[0]);

        // Pools: derive from the union of available:* and inuse:* labels to avoid missing pools with zero available
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rk in server.Keys(pattern: RedisKeys.AvailablePrefix + "*"))
        {
            var availKey = rk.ToString();
            var label = availKey[RedisKeys.AvailablePrefix.Length..];
            labels.Add(label);
        }

        foreach (var rk in server.Keys(pattern: RedisKeys.InUsePrefix + "*"))
        {
            var inuseKey = rk.ToString();
            var label = inuseKey[RedisKeys.InUsePrefix.Length..];
            labels.Add(label);
        }

        foreach (var label in labels)
        {
            var availKey = RedisKeys.Available(label);
            var inuseKey = RedisKeys.InUse(label);

            var availLen = await db.ListLengthAsync(availKey);
            var inuseLen = await db.ListLengthAsync(inuseKey);

            var displayAvail = (int)availLen;
            var displayInuse = (int)inuseLen;
            var maintenanceActive = false;

            try
            {
                if (db.KeyExists(RedisKeys.MaintenanceFlag(label)))
                {
                    var targetStr = db.StringGet(RedisKeys.MaintenanceTarget(label));
                    long target = 0;
                    if (!targetStr.IsNullOrEmpty)
                    {
                        long.TryParse(targetStr.ToString(), out target);
                    }

                    // Auto-clear maintenance if finished, but only after a short hold so UI can reflect maintenance state
                    var sinceStr = db.StringGet(RedisKeys.MaintenanceSince(label));
                    DateTime.TryParse(sinceStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                        out var since);
                    var hold = TimeSpan.FromSeconds(10);
                    var canClear = since != default && DateTime.UtcNow - since >= hold;

                    if (inuseLen == 0 && (availLen >= target || DateTime.UtcNow - since >= TimeSpan.FromMinutes(2)) &&
                        canClear)
                    {
                        try
                        {
                            db.KeyDelete(RedisKeys.MaintenanceFlag(label));
                            db.KeyDelete(RedisKeys.MaintenanceTarget(label));
                            db.KeyDelete(RedisKeys.MaintenanceSnapAvail(label));
                            db.KeyDelete(RedisKeys.MaintenanceSnapInuse(label));
                            db.KeyDelete(RedisKeys.MaintenanceSince(label));
                        }
                        catch { }
                    }

                    // If still active, freeze counts to snapshot
                    maintenanceActive = db.KeyExists(RedisKeys.MaintenanceFlag(label));
                    if (maintenanceActive)
                    {
                        var sa = db.StringGet(RedisKeys.MaintenanceSnapAvail(label));
                        var si = db.StringGet(RedisKeys.MaintenanceSnapInuse(label));
                        if (!sa.IsNullOrEmpty && int.TryParse(sa.ToString(), out var saInt))
                        {
                            displayAvail = saInt;
                        }

                        if (!si.IsNullOrEmpty && int.TryParse(si.ToString(), out var siInt))
                        {
                            displayInuse = siInt;
                        }

                        if (target > 0)
                        {
                            var total = displayAvail + displayInuse;
                            if (total != target)
                            {
                                // Keep borrowed snapshot and adjust available to match target
                                displayAvail = (int)Math.Max(0, target - displayInuse);
                            }
                        }
                    }
                }
            }
            catch { }

            pools.Add(new PoolEntryDto
            {
                Label = label,
                Total = displayAvail + displayInuse,
                Borrowed = displayInuse,
                MaintenanceActive = maintenanceActive
            });
        }

        // Workers: derive from "nodes" set and node:{id} hash
        var nodeIds = db.SetMembers("nodes").Select(v => v.ToString()).ToArray();
        foreach (var nodeId in nodeIds)
        {
            var key = $"node:{nodeId}";
            var lastSeenStr = db.HashGet(key, "LastSeen");
            var labelsJson = db.HashGet(key, "Labels");
            var capacityStr = db.HashGet(key, "Capacity");
            var pwVer = db.HashGet(key, "PlaywrightVersion");
            var pwExpected = db.HashGet(key, "PlaywrightVersionExpected");
            var pwMismatch = db.HashGet(key, "PlaywrightVersionMismatch");

            // Parse using round-trip ISO 8601 to preserve UTC
            DateTime.TryParse(lastSeenStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var lastSeen);
            var labelsDict = !labelsJson.IsNullOrEmpty
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(labelsJson.ToString()) ??
                  new Dictionary<string, string>()
                : new Dictionary<string, string>();

            // Preserve keys to make labels self-descriptive (e.g., region=local)
            var labelsList = labelsDict
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                .Select(kvp => $"{kvp.Key}={kvp.Value}")
                .ToList();

            int.TryParse(capacityStr, out var totalBrowsers);

            var computedLastSeen = lastSeen == default ? DateTime.MinValue : lastSeen;

            // Only include workers that were seen recently to avoid showing stale/ghost nodes
            var isRecent = computedLastSeen != DateTime.MinValue &&
                           DateTime.UtcNow - computedLastSeen <= TimeSpan.FromMinutes(2);
            if (!isRecent)
            {
                continue;
            }

            workers.Add(new WorkerStatusDto
            {
                Id = nodeId,
                Labels = labelsList,
                LastSeen = computedLastSeen,
                Pools = new Dictionary<string, PoolCounts>(),
                TotalBrowsers = totalBrowsers,
                PlaywrightVersion = pwVer.IsNullOrEmpty ? null : pwVer.ToString()
            });
        }

        // Populate per-worker per-pool counts by scanning available:* and inuse:* lists
        var workerById = workers.ToDictionary(w => w.Id, w => w, StringComparer.Ordinal);

        static string? TryGetNodeId(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("nodeId", out var nodeEl) &&
                    nodeEl.ValueKind == JsonValueKind.String)
                {
                    return nodeEl.GetString();
                }
            }
            catch
            {
                // ignore malformed entries
            }

            return null;
        }

        static string? TryGetBrowserVersion(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("browserVersion", out var vEl) &&
                    vEl.ValueKind == JsonValueKind.String)
                {
                    return vEl.GetString();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        foreach (var rk in server.Keys(pattern: RedisKeys.AvailablePrefix + "*"))
        {
            var key = rk.ToString();
            var label = key[RedisKeys.AvailablePrefix.Length..];
            var items = db.ListRange(key);
            foreach (var item in items)
            {
                var s = item.ToString();
                var nodeId = TryGetNodeId(s);
                if (!string.IsNullOrEmpty(nodeId))
                {
                    Bump(nodeId, label, false);
                }

                if (!browserVersionByLabel.ContainsKey(label))
                {
                    var v = TryGetBrowserVersion(s);
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        browserVersionByLabel[label] = v;
                    }
                }
            }
        }

        foreach (var rk in server.Keys(pattern: RedisKeys.InUsePrefix + "*"))
        {
            var key = rk.ToString();
            var label = key[RedisKeys.InUsePrefix.Length..];
            var items = db.ListRange(key);
            foreach (var item in items)
            {
                var s = item.ToString();
                var nodeId = TryGetNodeId(s);
                if (!string.IsNullOrEmpty(nodeId))
                {
                    Bump(nodeId, label, true);
                }

                if (!browserVersionByLabel.ContainsKey(label))
                {
                    var v = TryGetBrowserVersion(s);
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        browserVersionByLabel[label] = v;
                    }
                }
            }
        }

        // Assign discovered browser versions per pool label
        foreach (var p in pools)
        {
            if (browserVersionByLabel.TryGetValue(p.Label, out var ver))
            {
                p.BrowserVersion = ver;
            }
        }

        var dto = new PoolStateDto
        {
            Pools = pools.OrderBy(p => p.Label).ToList(),
            Workers = workers.OrderBy(w => w.Id).ToList(),
            Now = DateTime.UtcNow
        };

        return await Task.FromResult(dto);

        void Bump(string nodeId, string label, bool borrowed)
        {
            if (!workerById.TryGetValue(nodeId, out var w))
            {
                return;
            }

            if (!w.Pools.TryGetValue(label, out var pc))
            {
                pc = new PoolCounts();
                w.Pools[label] = pc;
            }

            pc.Total++;
            if (borrowed)
            {
                pc.Borrowed++;
            }
        }
    }
}
