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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using PlaywrightHub.Infrastructure.Services;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     Endpoints for password reset flow (forgot password, reset password)
/// </summary>
public static class PasswordResetEndpoints
{
    private const int MaxResetAttemptsPerHour = 3;
    private const int ResetTokenExpiryMinutes = 60;

    public static void MapPasswordResetEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/auth");

        // POST /admin/auth/forgot-password
        group.MapPost("/forgot-password", ForgotPassword);

        // GET /admin/auth/reset-password/{token}
        group.MapGet("/reset-password/{token}", ValidateResetToken);

        // POST /admin/auth/reset-password/{token}
        group.MapPost("/reset-password/{token}", ResetPassword);
    }

    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IDatabase db,
        [FromServices] IEmailService emailService,
        [FromServices] IConfiguration config,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("PasswordReset");
        var chunkedLogger = new ChunkedLogger(logger, "PasswordReset.Forgot");

        chunkedLogger.LogMilestone(
            EventCodes.PasswordReset.ResetRequested,
            "email={Email}",
            request.Email);

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                "error=MissingEmail");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["email"] = ["Email is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (!AdminValidation.TryValidateEmail(request.Email, out var validationError))
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                "error={ValidationError} email={Email}",
                validationError, request.Email);
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["email"] = [validationError] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var emailLower = request.Email.Trim().ToLowerInvariant();

        try
        {
            // Check rate limit
            var rateLimitKey = RedisKeys.AdminPasswordResetRateLimit(emailLower);
            var attempts = await db.StringGetAsync(rateLimitKey);
            if (attempts.HasValue && int.TryParse(attempts, out var count) && count >= MaxResetAttemptsPerHour)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.ResetRateLimitExceeded,
                    "email={Email} attempts={Count}",
                    emailLower, count);

                // Return success to prevent email enumeration
                return Results.Ok(new
                {
                    message = "If an account exists with this email, you will receive a password reset link."
                });
            }

            // Check if a user exists by email
            var userIdValue = await db.StringGetAsync(RedisKeys.AdminUserByEmail(emailLower));
            if (userIdValue.IsNullOrEmpty)
            {
                // Return success even if the user doesn't exist (security: don't reveal if email exists)
                return Results.Ok(new
                {
                    message = "If an account exists with this email, you will receive a password reset link."
                });
            }

            var userId = userIdValue.ToString();

            // Get user details for email
            var userJson = await db.StringGetAsync(RedisKeys.AdminUser(userId));
            if (userJson.IsNullOrEmpty)
            {
                return Results.Ok(new
                {
                    message = "If an account exists with this email, you will receive a password reset link."
                });
            }

            var userDoc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(userJson!);
            var username = userDoc != null && userDoc.TryGetValue("username", out var un) &&
                           un.ValueKind == JsonValueKind.String
                ? un.GetString() ?? userId
                : userId;

            // Delete any existing reset token for this email
            var existingToken = await db.StringGetAsync(RedisKeys.AdminPasswordResetByEmail(emailLower));
            if (!existingToken.IsNullOrEmpty)
            {
                await db.KeyDeleteAsync(RedisKeys.AdminPasswordResetToken(existingToken!));
            }

            // Generate new reset token
            var token = Guid.NewGuid().ToString("n");
            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(ResetTokenExpiryMinutes);

            var resetData = new
            {
                token,
                email = request.Email,
                userId,
                username,
                createdUtc = now,
                expiresUtc = expires
            };

            var resetJson = JsonSerializer.Serialize(resetData);
            var ttl = TimeSpan.FromMinutes(ResetTokenExpiryMinutes);

            // Store reset token
            var tran = db.CreateTransaction();
            _ = tran.StringSetAsync(RedisKeys.AdminPasswordResetToken(token), resetJson, ttl);
            _ = tran.StringSetAsync(RedisKeys.AdminPasswordResetByEmail(emailLower), token, ttl);
            var ok = await tran.ExecuteAsync();

            if (!ok)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.PasswordResetFailed,
                    "error=RedisTransactionFailed userId={UserId}", userId);

                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to create password reset token",
                    eventCode: EventCodes.PasswordReset.PasswordResetFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            // Increment rate limit counter
            await db.StringIncrementAsync(rateLimitKey);
            await db.KeyExpireAsync(rateLimitKey, TimeSpan.FromHours(1));

            // Send reset email
            var dashboardUrl = config["AGENIX_DASHBOARD_PUBLIC_URL"] ?? "http://localhost:5001";
            var resetUrl = $"{dashboardUrl}/reset-password/{token}";

            try
            {
                await emailService.SendPasswordResetEmailAsync(request.Email, username, token, resetUrl);
            }
            catch (Exception ex)
            {
                // Log error but don't expose to user
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.ResetRequestDenied,
                    ex,
                    "error={Error} email={Email}",
                    ex.Message, request.Email);
                // Still return success to prevent email enumeration
            }

            return Results.Ok(new
            {
                message = "If an account exists with this email, you will receive a password reset link."
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                ex,
                "error={Error}",
                ex.Message);
            return ProblemDetailsHelpers.InternalServerError(
                "An error occurred processing your request",
                eventCode: EventCodes.PasswordReset.PasswordResetFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> ValidateResetToken(
        [FromRoute] string token,
        [FromServices] IDatabase db,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("PasswordReset");
        var chunkedLogger = new ChunkedLogger(logger, "PasswordReset.Validate");

        chunkedLogger.LogMilestone(
            EventCodes.PasswordReset.TokenValidated,
            "token={Token}",
            token?.Substring(0, Math.Min(8, token?.Length ?? 0)) + "...");

        if (string.IsNullOrWhiteSpace(token))
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.TokenInvalid,
                "error=MissingToken");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["token"] = ["Token is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            var resetJson = await db.StringGetAsync(RedisKeys.AdminPasswordResetToken(token));
            if (resetJson.IsNullOrEmpty)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenInvalid,
                    "token={Token}",
                    token.Substring(0, Math.Min(8, token.Length)) + "...");
                return ProblemDetailsHelpers.NotFound(
                    "Invalid or expired reset token",
                    eventCode: EventCodes.PasswordReset.TokenInvalid,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var resetData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(resetJson!);
            if (resetData == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenInvalid,
                    "error=InvalidJsonFormat");
                return ProblemDetailsHelpers.NotFound(
                    "Invalid reset token",
                    eventCode: EventCodes.PasswordReset.TokenInvalid,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var expiresUtc = resetData.TryGetValue("expiresUtc", out var exp) && exp.ValueKind == JsonValueKind.String
                ? DateTime.Parse(exp.GetString()!)
                : DateTime.MinValue;

            if (expiresUtc < DateTime.UtcNow)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenExpired,
                    "token={Token} expiresUtc={ExpiresUtc}",
                    token.Substring(0, Math.Min(8, token.Length)) + "...",
                    expiresUtc);

                // Clean up expired token
                await db.KeyDeleteAsync(RedisKeys.AdminPasswordResetToken(token));
                var email = resetData.TryGetValue("email", out var em) && em.ValueKind == JsonValueKind.String
                    ? em.GetString()
                    : null;
                if (!string.IsNullOrEmpty(email))
                {
                    await db.KeyDeleteAsync(RedisKeys.AdminPasswordResetByEmail(email.ToLowerInvariant()));
                }

                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["token"] = ["Reset token has expired"] },
                    eventCode: EventCodes.PasswordReset.TokenExpired,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var email2 = resetData.TryGetValue("email", out var em2) && em2.ValueKind == JsonValueKind.String
                ? em2.GetString() ?? string.Empty
                : string.Empty;
            var username = resetData.TryGetValue("username", out var un) && un.ValueKind == JsonValueKind.String
                ? un.GetString() ?? string.Empty
                : string.Empty;

            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.TokenValidated,
                "email={Email} expiresUtc={ExpiresUtc}",
                email2,
                expiresUtc);

            return Results.Ok(new { email = email2, username, expiresUtc });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.TokenInvalid,
                ex,
                "error={Error}",
                ex.Message);
            return ProblemDetailsHelpers.InternalServerError(
                "An error occurred validating the token",
                eventCode: EventCodes.PasswordReset.PasswordResetFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> ResetPassword(
        [FromRoute] string token,
        [FromBody] ResetPasswordRequest request,
        [FromServices] IDatabase db,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("PasswordReset");
        var chunkedLogger = new ChunkedLogger(logger, "PasswordReset.Reset");

        chunkedLogger.LogMilestone(
            EventCodes.PasswordReset.ResetRequested,
            "token={Token}",
            token?.Substring(0, Math.Min(8, token?.Length ?? 0)) + "...");

        if (string.IsNullOrWhiteSpace(token))
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                "error=MissingToken");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["token"] = ["Token is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                "error=MissingPassword");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = ["Password is required"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (request.Password.Length < 8)
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                "error=PasswordTooShort length={Length}",
                request.Password.Length);
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = ["Password must be at least 8 characters long"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        if (request.Password != request.ConfirmPassword)
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                "error=PasswordMismatch");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["confirmPassword"] = ["Passwords do not match"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            // Validate token
            var resetJson = await db.StringGetAsync(RedisKeys.AdminPasswordResetToken(token));
            if (resetJson.IsNullOrEmpty)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenInvalid,
                    "token={Token}",
                    token[..Math.Min(8, token.Length)] + "...");
                return ProblemDetailsHelpers.NotFound(
                    "Invalid or expired reset token",
                    eventCode: EventCodes.PasswordReset.TokenInvalid,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var resetData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(resetJson!);
            if (resetData == null)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenInvalid,
                    "error=InvalidJsonFormat");
                return ProblemDetailsHelpers.NotFound(
                    "Invalid reset token",
                    eventCode: EventCodes.PasswordReset.TokenInvalid,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var expiresUtc = resetData.TryGetValue("expiresUtc", out var exp) && exp.ValueKind == JsonValueKind.String
                ? DateTime.Parse(exp.GetString()!)
                : DateTime.MinValue;

            if (expiresUtc < DateTime.UtcNow)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenExpired,
                    "token={Token} expiresUtc={ExpiresUtc}",
                    token.Substring(0, Math.Min(8, token.Length)) + "...",
                    expiresUtc);
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["token"] = ["Reset token has expired"] },
                    eventCode: EventCodes.PasswordReset.TokenExpired,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var userId = resetData.TryGetValue("userId", out var uid) && uid.ValueKind == JsonValueKind.String
                ? uid.GetString()
                : null;

            if (string.IsNullOrEmpty(userId))
            {
                chunkedLogger.LogMilestone(
                    EventCodes.PasswordReset.TokenInvalid,
                    "error=MissingUserId token={Token}",
                    token.Substring(0, Math.Min(8, token.Length)) + "...");
                return ProblemDetailsHelpers.ValidationProblem(
                    new Dictionary<string, string[]> { ["token"] = ["Invalid reset token"] },
                    eventCode: EventCodes.PasswordReset.TokenInvalid,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.PasswordResetCompleted,
                "userId={UserId}",
                userId);

            // Hash the new password using PBKDF2 (same as the existing implementation)
            var salt = new byte[32];
            RandomNumberGenerator.Fill(salt);
            const int iterations = 100_000;

            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(request.Password),
                salt,
                iterations,
                HashAlgorithmName.SHA256);

            var hash = pbkdf2.GetBytes(32);

            var passwordData = new
            {
                alg = "pbkdf2-sha256",
                salt = Convert.ToBase64String(salt),
                hash = Convert.ToBase64String(hash),
                iter = iterations,
                createdUtc = DateTime.UtcNow
            };

            var passwordJson = JsonSerializer.Serialize(passwordData);

            // Update password
            await db.StringSetAsync(RedisKeys.AdminUserPassword(userId), passwordJson);

            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.PasswordResetCompleted,
                "userId={UserId}",
                userId);

            // Delete reset token (one-time use)
            await db.KeyDeleteAsync(RedisKeys.AdminPasswordResetToken(token));
            var email = resetData.TryGetValue("email", out var em) && em.ValueKind == JsonValueKind.String
                ? em.GetString()
                : null;
            if (!string.IsNullOrEmpty(email))
            {
                await db.KeyDeleteAsync(RedisKeys.AdminPasswordResetByEmail(email.ToLowerInvariant()));
            }

            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.PasswordResetCompleted,
                "userId={UserId}",
                userId);

            return Results.Ok(new { message = "Password has been reset successfully" });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(
                EventCodes.PasswordReset.ResetRequestDenied,
                ex,
                "error={Error}",
                ex.Message);
            return ProblemDetailsHelpers.InternalServerError(
                "An error occurred resetting your password",
                eventCode: EventCodes.PasswordReset.PasswordResetFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }
}

public record ForgotPasswordRequest
{
    public string Email { get; init; } = string.Empty;
}

public record ResetPasswordRequest
{
    public string Password { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;
}
