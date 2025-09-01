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

using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

/// <summary>
///     Background sweeper that auto-returns borrowed sessions whose TTL/lease has expired.
///     Persists and consults session:* hashes to recover context after Hub restarts.
/// </summary>
public sealed class BorrowTtlSweeperService(IDatabase db, IConnectionMultiplexer mux, IConfiguration config)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = int.TryParse(config["HUB_BORROW_TTL_SWEEP_SECONDS"], out var s) ? Math.Max(5, s) : 10;
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var luaReturn = @"
local inuse = KEYS[1]
local avail = KEYS[2]
local browserId = ARGV[1]
local list = redis.call('LRANGE', inuse, 0, -1)
for i,item in ipairs(list) do
  if string.find(item, browserId, 1, true) then
    redis.call('LREM', inuse, 1, item)
    redis.call('RPUSH', avail, item)
    return item
  end
end
return nil
";

        Console.WriteLine($"[BorrowTTL] Starting. sweepInterval={intervalSeconds}s");

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            var processed = 0;
            var returned = 0;
            var errors = 0;
            try
            {
                foreach (var key in server.Keys(pattern: "session:*"))
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var sessionKey = key.ToString();
                    var browserId = sessionKey.Substring("session:".Length);
                    try
                    {
                        // if lease key still exists -> not expired
                        if (await db.KeyExistsAsync($"borrow_ttl:{browserId}"))
                        {
                            processed++;
                            continue;
                        }

                        // No lease key -> TTL expired. Load session record to know labelKey and attempt return.
                        var entries = await db.HashGetAllAsync(sessionKey);
                        if (entries is null || entries.Length == 0)
                        {
                            // Nothing to do; clean stray session key
                            try { await db.KeyDeleteAsync(sessionKey); }
                            catch { }

                            processed++;
                            continue;
                        }

                        string labelKey = entries.FirstOrDefault(e => e.Name == "labelKey").Value;
                        if (!string.IsNullOrWhiteSpace(labelKey))
                        {
                            var inuseKey = $"inuse:{labelKey}";
                            var availKey = $"available:{labelKey}";
                            var res = await db.ScriptEvaluateAsync(luaReturn, new RedisKey[] { inuseKey, availKey },
                                new RedisValue[] { browserId });
                            if (!res.IsNull)
                            {
                                returned++;
                                // cleanup mappings
                                try { await db.KeyDeleteAsync($"browser_run:{browserId}"); }
                                catch { }

                                try { await db.KeyDeleteAsync($"browser_test:{browserId}"); }
                                catch { }

                                try { await db.KeyDeleteAsync($"borrow_ttl:{browserId}"); }
                                catch { }

                                try { await db.KeyDeleteAsync(sessionKey); }
                                catch { }

                                // request recycle on worker
                                try { await db.StringSetAsync($"recycle:{browserId}", "1", TimeSpan.FromMinutes(2)); }
                                catch { }
                            }
                            else
                            {
                                // Could not find in inuse list; just clean session keys
                                try { await db.KeyDeleteAsync($"borrow_ttl:{browserId}"); }
                                catch { }

                                try { await db.KeyDeleteAsync(sessionKey); }
                                catch { }
                            }
                        }
                        else
                        {
                            // malformed session; delete
                            try { await db.KeyDeleteAsync(sessionKey); }
                            catch { }
                        }

                        processed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.WriteLine($"[BorrowTTL] Error while processing {sessionKey}: {ex.Message}");
                    }
                }
            }
            catch (Exception exOuter)
            {
                errors++;
                Console.WriteLine($"[BorrowTTL] Sweep error: {exOuter.Message}");
            }

            var took = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            Console.WriteLine(
                $"[BorrowTTL] Tick done processed={processed} returned={returned} errors={errors} took={took}ms");

            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
