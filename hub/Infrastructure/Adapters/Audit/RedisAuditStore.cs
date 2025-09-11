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
using Agenix.PlaywrightGrid.Domain;
using Microsoft.Extensions.Configuration;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Audit;

/// <summary>
///     Redis-backed append-only audit log. Stores latest N entries (configurable) in a Redis list.
/// </summary>
public sealed class RedisAuditStore(IDatabase db, IConfiguration config) : IAuditStore
{
    private readonly IDatabase _db = db;
    private readonly int _maxEntries = Math.Max(100, int.TryParse(config["HUB_AUDIT_MAX_ENTRIES"], out var n) ? n : 5000);
    private static readonly string Key = RedisKeys.AuditEntries();

    public async Task AppendAsync(AuditEntryDto entry, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(entry);
        await _db.ListLeftPushAsync(Key, json);
        await _db.ListTrimAsync(Key, 0, _maxEntries - 1);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> QueryAsync(int skip = 0, int take = 100, string? category = null, string? action = null, DateTime? sinceUtc = null, CancellationToken ct = default)
    {
        // Fetch a window large enough to filter; we cap by _maxEntries
        var range = await _db.ListRangeAsync(Key, 0, _maxEntries - 1);
        var list = new List<AuditEntryDto>(range.Length);
        foreach (var rv in range)
        {
            try
            {
                var e = JsonSerializer.Deserialize<AuditEntryDto>(rv!);
                if (e is null) continue;
                if (category is not null && !string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                if (action is not null && !string.Equals(e.Action, action, StringComparison.OrdinalIgnoreCase)) continue;
                if (sinceUtc is not null && e.TimestampUtc < sinceUtc.Value) continue;
                list.Add(e);
            }
            catch
            {
                // ignore malformed entries
            }
        }

        // Entries were pushed left, so range is newest->oldest already.
        var sk = Math.Max(0, skip);
        var tk = Math.Max(1, take);
        if (sk >= list.Count) return Array.Empty<AuditEntryDto>();
        var count = Math.Min(tk, list.Count - sk);
        return list.GetRange(sk, count);
    }
}
