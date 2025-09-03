using StackExchange.Redis;
using WorkerService.Application.Ports;
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

namespace WorkerService.Infrastructure.Adapters;

public sealed class RedisStateRepository : IStateRepository, IAsyncDisposable
{
    private readonly IDatabase _db;
    private readonly ConnectionMultiplexer _mux;

    public RedisStateRepository(string redisConnectionString)
    {
        _mux = ConnectionMultiplexer.Connect(redisConnectionString);
        _db = _mux.GetDatabase();
    }

    public async ValueTask DisposeAsync()
    {
        try { await _mux.CloseAsync(); }
        catch { }

        _mux.Dispose();
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
        var values = await _db.ListRangeAsync(listKey);
        // Ensure non-null strings; RedisValue.ToString() can return null for Null values
        return values.Select(rv => rv.ToString() ?? string.Empty).ToList();
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
}
