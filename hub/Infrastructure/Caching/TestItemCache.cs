#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
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

using Microsoft.Extensions.Caching.Memory;

namespace PlaywrightHub.Infrastructure.Caching;

/// <summary>
///     In-memory cache for frequently accessed test items to reduce database load.
///     Uses IMemoryCache for automatic expiration and memory management.
/// </summary>
public class TestItemCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(10);
    private readonly ILogger<TestItemCache> _logger;

    // Track cache hits/misses for telemetry
    private long _hits;
    private long _misses;

    public TestItemCache(IMemoryCache cache, ILogger<TestItemCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public T? Get<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out T? value))
        {
            Interlocked.Increment(ref _hits);
            return value;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? _defaultExpiration,
            Size = 1 // For memory management
        };

        _cache.Set(key, value, options);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }

    public void Clear()
    {
        // IMemoryCache doesn't have Clear(), so we track keys separately if needed
        _logger.LogInformation("Cache clear requested");
    }

    public (long Hits, long Misses, double HitRate) GetStatistics()
    {
        var totalHits = Interlocked.Read(ref _hits);
        var totalMisses = Interlocked.Read(ref _misses);
        var total = totalHits + totalMisses;
        var hitRate = total > 0 ? (double)totalHits / total : 0;

        return (totalHits, totalMisses, hitRate);
    }
}
