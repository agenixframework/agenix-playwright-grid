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

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using Prometheus;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

internal static class AdminProjectsUsersEndpoints
{
    // Prometheus metrics for admin domain (low cardinality only)
    private static readonly Counter MembershipChangesCounter = Prometheus.Metrics.CreateCounter(
        "hub_admin_membership_changes_total",
        "Total membership changes across all projects (labels: action=add|remove|role_update)",
        new CounterConfiguration { LabelNames = ["action"] });

    private static readonly Gauge ActiveProjectsGauge = Prometheus.Metrics.CreateGauge(
        "hub_admin_active_projects",
        "Current number of Active projects");

    private static readonly Gauge ActiveUsersGauge = Prometheus.Metrics.CreateGauge(
        "hub_admin_active_users",
        "Current number of Active users");

    // OpenTelemetry ActivitySource for tracing important admin operations
    private static readonly ActivitySource Activity = new("playwright-hub.admin");

    private static bool TryParseAccountRole(string? input, out AccountRole role)
    {
        role = AccountRole.User;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        // accept canonical labels
        if (Enum.TryParse<AccountRole>(s, true, out var ar))
        {
            role = ar;
            return true;
        }

        // accept legacy synonyms
        var sl = s.ToLowerInvariant();
        if (sl is "admin" or "administrator")
        {
            role = AccountRole.Administrator;
            return true;
        }

        if (sl is "viewer" or "user")
        {
            role = AccountRole.User;
            return true;
        }

        return false;
    }

    public static void MapProjectsUsersAdminEndpoints(WebApplication app)
    {
        var config = app.Configuration;
        var services = app.Services;
        var db = services.GetRequiredService<IDatabase>();
        var auditStore = services.GetRequiredService<IAuditStore>();
        var adminStore = services.GetService<IAdminDurableStore>();
        var logger = app.Logger;

        // Lightweight local auth: POST /admin/auth/login (public)
        // Verifies username/password against stored PBKDF2 hash for admin users (seeded via HUB_BOOTSTRAP_*).
        // Returns 200 { id, username, role, status } on success, or 401 on failure.
        app.MapPost("/admin/auth/login", async (HttpRequest req, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminLogin");
            var chunkedLogger = new ChunkedLogger(logger, "AdminLogin");

            using var operation = chunkedLogger.BeginOperation("AdminLogin");

            try
            {
                var doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                var id = doc != null && doc.TryGetValue("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? (idEl.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (string.IsNullOrEmpty(id) && doc != null && doc.TryGetValue("username", out var unEl) &&
                    unEl.ValueKind == JsonValueKind.String)
                {
                    id = (unEl.GetString() ?? string.Empty).Trim();
                }

                var password =
                    doc != null && doc.TryGetValue("password", out var pwEl) && pwEl.ValueKind == JsonValueKind.String
                        ? pwEl.GetString() ?? string.Empty
                        : string.Empty;

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(password))
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        "error=MissingCredentials");

                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["id"] = string.IsNullOrWhiteSpace(id) ? ["id or username is required"] : [],
                            ["password"] = string.IsNullOrEmpty(password) ? ["password is required"] : []
                        }.Where(x => x.Value.Length > 0).ToDictionary(x => x.Key, x => x.Value),
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.Authentication.LoginAttempt,
                    "id={Id}", id);

                // Resolve user by username/email only (do not allow login by internal id)
                var loginInput = id;
                var loginLower = loginInput.Trim().ToLowerInvariant();
                string? resolvedId = null;
                // Username mapping
                var byUsername = await db.StringGetAsync(RedisKeys.AdminUserByUsername(loginLower));
                if (!byUsername.IsNullOrEmpty)
                {
                    resolvedId = byUsername!;
                }

                // Email mapping
                if (resolvedId is null && loginLower.Contains('@'))
                {
                    var byEmail = await db.StringGetAsync(RedisKeys.AdminUserByEmail(loginLower));
                    if (!byEmail.IsNullOrEmpty)
                    {
                        resolvedId = byEmail!;
                    }
                }

                if (resolvedId is null)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        "error=UserNotFound loginInput={LoginInput}", id);
                    return ProblemDetailsHelpers.Unauthorized(
                        "Invalid username or password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var userJson = await db.StringGetAsync(RedisKeys.AdminUser(resolvedId));
                if (userJson.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        "error=UserDataMissing resolvedId={ResolvedId}", resolvedId);
                    return ProblemDetailsHelpers.Unauthorized(
                        "Invalid username or password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                User? user;
                try { user = JsonSerializer.Deserialize<User>(userJson!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        ex,
                        "error=UserDeserializationFailed resolvedId={ResolvedId}", resolvedId);
                    user = null;
                }

                if (user is null || user.Status != UserStatus.Active)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        "error=UserInactiveOrNull resolvedId={ResolvedId}", resolvedId);
                    return ProblemDetailsHelpers.Unauthorized(
                        "User account is inactive or not found",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var pwdJson = await db.StringGetAsync(RedisKeys.AdminUserPassword(resolvedId));
                if (pwdJson.IsNullOrEmpty)
                {
                    // Initialize password on first login for existing active users
                    try
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.AdminProjectsUsers.UserManagement.UserPasswordReset, // Using Reset for initialization
                            "action=InitializePasswordOnFirstLogin userId={UserId}", user.Id);

                        using var rng = RandomNumberGenerator.Create();
                        var saltInit = new byte[16];
                        rng.GetBytes(saltInit);
                        const int iterInit = 100_000;
                        using var pbkdf2 =
                            new Rfc2898DeriveBytes(password, saltInit, iterInit, HashAlgorithmName.SHA256);
                        var hashInit = pbkdf2.GetBytes(32);
                        var pwdInit = new
                        {
                            alg = "PBKDF2-SHA256",
                            iter = iterInit,
                            salt = Convert.ToBase64String(saltInit),
                            hash = Convert.ToBase64String(hashInit),
                            createdUtc = DateTime.UtcNow
                        };
                        await db.StringSetAsync(RedisKeys.AdminUserPassword(resolvedId),
                            JsonSerializer.Serialize(pwdInit));
                        try
                        {
                            await auditStore.AppendAsync(new AuditEntryDto
                            {
                                Timestamp = DateTime.Now,
                                Category = "admin",
                                Action = "user.password.initialized",
                                Actor = resolvedId,
                                Details = new Dictionary<string, string> { ["id"] = resolvedId }
                            });
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            ex,
                            "error=PasswordInitializationFailed userId={UserId}", user.Id);
                        return ProblemDetailsHelpers.Unauthorized(
                            "Authentication failed",
                            eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    // Re-read for verification path consistency
                    pwdJson = await db.StringGetAsync(RedisKeys.AdminUserPassword(resolvedId));
                }

                Dictionary<string, JsonElement>? pwd;
                try { pwd = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(pwdJson!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        ex,
                        "error=PasswordDataDeserializationFailed userId={UserId}", user.Id);
                    pwd = null;
                }

                if (pwd is null)
                {
                    return ProblemDetailsHelpers.Unauthorized(
                        "Invalid username or password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var alg = pwd.TryGetValue("alg", out var algEl) && algEl.ValueKind == JsonValueKind.String
                    ? algEl.GetString() ?? string.Empty
                    : string.Empty;
                var iter = pwd.TryGetValue("iter", out var itEl) && itEl.ValueKind == JsonValueKind.Number
                    ? itEl.GetInt32()
                    : 0;
                var saltB64 = pwd.TryGetValue("salt", out var saltEl) && saltEl.ValueKind == JsonValueKind.String
                    ? saltEl.GetString() ?? string.Empty
                    : string.Empty;
                var hashB64 = pwd.TryGetValue("hash", out var hashEl) && hashEl.ValueKind == JsonValueKind.String
                    ? hashEl.GetString() ?? string.Empty
                    : string.Empty;
                if (!string.Equals(alg, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase) || iter <= 0 ||
                    string.IsNullOrEmpty(saltB64) || string.IsNullOrEmpty(hashB64))
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        "error=InvalidPasswordFormat userId={UserId} alg={Alg}", user.Id, alg);
                    return ProblemDetailsHelpers.Unauthorized(
                        "Invalid username or password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                byte[] salt, expected;
                try
                {
                    salt = Convert.FromBase64String(saltB64);
                    expected = Convert.FromBase64String(hashB64);
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        ex,
                        "error=Base64DecodingFailed userId={UserId}", user.Id);
                    return ProblemDetailsHelpers.Unauthorized(
                        "Invalid username or password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                try
                {
                    using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256);
                    var computed = pbkdf2.GetBytes(expected.Length);
                    var ok = CryptographicOperations.FixedTimeEquals(computed, expected);
                    if (!ok)
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            "error=InvalidPassword userId={UserId}", user.Id);
                        return ProblemDetailsHelpers.Unauthorized(
                            "Invalid username or password",
                            eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        ex,
                        "error=Pbkdf2ExecutionFailed userId={UserId}", user.Id);
                    return ProblemDetailsHelpers.Unauthorized(
                        "Invalid username or password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Update last login (best-effort)
                try
                {
                    var updated = user with { LastLoginUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
                    await db.StringSetAsync(RedisKeys.AdminUser(user.Id), JsonSerializer.Serialize(updated));
                    if (adminStore != null)
                    {
                        try { await adminStore.UpsertUserAsync(updated); }
                        catch { }
                    }

                    try
                    {
                        await auditStore.AppendAsync(new AuditEntryDto
                        {
                            Timestamp = DateTime.Now,
                            Category = "admin",
                            Action = "user.login.success",
                            Actor = user.Id,
                            Details = new Dictionary<string, string> { ["id"] = user.Id }
                        });
                    }
                    catch { }
                }
                catch { }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.Authentication.LoginSucceeded,
                    "userId={UserId} username={Username} role={Role}",
                    user.Id, user.Username, user.AccountRole);

                operation.Complete();

                return Results.Ok(new
                {
                    id = user.Id,
                    username = user.Username,
                    accountRole = user.AccountRole.ToString(),
                    status = user.Status.ToString(),
                    role = user.AccountRole == AccountRole.Administrator ? "Admin" : "User"
                });
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication failed",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - change password
        app.MapPost("/admin/users/{id}/password", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Actor must be self or Admin
            var actor = req.Headers["x-user-id"].FirstOrDefault() ?? string.Empty;
            var isAdminActor = false;
            if (!string.IsNullOrWhiteSpace(actor))
            {
                try
                {
                    var ujson = await db.StringGetAsync(RedisKeys.AdminUser(actor));
                    if (!ujson.IsNullOrEmpty)
                    {
                        var u = JsonSerializer.Deserialize<User>(ujson!);
                        isAdminActor = u is not null && u.Status == UserStatus.Active &&
                                       u.AccountRole == AccountRole.Administrator;
                    }
                }
                catch { }
            }

            if (!isAdminActor && !string.Equals(actor, id, StringComparison.Ordinal))
            {
                return ProblemDetailsHelpers.Forbidden(
                    "Insufficient permissions to change password for this user",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed, // Use generic auth fail for now
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            Dictionary<string, JsonElement>? body;
            try { body = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
            catch
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var oldPassword =
                body != null && body.TryGetValue("oldPassword", out var oEl) && oEl.ValueKind == JsonValueKind.String
                    ? oEl.GetString() ?? string.Empty
                    : string.Empty;
            var newPassword =
                body != null && body.TryGetValue("newPassword", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString() ?? string.Empty
                    : string.Empty;
            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["oldPassword"] = string.IsNullOrEmpty(oldPassword) ? ["Required"] : Array.Empty<string>(),
                        ["newPassword"] = string.IsNullOrEmpty(newPassword) ? ["Required"] : Array.Empty<string>()
                    }.Where(x => x.Value.Length > 0).ToDictionary(x => x.Key, x => x.Value),
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            static bool MeetsComplexity(string s)
            {
                if (string.IsNullOrEmpty(s) || s.Length < 8)
                {
                    return false;
                }

                bool hasU = false, hasL = false, hasD = false, hasS = false;
                foreach (var ch in s)
                {
                    if (char.IsUpper(ch))
                    {
                        hasU = true;
                    }
                    else if (char.IsLower(ch))
                    {
                        hasL = true;
                    }
                    else if (char.IsDigit(ch))
                    {
                        hasD = true;
                    }
                    else
                    {
                        hasS = true; // any non-alnum considered special
                    }
                }

                return hasU && hasL && hasD && hasS;
            }

            if (!MeetsComplexity(newPassword))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["newPassword"] = ["Password must be at least 8 chars and include uppercase, lowercase, digit, and special symbol."]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Load current user and password hash
            var userJson = await db.StringGetAsync(RedisKeys.AdminUser(id));
            if (userJson.IsNullOrEmpty)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"User {id} not found",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted, // Use generic not found for user
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var pwdJson = await db.StringGetAsync(RedisKeys.AdminUserPassword(id));
            if (pwdJson.IsNullOrEmpty)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["oldPassword"] = ["Password not initialized for this user"]
                    },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            Dictionary<string, JsonElement>? pwd;
            try { pwd = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(pwdJson!); }
            catch { pwd = null; }

            if (pwd is null)
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "Corrupt password hash",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var alg = pwd.TryGetValue("alg", out var algEl) && algEl.ValueKind == JsonValueKind.String
                ? algEl.GetString() ?? string.Empty
                : string.Empty;
            var iter = pwd.TryGetValue("iter", out var itEl) && itEl.ValueKind == JsonValueKind.Number
                ? itEl.GetInt32()
                : 0;
            var saltB64 = pwd.TryGetValue("salt", out var saltEl) && saltEl.ValueKind == JsonValueKind.String
                ? saltEl.GetString() ?? string.Empty
                : string.Empty;
            var hashB64 = pwd.TryGetValue("hash", out var hashEl) && hashEl.ValueKind == JsonValueKind.String
                ? hashEl.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(alg, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase) || iter <= 0 ||
                string.IsNullOrEmpty(saltB64) || string.IsNullOrEmpty(hashB64))
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "Unsupported password hash format",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(saltB64);
                expected = Convert.FromBase64String(hashB64);
            }
            catch
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "Invalid password hash",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            try
            {
                using var pbkdf2 = new Rfc2898DeriveBytes(oldPassword, salt, iter, HashAlgorithmName.SHA256);
                var computed = pbkdf2.GetBytes(expected.Length);
                if (!CryptographicOperations.FixedTimeEquals(computed, expected))
                {
                    return ProblemDetailsHelpers.Unauthorized(
                        "Current password is incorrect",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }
            }
            catch
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Current password is incorrect",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Hash new password with new salt
            using var rng = RandomNumberGenerator.Create();
            var saltNew = new byte[16];
            rng.GetBytes(saltNew);
            const int iterNew = 100_000;
            using (var pbkdf2n = new Rfc2898DeriveBytes(newPassword, saltNew, iterNew, HashAlgorithmName.SHA256))
            {
                var hashNew = pbkdf2n.GetBytes(32);
                var payload = new
                {
                    alg = "PBKDF2-SHA256",
                    iter = iterNew,
                    salt = Convert.ToBase64String(saltNew),
                    hash = Convert.ToBase64String(hashNew),
                    updatedUtc = DateTime.UtcNow
                };
                await db.StringSetAsync(RedisKeys.AdminUserPassword(id), JsonSerializer.Serialize(payload));
            }

            // Audit
            try
            {
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "user.password.changed",
                    Actor = string.IsNullOrWhiteSpace(actor) ? "dashboard" : actor,
                    Details = new Dictionary<string, string> { ["id"] = id }
                });
            }
            catch { }

            return Results.Ok(new { ok = true });
        });

        // Users - admin reset password (admin only, does not require old password)
        app.MapPost("/admin/users/{id}/reset-password", async (HttpRequest req, string id, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserResetPassword");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserResetPassword");

            var actor = req.Headers["x-user-id"].FirstOrDefault() ?? string.Empty;
            using var operation = chunkedLogger.BeginOperation("AdminUserResetPassword", new Dictionary<string, object>
            {
                ["targetUserId"] = id,
                ["actorId"] = actor
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Authentication.LoginFailed, // Reusing ADM03 for general auth failure
                        "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Actor must be Admin
                var isAdminActor = false;
                if (!string.IsNullOrWhiteSpace(actor))
                {
                    try
                    {
                        var ujson = await db.StringGetAsync(RedisKeys.AdminUser(actor));
                        if (!ujson.IsNullOrEmpty)
                        {
                            var u = JsonSerializer.Deserialize<User>(ujson!);
                            isAdminActor = u is not null && u.Status == UserStatus.Active &&
                                           u.AccountRole == AccountRole.Administrator;
                        }
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogDebug(null, "Error checking actor role: {Message}", ex.Message);
                    }
                }

                if (!isAdminActor)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed, // ADM91
                        "error=Forbidden actorId={ActorId}", actor);
                    return ProblemDetailsHelpers.Forbidden(
                        "Insufficient permissions to reset password",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Dictionary<string, JsonElement>? body;
                try { body = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        ex,
                        "error=InvalidJson");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var newPassword =
                    body != null && body.TryGetValue("newPassword", out var nEl) && nEl.ValueKind == JsonValueKind.String
                        ? nEl.GetString() ?? string.Empty
                        : string.Empty;
                if (string.IsNullOrEmpty(newPassword))
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=MissingNewPassword");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["newPassword"] = ["Required"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                static bool MeetsComplexity(string s)
                {
                    if (string.IsNullOrEmpty(s) || s.Length < 8)
                    {
                        return false;
                    }

                    bool hasU = false, hasL = false, hasD = false, hasS = false;
                    foreach (var ch in s)
                    {
                        if (char.IsUpper(ch))
                        {
                            hasU = true;
                        }
                        else if (char.IsLower(ch))
                        {
                            hasL = true;
                        }
                        else if (char.IsDigit(ch))
                        {
                            hasD = true;
                        }
                        else
                        {
                            hasS = true; // any non-alnum considered special
                        }
                    }

                    return hasU && hasL && hasD && hasS;
                }

                if (!MeetsComplexity(newPassword))
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=PasswordComplexityNotMet");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["newPassword"] = ["Password must be at least 8 chars and include uppercase, lowercase, digit, and special symbol."]
                        },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Verify user exists
                var userJson = await db.StringGetAsync(RedisKeys.AdminUser(id));
                if (userJson.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.UserManagement.UserUpdated, // ADM22
                        "error=UserNotFound userId={UserId}", id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {id} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.Validation.ValidationSucceeded, // ADM92
                    "userId={UserId}", id);

                // Hash new password with new salt
                using var rng = RandomNumberGenerator.Create();
                var saltNew = new byte[16];
                rng.GetBytes(saltNew);
                const int iterNew = 100_000;
                using (var pbkdf2n = new Rfc2898DeriveBytes(newPassword, saltNew, iterNew, HashAlgorithmName.SHA256))
                {
                    var hashNew = pbkdf2n.GetBytes(32);
                    var payload = new
                    {
                        alg = "PBKDF2-SHA256",
                        iter = iterNew,
                        salt = Convert.ToBase64String(saltNew),
                        hash = Convert.ToBase64String(hashNew),
                        updatedUtc = DateTime.UtcNow
                    };
                    await db.StringSetAsync(RedisKeys.AdminUserPassword(id), JsonSerializer.Serialize(payload));
                }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.UserManagement.UserPasswordReset, // ADM26
                    "userId={UserId} actorId={ActorId}", id, actor);

                // Audit
                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "user.password.reset",
                        Actor = actor,
                        Details = new Dictionary<string, string> { ["id"] = id, ["resetBy"] = actor }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred during password reset",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserPasswordReset,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Admin metrics: periodic refresh of active projects/users gauges (low overhead)
        try
        {
            var metricsRefreshSeconds =
                int.TryParse(config["ADMIN_METRICS_REFRESH_SECONDS"], out var s) ? Math.Max(5, s) : 15;
            var cts = new CancellationTokenSource();
            app.Lifetime.ApplicationStopping.Register(() => cts.Cancel());
            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        // Active projects
                        var projectKeys = await db.SetMembersAsync(RedisKeys.AdminProjectsSet());
                        var activeProjects = 0;
                        foreach (var pk in projectKeys)
                        {
                            var pj = await db.StringGetAsync(RedisKeys.AdminProject(pk!));
                            if (pj.IsNullOrEmpty)
                            {
                                continue;
                            }

                            try
                            {
                                var p = JsonSerializer.Deserialize<Project>(pj!);
                                if (p != null && p.Status == ProjectStatus.Active)
                                {
                                    activeProjects++;
                                }
                            }
                            catch { }
                        }

                        ActiveProjectsGauge.Set(activeProjects);

                        // Active users
                        var userIds = await db.SetMembersAsync(RedisKeys.AdminUsersSet());
                        var activeUsers = 0;
                        foreach (var uid in userIds)
                        {
                            var uj = await db.StringGetAsync(RedisKeys.AdminUser(uid!));
                            if (uj.IsNullOrEmpty)
                            {
                                continue;
                            }

                            try
                            {
                                var u = JsonSerializer.Deserialize<User>(uj!);
                                if (u != null && u.Status == UserStatus.Active)
                                {
                                    activeUsers++;
                                }
                            }
                            catch { }
                        }

                        ActiveUsersGauge.Set(activeUsers);
                    }
                    catch (Exception ex)
                    {
                        try { logger.LogDebug(ex, "[admin-metrics] refresh failed"); }
                        catch { }
                    }

                    try { await Task.Delay(TimeSpan.FromSeconds(metricsRefreshSeconds), cts.Token); }
                    catch (TaskCanceledException) { break; }
                }
            }, cts.Token);
        }
        catch { }

        bool CheckAuthentication(HttpRequest req)
        {
            // RBAC: Verify authenticated Global Admin via x-user-id header
            var uid = req.Headers["x-user-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(uid))
            {
                uid = req.Headers["x-dashboard-user"].FirstOrDefault();
            }

            uid = (uid ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            try
            {
                var json = db.StringGet(RedisKeys.AdminUser(uid));
                if (json.IsNullOrEmpty)
                {
                    return false;
                }

                var user = JsonSerializer.Deserialize<User>(json!);
                return user is not null && user.Status == UserStatus.Active &&
                       user.AccountRole == AccountRole.Administrator;
            }
            catch { return false; }
        }


        // Bootstrap: seed default admin user/project if enabled
        try
        {
            var bootstrapRaw = config["AGENIX_HUB_BOOTSTRAP_ENABLED"];
            var bootstrapEnabled = string.IsNullOrWhiteSpace(bootstrapRaw) ||
                                   string.Equals(bootstrapRaw, "1", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(bootstrapRaw, "true", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(bootstrapRaw, "yes", StringComparison.OrdinalIgnoreCase);

            if (bootstrapEnabled)
            {
                _ = Task.Run(async () =>
                {
                    var chunkedLogger = new ChunkedLogger(logger, "bootstrap");
                    using var op = chunkedLogger.BeginOperation("System Bootstrap");

                    try
                    {
                        var adminId = (config["AGENIX_HUB_BOOTSTRAP_ADMIN_USER"] ?? "admin").Trim();
                        var adminPassword = config["AGENIX_HUB_BOOTSTRAP_ADMIN_PASSWORD"] ?? "agenix-admin";
                        var projectKey = (config["AGENIX_HUB_BOOTSTRAP_DEFAULT_PROJECT"] ?? "admin_default").Trim();
                        var adminEmail =
                            (config["AGENIX_HUB_BOOTSTRAP_ADMIN_EMAIL"] ?? "agenix.admin@domain.com").Trim();

                        if (!AdminValidation.TryValidateUserId(adminId, out _))
                        {
                            chunkedLogger.LogWarning(EventCodes.System.BootstrapFailed,
                                "Invalid HUB_BOOTSTRAP_ADMIN_USER '{AdminId}' – skipping bootstrap",
                                adminId);

                            return;
                        }

                        if (!AdminValidation.TryValidateProjectKey(projectKey, out _))
                        {
                            chunkedLogger.LogWarning(EventCodes.System.BootstrapFailed,
                                "Invalid HUB_BOOTSTRAP_DEFAULT_PROJECT '{ProjectKey}' – skipping project bootstrap",
                                projectKey);

                            return;
                        }

                        if (string.IsNullOrWhiteSpace(adminEmail) ||
                            !AdminValidation.TryValidateEmail(adminEmail, out _))
                        {
                            chunkedLogger.LogWarning(EventCodes.System.BootstrapFailed,
                                "HUB_BOOTSTRAP_ADMIN_EMAIL is required and must be a valid email – skipping bootstrap");

                            return;
                        }

                        var now = DateTime.UtcNow;

                        // Seed admin user
                        var userKey = RedisKeys.AdminUser(adminId);
                        if (!await db.KeyExistsAsync(userKey))
                        {
                            var emailLower = adminEmail.ToLowerInvariant();
                            var existingByEmail = await db.StringGetAsync(RedisKeys.AdminUserByEmail(emailLower));
                            if (!existingByEmail.IsNullOrEmpty && !string.Equals(existingByEmail.ToString(), adminId,
                                    StringComparison.Ordinal))
                            {
                                chunkedLogger.LogWarning(EventCodes.System.BootstrapFailed,
                                    "Email '{AdminEmail}' already associated with user '{ExistingUserId}' – skipping bootstrap",
                                    adminEmail, existingByEmail.ToString());

                                return;
                            }

                            var user = new User
                            {
                                Id = adminId,
                                Username = adminId,
                                Email = adminEmail,
                                AccountRole = AccountRole.Administrator,
                                Status = UserStatus.Active,
                                ProjectsCount = 0,
                                LastLoginUtc = null,
                                CreatedUtc = now,
                                UpdatedUtc = now,
                                CreatedBy = "bootstrap",
                                UpdatedBy = "bootstrap"
                            };
                            var ujson = JsonSerializer.Serialize(user);
                            var tran = db.CreateTransaction();
                            _ = tran.StringSetAsync(userKey, ujson);
                            _ = tran.SetAddAsync(RedisKeys.AdminUsersSet(), adminId);
                            _ = tran.StringSetAsync(RedisKeys.AdminUserByEmail(emailLower), adminId);
                            var usernameLower = adminId.ToLowerInvariant();
                            _ = tran.StringSetAsync(RedisKeys.AdminUserByUsername(usernameLower), adminId);
                            var ok = await tran.ExecuteAsync();
                            if (ok)
                            {
                                try
                                {
                                    if (adminStore != null)
                                    {
                                        await adminStore.UpsertUserAsync(user);
                                    }
                                }
                                catch { }

                                try
                                {
                                    await auditStore.AppendAsync(new AuditEntryDto
                                    {
                                        Timestamp = now,
                                        Category = "admin",
                                        Action = "bootstrap.user.created",
                                        Actor = "bootstrap",
                                        Details = new Dictionary<string, string>
                                        {
                                            ["id"] = adminId,
                                            ["email"] = adminEmail
                                        }
                                    });
                                }
                                catch { }

                                // Store password hash
                                try
                                {
                                    using var rng = RandomNumberGenerator.Create();
                                    var salt = new byte[16];
                                    rng.GetBytes(salt);
                                    const int iter = 100_000;
                                    using var pbkdf2 = new Rfc2898DeriveBytes(adminPassword, salt, iter,
                                        HashAlgorithmName.SHA256);
                                    var hash = pbkdf2.GetBytes(32);
                                    var pwd = new
                                    {
                                        alg = "PBKDF2-SHA256",
                                        iter,
                                        salt = Convert.ToBase64String(salt),
                                        hash = Convert.ToBase64String(hash),
                                        createdUtc = now
                                    };
                                    var pjson = JsonSerializer.Serialize(pwd);
                                    await db.StringSetAsync(RedisKeys.AdminUserPassword(adminId), pjson);
                                }
                                catch { }
                            }
                        }

                        // Ensure username mapping exists even if user already present (backfill)
                        try
                        {
                            var usernameLower = adminId.ToLowerInvariant();
                            var existingUsernameMap =
                                await db.StringGetAsync(RedisKeys.AdminUserByUsername(usernameLower));
                            if (existingUsernameMap.IsNullOrEmpty)
                            {
                                await db.StringSetAsync(RedisKeys.AdminUserByUsername(usernameLower), adminId);
                            }
                        }
                        catch { }

                        // Seed default project
                        var projKey = RedisKeys.AdminProject(projectKey);
                        if (!await db.KeyExistsAsync(projKey))
                        {
                            var proj = new Project
                            {
                                Key = projectKey,
                                Name = projectKey,
                                OwnerUserId = adminId,
                                Status = ProjectStatus.Active,
                                MembersCount = 0,
                                RunsCount = 0,
                                LastActivityUtc = null,
                                CreatedUtc = now,
                                UpdatedUtc = now,
                                CreatedBy = "bootstrap",
                                UpdatedBy = "bootstrap"
                            };
                            var pjson = JsonSerializer.Serialize(proj);
                            var tran2 = db.CreateTransaction();
                            _ = tran2.StringSetAsync(projKey, pjson);
                            _ = tran2.SetAddAsync(RedisKeys.AdminProjectsSet(), projectKey);
                            _ = tran2.StringSetAsync(RedisKeys.AdminProjectByName(projectKey.ToLowerInvariant()),
                                projectKey);
                            var ok2 = await tran2.ExecuteAsync();
                            if (ok2)
                            {
                                try
                                {
                                    if (adminStore != null)
                                    {
                                        await adminStore.UpsertProjectAsync(proj);
                                    }
                                }
                                catch { }

                                try
                                {
                                    await auditStore.AppendAsync(new AuditEntryDto
                                    {
                                        Timestamp = now,
                                        Category = "admin",
                                        Action = "bootstrap.project.created",
                                        Actor = "bootstrap",
                                        Details = new Dictionary<string, string> { ["key"] = projectKey }
                                    });
                                }
                                catch { }
                            }
                        }

                        // Ensure membership admin in project as Project Admin
                        var membershipKey = RedisKeys.AdminMembership(projectKey, adminId);
                        if (await db.KeyExistsAsync(RedisKeys.AdminProject(projectKey)) &&
                            await db.KeyExistsAsync(RedisKeys.AdminUser(adminId)) &&
                            !await db.KeyExistsAsync(membershipKey))
                        {
                            var membership = new Membership
                            {
                                ProjectKey = projectKey,
                                UserId = adminId,
                                Role = ProjectRole.ProjectLead,
                                CreatedUtc = now,
                                UpdatedUtc = now,
                                CreatedBy = "bootstrap",
                                UpdatedBy = "bootstrap"
                            };
                            var mjson = JsonSerializer.Serialize(membership);
                            var tran3 = db.CreateTransaction();
                            _ = tran3.StringSetAsync(membershipKey, mjson);
                            _ = tran3.SetAddAsync(RedisKeys.AdminMembersByProject(projectKey), adminId);
                            _ = tran3.SetAddAsync(RedisKeys.AdminProjectsByUser(adminId), projectKey);
                            var ok3 = await tran3.ExecuteAsync();
                            if (ok3)
                            {
                                try
                                {
                                    if (adminStore != null)
                                    {
                                        await adminStore.UpsertMembershipAsync(membership);
                                    }
                                }
                                catch { }

                                try
                                {
                                    await auditStore.AppendAsync(new AuditEntryDto
                                    {
                                        Timestamp = now,
                                        Category = "admin",
                                        Action = "bootstrap.membership.created",
                                        Actor = "bootstrap",
                                        Details = new Dictionary<string, string>
                                        {
                                            ["project"] = projectKey,
                                            ["user"] = adminId
                                        }
                                    });
                                }
                                catch { }
                            }
                        }

                        // Initialize default retention settings for bootstrap project (if not exists)
                        try
                        {
                            var settingsKey = $"project:{projectKey}:settings";

                            // Only initialize if settings don't exist (prevents overwriting test/custom values)
                            if (!await db.KeyExistsAsync(settingsKey))
                            {
                                var retentionSettings = new
                                {
                                    launchInactivityTimeout = "1d",
                                    keepLaunches = "30",
                                    keepLogs = "7",
                                    keepAttachments = "7",
                                    keepAudit = "90"
                                };
                                var settingsJson = JsonSerializer.Serialize(retentionSettings);
                                await db.StringSetAsync(settingsKey, settingsJson);
                                chunkedLogger.LogMilestone(EventCodes.Generic,
                                    "Initialized retention settings for project {ProjectKey}",
                                    projectKey);
                            }
                            else
                            {
                                chunkedLogger.LogDebug(null,
                                    "Settings already exist for project {ProjectKey}, skipping initialization",
                                    projectKey);
                            }
                        }
                        catch (Exception)
                        {
                            chunkedLogger.LogWarning(EventCodes.Generic,
                                "Failed to initialize retention settings for project {ProjectKey}",
                                projectKey);
                        }

                        chunkedLogger.LogMilestone(EventCodes.System.BootstrapCompleted,
                            "Completed (user={AdminId}, project={ProjectKey})",
                            adminId, projectKey);
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogError(ex, EventCodes.System.BootstrapFailed, "Failed during seeding");
                    }
                });
            }
        }
        catch { }

        // Projects - list
        app.MapGet("/admin/projects", async (HttpRequest req, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectList");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectList");

            using var operation = chunkedLogger.BeginOperation("AdminProjectList");

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                int.TryParse(req.Query["skip"], out var skip);
                int.TryParse(req.Query["take"], out var take);
                if (take <= 0)
                {
                    take = 50;
                }

                var q = (req.Query["q"].FirstOrDefault() ?? string.Empty).Trim();
                var statusFilter = req.Query["status"].FirstOrDefault();
                var ownerFilter = (req.Query["owner"].FirstOrDefault() ?? string.Empty).Trim();
                var sort = (req.Query["sort"].FirstOrDefault() ?? "key").Trim().ToLowerInvariant();
                var order = (req.Query["order"].FirstOrDefault() ?? "asc").Trim().ToLowerInvariant();

                var members = await db.SetMembersAsync(RedisKeys.AdminProjectsSet());
                var list = new List<Project>(members.Length);
                foreach (var m in members)
                {
                    var json = await db.StringGetAsync(RedisKeys.AdminProject(m!));
                    if (json.IsNullOrEmpty)
                    {
                        continue;
                    }

                    try
                    {
                        var proj = JsonSerializer.Deserialize<Project>(json!);
                        if (proj is null)
                        {
                            continue;
                        }

                        list.Add(proj);
                    }
                    catch { }
                }

                // Fallback: if the projects set is empty (e.g., upgraded instance or external writes), scan keys
                if (list.Count == 0)
                {
                    try
                    {
                        var result = await db.ExecuteAsync("KEYS", "admin:project:*");
                        string[] keys;
                        if (result.IsNull)
                        {
                            keys = Array.Empty<string>();
                        }
                        else
                        {
                            try { keys = (string[])result!; }
                            catch { keys = Array.Empty<string>(); }
                        }

                        foreach (var k in keys)
                        {
                            var kk = k ?? string.Empty;
                            if (string.IsNullOrEmpty(kk) ||
                                kk.StartsWith("admin:project:byName:", StringComparison.Ordinal))
                            {
                                continue; // skip secondary index keys
                            }

                            var json = await db.StringGetAsync(kk);
                            if (json.IsNullOrEmpty)
                            {
                                continue;
                            }

                            try
                            {
                                var proj = JsonSerializer.Deserialize<Project>(json!);
                                if (proj != null)
                                {
                                    list.Add(proj);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // server-side filtering
                IEnumerable<Project> filtered = list;
                if (!string.IsNullOrEmpty(q))
                {
                    filtered = filtered.Where(p => (p.Key?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                                   (p.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                if (!string.IsNullOrWhiteSpace(statusFilter) &&
                    Enum.TryParse<ProjectStatus>(statusFilter, true, out var ps))
                {
                    filtered = filtered.Where(p => p.Status == ps);
                }

                if (!string.IsNullOrEmpty(ownerFilter))
                {
                    filtered = filtered.Where(p =>
                        string.Equals(p.OwnerUserId ?? string.Empty, ownerFilter, StringComparison.OrdinalIgnoreCase));
                }

                // enrich: members count (derive from membership set length) and launches count
                var filteredList = filtered.ToList();

                // Prepare launches count query (batch all projects)
                var launchesCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (filteredList.Count > 0)
                {
                    try
                    {
                        var postgresConn = config["POSTGRES_CONNECTION_STRING"];
                        if (string.IsNullOrEmpty(postgresConn))
                        {
                            throw new InvalidOperationException(
                                "POSTGRES_CONNECTION_STRING environment variable is required");
                        }

                        await using var conn = new NpgsqlConnection(postgresConn);
                        await conn.OpenAsync();

                        // Query launches count grouped by project_key
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT project_key, COUNT(*) as cnt FROM launches GROUP BY project_key";
                        await using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            var projectKey = reader.GetString(0);
                            var count = Convert.ToInt32(reader.GetInt64(1));
                            launchesCounts[projectKey] = count;
                        }
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogWarning(null, "Failed to query launches counts: {Message}", ex.Message);
                    }
                }

                for (var i = 0; i < filteredList.Count; i++)
                {
                    var p = filteredList[i];
                    try
                    {
                        var mc = (int)await db.SetLengthAsync(RedisKeys.AdminMembersByProject(p.Key));
                        var rc = launchesCounts.TryGetValue(p.Key, out var cnt) ? cnt : 0;
                        filteredList[i] = p with { MembersCount = mc, RunsCount = rc };
                    }
                    catch { }
                }

                // sorting
                Func<Project, object?> keySelector = sort switch
                {
                    "name" => p => p.Name,
                    "status" => p => p.Status,
                    "lastactivity" => p => p.LastActivityUtc ?? DateTime.MinValue,
                    "members" => p => p.MembersCount,
                    "runs" => p => p.RunsCount,
                    _ => p => p.Key
                };
                IEnumerable<Project> ordered = order == "desc"
                    ? filteredList.OrderByDescending(keySelector)
                    : filteredList.OrderBy(keySelector);

                var total = filteredList.Count;
                var page = ordered.Skip(skip).Take(take).ToArray();

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.ProjectListRetrieved,
                    "total={TotalCount} skip={Skip} take={Take} q={Query}", total, skip, take, q);

                operation.Complete();
                return Results.Ok(new { total, items = page });
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while listing projects",
                    eventCode: EventCodes.AdminProjectsUsers.Query.ProjectListRetrieved,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Projects - get
        app.MapGet("/admin/projects/{key}", async (HttpRequest req, string key, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectDetails");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectDetails");

            using var operation = chunkedLogger.BeginOperation("AdminProjectDetails", new Dictionary<string, object>
            {
                ["projectKey"] = key
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var json = await db.StringGetAsync(RedisKeys.AdminProject(key));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.ProjectDetailsRetrieved, "status=NotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted, // Use generic project error for not found
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.ProjectDetailsRetrieved, "projectKey={ProjectKey}", key);

                operation.Complete();
                return Results.Text(json!, "application/json");
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while retrieving project details",
                    eventCode: EventCodes.AdminProjectsUsers.Query.ProjectDetailsRetrieved,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Projects - create
        app.MapPost("/admin/projects", async (HttpRequest req, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectCreate");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectCreate");

            using var operation = chunkedLogger.BeginOperation("AdminProjectCreate");

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Project? body;
                try
                {
                    body = await req.ReadFromJsonAsync<Project>();
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=InvalidJson");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (body is null)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=MissingBody");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Missing request body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!AdminValidation.TryValidateProjectKey(body.Key, out var err))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=InvalidProjectKey key={ProjectKey} error={ErrorMessage}", body.Key, err);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["key"] = [err] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!AdminValidation.TryValidateProjectName(body.Name, out err))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=InvalidProjectName name={ProjectName} error={ErrorMessage}", body.Name, err);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["name"] = [err] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var act = Activity.StartActivity("admin.project.create");
                var now = DateTime.UtcNow;
                var key = body.Key.Trim();
                act?.SetTag("project.key", key);
                act?.SetTag("project.name", body.Name ?? string.Empty);
                var nameLower = (body.Name ?? string.Empty).Trim().ToLowerInvariant();
                var projKey = RedisKeys.AdminProject(key);
                if (await db.KeyExistsAsync(projKey))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectAlreadyExists projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.Conflict(
                        $"Project with key '{key}' already exists",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Ensure unique project name (case-insensitive)
                var nameIdxKey = RedisKeys.AdminProjectByName(nameLower);
                var existingByName = await db.StringGetAsync(nameIdxKey);
                if (!existingByName.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectNameAlreadyExists name={ProjectName}", body.Name);
                    return ProblemDetailsHelpers.Conflict(
                        $"Project with name '{body.Name}' already exists",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var proj = body with { Status = body.Status, CreatedUtc = now, UpdatedUtc = now };
                var json = JsonSerializer.Serialize(proj);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(projKey, json);
                _ = tran.SetAddAsync(RedisKeys.AdminProjectsSet(), key);
                _ = tran.StringSetAsync(nameIdxKey, key);
                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated, "status=TransactionFailed");
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to create project in database",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated, // ADM31
                    "projectKey={ProjectKey} name={Name}", key, proj.Name);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = now,
                        Category = "admin",
                        Action = "project.created",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["key"] = key, ["name"] = proj.Name! }
                    });
                }
                catch { }

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertProjectAsync(proj);
                    }
                }
                catch { }

                // Initialize default retention settings for new project (if not exists)
                try
                {
                    var settingsKey = $"project:{key}:settings";

                    // Only initialize if settings don't exist (prevents overwriting test/custom values)
                    if (!await db.KeyExistsAsync(settingsKey))
                    {
                        var retentionSettings = new
                        {
                            launchInactivityTimeout = "1d",
                            keepLaunches = "30",
                            keepLogs = "7",
                            keepAttachments = "7",
                            keepAudit = "90"
                        };
                        var settingsJson = JsonSerializer.Serialize(retentionSettings);
                        await db.StringSetAsync(settingsKey, settingsJson);
                        chunkedLogger.LogMilestone(EventCodes.ProjectSettings.SettingsPersisted, "projectKey={ProjectKey} action=InitializeDefaults", key);
                    }
                }
                catch { }

                operation.Complete();
                return Results.Ok(proj);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while creating project",
                    eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Projects - patch (name/status/owner)
        app.MapPatch("/admin/projects/{key}", async (HttpRequest req, string key, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectUpdate");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectUpdate");

            using var operation = chunkedLogger.BeginOperation("AdminProjectUpdate", new Dictionary<string, object>
            {
                ["projectKey"] = key
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var act = Activity.StartActivity("admin.project.update");
                act?.SetTag("project.key", key);
                var json = await db.StringGetAsync(RedisKeys.AdminProject(key));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectNotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Project? proj;
                try { proj = JsonSerializer.Deserialize<Project>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=ProjectDeserializationFailed projectKey={ProjectKey}", key);
                    proj = null;
                }

                if (proj is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt project data",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectUpdated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var patch = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>() ??
                            new Dictionary<string, JsonElement>();
                var name = proj.Name;
                var status = proj.Status;
                var owner = proj.OwnerUserId;

                if (patch.TryGetValue("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    var candidate = nameEl.GetString() ?? string.Empty;
                    if (!AdminValidation.TryValidateProjectName(candidate, out var err))
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=InvalidProjectName name={ProjectName} error={ErrorMessage}", candidate, err);
                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]> { ["name"] = [err] },
                            eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    name = candidate;
                }

                if (patch.TryGetValue("status", out var stEl) && stEl.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<ProjectStatus>(stEl.GetString(), true, out var ps))
                {
                    status = ps;
                }

                if (patch.TryGetValue("ownerUserId", out var ownerEl) && ownerEl.ValueKind == JsonValueKind.String)
                {
                    owner = (ownerEl.GetString() ?? string.Empty).Trim();
                }

                var updated = proj with { Name = name, Status = status, OwnerUserId = owner, UpdatedUtc = DateTime.UtcNow };
                var updatedJson = JsonSerializer.Serialize(updated);

                // If name changed, enforce uniqueness and update secondary index atomically
                var nameChanged = !string.Equals(proj.Name, name, StringComparison.Ordinal);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(RedisKeys.AdminProject(key), updatedJson);
                if (nameChanged)
                {
                    var oldLower = (proj.Name ?? string.Empty).Trim().ToLowerInvariant();
                    var newLower = (name ?? string.Empty).Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(newLower))
                    {
                        var existing = await db.StringGetAsync(RedisKeys.AdminProjectByName(newLower));
                        if (!existing.IsNullOrEmpty && !string.Equals(existing.ToString(), key, StringComparison.Ordinal))
                        {
                            chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectNameAlreadyExists name={ProjectName}", name);
                            return ProblemDetailsHelpers.Conflict(
                                $"Project with name '{name}' already exists",
                                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                                instance: req.Path,
                                traceId: req.HttpContext.TraceIdentifier);
                        }

                        _ = tran.StringSetAsync(RedisKeys.AdminProjectByName(newLower), key);
                    }

                    if (!string.IsNullOrEmpty(oldLower))
                    {
                        _ = tran.KeyDeleteAsync(RedisKeys.AdminProjectByName(oldLower));
                    }
                }

                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectUpdated, "status=TransactionFailed projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to update project in database",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectUpdated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectUpdated, // ADM32
                    "projectKey={ProjectKey} name={Name} status={Status} ownerUserId={OwnerUserId}", key, updated.Name, updated.Status, updated.OwnerUserId);

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertProjectAsync(updated);
                    }
                }
                catch { }

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "project.updated",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["key"] = key }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while updating project",
                    eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectUpdated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Projects - archive (soft)
        app.MapPost("/admin/projects/{key}/archive", async (HttpRequest req, string key, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectArchive");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectArchive");

            using var operation = chunkedLogger.BeginOperation("AdminProjectArchive", new Dictionary<string, object>
            {
                ["projectKey"] = key
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var act = Activity.StartActivity("admin.project.archive");
                act?.SetTag("project.key", key);
                var json = await db.StringGetAsync(RedisKeys.AdminProject(key));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=ProjectNotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Project? proj;
                try { proj = JsonSerializer.Deserialize<Project>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        ex,
                        "error=ProjectDeserializationFailed projectKey={ProjectKey}", key);
                    proj = null;
                }

                if (proj is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt project data",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectArchived,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (proj.Status == ProjectStatus.Archived)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.ProjectManagement.ProjectArchived,
                        "status=AlreadyArchived projectKey={ProjectKey}", key);
                    operation.Complete();
                    return Results.Ok(proj);
                }

                var updated = proj with { Status = ProjectStatus.Archived, UpdatedUtc = DateTime.UtcNow };
                var updatedJson = JsonSerializer.Serialize(updated);
                await db.StringSetAsync(RedisKeys.AdminProject(key), updatedJson);
                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertProjectAsync(updated);
                    }
                }
                catch { }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.ProjectManagement.ProjectArchived, // ADM34
                    "projectKey={ProjectKey} name={Name}", key, proj.Name);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "project.archived",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["key"] = key }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while archiving project",
                    eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectArchived,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Projects - restore (from archived/disabled to active)
        app.MapPost("/admin/projects/{key}/restore", async (HttpRequest req, string key, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectRestore");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectRestore");

            using var operation = chunkedLogger.BeginOperation("AdminProjectRestore", new Dictionary<string, object>
            {
                ["projectKey"] = key
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var act = Activity.StartActivity("admin.project.restore");
                act?.SetTag("project.key", key);
                var json = await db.StringGetAsync(RedisKeys.AdminProject(key));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectNotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Project? proj;
                try { proj = JsonSerializer.Deserialize<Project>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=ProjectDeserializationFailed projectKey={ProjectKey}", key);
                    proj = null;
                }

                if (proj is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt project data",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectRestored,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (proj.Status == ProjectStatus.Active)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectRestored, "status=AlreadyActive projectKey={ProjectKey}", key);
                    operation.Complete();
                    return Results.Ok(proj);
                }

                var updated = proj with { Status = ProjectStatus.Active, UpdatedUtc = DateTime.UtcNow };
                var updatedJson = JsonSerializer.Serialize(updated);
                await db.StringSetAsync(RedisKeys.AdminProject(key), updatedJson);
                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertProjectAsync(updated);
                    }
                }
                catch { }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectRestored, // ADM35
                    "projectKey={ProjectKey} name={Name}", key, proj.Name);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "project.restored",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["key"] = key }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while restoring project",
                    eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectRestored,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Projects - hard delete (permanently remove)
        app.MapDelete("/admin/projects/{key}", async (HttpRequest req, string key, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminProjectDelete");
            var chunkedLogger = new ChunkedLogger(logger, "AdminProjectDelete");

            using var operation = chunkedLogger.BeginOperation("AdminProjectDelete", new Dictionary<string, object>
            {
                ["projectKey"] = key
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var act = Activity.StartActivity("admin.project.delete");
                act?.SetTag("project.key", key);

                // Get project to verify it exists
                var json = await db.StringGetAsync(RedisKeys.AdminProject(key));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectNotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Project? proj;
                try { proj = JsonSerializer.Deserialize<Project>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=ProjectDeserializationFailed projectKey={ProjectKey}", key);
                    proj = null;
                }

                if (proj is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt project data",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Get project name for index cleanup
                var nameLower = proj.Name.ToLowerInvariant();
                var nameIdxKey = RedisKeys.AdminProjectByName(nameLower);

                // Delete project data using transaction
                var tran = db.CreateTransaction();
                _ = tran.KeyDeleteAsync(RedisKeys.AdminProject(key));
                _ = tran.SetRemoveAsync(RedisKeys.AdminProjectsSet(), key);
                _ = tran.KeyDeleteAsync(nameIdxKey);

                // Get all members to clean up their project associations
                var memberIds = await db.SetMembersAsync(RedisKeys.AdminMembersByProject(key));
                foreach (var memberId in memberIds)
                {
                    var userId = memberId.ToString();
                    _ = tran.KeyDeleteAsync(RedisKeys.AdminMembership(key, userId));
                    _ = tran.SetRemoveAsync(RedisKeys.AdminProjectsByUser(userId), key);
                }

                _ = tran.KeyDeleteAsync(RedisKeys.AdminMembersByProject(key));

                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted, "status=TransactionFailed");
                    act?.SetTag("delete.ok", false);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to delete project from database",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted, // ADM33
                    "projectKey={ProjectKey} name={Name} membersCount={MembersCount}", key, proj.Name, memberIds.Length);

                act?.SetTag("delete.ok", true);

                // Delete from durable store
                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.DeleteProjectAsync(key);
                    }
                }
                catch { }

                // Clean up memberships from durable store
                foreach (var memberId in memberIds)
                {
                    var userId = memberId.ToString();
                    try
                    {
                        if (adminStore != null)
                        {
                            await adminStore.RemoveMembershipAsync(key, userId);
                        }
                    }
                    catch { }
                }

                // Audit log
                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "project.deleted",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string>
                        {
                            ["key"] = key,
                            ["name"] = proj.Name,
                            ["membersCount"] = memberIds.Length.ToString()
                        }
                    });
                }
                catch { }

                operation.Complete();
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while deleting project",
                    eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - list
        app.MapGet("/admin/users", async (HttpRequest req, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserList");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserList");

            using var operation = chunkedLogger.BeginOperation("AdminUserList");

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                int.TryParse(req.Query["skip"], out var skip);
                int.TryParse(req.Query["take"], out var take);
                if (take <= 0)
                {
                    take = 50;
                }

                var q = (req.Query["q"].FirstOrDefault() ?? string.Empty).Trim();
                var statusFilter = req.Query["status"].FirstOrDefault();
                var roleFilter = req.Query["role"].FirstOrDefault();
                var sort = (req.Query["sort"].FirstOrDefault() ?? "username").Trim().ToLowerInvariant();
                var order = (req.Query["order"].FirstOrDefault() ?? "asc").Trim().ToLowerInvariant();

                var members = await db.SetMembersAsync(RedisKeys.AdminUsersSet());
                var list = new List<User>(members.Length);
                foreach (var m in members)
                {
                    var json = await db.StringGetAsync(RedisKeys.AdminUser(m!));
                    if (json.IsNullOrEmpty)
                    {
                        continue;
                    }

                    try
                    {
                        var u = JsonSerializer.Deserialize<User>(json!);
                        if (u is null)
                        {
                            continue;
                        }

                        list.Add(u);
                    }
                    catch { }
                }

                IEnumerable<User> filtered = list;
                if (!string.IsNullOrEmpty(q))
                {
                    filtered = filtered.Where(u => (u.Username?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                                   (u.FullName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                                   (u.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                                   (u.Id?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                if (!string.IsNullOrWhiteSpace(statusFilter) && Enum.TryParse<UserStatus>(statusFilter, true, out var us))
                {
                    filtered = filtered.Where(u => u.Status == us);
                }

                if (!string.IsNullOrWhiteSpace(roleFilter) && TryParseAccountRole(roleFilter, out var ar))
                {
                    filtered = filtered.Where(u => u.AccountRole == ar);
                }

                // Enrich with projects count
                var filteredList = filtered.ToList();
                for (var i = 0; i < filteredList.Count; i++)
                {
                    var u = filteredList[i];
                    try
                    {
                        var pc = (int)await db.SetLengthAsync(RedisKeys.AdminProjectsByUser(u.Id));
                        filteredList[i] = u with { ProjectsCount = pc };
                    }
                    catch { }
                }

                // Sorting
                Func<User, object?> keySelector = sort switch
                {
                    "email" => u => u.Email,
                    "role" => u => u.AccountRole,
                    "projects" => u => u.ProjectsCount,
                    "lastlogin" => u => u.LastLoginUtc ?? DateTime.MinValue,
                    "status" => u => u.Status,
                    "created" => u => u.CreatedUtc,
                    "updated" => u => u.UpdatedUtc,
                    _ => u => u.Username
                };

                IEnumerable<User> ordered = order == "desc"
                    ? filteredList.OrderByDescending(keySelector)
                    : filteredList.OrderBy(keySelector);

                var total = filteredList.Count;
                var page = ordered.Skip(skip).Take(take).ToArray();

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.UserListRetrieved,
                    "total={TotalCount} skip={Skip} take={Take} q={Query}", total, skip, take, q);

                operation.Complete();
                return Results.Ok(new { total, items = page });
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while listing users",
                    eventCode: EventCodes.AdminProjectsUsers.Query.UserListRetrieved,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - get
        app.MapGet("/admin/users/{id}", async (HttpRequest req, string id, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserDetails");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserDetails");

            using var operation = chunkedLogger.BeginOperation("AdminUserDetails", new Dictionary<string, object>
            {
                ["targetUserId"] = id
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var json = await db.StringGetAsync(RedisKeys.AdminUser(id));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.UserDetailsRetrieved, "status=NotFound userId={UserId}", id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {id} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.UserDetailsRetrieved, "userId={UserId}", id);

                operation.Complete();
                return Results.Text(json!, "application/json");
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while retrieving user details",
                    eventCode: EventCodes.AdminProjectsUsers.Query.UserDetailsRetrieved,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - create
        app.MapPost("/admin/users", async (HttpRequest req, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserCreate");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserCreate");

            using var operation = chunkedLogger.BeginOperation("AdminUserCreate");

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                User? body;
                try
                {
                    body = await req.ReadFromJsonAsync<User>();
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=InvalidJson");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (body is null)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=MissingBody");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Missing request body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!AdminValidation.TryValidateUserId(body.Id, out var err))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=InvalidUserId userId={UserId} error={ErrorMessage}", body.Id, err);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["id"] = [err] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!AdminValidation.TryValidateEmail(body.Email, out err))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=InvalidEmail email={Email} error={ErrorMessage}", body.Email, err);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["email"] = [err] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                using var act = Activity.StartActivity("admin.user.create");
                var now = DateTime.UtcNow;
                var id = body.Id.Trim();
                act?.SetTag("user.id", id);
                act?.SetTag("user.email", body.Email ?? string.Empty);
                var userKey = RedisKeys.AdminUser(id);
                if (await db.KeyExistsAsync(userKey))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=UserAlreadyExists userId={UserId}", id);
                    return ProblemDetailsHelpers.Conflict(
                        $"User with ID '{id}' already exists",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserCreated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var user = body with { CreatedUtc = now, UpdatedUtc = now };
                var json = JsonSerializer.Serialize(user);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(userKey, json);
                _ = tran.SetAddAsync(RedisKeys.AdminUsersSet(), id);
                // Maintain username index (case-insensitive) with uniqueness
                var usernameLower = (user.Username ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(usernameLower))
                {
                    var existingByUsername = await db.StringGetAsync(RedisKeys.AdminUserByUsername(usernameLower));
                    if (!existingByUsername.IsNullOrEmpty)
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                            "error=UsernameAlreadyExists username={Username}", user.Username);
                        return ProblemDetailsHelpers.Conflict(
                            $"Username '{user.Username}' already exists",
                            eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserCreated,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    _ = tran.StringSetAsync(RedisKeys.AdminUserByUsername(usernameLower), id);
                }

                if (!string.IsNullOrWhiteSpace(body.Email))
                {
                    var emailLower = body.Email!.Trim().ToLowerInvariant();
                    // enforce unique email
                    var existingByEmail = await db.StringGetAsync(RedisKeys.AdminUserByEmail(emailLower));
                    if (!existingByEmail.IsNullOrEmpty)
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                            "error=EmailAlreadyExists email={Email}", body.Email);
                        return ProblemDetailsHelpers.Conflict(
                            $"Email '{body.Email}' already exists",
                            eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserCreated,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    _ = tran.StringSetAsync(RedisKeys.AdminUserByEmail(emailLower), id);
                }

                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.UserManagement.UserCreated, "status=TransactionFailed");
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to create user in database",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserCreated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.UserManagement.UserCreated, // ADM21
                    "userId={UserId} username={Username} email={Email}", id, user.Username, user.Email);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = now,
                        Category = "admin",
                        Action = "user.created",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string>
                        {
                            ["id"] = id,
                            ["username"] = user.Username ?? string.Empty
                        }
                    });
                }
                catch { }

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertUserAsync(user);
                    }
                }
                catch { }

                // Create default project for the new user
                try
                {
                    var projectKey = $"{id}_default";
                    var projectName = $"{user.Username} Default";

                    // Check if validation allows this project key
                    if (AdminValidation.TryValidateProjectKey(projectKey, out _) &&
                        AdminValidation.TryValidateProjectName(projectName, out _))
                    {
                        var projKey = RedisKeys.AdminProject(projectKey);
                        var projExists = await db.KeyExistsAsync(projKey);

                        if (!projExists)
                        {
                            var nameLower = projectName.ToLowerInvariant();
                            var nameIdxKey = RedisKeys.AdminProjectByName(nameLower);
                            var existingByName = await db.StringGetAsync(nameIdxKey);

                            // Only create if name doesn't exist
                            if (existingByName.IsNullOrEmpty)
                            {
                                var defaultProject = new Project
                                {
                                    Key = projectKey,
                                    Name = projectName,
                                    OwnerUserId = id,
                                    Status = ProjectStatus.Active,
                                    MembersCount = 1,
                                    RunsCount = 0,
                                    LastActivityUtc = null,
                                    CreatedUtc = now,
                                    UpdatedUtc = now,
                                    CreatedBy = "system",
                                    UpdatedBy = "system"
                                };

                                var projJson = JsonSerializer.Serialize(defaultProject);
                                var projTran = db.CreateTransaction();
                                _ = projTran.StringSetAsync(projKey, projJson);
                                _ = projTran.SetAddAsync(RedisKeys.AdminProjectsSet(), projectKey);
                                _ = projTran.StringSetAsync(nameIdxKey, projectKey);
                                var projOk = await projTran.ExecuteAsync();

                                if (projOk)
                                {
                                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.ProjectManagement.ProjectCreated,
                                        "projectKey={ProjectKey} action=CreateDefaultProject", projectKey);

                                    try
                                    {
                                        if (adminStore != null)
                                        {
                                            await adminStore.UpsertProjectAsync(defaultProject);
                                        }
                                    }
                                    catch { }

                                    // Create membership with ProjectLead role
                                    var membership = new Membership
                                    {
                                        ProjectKey = projectKey,
                                        UserId = id,
                                        Role = ProjectRole.ProjectLead,
                                        CreatedUtc = now,
                                        UpdatedUtc = now,
                                        CreatedBy = "system",
                                        UpdatedBy = "system"
                                    };

                                    var membershipKey = RedisKeys.AdminMembership(projectKey, id);
                                    var membershipJson = JsonSerializer.Serialize(membership);
                                    var memberTran = db.CreateTransaction();
                                    _ = memberTran.StringSetAsync(membershipKey, membershipJson);
                                    _ = memberTran.SetAddAsync(RedisKeys.AdminMembersByProject(projectKey), id);
                                    _ = memberTran.SetAddAsync(RedisKeys.AdminProjectsByUser(id), projectKey);
                                    var memberOk = await memberTran.ExecuteAsync();

                                    if (memberOk)
                                    {
                                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipAdded,
                                            "projectKey={ProjectKey} userId={UserId} role=ProjectLead", projectKey, id);

                                        try
                                        {
                                            if (adminStore != null)
                                            {
                                                await adminStore.UpsertMembershipAsync(membership);
                                            }
                                        }
                                        catch { }

                                        try
                                        {
                                            await auditStore.AppendAsync(new AuditEntryDto
                                            {
                                                Timestamp = now,
                                                Category = "admin",
                                                Action = "project.default.created",
                                                Actor = "system",
                                                Details = new Dictionary<string, string>
                                                {
                                                    ["userId"] = id,
                                                    ["projectKey"] = projectKey,
                                                    ["projectName"] = projectName
                                                }
                                            });
                                        }
                                        catch { }

                                        // Initialize default retention settings for user's default project (if not exists)
                                        try
                                        {
                                            var settingsKey = $"project:{projectKey}:settings";

                                            // Only initialize if settings don't exist (prevents overwriting test/custom values)
                                            if (!await db.KeyExistsAsync(settingsKey))
                                            {
                                                var retentionSettings = new
                                                {
                                                    launchInactivityTimeout = "1d",
                                                    keepLaunches = "30",
                                                    keepLogs = "7",
                                                    keepAttachments = "7",
                                                    keepAudit = "90"
                                                };
                                                var settingsJson = JsonSerializer.Serialize(retentionSettings);
                                                await db.StringSetAsync(settingsKey, settingsJson);
                                                chunkedLogger.LogMilestone(EventCodes.ProjectSettings.SettingsPersisted,
                                                    "projectKey={ProjectKey} action=InitializeDefaults", projectKey);
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't fail user creation if default project creation fails
                    chunkedLogger.LogWarning(null, "Failed to create default project for user {UserId}: {Message}", id, ex.Message);
                }

                operation.Complete();
                return Results.Created($"/admin/users/{id}", user);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    $"Failed to create user: {ex.Message}",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserCreated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - patch (username/role/status/email)
        app.MapPatch("/admin/users/{id}", async (HttpRequest req, string id, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserUpdate");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserUpdate");

            using var operation = chunkedLogger.BeginOperation("AdminUserUpdate", new Dictionary<string, object>
            {
                ["targetUserId"] = id
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var json = await db.StringGetAsync(RedisKeys.AdminUser(id));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UserNotFound userId={UserId}", id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {id} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                User? user;
                try { user = JsonSerializer.Deserialize<User>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=UserDeserializationFailed userId={UserId}", id);
                    user = null;
                }

                if (user is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt user data",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserUpdated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var patch = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>() ??
                            new Dictionary<string, JsonElement>();
                var username = user.Username;
                var email = user.Email;
                var accountRole = user.AccountRole;
                var status = user.Status;

                if (patch.TryGetValue("username", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    var candidate = nameEl.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UsernameRequired");
                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]> { ["username"] = ["Username is required"] },
                            eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    username = candidate.Trim();
                }

                if (patch.TryGetValue("email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String)
                {
                    var candidate = emailEl.GetString();
                    if (!AdminValidation.TryValidateEmail(candidate, out var err))
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=InvalidEmail email={Email} error={ErrorMessage}", candidate, err);
                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]> { ["email"] = [err] },
                            eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier);
                    }

                    email = string.IsNullOrWhiteSpace(candidate) ? null : candidate!.Trim();
                }

                if (patch.TryGetValue("accountRole", out var arEl) && arEl.ValueKind == JsonValueKind.String &&
                    TryParseAccountRole(arEl.GetString(), out var ar1))
                {
                    accountRole = ar1;
                }
                else if (patch.TryGetValue("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.String &&
                         TryParseAccountRole(roleEl.GetString(), out var ar2))
                {
                    accountRole = ar2;
                }

                if (patch.TryGetValue("status", out var stEl) && stEl.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<UserStatus>(stEl.GetString(), true, out var us))
                {
                    status = us;
                }

                var updated = user with
                {
                    Username = username,
                    Email = email,
                    AccountRole = accountRole,
                    Status = status,
                    UpdatedUtc = DateTime.UtcNow
                };
                var updatedJson = JsonSerializer.Serialize(updated);
                var tran2 = db.CreateTransaction();
                _ = tran2.StringSetAsync(RedisKeys.AdminUser(id), updatedJson);

                // Maintain email index (case-insensitive) with uniqueness and removal of old mapping
                var oldEmailLower = (user.Email ?? string.Empty).Trim().ToLowerInvariant();
                var newEmailLower = (email ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.Equals(oldEmailLower, newEmailLower, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(newEmailLower))
                    {
                        var existingByEmail = await db.StringGetAsync(RedisKeys.AdminUserByEmail(newEmailLower));
                        if (!existingByEmail.IsNullOrEmpty &&
                            !string.Equals(existingByEmail.ToString(), id, StringComparison.Ordinal))
                        {
                            chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=EmailAlreadyExists email={Email}", email);
                            return ProblemDetailsHelpers.Conflict(
                                $"Email '{email}' already exists",
                                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                                instance: req.Path,
                                traceId: req.HttpContext.TraceIdentifier);
                        }

                        _ = tran2.StringSetAsync(RedisKeys.AdminUserByEmail(newEmailLower), id);
                    }

                    if (!string.IsNullOrEmpty(oldEmailLower))
                    {
                        _ = tran2.KeyDeleteAsync(RedisKeys.AdminUserByEmail(oldEmailLower));
                    }
                }

                // Maintain username index (case-insensitive) with uniqueness and removal of old mapping
                var oldUsernameLower = (user.Username ?? string.Empty).Trim().ToLowerInvariant();
                var newUsernameLower = (username ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.Equals(oldUsernameLower, newUsernameLower, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(newUsernameLower))
                    {
                        var existingByUsername = await db.StringGetAsync(RedisKeys.AdminUserByUsername(newUsernameLower));
                        if (!existingByUsername.IsNullOrEmpty &&
                            !string.Equals(existingByUsername.ToString(), id, StringComparison.Ordinal))
                        {
                            chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UsernameAlreadyExists username={Username}", username);
                            return ProblemDetailsHelpers.Conflict(
                                $"Username '{username}' already exists",
                                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                                instance: req.Path,
                                traceId: req.HttpContext.TraceIdentifier);
                        }

                        _ = tran2.StringSetAsync(RedisKeys.AdminUserByUsername(newUsernameLower), id);
                    }

                    if (!string.IsNullOrEmpty(oldUsernameLower))
                    {
                        _ = tran2.KeyDeleteAsync(RedisKeys.AdminUserByUsername(oldUsernameLower));
                    }
                }

                var ok2 = await tran2.ExecuteAsync();
                if (!ok2)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.UserManagement.UserUpdated, "status=TransactionFailed userId={UserId}", id);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to update user in database",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserUpdated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.UserManagement.UserUpdated, // ADM22
                    "userId={UserId} username={Username} email={Email} role={Role} status={Status}", id, updated.Username, updated.Email, updated.AccountRole, updated.Status);

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertUserAsync(updated);
                    }
                }
                catch { }

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "user.updated",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["id"] = id }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while updating user",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserUpdated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - activate
        app.MapPost("/admin/users/{id}/activate", async (HttpRequest req, string id, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserActivate");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserActivate");

            using var operation = chunkedLogger.BeginOperation("AdminUserActivate", new Dictionary<string, object>
            {
                ["targetUserId"] = id
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var json = await db.StringGetAsync(RedisKeys.AdminUser(id));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=UserNotFound userId={UserId}", id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {id} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                User? user;
                try { user = JsonSerializer.Deserialize<User>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        ex,
                        "error=UserDeserializationFailed userId={UserId}", id);
                    user = null;
                }

                if (user is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt user data",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserActivated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (user.Status == UserStatus.Active)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.UserManagement.UserActivated,
                        "status=AlreadyActive userId={UserId}", id);
                    operation.Complete();
                    return Results.Ok(user);
                }

                var updated = user with { Status = UserStatus.Active, UpdatedUtc = DateTime.UtcNow };
                var updatedJson = JsonSerializer.Serialize(updated);
                await db.StringSetAsync(RedisKeys.AdminUser(id), updatedJson);
                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertUserAsync(updated);
                    }
                }
                catch { }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.UserManagement.UserActivated, // ADM24
                    "userId={UserId} username={Username}", id, user.Username);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "user.activated",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["id"] = id }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while activating user",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserActivated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - deactivate
        app.MapPost("/admin/users/{id}/deactivate", async (HttpRequest req, string id, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserDeactivate");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserDeactivate");

            using var operation = chunkedLogger.BeginOperation("AdminUserDeactivate", new Dictionary<string, object>
            {
                ["targetUserId"] = id
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var json = await db.StringGetAsync(RedisKeys.AdminUser(id));
                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        "error=UserNotFound userId={UserId}", id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {id} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                User? user;
                try { user = JsonSerializer.Deserialize<User>(json!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        ex,
                        "error=UserDeserializationFailed userId={UserId}", id);
                    user = null;
                }

                if (user is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt user data",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeactivated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (user.Status == UserStatus.Disabled)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.AdminProjectsUsers.UserManagement.UserDeactivated,
                        "status=AlreadyDisabled userId={UserId}", id);
                    operation.Complete();
                    return Results.Ok(user);
                }

                var updated = user with { Status = UserStatus.Disabled, UpdatedUtc = DateTime.UtcNow };
                var updatedJson = JsonSerializer.Serialize(updated);
                await db.StringSetAsync(RedisKeys.AdminUser(id), updatedJson);
                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertUserAsync(updated);
                    }
                }
                catch { }

                chunkedLogger.LogMilestone(
                    EventCodes.AdminProjectsUsers.UserManagement.UserDeactivated, // ADM25
                    "userId={UserId} username={Username}", id, user.Username);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "user.deactivated",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["id"] = id }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(updated);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while deactivating user",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeactivated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - delete (only when Disabled)
        app.MapDelete("/admin/users/{id}", async (HttpRequest req, string id, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserDelete");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserDelete");

            using var operation = chunkedLogger.BeginOperation("AdminUserDelete", new Dictionary<string, object>
            {
                ["targetUserId"] = id
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var ujson = await db.StringGetAsync(RedisKeys.AdminUser(id));
                if (ujson.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UserNotFound userId={UserId}", id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {id} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                User? user;
                try { user = JsonSerializer.Deserialize<User>(ujson!); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=UserDeserializationFailed userId={UserId}", id);
                    user = null;
                }

                if (user is null)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Corrupt user data",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (user.Status != UserStatus.Disabled)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UserNotDisabled userId={UserId} status={Status}", id, user.Status);
                    return ProblemDetailsHelpers.Conflict(
                        "User must be Disabled before they can be permanently deleted",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Collect related keys and indices to remove
                var usernameLower = (user.Username ?? string.Empty).Trim().ToLowerInvariant();
                var emailLower = (user.Email ?? string.Empty).Trim().ToLowerInvariant();

                // Collect projects for membership cleanup
                var projectKeys = await db.SetMembersAsync(RedisKeys.AdminProjectsByUser(id));

                var tran = db.CreateTransaction();
                // Primary user record and membership set
                _ = tran.KeyDeleteAsync(RedisKeys.AdminUser(id));
                _ = tran.SetRemoveAsync(RedisKeys.AdminUsersSet(), id);
                _ = tran.KeyDeleteAsync(RedisKeys.AdminProjectsByUser(id));

                // Secondary indices
                if (!string.IsNullOrEmpty(usernameLower))
                {
                    _ = tran.KeyDeleteAsync(RedisKeys.AdminUserByUsername(usernameLower));
                }

                if (!string.IsNullOrEmpty(emailLower))
                {
                    _ = tran.KeyDeleteAsync(RedisKeys.AdminUserByEmail(emailLower));
                }

                // Credentials
                _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPassword(id));

                // Photo and metadata
                _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPhoto(id));
                _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPhotoContentType(id));
                _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPhotoUpdated(id));

                // API keys
                var apiKeySetKey = RedisKeys.AdminUserApiKeys(id);
                var apiKeySlugs = await db.SetMembersAsync(apiKeySetKey);
                foreach (var slug in apiKeySlugs)
                {
                    _ = tran.KeyDeleteAsync(RedisKeys.AdminUserApiKey(id, slug!));
                }

                _ = tran.KeyDeleteAsync(apiKeySetKey);

                // Membership entries and reverse sets
                foreach (var pk in projectKeys)
                {
                    var p = pk!.ToString();
                    _ = tran.KeyDeleteAsync(RedisKeys.AdminMembership(p, id));
                    _ = tran.SetRemoveAsync(RedisKeys.AdminMembersByProject(p), id);
                }

                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.UserManagement.UserDeleted, "status=TransactionFailed");
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to delete user from database",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.UserManagement.UserDeleted, // ADM23
                    "userId={UserId} username={Username} projectMembershipsCount={ProjectCount}", id, user.Username, projectKeys.Length);

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.DeleteUserAsync(id);
                    }
                }
                catch { }

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "user.deleted",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["id"] = id }
                    });
                }
                catch { }

                operation.Complete();
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while deleting user",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - invitation (optional): create token
        app.MapPost("/admin/users/invite", async (HttpRequest req, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserInvite");
            var chunkedLogger = new ChunkedLogger(logger, "AdminUserInvite");

            using var operation = chunkedLogger.BeginOperation("AdminUserInvite");

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Dictionary<string, JsonElement>? doc;
                try { doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=InvalidJson");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var email = doc != null && doc.TryGetValue("email", out var em) && em.ValueKind == JsonValueKind.String
                    ? (em.GetString() ?? "").Trim()
                    : string.Empty;
                var suggestedUsername =
                    doc != null && doc.TryGetValue("username", out var un) && un.ValueKind == JsonValueKind.String
                        ? (un.GetString() ?? "").Trim()
                        : null;
                var ttlDays = 7;
                if (doc != null && doc.TryGetValue("ttlDays", out var ttl) && ttl.ValueKind == JsonValueKind.Number)
                {
                    try { ttlDays = Math.Clamp(ttl.GetInt32(), 1, 30); }
                    catch { }
                }

                if (!AdminValidation.TryValidateEmail(email, out var err))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=InvalidEmail email={Email} error={ErrorMessage}", email, err);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["email"] = [err] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var emailLower = email.Trim().ToLowerInvariant();

                // Reject if a user with this email already exists
                var existingByEmail = await db.StringGetAsync(RedisKeys.AdminUserByEmail(emailLower));
                if (!existingByEmail.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=EmailAlreadyExists email={Email}", email);
                    return ProblemDetailsHelpers.Conflict(
                        $"User with email '{email}' already exists",
                        eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.AdminInvited,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Reuse existing invite token for same email if present
                var existingToken = await db.StringGetAsync(RedisKeys.AdminInviteByEmail(emailLower));
                if (!existingToken.IsNullOrEmpty)
                {
                    var existingInvite = await db.StringGetAsync(RedisKeys.AdminInviteToken(existingToken!));
                    if (!existingInvite.IsNullOrEmpty)
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.AdminUserManagement.AdminInvited, "status=ReuseExisting token={InviteToken} email={Email}", existingToken, email);
                        operation.Complete();
                        return Results.Ok(new { token = existingToken.ToString(), invite = JsonSerializer.Deserialize<object>(existingInvite!) });
                    }
                }

                var token = Guid.NewGuid().ToString("n");
                var now = DateTime.UtcNow;
                var expires = now.AddDays(ttlDays);
                var invite = new
                {
                    token,
                    email,
                    username = suggestedUsername,
                    createdUtc = now,
                    expiresUtc = expires
                };
                var inviteJson = JsonSerializer.Serialize(invite);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(RedisKeys.AdminInviteToken(token), inviteJson, expires - now);
                _ = tran.StringSetAsync(RedisKeys.AdminInviteByEmail(emailLower), token, expires - now);
                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.AdminUserManagement.AdminInvited, "status=TransactionFailed");
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to create invite in database",
                        eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.AdminInvited,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.AdminUserManagement.AdminInvited, // ADM51
                    "token={InviteToken} email={Email} expiresUtc={Expires}", token, email, expires);

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = now,
                        Category = "admin",
                        Action = "user.invite.created",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["email"] = email }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Created($"/admin/users/invite/{token}", new { token, expiresUtc = expires });
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while creating invitation",
                    eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.AdminInvited,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - invitation: get by token
        app.MapGet("/admin/users/invite/{token}", async (HttpRequest req, string token) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var json = await db.StringGetAsync(RedisKeys.AdminInviteToken(token));
            if (json.IsNullOrEmpty)
            {
                return ProblemDetailsHelpers.NotFound(
                    "Invitation not found or expired",
                    eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.InviteExpired,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            return Results.Text(json!, "application/json");
        });

        // Users - invitation: accept token and create user
        app.MapPost("/admin/users/invite/{token}/accept", async (HttpRequest req, string token, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminInviteAccept");
            var chunkedLogger = new ChunkedLogger(logger, "AdminInviteAccept");

            using var operation = chunkedLogger.BeginOperation("AdminInviteAccept", new Dictionary<string, object>
            {
                ["inviteToken"] = token
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var inviteJson = await db.StringGetAsync(RedisKeys.AdminInviteToken(token));
                if (inviteJson.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "status=InvalidOrExpiredToken token={InviteToken}", token);
                    return ProblemDetailsHelpers.NotFound(
                        "Invitation token is invalid or has expired",
                        eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.InviteExpired,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                Dictionary<string, JsonElement>? doc;
                try { doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=InvalidJson");
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var id = doc != null && doc.TryGetValue("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? (idEl.GetString() ?? "").Trim()
                    : string.Empty;
                var username =
                    doc != null && doc.TryGetValue("username", out var unEl) && unEl.ValueKind == JsonValueKind.String
                        ? (unEl.GetString() ?? "").Trim()
                        : string.Empty;
                if (!AdminValidation.TryValidateUserId(id, out var idErr))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=InvalidUserId userId={UserId} error={ErrorMessage}", id, idErr);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["id"] = [idErr] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var invite = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inviteJson!);
                var email = invite != null && invite.TryGetValue("email", out var em) &&
                            em.ValueKind == JsonValueKind.String
                    ? (em.GetString() ?? "").Trim()
                    : string.Empty;
                var suggested =
                    invite != null && invite.TryGetValue("username", out var s) && s.ValueKind == JsonValueKind.String
                        ? (s.GetString() ?? "").Trim()
                        : string.Empty;
                if (!AdminValidation.TryValidateEmail(email, out var mailErr))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=InvalidEmail email={Email} error={ErrorMessage}", email, mailErr);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["email"] = [mailErr] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var emailLower = email.ToLowerInvariant();

                // Ensure unique id and email
                if (await db.KeyExistsAsync(RedisKeys.AdminUser(id)))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UserAlreadyExists userId={UserId}", id);
                    return ProblemDetailsHelpers.Conflict(
                        $"User with ID '{id}' already exists",
                        eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.InviteAccepted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var existingByEmail = await db.StringGetAsync(RedisKeys.AdminUserByEmail(emailLower));
                if (!existingByEmail.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=EmailAlreadyExists email={Email}", email);
                    return ProblemDetailsHelpers.Conflict(
                        $"User with email '{email}' already exists",
                        eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.InviteAccepted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (string.IsNullOrWhiteSpace(username))
                {
                    username = string.IsNullOrWhiteSpace(suggested) ? id : suggested;
                }

                var now = DateTime.UtcNow;
                var user = new User
                {
                    Id = id,
                    Username = username,
                    Email = email,
                    AccountRole = AccountRole.User,
                    Status = UserStatus.Active,
                    ProjectsCount = 0,
                    LastLoginUtc = null,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    CreatedBy = "invite",
                    UpdatedBy = "invite"
                };
                var userJson = JsonSerializer.Serialize(user);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(RedisKeys.AdminUser(id), userJson);
                _ = tran.SetAddAsync(RedisKeys.AdminUsersSet(), id);
                _ = tran.StringSetAsync(RedisKeys.AdminUserByEmail(emailLower), id);
                _ = tran.KeyDeleteAsync(RedisKeys.AdminInviteToken(token));
                _ = tran.KeyDeleteAsync(RedisKeys.AdminInviteByEmail(emailLower));
                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.AdminUserManagement.InviteAccepted, "status=TransactionFailed");
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to accept invitation in database",
                        eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.InviteAccepted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.AdminUserManagement.InviteAccepted, // ADM52
                    "userId={UserId} email={Email}", id, email);

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertUserAsync(user);
                    }
                }
                catch { }

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = now,
                        Category = "admin",
                        Action = "user.invite.accepted",
                        Actor = "dashboard",
                        Details = new Dictionary<string, string> { ["id"] = id, ["email"] = email }
                    });
                }
                catch { }

                operation.Complete();
                return Results.Created($"/admin/users/{id}", user);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while accepting invitation",
                    eventCode: EventCodes.AdminProjectsUsers.AdminUserManagement.InviteAccepted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Memberships - list members of a project
        app.MapGet("/admin/projects/{key}/members", async (HttpRequest req, string key, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminMembershipList");
            var chunkedLogger = new ChunkedLogger(logger, "AdminMembershipList");

            using var operation = chunkedLogger.BeginOperation("AdminMembershipList", new Dictionary<string, object>
            {
                ["projectKey"] = key
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!await db.KeyExistsAsync(RedisKeys.AdminProject(key)))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.MembershipListRetrieved, "status=ProjectNotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var userIds = await db.SetMembersAsync(RedisKeys.AdminMembersByProject(key));
                var items = new List<Membership>(userIds.Length);
                foreach (var uid in userIds)
                {
                    var mj = await db.StringGetAsync(RedisKeys.AdminMembership(key, uid!));
                    if (mj.IsNullOrEmpty)
                    {
                        continue;
                    }

                    try
                    {
                        var m = JsonSerializer.Deserialize<Membership>(mj!);
                        if (m != null)
                        {
                            items.Add(m);
                        }
                    }
                    catch { }
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Query.MembershipListRetrieved,
                    "projectKey={ProjectKey} total={TotalCount}", key, items.Count);

                operation.Complete();
                return Results.Ok(new { total = items.Count, items });
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while listing project members",
                    eventCode: EventCodes.AdminProjectsUsers.Query.MembershipListRetrieved,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Memberships - list projects of a user
        app.MapGet("/admin/users/{id}/projects", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (!await db.KeyExistsAsync(RedisKeys.AdminUser(id)))
            {
                return ProblemDetailsHelpers.NotFound(
                    $"User {id} not found",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var projectKeys = await db.SetMembersAsync(RedisKeys.AdminProjectsByUser(id));
            var items = new List<Membership>(projectKeys.Length);
            foreach (var pk in projectKeys)
            {
                var mj = await db.StringGetAsync(RedisKeys.AdminMembership(pk!, id));
                if (mj.IsNullOrEmpty)
                {
                    continue;
                }

                try
                {
                    var m = JsonSerializer.Deserialize<Membership>(mj!);
                    if (m != null)
                    {
                        items.Add(m);
                    }
                }
                catch { }
            }

            return Results.Ok(new { total = items.Count, items });
        });

        // Memberships - add/update
        app.MapPut("/admin/projects/{key}/members/{userId}", async (HttpRequest req, string key, string userId, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminMembershipUpsert");
            var chunkedLogger = new ChunkedLogger(logger, "AdminMembershipUpsert");

            using var operation = chunkedLogger.BeginOperation("AdminMembershipUpsert", new Dictionary<string, object>
            {
                ["projectKey"] = key,
                ["targetUserId"] = userId
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Get the actual user performing the action
                var actor = req.Headers["x-user-id"].FirstOrDefault() ??
                            req.Headers["x-dashboard-user"].FirstOrDefault() ?? "dashboard";
                if (!AdminValidation.TryValidateMembership(userId, key, out var err))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error={ErrorMessage} userId={UserId} projectKey={ProjectKey}", err, userId, key);
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["membership"] = [err] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!await db.KeyExistsAsync(RedisKeys.AdminProject(key)))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=ProjectNotFound projectKey={ProjectKey}", key);
                    return ProblemDetailsHelpers.NotFound(
                        $"Project {key} not found",
                        eventCode: EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (!await db.KeyExistsAsync(RedisKeys.AdminUser(userId)))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "error=UserNotFound userId={UserId}", userId);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {userId} not found",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var role = ProjectRole.Client;
                try
                {
                    var doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                    if (doc != null && doc.TryGetValue("role", out var roleEl) &&
                        roleEl.ValueKind == JsonValueKind.String &&
                        Enum.TryParse<ProjectRole>(roleEl.GetString(), true, out var parsed))
                    {
                        role = parsed;
                    }
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogDebug(null, "Error parsing membership role: {Message}", ex.Message);
                }

                using var act = Activity.StartActivity("admin.membership.upsert");
                // Preserve CreatedUtc if membership exists and capture prev role for metrics
                var existingJson = await db.StringGetAsync(RedisKeys.AdminMembership(key, userId));
                var created = DateTime.UtcNow;
                var isNew = existingJson.IsNullOrEmpty;
                ProjectRole? prevRole = null;
                if (!existingJson.IsNullOrEmpty)
                {
                    try
                    {
                        var ex = JsonSerializer.Deserialize<Membership>(existingJson!);
                        if (ex != null)
                        {
                            if (ex.CreatedUtc != default)
                            {
                                created = ex.CreatedUtc;
                            }

                            prevRole = ex.Role;
                        }
                    }
                    catch (Exception ex)
                    {
                        chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Validation.ValidationFailed, ex, "error=MembershipDeserializationFailed projectKey={ProjectKey} userId={UserId}", key, userId);
                    }
                }

                act?.SetTag("project.key", key);
                act?.SetTag("user.id", userId);
                act?.SetTag("role.new", role.ToString());
                if (prevRole.HasValue)
                {
                    act?.SetTag("role.prev", prevRole.Value.ToString());
                }

                act?.SetTag("membership.is_new", isNew);

                var membership = new Membership
                {
                    ProjectKey = key,
                    UserId = userId,
                    Role = role,
                    CreatedUtc = created,
                    UpdatedUtc = DateTime.UtcNow
                };
                var mjson = JsonSerializer.Serialize(membership);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(RedisKeys.AdminMembership(key, userId), mjson);
                _ = tran.SetAddAsync(RedisKeys.AdminMembersByProject(key), userId);
                _ = tran.SetAddAsync(RedisKeys.AdminProjectsByUser(userId), key);
                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipAdded, "status=TransactionFailed");
                    act?.SetTag("save.ok", false);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to save membership in database",
                        eventCode: EventCodes.AdminProjectsUsers.MembershipManagement.MembershipAdded,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var action = isNew ? "add" : prevRole.HasValue && prevRole.Value != role ? "role_update" : "upsert";
                MembershipChangesCounter.WithLabels(action).Inc();
                act?.SetTag("save.ok", true);
                act?.SetTag("membership.action", action);

                if (isNew)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipAdded, // ADM41
                        "projectKey={ProjectKey} userId={UserId} role={Role}", key, userId, role);
                }
                else if (prevRole.HasValue && prevRole.Value != role)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipRoleUpdated, // ADM43
                        "projectKey={ProjectKey} userId={UserId} oldRole={OldRole} newRole={NewRole}", key, userId, prevRole.Value, role);
                }
                else
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipAdded,
                        "projectKey={ProjectKey} userId={UserId} status=UpdatedNoRoleChange", key, userId);
                }

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.UpsertMembershipAsync(membership);
                    }
                }
                catch { }

                try
                {
                    var details = new Dictionary<string, string>
                    {
                        ["project"] = key,
                        ["user"] = userId,
                        ["role"] = role.ToString(),
                        ["action"] = isNew ? "Assign user" : "Update role"
                    };
                    if (prevRole.HasValue)
                    {
                        details["oldRole"] = prevRole.Value.ToString();
                    }

                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = isNew ? "membership.assigned" : "membership.role.updated",
                        Actor = actor,
                        Details = details
                    });
                }
                catch { }

                operation.Complete();
                return Results.Ok(membership);
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while saving membership",
                    eventCode: EventCodes.AdminProjectsUsers.MembershipManagement.MembershipAdded,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Memberships - remove
        app.MapDelete("/admin/projects/{key}/members/{userId}", async (HttpRequest req, string key, string userId, [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminMembershipRemove");
            var chunkedLogger = new ChunkedLogger(logger, "AdminMembershipRemove");

            using var operation = chunkedLogger.BeginOperation("AdminMembershipRemove", new Dictionary<string, object>
            {
                ["projectKey"] = key,
                ["targetUserId"] = userId
            });

            try
            {
                if (!CheckAuthentication(req))
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.Authentication.LoginFailed, "error=NotAuthenticated");
                    return ProblemDetailsHelpers.Unauthorized(
                        "Authentication required",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Get the actual user performing the action
                var actor = req.Headers["x-user-id"].FirstOrDefault() ??
                            req.Headers["x-dashboard-user"].FirstOrDefault() ?? "dashboard";

                using var act = Activity.StartActivity("admin.membership.remove");
                act?.SetTag("project.key", key);
                act?.SetTag("user.id", userId);
                var tran = db.CreateTransaction();
                _ = tran.KeyDeleteAsync(RedisKeys.AdminMembership(key, userId));
                _ = tran.SetRemoveAsync(RedisKeys.AdminMembersByProject(key), userId);
                _ = tran.SetRemoveAsync(RedisKeys.AdminProjectsByUser(userId), key);
                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipRemoved, "status=TransactionFailed projectKey={ProjectKey} userId={UserId}", key, userId);
                    act?.SetTag("save.ok", false);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to remove membership from database",
                        eventCode: EventCodes.AdminProjectsUsers.MembershipManagement.MembershipRemoved,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                chunkedLogger.LogMilestone(EventCodes.AdminProjectsUsers.MembershipManagement.MembershipRemoved, // ADM42
                    "projectKey={ProjectKey} userId={UserId}", key, userId);

                MembershipChangesCounter.WithLabels("remove").Inc();
                act?.SetTag("save.ok", true);

                try
                {
                    if (adminStore != null)
                    {
                        await adminStore.RemoveMembershipAsync(key, userId);
                    }
                }
                catch { }

                try
                {
                    await auditStore.AppendAsync(new AuditEntryDto
                    {
                        Timestamp = DateTime.Now,
                        Category = "admin",
                        Action = "membership.unassigned",
                        Actor = actor,
                        Details = new Dictionary<string, string>
                        {
                            ["project"] = key,
                            ["user"] = userId,
                            ["action"] = "Unassign user"
                        }
                    });
                }
                catch { }

                operation.Complete();
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                operation.Fail(ex, ErrorType.Unexpected);
                return ProblemDetailsHelpers.InternalServerError(
                    "An unexpected error occurred while removing membership",
                    eventCode: EventCodes.AdminProjectsUsers.MembershipManagement.MembershipRemoved,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - profile photo: upload (multipart/form-data)
        app.MapPost("/admin/users/{id}/photo", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (!await db.KeyExistsAsync(RedisKeys.AdminUser(id)))
            {
                return ProblemDetailsHelpers.NotFound(
                    $"User {id} not found",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Limits and validation
            var maxKb = int.TryParse(config["AGENIX_HUB_USER_PHOTO_MAX_KB"], out var mkb)
                ? Math.Clamp(mkb, 32, 1024)
                : 256;
            var maxBytes = maxKb * 1024;
            IFormFile? file = null;
            try
            {
                if (!req.HasFormContentType)
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["contentType"] = ["multipart/form-data required"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var form = await req.ReadFormAsync();
                file = form.Files.Count > 0 ? form.Files[0] : null;
                if (file == null)
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["file is required"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (file.Length <= 0)
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["empty file"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                if (file.Length > maxBytes)
                {
                    return ProblemDetailsHelpers.PayloadTooLarge(
                        $"File size exceeds limit of {maxKb} KB",
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Accept common safe image types only
                var ct = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "image/jpeg",
                    "image/jpg",
                    "image/png",
                    "image/gif",
                    "image/webp"
                };
                if (!allowed.Contains(ct))
                {
                    // Try infer from file name extension if content type is generic
                    var name = file.FileName ?? string.Empty;
                    if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        ct = "image/jpeg";
                    }
                    else if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        ct = "image/png";
                    }
                    else if (name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    {
                        ct = "image/gif";
                    }
                    else if (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                    {
                        ct = "image/webp";
                    }
                }

                if (!allowed.Contains(ct))
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["contentType"] = ["unsupported image type. Allowed: JPEG, PNG, GIF, WEBP"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Read bytes
                await using var ms = new MemoryStream((int)file.Length);
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();

                // Basic magic check for JPEG/PNG/GIF/WEBP
                bool LooksLikeImage(byte[] b)
                {
                    if (b.Length < 4)
                    {
                        return false;
                    }

                    // JPEG
                    if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                    {
                        return true;
                    }

                    // PNG
                    if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
                    {
                        return true;
                    }

                    // GIF
                    if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46)
                    {
                        return true;
                    }

                    // WEBP (RIFF....WEBP)
                    if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
                        b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
                    {
                        return true;
                    }

                    return false;
                }

                if (!LooksLikeImage(bytes))
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["file content does not appear to be a valid image"] },
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Store in Redis (binary + metadata)
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(RedisKeys.AdminUserPhoto(id), bytes);
                _ = tran.StringSetAsync(RedisKeys.AdminUserPhotoContentType(id), ct);
                var updated = DateTime.UtcNow.Ticks.ToString();
                _ = tran.StringSetAsync(RedisKeys.AdminUserPhotoUpdated(id), updated);
                var ok = await tran.ExecuteAsync();
                if (!ok)
                {
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to save photo in database",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserUpdated,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Compute etag (hex sha256)
                string etag;
                using (var sha = SHA256.Create())
                {
                    etag = Convert.ToHexString(sha.ComputeHash(bytes));
                }

                return Results.Ok(new { etag, updated });
            }
            catch (Exception ex)
            {
                return ProblemDetailsHelpers.InternalServerError(
                    $"Upload failed: {ex.Message}",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserUpdated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // Users - profile photo: get
        app.MapGet("/admin/users/{id}/photo", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var bytes = (byte[]?)await db.StringGetAsync(RedisKeys.AdminUserPhoto(id));
            if (bytes is null || bytes.Length == 0)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"Photo for user {id} not found",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var ct = (await db.StringGetAsync(RedisKeys.AdminUserPhotoContentType(id))).ToString();
            if (string.IsNullOrWhiteSpace(ct))
            {
                ct = "image/png";
            }

            string etag;
            using (var sha = SHA256.Create())
            {
                etag = Convert.ToHexString(sha.ComputeHash(bytes));
            }

            var resp = Results.File(bytes, ct);
            try
            {
                var u = await db.StringGetAsync(RedisKeys.AdminUserPhotoUpdated(id));
                if (!u.IsNullOrEmpty)
                {
                    // Weak ETag and Last-Modified headers
                    req.HttpContext.Response.Headers["ETag"] = $"W/\"{etag}\"";
                    if (long.TryParse(u.ToString(), out var ticks))
                    {
                        var dt = new DateTime(ticks, DateTimeKind.Utc).ToString("R");
                        req.HttpContext.Response.Headers["Last-Modified"] = dt;
                    }
                }
            }
            catch { }

            return resp;
        });

        // Users - profile photo: remove
        app.MapDelete("/admin/users/{id}/photo", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var tran = db.CreateTransaction();
            _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPhoto(id));
            _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPhotoContentType(id));
            _ = tran.KeyDeleteAsync(RedisKeys.AdminUserPhotoUpdated(id));
            await tran.ExecuteAsync();
            return Results.NoContent();
        });

        // Users - API Keys: create
        app.MapPost("/admin/users/{id}/api-keys", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Ensure user exists
            var uj = await db.StringGetAsync(RedisKeys.AdminUser(id));
            if (uj.IsNullOrEmpty)
            {
                return ProblemDetailsHelpers.NotFound(
                    $"User {id} not found",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            Dictionary<string, JsonElement>? doc;
            try { doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
            catch (Exception)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var name = doc != null && doc.TryGetValue("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                ? (nEl.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["name"] = ["API key name is required"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (name.Length > 40)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["name"] = ["API key name must be 1..40 characters"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Normalize prefix: replace spaces/underscores with hyphens (do not lower-case for display)
            static string MakePrefix(string s)
            {
                var p = s.Replace(' ', '-').Replace('_', '-');
                // collapse multiple dashes
                while (p.Contains("--", StringComparison.Ordinal))
                {
                    p = p.Replace("--", "-");
                }

                // trim leading/trailing dashes
                return p.Trim('-');
            }

            var prefix = MakePrefix(name);
            var nameLower = name.ToLowerInvariant();

            // Enforce uniqueness per user (case-insensitive by name)
            var setKey = RedisKeys.AdminUserApiKeys(id);
            var already = await db.SetContainsAsync(setKey, nameLower);
            if (already)
            {
                return ProblemDetailsHelpers.Conflict(
                    $"API key with name '{name}' already exists for this user",
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Generate secure token and compose full key
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
            var fullKey = string.IsNullOrWhiteSpace(prefix) ? token : $"{prefix}-{token}";

            // Hash for storage (sha256 of full key)
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullKey))).ToLowerInvariant();
            var now = DateTime.UtcNow;
            var actor = (req.Headers["x-user-id"].FirstOrDefault() ??
                         req.Headers["x-dashboard-user"].FirstOrDefault() ?? "system").Trim();
            var entry = new
            {
                name,
                nameLower,
                prefix,
                createdUtc = now,
                createdBy = string.IsNullOrWhiteSpace(actor) ? null : actor,
                alg = "sha256",
                hash
            };

            // Save atomically: add to set and store entry under a stable key (by nameLower)
            var tran = db.CreateTransaction();
            _ = tran.SetAddAsync(setKey, nameLower);
            _ = tran.StringSetAsync(RedisKeys.AdminUserApiKey(id, nameLower), JsonSerializer.Serialize(entry));
            var ok = await tran.ExecuteAsync();
            if (!ok)
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to save API key in database",
                    eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserUpdated,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            return Results.Created($"/admin/users/{id}/api-keys/{Uri.EscapeDataString(nameLower)}",
                new { name, apiKey = fullKey });
        });

        // Users - API Keys: list
        app.MapGet("/admin/users/{id}/api-keys", async (HttpRequest req, string id) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var setKey = RedisKeys.AdminUserApiKeys(id);
            var members = await db.SetMembersAsync(setKey);
            var items = new List<object>(members.Length);
            foreach (var m in members)
            {
                var slug = m.ToString();
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                var entryJson = await db.StringGetAsync(RedisKeys.AdminUserApiKey(id, slug));
                if (entryJson.IsNullOrEmpty)
                {
                    items.Add(new { name = slug, createdUtc = (DateTime?)null });
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(entryJson.ToString());
                    var root = doc.RootElement;
                    var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? slug
                        : slug;
                    DateTime? created = null;
                    if (root.TryGetProperty("createdUtc", out var c))
                    {
                        if (c.ValueKind == JsonValueKind.String && DateTime.TryParse(c.GetString(), out var d1))
                        {
                            created = DateTime.SpecifyKind(d1, DateTimeKind.Utc);
                        }
                        else if (c.ValueKind == JsonValueKind.Number)
                        {
                            try { created = c.GetDateTime(); }
                            catch { }
                        }
                    }

                    items.Add(new { name, createdUtc = created });
                }
                catch
                {
                    items.Add(new { name = slug, createdUtc = (DateTime?)null });
                }
            }

            // Order by created desc when available
            var ordered = items
                .Select(o => new
                {
                    name = (string)o.GetType().GetProperty("name")!.GetValue(o)!,
                    createdUtc = (DateTime?)o.GetType().GetProperty("createdUtc")!.GetValue(o)
                })
                .OrderByDescending(x => x.createdUtc ?? DateTime.MinValue)
                .ToList();
            return Results.Ok(new { items = ordered });
        });

        // Users - API Keys: revoke
        app.MapDelete("/admin/users/{id}/api-keys/{name}", async (HttpRequest req, string id, string name) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var slug = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(slug))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["name"] = ["API key name is required"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var setKey = RedisKeys.AdminUserApiKeys(id);
            var tran = db.CreateTransaction();
            _ = tran.SetRemoveAsync(setKey, slug);
            _ = tran.KeyDeleteAsync(RedisKeys.AdminUserApiKey(id, slug));
            await tran.ExecuteAsync();
            return Results.NoContent();
        });

        // Settings - Get
        app.MapGet("/admin/settings", async (HttpRequest req) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var settingsKey = RedisKeys.AdminSettings();
            var json = await db.StringGetAsync(settingsKey);

            if (json.IsNullOrEmpty)
            {
                // Fallback to PostgreSQL if Redis is empty
                if (adminStore != null)
                {
                    var pgJson = await adminStore.GetSettingAsync("admin:settings");
                    if (!string.IsNullOrEmpty(pgJson))
                    {
                        // Repopulate Redis from PostgreSQL
                        await db.StringSetAsync(settingsKey, pgJson);
                        json = pgJson;
                    }
                    else
                    {
                        // Return defaults
                        return Results.Ok(new { sessionTimeoutMinutes = 1440 }); // 24 hours default
                    }
                }
                else
                {
                    // Return defaults
                    return Results.Ok(new { sessionTimeoutMinutes = 1440 }); // 24 hours default
                }
            }

            try
            {
                var doc = JsonDocument.Parse(json.ToString());
                var sessionTimeoutMinutes = doc.RootElement.TryGetProperty("sessionTimeoutMinutes", out var timeoutEl)
                    ? timeoutEl.GetInt32()
                    : 1440;

                return Results.Ok(new { sessionTimeoutMinutes });
            }
            catch
            {
                return Results.Ok(new { sessionTimeoutMinutes = 1440 });
            }
        });

        // Settings - Save
        app.MapPost("/admin/settings", async (HttpRequest req) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            Dictionary<string, JsonElement>? doc;
            try { doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
            catch (Exception)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (doc == null || !doc.TryGetValue("sessionTimeoutMinutes", out var timeoutEl) ||
                timeoutEl.ValueKind != JsonValueKind.Number)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["sessionTimeoutMinutes"] = ["sessionTimeoutMinutes is required and must be a number"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var sessionTimeoutMinutes = timeoutEl.GetInt32();

            // Validate timeout value (must be one of: 15, 60, 720, 1440)
            if (sessionTimeoutMinutes != 15 && sessionTimeoutMinutes != 60 && sessionTimeoutMinutes != 720 &&
                sessionTimeoutMinutes != 1440)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["sessionTimeoutMinutes"] = ["sessionTimeoutMinutes must be 15, 60, 720, or 1440"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var settings = new { sessionTimeoutMinutes };
            var settingsJson = JsonSerializer.Serialize(settings);
            var settingsKey = RedisKeys.AdminSettings();

            await db.StringSetAsync(settingsKey, settingsJson);

            // Also persist to PostgreSQL for durability
            if (adminStore != null)
            {
                await adminStore.SaveSettingAsync("admin:settings", settingsJson);
            }

            try
            {
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "settings.updated",
                    Actor = "dashboard",
                    Details = new Dictionary<string, string>
                    {
                        ["sessionTimeoutMinutes"] = sessionTimeoutMinutes.ToString()
                    }
                });
            }
            catch { }

            return Results.Ok(new { ok = true, sessionTimeoutMinutes });
        });

        // Features - Get
        app.MapGet("/admin/features", async (HttpRequest req) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            var settingsKey = RedisKeys.AdminSettings();
            var json = await db.StringGetAsync(settingsKey);

            var importantLaunchesEnabled = false;
            var resetPasswordEnabled = false; // Default to disabled

            if (!json.IsNullOrEmpty)
            {
                try
                {
                    var doc = JsonDocument.Parse(json.ToString());
                    if (doc.RootElement.TryGetProperty("importantLaunchesEnabled", out var featureEl))
                    {
                        importantLaunchesEnabled = featureEl.GetBoolean();
                    }

                    if (doc.RootElement.TryGetProperty("resetPasswordEnabled", out var resetEl))
                    {
                        resetPasswordEnabled = resetEl.GetBoolean();
                    }
                }
                catch { }
            }
            else
            {
                // Fallback to PostgreSQL if Redis is empty
                if (adminStore != null)
                {
                    var pgJson = await adminStore.GetSettingAsync("admin:settings");
                    if (!string.IsNullOrEmpty(pgJson))
                    {
                        try
                        {
                            var doc = JsonDocument.Parse(pgJson);
                            if (doc.RootElement.TryGetProperty("importantLaunchesEnabled", out var featureEl))
                            {
                                importantLaunchesEnabled = featureEl.GetBoolean();
                            }

                            if (doc.RootElement.TryGetProperty("resetPasswordEnabled", out var resetEl))
                            {
                                resetPasswordEnabled = resetEl.GetBoolean();
                            }
                        }
                        catch { }
                    }
                }
            }

            return Results.Ok(new { importantLaunchesEnabled, resetPasswordEnabled });
        });

        // Features - Save
        app.MapPost("/admin/features", async (HttpRequest req) =>
        {
            if (!CheckAuthentication(req))
            {
                return ProblemDetailsHelpers.Unauthorized(
                    "Authentication required",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            Dictionary<string, JsonElement>? doc;
            try { doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>(); }
            catch (Exception)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["Invalid JSON body"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (doc == null)
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["body"] = ["Missing request body"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            // Extract feature flags (both are optional in the request)
            bool? importantLaunchesEnabled = null;
            bool? resetPasswordEnabled = null;

            if (doc.TryGetValue("importantLaunchesEnabled", out var featureEl) &&
                (featureEl.ValueKind == JsonValueKind.True || featureEl.ValueKind == JsonValueKind.False))
            {
                importantLaunchesEnabled = featureEl.GetBoolean();
            }

            if (doc.TryGetValue("resetPasswordEnabled", out var resetEl) && (resetEl.ValueKind == JsonValueKind.True ||
                                                                             resetEl.ValueKind == JsonValueKind.False))
            {
                resetPasswordEnabled = resetEl.GetBoolean();
            }

            // Get existing settings to merge
            var settingsKey = RedisKeys.AdminSettings();
            var existingJson = await db.StringGetAsync(settingsKey);

            var sessionTimeoutMinutes = 1440; // default
            var existingImportantLaunches = false;
            var existingResetPassword = false; // default disabled

            if (!existingJson.IsNullOrEmpty)
            {
                try
                {
                    var existingDoc = JsonDocument.Parse(existingJson.ToString());
                    if (existingDoc.RootElement.TryGetProperty("sessionTimeoutMinutes", out var timeoutEl))
                    {
                        sessionTimeoutMinutes = timeoutEl.GetInt32();
                    }

                    if (existingDoc.RootElement.TryGetProperty("importantLaunchesEnabled", out var ilEl))
                    {
                        existingImportantLaunches = ilEl.GetBoolean();
                    }

                    if (existingDoc.RootElement.TryGetProperty("resetPasswordEnabled", out var rpEl))
                    {
                        existingResetPassword = rpEl.GetBoolean();
                    }
                }
                catch { }
            }

            // Merge settings - use new values if provided, otherwise keep existing
            var finalImportantLaunches = importantLaunchesEnabled ?? existingImportantLaunches;
            var finalResetPassword = resetPasswordEnabled ?? existingResetPassword;

            var settings = new
            {
                sessionTimeoutMinutes,
                importantLaunchesEnabled = finalImportantLaunches,
                resetPasswordEnabled = finalResetPassword
            };
            var settingsJson = JsonSerializer.Serialize(settings);

            await db.StringSetAsync(settingsKey, settingsJson);

            // Also persist to PostgreSQL for durability
            if (adminStore != null)
            {
                await adminStore.SaveSettingAsync("admin:settings", settingsJson);
            }

            try
            {
                var details = new Dictionary<string, string>
                {
                    ["importantLaunchesEnabled"] = finalImportantLaunches.ToString(),
                    ["resetPasswordEnabled"] = finalResetPassword.ToString()
                };
                await auditStore.AppendAsync(new AuditEntryDto
                {
                    Timestamp = DateTime.Now,
                    Category = "admin",
                    Action = "features.updated",
                    Actor = "dashboard",
                    Details = details
                });
            }
            catch { }

            return Results.Ok(new
            {
                ok = true,
                importantLaunchesEnabled = finalImportantLaunches,
                resetPasswordEnabled = finalResetPassword
            });
        });

        // Remember-me token management endpoints

        // POST /admin/auth/remember-me - Create a new remember-me token
        app.MapPost("/admin/auth/remember-me", async (HttpRequest req) =>
        {
            if (adminStore is null)
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "AdminDurableStore not configured",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            try
            {
                var doc = await req.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                var userId =
                    doc != null && doc.TryGetValue("userId", out var uidEl) && uidEl.ValueKind == JsonValueKind.String
                        ? uidEl.GetString()
                        : null;
                var tokenHash =
                    doc != null && doc.TryGetValue("tokenHash", out var thEl) && thEl.ValueKind == JsonValueKind.String
                        ? thEl.GetString()
                        : null;
                var expiresUtc =
                    doc != null && doc.TryGetValue("expiresUtc", out var expEl) &&
                    expEl.ValueKind == JsonValueKind.String
                        ? DateTime.Parse(expEl.GetString() ?? "")
                        : DateTime.UtcNow.AddDays(90);

                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tokenHash))
                {
                    return ProblemDetailsHelpers.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["userId"] = string.IsNullOrWhiteSpace(userId) ? ["userId is required"] : Array.Empty<string>(),
                            ["tokenHash"] = string.IsNullOrWhiteSpace(tokenHash) ? ["tokenHash is required"] : Array.Empty<string>()
                        }.Where(x => x.Value.Length > 0).ToDictionary(x => x.Key, x => x.Value),
                        eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                var token = new RememberMeToken
                {
                    UserId = userId,
                    TokenHash = tokenHash,
                    CreatedUtc = DateTime.UtcNow,
                    ExpiresUtc = expiresUtc
                };

                await adminStore.CreateRememberMeTokenAsync(token);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create remember-me token");
                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to create remember-me token",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // GET /admin/auth/remember-me?tokenHash=... - Validate and return user info for remember-me token
        app.MapGet("/admin/auth/remember-me", async (HttpRequest req, string tokenHash) =>
        {
            if (adminStore is null)
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "AdminDurableStore not configured",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (string.IsNullOrWhiteSpace(tokenHash))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["tokenHash"] = ["tokenHash is required"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            try
            {
                var token = await adminStore.GetRememberMeTokenAsync(tokenHash);
                if (token is null)
                {
                    return ProblemDetailsHelpers.NotFound(
                        "Remember-me token not found",
                        eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                // Update last used timestamp
                await adminStore.UpdateRememberMeTokenLastUsedAsync(token.Id, DateTime.UtcNow);

                // Get user info from Redis - find user by matching userId
                var members = await db.SetMembersAsync(RedisKeys.AdminUsersSet());
                User? user = null;
                foreach (var m in members)
                {
                    var json = await db.StringGetAsync(RedisKeys.AdminUser(m!));
                    if (json.IsNullOrEmpty)
                    {
                        continue;
                    }

                    try
                    {
                        var u = JsonSerializer.Deserialize<User>(json!);
                        if (u is not null && string.Equals(u.Id, token.UserId, StringComparison.Ordinal))
                        {
                            user = u;
                            break;
                        }
                    }
                    catch { }
                }

                if (user is null || user.Status != UserStatus.Active)
                {
                    // User deleted or inactive - clean up token
                    await adminStore.DeleteRememberMeTokenAsync(token.Id);
                    return ProblemDetailsHelpers.NotFound(
                        $"User {token.UserId} not found or inactive",
                        eventCode: EventCodes.AdminProjectsUsers.UserManagement.UserDeleted,
                        instance: req.Path,
                        traceId: req.HttpContext.TraceIdentifier);
                }

                return Results.Ok(new
                {
                    userId = token.UserId,
                    username = user.Id,
                    role = user.AccountRole.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to validate remember-me token");
                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to validate remember-me token",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });

        // DELETE /admin/auth/remember-me?tokenHash=... - Delete a specific remember-me token (logout)
        app.MapDelete("/admin/auth/remember-me", async (HttpRequest req, string tokenHash) =>
        {
            if (adminStore is null)
            {
                return ProblemDetailsHelpers.InternalServerError(
                    "AdminDurableStore not configured",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            if (string.IsNullOrWhiteSpace(tokenHash))
            {
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["tokenHash"] = ["tokenHash is required"] },
                    eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }

            try
            {
                var token = await adminStore.GetRememberMeTokenAsync(tokenHash);
                if (token is not null)
                {
                    await adminStore.DeleteRememberMeTokenAsync(token.Id);
                }

                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete remember-me token");
                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to delete remember-me token",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier);
            }
        });
    }
}
