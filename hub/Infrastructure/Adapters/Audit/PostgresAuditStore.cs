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

using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Audit;

/// <summary>
///     PostgreSQL-backed persistent audit log with event-driven architecture.
///     Stores unlimited audit entries with transactional integrity and real-time notifications via pg_notify.
/// </summary>
public sealed class PostgresAuditStore(NpgsqlDataSource db) : IAuditStore
{
    /// <summary>
    ///     Appends a new audit entry to the database with transactional integrity.
    ///     Triggers pg_notify event for real-time dashboard updates.
    /// </summary>
    public async Task AppendAsync(AuditEntryDto entry, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO audit_entries (timestamp, category, action, actor, remote_ip, correlation_id, severity, details)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8)";

            cmd.Parameters.AddWithValue(entry.Timestamp);
            cmd.Parameters.AddWithValue(entry.Category);
            cmd.Parameters.AddWithValue(entry.Action);
            cmd.Parameters.AddWithValue(entry.Actor ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(entry.RemoteIp ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(entry.CorrelationId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue(entry.Severity ?? "Info");

            // Serialize details dictionary to JSONB
            var detailsJson = entry.Details != null ? JsonSerializer.Serialize(entry.Details) : "{}";
            cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, detailsJson);

            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    ///     Queries audit entries with optional filtering by category, action, and time range.
    ///     Supports pagination with skip/take parameters.
    /// </summary>
    public async Task<IReadOnlyList<AuditEntryDto>> QueryAsync(
        int skip = 0, int take = 100, string? category = null,
        string? action = null, DateTime? sinceUtc = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var conditions = new List<string>();
        var paramIndex = 1;

        if (category != null)
        {
            conditions.Add($"category ILIKE ${paramIndex}");
            cmd.Parameters.AddWithValue(category);
            paramIndex++;
        }

        if (action != null)
        {
            conditions.Add($"action ILIKE ${paramIndex}");
            cmd.Parameters.AddWithValue(action);
            paramIndex++;
        }

        if (sinceUtc != null)
        {
            conditions.Add($"timestamp >= ${paramIndex}");
            cmd.Parameters.AddWithValue(sinceUtc.Value);
            paramIndex++;
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        cmd.CommandText = $@"
            SELECT timestamp, category, action, actor, remote_ip, correlation_id, severity, details
            FROM audit_entries
            {whereClause}
            ORDER BY timestamp DESC
            LIMIT ${paramIndex} OFFSET ${paramIndex + 1}";

        cmd.Parameters.AddWithValue(take);
        cmd.Parameters.AddWithValue(skip);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AuditEntryDto>();

        while (await reader.ReadAsync(ct))
        {
            var detailsJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            Dictionary<string, string>? details = null;
            if (detailsJson != null)
            {
                try
                {
                    details = JsonSerializer.Deserialize<Dictionary<string, string>>(detailsJson);
                }
                catch
                {
                    // Ignore malformed JSON details
                }
            }

            results.Add(new AuditEntryDto
            {
                Timestamp = reader.GetDateTime(0),
                Category = reader.GetString(1),
                Action = reader.GetString(2),
                Actor = reader.IsDBNull(3) ? null : reader.GetString(3),
                RemoteIp = reader.IsDBNull(4) ? null : reader.GetString(4),
                CorrelationId = reader.IsDBNull(5) ? null : reader.GetString(5),
                Severity = reader.IsDBNull(6) ? null : reader.GetString(6),
                Details = details
            });
        }

        return results;
    }
}
