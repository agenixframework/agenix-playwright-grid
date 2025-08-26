using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;
using WorkerService.Application.Ports;

namespace WorkerService.Infrastructure.Adapters;

public sealed class RedisStateRepository : IStateRepository, IAsyncDisposable
{
    private readonly ConnectionMultiplexer _mux;
    private readonly IDatabase _db;

    public RedisStateRepository(string redisConnectionString)
    {
        _mux = ConnectionMultiplexer.Connect(redisConnectionString);
        _db = _mux.GetDatabase();
    }

    public async Task ListRightPushAsync(string listKey, string itemJson)
    {
        await _db.ListRightPushAsync(listKey, itemJson);
    }

    public async Task<long> ListLengthAsync(string listKey)
    {
        return await _db.ListLengthAsync(listKey);
    }

    public async Task<IReadOnlyList<string>> ListRangeAsync(string listKey)
    {
        return (await _db.ListRangeAsync(listKey)).Select(rv => (string)rv).ToArray();
    }

    public async Task<long> ListRemoveAsync(string listKey, string itemJson, long count = 0)
    {
        return await _db.ListRemoveAsync(listKey, itemJson, count);
    }

    public async Task HashSetAsync(string key, string field, string value)
    {
        await _db.HashSetAsync(key, new HashEntry[] { new(field, value) });
    }

    public async Task SetAddAsync(string key, string member)
    {
        await _db.SetAddAsync(key, member);
    }

    public async Task StringSetAsync(string key, string value, TimeSpan? expiry)
    {
        await _db.StringSetAsync(key, value, expiry);
    }

    public async Task<bool> SetRemoveAsync(string key, string member)
    {
        return await _db.SetRemoveAsync(key, member);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _mux.CloseAsync(); }
        catch { }

        _mux.Dispose();
    }
}
