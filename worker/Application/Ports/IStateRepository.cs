using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkerService.Application.Ports;

public interface IStateRepository
{
    Task ListRightPushAsync(string listKey, string itemJson);
    Task<long> ListLengthAsync(string listKey);
    Task<IReadOnlyList<string>> ListRangeAsync(string listKey);
    Task<long> ListRemoveAsync(string listKey, string itemJson, long count = 0);

    Task HashSetAsync(string key, string field, string value);
    Task SetAddAsync(string key, string member);
    Task StringSetAsync(string key, string value, TimeSpan? expiry);
    Task<bool> SetRemoveAsync(string key, string member);
}
