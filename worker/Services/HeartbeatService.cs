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

using System.Text.Json;
using StackExchange.Redis;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class HeartbeatService(WorkerOptions options, IDatabase db)
{
    public async Task HeartbeatOnceAsync()
    {
        try
        {
            var key = $"node:{options.NodeId}";
            var nowIso = DateTime.UtcNow.ToString("o");
            await db.HashSetAsync(key, "LastSeen", nowIso);
            var lblsJson = JsonSerializer.Serialize(options.Labels.ToDictionary(k => k.Key, v => v.Value));
            await db.HashSetAsync(key, "Labels", lblsJson);
            await db.HashSetAsync(key, "Capacity", options.PoolConfig.Values.Sum().ToString());
            await db.SetAddAsync("nodes", options.NodeId);
            await db.StringSetAsync($"node_alive:{options.NodeId}", "1", TimeSpan.FromSeconds(90));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HeartbeatOnce] error: {ex.Message}");
        }
    }

    public async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var hbInterval = TimeSpan.FromSeconds(10);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var key = $"node:{options.NodeId}";
                var nowIso = DateTime.UtcNow.ToString("o");
                await db.HashSetAsync(key, "LastSeen", nowIso);
                var lblsJson = JsonSerializer.Serialize(options.Labels.ToDictionary(k => k.Key, v => v.Value));
                await db.HashSetAsync(key, "Labels", lblsJson);
                await db.HashSetAsync(key, "Capacity", options.PoolConfig.Values.Sum().ToString());
                await db.SetAddAsync("nodes", options.NodeId);
                await db.StringSetAsync($"node_alive:{options.NodeId}", "1", TimeSpan.FromSeconds(90));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Heartbeat] error: {ex.Message}");
            }

            try { await Task.Delay(hbInterval, ct); }
            catch { }
        }
    }
}
