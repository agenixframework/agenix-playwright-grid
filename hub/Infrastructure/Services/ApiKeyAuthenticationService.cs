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
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Service for validating API keys and authorizing access to projects.
/// </summary>
public class ApiKeyAuthenticationService(IConnectionMultiplexer redis, ILogger<ApiKeyAuthenticationService> logger)
    : IApiKeyAuthenticationService
{
    private readonly IDatabase _redis = redis.GetDatabase();

    /// <inheritdoc />
    public async Task<ApiKeyAuthResult> ValidateApiKeyAsync(string apiKey, string projectKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.InvalidApiKey };
        }

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.ProjectNotFound };
        }

        // Hash the API key using SHA256
        string apiKeyHash;
        try
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            apiKeyHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to hash API key");
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.InvalidApiKey };
        }

        // Find the API key by scanning all users
        // In a production system with many users, this could be optimized with a reverse index
        var (userId, keyFound) = await FindUserByApiKeyHashAsync(apiKeyHash);

        if (!keyFound || userId == null)
        {
            logger.LogWarning("API key not found or revoked");
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.InvalidApiKey };
        }

        // Validate user and project access
        return await ValidateUserAndProjectAccessAsync(userId, projectKey);
    }

    /// <inheritdoc />
    public async Task<ApiKeyAuthResult> ValidateUserAccessAsync(string userId, string projectKey)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.UserNotFound };
        }

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.ProjectNotFound };
        }

        // Validate user and project access
        return await ValidateUserAndProjectAccessAsync(userId, projectKey);
    }

    /// <summary>
    ///     Common validation logic for user and project access.
    ///     Used by both API key and dashboard authentication paths.
    /// </summary>
    private async Task<ApiKeyAuthResult> ValidateUserAndProjectAccessAsync(string userId, string projectKey)
    {
        // Load user from Redis
        var userKey = RedisKeys.AdminUser(userId);
        var userJson = await _redis.StringGetAsync(userKey);

        if (userJson.IsNullOrEmpty)
        {
            logger.LogWarning("User {UserId} not found", userId);
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.UserNotFound };
        }

        User? user;
        try
        {
            user = JsonSerializer.Deserialize<User>(userJson!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize user {UserId}", userId);
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.UserNotFound };
        }

        if (user is not { Status: UserStatus.Active })
        {
            logger.LogWarning("User {UserId} is not active (status: {Status})", userId, user?.Status);
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.UserDisabled };
        }

        // Check project membership
        var membershipKey = RedisKeys.AdminMembership(projectKey, userId);
        var membershipJson = await _redis.StringGetAsync(membershipKey);

        if (membershipJson.IsNullOrEmpty)
        {
            logger.LogWarning("User {UserId} is not a member of project {ProjectKey}", userId, projectKey);
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.NoProjectAccess };
        }

        Membership? membership;
        try
        {
            membership = JsonSerializer.Deserialize<Membership>(membershipJson!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize membership for user {UserId} and project {ProjectKey}", userId,
                projectKey);
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.NoProjectAccess };
        }

        if (membership == null)
        {
            logger.LogWarning("Membership not found for user {UserId} and project {ProjectKey}", userId, projectKey);
            return new ApiKeyAuthResult { IsValid = false, FailureReason = AuthFailureReason.NoProjectAccess };
        }

        // Success - user is active and is a member of the project
        logger.LogDebug("Access validated successfully for user {UserId} in project {ProjectKey} with role {Role}",
            userId, projectKey, membership.Role);

        return new ApiKeyAuthResult
        {
            IsValid = true,
            UserId = userId,
            Username = user.Username,
            ProjectRole = membership.Role
        };
    }

    /// <summary>
    ///     Finds a user by API key hash using a reverse index for O(1) lookup.
    ///     Falls back to linear scan if the reverse index is not found (for backward compatibility).
    /// </summary>
    private async Task<(string? UserId, bool Found)> FindUserByApiKeyHashAsync(string hash)
    {
        // Try the reverse index first (fast path - O(1))
        var reverseIndexKey = RedisKeys.AdminApiKeyHashIndex(hash);
        var userId = await _redis.StringGetAsync(reverseIndexKey);

        if (!userId.IsNullOrEmpty)
        {
            logger.LogDebug("Found API key via reverse index for hash {Hash}", hash);
            return (userId.ToString(), true);
        }

        // Fallback to linear scan (for backward compatibility with existing API keys)
        logger.LogWarning("API key hash {Hash} not found in reverse index, falling back to linear scan", hash);

        var server = _redis.Multiplexer.GetServer(_redis.Multiplexer.GetEndPoints().First());

        await foreach (var key in server.KeysAsync(pattern: "admin:user:apikeys:*"))
        {
            var keyStr = key.ToString();
            if (!keyStr.StartsWith("admin:user:apikeys:"))
            {
                continue;
            }

            var scanUserId = keyStr["admin:user:apikeys:".Length..];

            // Get all API key slugs for this user
            var keySlugs = await _redis.SetMembersAsync(keyStr);

            foreach (var keySlug in keySlugs)
            {
                var apiKeyKey = RedisKeys.AdminUserApiKey(scanUserId, keySlug.ToString());
                var apiKeyJson = await _redis.StringGetAsync(apiKeyKey);

                if (apiKeyJson.IsNullOrEmpty)
                {
                    continue;
                }

                try
                {
                    var apiKeyData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(apiKeyJson!);
                    if (apiKeyData != null &&
                        apiKeyData.TryGetValue("hash", out var hashEl) &&
                        hashEl.ValueKind == JsonValueKind.String)
                    {
                        var storedHash = hashEl.GetString();
                        if (string.Equals(storedHash, hash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Create a reverse index for future lookups
                            await _redis.StringSetAsync(reverseIndexKey, scanUserId, TimeSpan.FromDays(365));
                            logger.LogInformation("Created reverse index for API key hash {Hash}", hash);

                            return (scanUserId, true);
                        }
                    }
                }
                catch
                {
                    // Skip malformed entries
                }
            }
        }

        return (null, false);
    }
}
