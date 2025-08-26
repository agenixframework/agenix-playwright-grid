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
