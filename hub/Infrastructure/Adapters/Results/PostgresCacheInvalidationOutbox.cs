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

using Npgsql;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

public sealed class PostgresCacheInvalidationOutbox(NpgsqlDataSource dataSource) : ICacheInvalidationOutbox
{
    public async Task AddAsync(string key, NpgsqlConnection connection, NpgsqlTransaction? transaction = null, CancellationToken ct = default)
    {
        const string query = "INSERT INTO cache_invalidation_outbox (key) VALUES ($1)";
        await using var cmd = new NpgsqlCommand(query, connection, transaction);
        cmd.Parameters.AddWithValue(key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(long Id, string Key)>> GetPendingAsync(int limit = 100, CancellationToken ct = default)
    {
        const string query = "SELECT id, key FROM cache_invalidation_outbox ORDER BY id LIMIT $1 FOR UPDATE SKIP LOCKED";
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue(limit);

        var result = new List<(long Id, string Key)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return result;
    }

    public async Task DeleteAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;

        const string query = "DELETE FROM cache_invalidation_outbox WHERE id = ANY($1)";
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue(idList);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
