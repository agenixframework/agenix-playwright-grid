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

using Npgsql;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
///     Very lightweight PostgreSQL migrations runner used by PostgresResultsStore to
///     ensure the durable schema exists and is upgraded in a controlled, idempotent way.
///     Uses a monotonic integer version and a schema_migrations table to track state.
/// </summary>
internal static class PostgresSchemaMigrations
{
    internal sealed record Migration(int Version, string Name, string Sql);

    private static string LoadSqlResource(string fileName)
    {
        var asm = typeof(PostgresSchemaMigrations).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".Migrations.{fileName}", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Migration SQL resource not found: {fileName}");
        }
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // V1 schema mirrors the original inline DDL that PostgresResultsStore used.
    private static readonly Migration[] Migrations =
    {
        new(1, "init", LoadSqlResource("V1__init.sql"))
    };

    /// <summary>
    ///     Applies all pending migrations. Creates the schema_migrations table if necessary.
    /// </summary>
    internal static async Task ApplyAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Ensure version table exists
        await using (var ensureCmd = conn.CreateCommand())
        {
            ensureCmd.CommandText = @"CREATE TABLE IF NOT EXISTS schema_migrations (
    version INT PRIMARY KEY,
    name TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);";
            await ensureCmd.ExecuteNonQueryAsync(ct);
        }

        // Read applied versions
        var applied = new HashSet<int>();
        await using (var readCmd = conn.CreateCommand())
        {
            readCmd.CommandText = "SELECT version FROM schema_migrations";
            await using var reader = await readCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                applied.Add(reader.GetInt32(0));
            }
        }

        foreach (var m in Migrations.OrderBy(m => m.Version))
        {
            if (applied.Contains(m.Version)) continue;
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var migCmd = conn.CreateCommand())
                {
                    migCmd.Transaction = tx;
                    migCmd.CommandText = m.Sql;
                    await migCmd.ExecuteNonQueryAsync(ct);
                }

                await using (var insCmd = conn.CreateCommand())
                {
                    insCmd.Transaction = tx;
                    insCmd.CommandText = "INSERT INTO schema_migrations(version, name) VALUES (@v, @n)";
                    insCmd.Parameters.AddWithValue("@v", m.Version);
                    insCmd.Parameters.AddWithValue("@n", m.Name);
                    await insCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                try { await tx.RollbackAsync(ct); } catch { }
                throw;
            }
        }
    }
}
