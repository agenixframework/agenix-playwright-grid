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

using Agenix.PlaywrightGrid.Domain;
using Npgsql;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Admin;

/// <summary>
///     PostgreSQL-backed durable mirror for Admin entities (Projects, Users, Memberships).
///     Uses the same connection and migrations runner as PostgresResultsStore.
/// </summary>
public sealed class PostgresAdminStore(IConfiguration config, ILogger<PostgresAdminStore> logger) : IAdminDurableStore
{
    private readonly string _connString = config["POSTGRES_CONNECTION_STRING"]
                                          ?? throw new InvalidOperationException(
                                              "POSTGRES_CONNECTION_STRING environment variable is required");

    private bool _initialized;

    public async Task UpsertProjectAsync(Project project, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO admin_projects
(key, name, owner_user_id, status, members_count, runs_count, last_activity_utc, created_utc, updated_utc, created_by, updated_by)
VALUES (@k, @n, @owner, @status, @mc, @rc, @last, @created, @updated, @cb, @ub)
ON CONFLICT (key) DO UPDATE SET
    name = EXCLUDED.name,
    owner_user_id = EXCLUDED.owner_user_id,
    status = EXCLUDED.status,
    members_count = EXCLUDED.members_count,
    runs_count = EXCLUDED.runs_count,
    last_activity_utc = EXCLUDED.last_activity_utc,
    updated_utc = EXCLUDED.updated_utc,
    updated_by = EXCLUDED.updated_by;";
                cmd.Parameters.AddWithValue("@k", project.Key);
                cmd.Parameters.AddWithValue("@n", project.Name ?? string.Empty);
                cmd.Parameters.AddWithValue("@owner", (object?)project.OwnerUserId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@status", (int)project.Status);
                cmd.Parameters.AddWithValue("@mc", project.MembersCount);
                cmd.Parameters.AddWithValue("@rc", project.RunsCount);
                cmd.Parameters.AddWithValue("@last", (object?)project.LastActivityUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@created", project.CreatedUtc);
                cmd.Parameters.AddWithValue("@updated", project.UpdatedUtc);
                cmd.Parameters.AddWithValue("@cb", (object?)project.CreatedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ub", (object?)project.UpdatedBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] UpsertProject failed for {Key}", project.Key); }
            catch { }
        }
    }

    public async Task DeleteProjectAsync(string projectKey, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM admin_projects WHERE key = @k;";
                cmd.Parameters.AddWithValue("@k", projectKey);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] DeleteProject failed for {Key}", projectKey); }
            catch { }
        }
    }

    public async Task UpsertUserAsync(User user, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO admin_users
(id, username, email, role, status, projects_count, last_login_utc, created_utc, updated_utc, created_by, updated_by)
VALUES (@id, @username, @email, @role, @status, @pc, @last, @created, @updated, @cb, @ub)
ON CONFLICT (id) DO UPDATE SET
    username = EXCLUDED.username,
    email = EXCLUDED.email,
    role = EXCLUDED.role,
    status = EXCLUDED.status,
    projects_count = EXCLUDED.projects_count,
    last_login_utc = EXCLUDED.last_login_utc,
    updated_utc = EXCLUDED.updated_utc,
    updated_by = EXCLUDED.updated_by;";
                cmd.Parameters.AddWithValue("@id", user.Id);
                cmd.Parameters.AddWithValue("@username", user.Username ?? string.Empty);
                cmd.Parameters.AddWithValue("@email", (object?)user.Email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@role", (int)user.AccountRole);
                cmd.Parameters.AddWithValue("@status", (int)user.Status);
                cmd.Parameters.AddWithValue("@pc", user.ProjectsCount);
                cmd.Parameters.AddWithValue("@last", (object?)user.LastLoginUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@created", user.CreatedUtc);
                cmd.Parameters.AddWithValue("@updated", user.UpdatedUtc);
                cmd.Parameters.AddWithValue("@cb", (object?)user.CreatedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ub", (object?)user.UpdatedBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] UpsertUser failed for {Id}", user.Id); }
            catch { }
        }
    }

    public async Task UpsertMembershipAsync(Membership membership, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO admin_memberships
(project_key, user_id, role, created_utc, updated_utc, created_by, updated_by)
VALUES (@pk, @uid, @role, @created, @updated, @cb, @ub)
ON CONFLICT (project_key, user_id) DO UPDATE SET
    role = EXCLUDED.role,
    updated_utc = EXCLUDED.updated_utc,
    updated_by = EXCLUDED.updated_by;";
                cmd.Parameters.AddWithValue("@pk", membership.ProjectKey);
                cmd.Parameters.AddWithValue("@uid", membership.UserId);
                cmd.Parameters.AddWithValue("@role", (int)membership.Role);
                cmd.Parameters.AddWithValue("@created", membership.CreatedUtc);
                cmd.Parameters.AddWithValue("@updated", membership.UpdatedUtc);
                cmd.Parameters.AddWithValue("@cb", (object?)membership.CreatedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ub", (object?)membership.UpdatedBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogWarning(ex, "[admin-store][postgres] UpsertMembership failed for {Project}/{User}",
                    membership.ProjectKey, membership.UserId);
            }
            catch { }
        }
    }

    public async Task RemoveMembershipAsync(string projectKey, string userId, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM admin_memberships WHERE project_key=@pk AND user_id=@uid";
                cmd.Parameters.AddWithValue("@pk", projectKey);
                cmd.Parameters.AddWithValue("@uid", userId);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogWarning(ex, "[admin-store][postgres] RemoveMembership failed for {Project}/{User}",
                    projectKey, userId);
            }
            catch { }
        }
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Remove memberships first to satisfy FK constraints
                await using (var cmd1 = conn.CreateCommand())
                {
                    cmd1.CommandText = "DELETE FROM admin_memberships WHERE user_id=@uid";
                    cmd1.Parameters.AddWithValue("@uid", userId);
                    cmd1.Transaction = tx;
                    await cmd1.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "DELETE FROM admin_users WHERE id=@uid";
                    cmd2.Parameters.AddWithValue("@uid", userId);
                    cmd2.Transaction = tx;
                    await cmd2.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                try { await tx.RollbackAsync(ct); }
                catch { }

                throw;
            }
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] DeleteUser failed for {Id}", userId); }
            catch { }
        }
    }

    public async Task SaveSettingAsync(string key, string jsonValue, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO admin_settings (key, value, updated_at)
                    VALUES (@key, @value::jsonb, NOW())
                    ON CONFLICT (key)
                    DO UPDATE SET value = EXCLUDED.value, updated_at = NOW()";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", jsonValue);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] SaveSetting failed for key {Key}", key); }
            catch { }
        }
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value::text FROM admin_settings WHERE key=@key";
            cmd.Parameters.AddWithValue("@key", key);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result?.ToString();
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] GetSetting failed for key {Key}", key); }
            catch { }

            return null;
        }
    }

    public async Task CreateRememberMeTokenAsync(RememberMeToken token, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO remember_me_tokens (user_id, token_hash, created_at, expires_at, last_used_at)
                    VALUES (@userId, @tokenHash, @created, @expires, @lastUsed)";
                cmd.Parameters.AddWithValue("@userId", token.UserId);
                cmd.Parameters.AddWithValue("@tokenHash", token.TokenHash);
                cmd.Parameters.AddWithValue("@created", token.CreatedUtc);
                cmd.Parameters.AddWithValue("@expires", token.ExpiresUtc);
                cmd.Parameters.AddWithValue("@lastUsed", (object?)token.LastUsedUtc ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogWarning(ex, "[admin-store][postgres] CreateRememberMeToken failed for user {UserId}",
                    token.UserId);
            }
            catch { }
        }
    }

    public async Task<RememberMeToken?> GetRememberMeTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, user_id, token_hash, created_at, expires_at, last_used_at
                FROM remember_me_tokens
                WHERE token_hash = @tokenHash AND expires_at > @now";
            cmd.Parameters.AddWithValue("@tokenHash", tokenHash);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new RememberMeToken
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetString(1),
                    TokenHash = reader.GetString(2),
                    CreatedUtc = reader.GetDateTime(3),
                    ExpiresUtc = reader.GetDateTime(4),
                    LastUsedUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] GetRememberMeToken failed"); }
            catch { }

            return null;
        }
    }

    public async Task UpdateRememberMeTokenLastUsedAsync(int tokenId, DateTime lastUsedUtc,
        CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE remember_me_tokens SET last_used_at = @lastUsed WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", tokenId);
                cmd.Parameters.AddWithValue("@lastUsed", lastUsedUtc);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogWarning(ex,
                    "[admin-store][postgres] UpdateRememberMeTokenLastUsed failed for token {TokenId}", tokenId);
            }
            catch { }
        }
    }

    public async Task DeleteRememberMeTokenAsync(int tokenId, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM remember_me_tokens WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", tokenId);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogWarning(ex, "[admin-store][postgres] DeleteRememberMeToken failed for token {TokenId}",
                    tokenId);
            }
            catch { }
        }
    }

    public async Task DeleteAllRememberMeTokensForUserAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM remember_me_tokens WHERE user_id = @userId";
                cmd.Parameters.AddWithValue("@userId", userId);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogWarning(ex,
                    "[admin-store][postgres] DeleteAllRememberMeTokensForUser failed for user {UserId}", userId);
            }
            catch { }
        }
    }

    public async Task DeleteExpiredRememberMeTokensAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureCreatedAsync();
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM remember_me_tokens WHERE expires_at < @now";
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            try { logger.LogWarning(ex, "[admin-store][postgres] DeleteExpiredRememberMeTokens failed"); }
            catch { }
        }
    }

    private Task EnsureCreatedAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        // Migrations are now run once at application startup in HubServiceRunner.cs
        // No need to run migrations here

        _initialized = true;
        return Task.CompletedTask;
    }
}
