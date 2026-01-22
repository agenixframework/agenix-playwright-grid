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
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using StackExchange.Redis;

namespace Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;

/// <summary>
///     Common Redis helper methods for integration tests.
///     Provides reusable operations for user, API key, and membership setup.
/// </summary>
public static class RedisHelpers
{
    /// <summary>
    ///     Creates a project in Redis with the specified parameters.
    ///     Required for authentication to work - Hub checks if project exists before validating API keys.
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="projectKey">The project key (unique identifier).</param>
    /// <param name="projectName">The project display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CreateProjectAsync(
        IDatabase redis,
        string projectKey,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        var projectData = new
        {
            Id = Guid.NewGuid().ToString("N"),
            Key = projectKey,
            Name = projectName,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(projectData);
        await redis.StringSetAsync(RedisKeys.AdminProject(projectKey), json);
        await redis.StringSetAsync(RedisKeys.AdminProjectByName(projectName.ToLowerInvariant()), projectKey);
        await redis.SetAddAsync(RedisKeys.AdminProjectsSet(), projectKey);
    }

    /// <summary>
    ///     Creates a user in Redis with the specified parameters.
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="userId">The unique user identifier.</param>
    /// <param name="username">The username.</param>
    /// <param name="accountRole">The user's account role. Defaults to User.</param>
    /// <param name="status">The user's status. Defaults to Active.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CreateUserAsync(
        IDatabase redis,
        string userId,
        string username,
        AccountRole accountRole = AccountRole.User,
        UserStatus status = UserStatus.Active,
        CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            Id = userId,
            Username = username,
            Status = status,
            AccountRole = accountRole,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await redis.StringSetAsync(RedisKeys.AdminUser(userId), JsonSerializer.Serialize(user));
        await redis.SetAddAsync(RedisKeys.AdminUsersSet(), userId);
        await redis.StringSetAsync(RedisKeys.AdminUserByUsername(username.ToLowerInvariant()), userId);
    }

    /// <summary>
    ///     Creates an API key for a user in Redis.
    ///     The key is SHA256 hashed before storage.
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="userId">The user ID who owns the API key.</param>
    /// <param name="apiKeyName">The name of the API key.</param>
    /// <param name="apiKeyValue">The plain text API key value (will be hashed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SHA256 hash of the API key.</returns>
    public static async Task<string> CreateApiKeyAsync(
        IDatabase redis,
        string userId,
        string apiKeyName,
        string apiKeyValue,
        CancellationToken cancellationToken = default)
    {
        // Hash API key with SHA256
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKeyValue))).ToLowerInvariant();

        // Create API key entry
        var keyEntry = new
        {
            name = apiKeyName,
            nameLower = apiKeyName.ToLowerInvariant(),
            createdUtc = DateTime.UtcNow,
            alg = "sha256",
            hash
        };

        await redis.StringSetAsync(RedisKeys.AdminUserApiKey(userId, apiKeyName), JsonSerializer.Serialize(keyEntry));
        await redis.SetAddAsync(RedisKeys.AdminUserApiKeys(userId), apiKeyName);
        await redis.StringSetAsync(RedisKeys.AdminApiKeyHashIndex(hash), userId);

        return hash;
    }

    /// <summary>
    ///     Creates a project membership for a user in Redis.
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="projectKey">The project key.</param>
    /// <param name="role">The user's role in the project. Defaults to Client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CreateMembershipAsync(
        IDatabase redis,
        string userId,
        string projectKey,
        ProjectRole role = ProjectRole.Client,
        CancellationToken cancellationToken = default)
    {
        var membership = new Membership
        {
            UserId = userId,
            ProjectKey = projectKey,
            Role = role,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        // Key format must match Hub's expectation: admin:membership:{projectKey}:{userId}
        await redis.StringSetAsync(RedisKeys.AdminMembership(projectKey, userId), JsonSerializer.Serialize(membership));
        await redis.SetAddAsync(RedisKeys.AdminMembersByProject(projectKey), userId);
        await redis.SetAddAsync(RedisKeys.AdminProjectsByUser(userId), projectKey);
    }

    /// <summary>
    ///     Creates a complete test user with API key and project membership.
    ///     This is a convenience method that combines CreateUserAsync, CreateApiKeyAsync, and CreateMembershipAsync.
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="username">The username.</param>
    /// <param name="apiKeyName">The name of the API key.</param>
    /// <param name="apiKeyValue">The plain text API key value.</param>
    /// <param name="projectKey">The project key for membership.</param>
    /// <param name="projectRole">The user's role in the project. Defaults to Client.</param>
    /// <param name="accountRole">The user's account role. Defaults to User.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SHA256 hash of the API key.</returns>
    public static async Task<string> CreateTestUserWithApiKeyAsync(
        IDatabase redis,
        string userId,
        string username,
        string apiKeyName,
        string apiKeyValue,
        string projectKey,
        ProjectRole projectRole = ProjectRole.Client,
        AccountRole accountRole = AccountRole.User,
        CancellationToken cancellationToken = default)
    {
        await CreateUserAsync(redis, userId, username, accountRole, cancellationToken: cancellationToken);
        var hash = await CreateApiKeyAsync(redis, userId, apiKeyName, apiKeyValue, cancellationToken);
        await CreateMembershipAsync(redis, userId, projectKey, projectRole, cancellationToken);

        return hash;
    }

    /// <summary>
    ///     Creates a test user with an API key and project membership, returning full user info.
    ///     This is a convenience wrapper around CreateTestUserWithApiKeyAsync that returns TestUserInfo.
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="username">The username.</param>
    /// <param name="apiKeyName">The API key name.</param>
    /// <param name="projectKey">The project key for membership.</param>
    /// <param name="projectRole">The project role for the user. Defaults to Client.</param>
    /// <param name="userStatus">The user status (Active or Disabled). Defaults to Active.</param>
    /// <param name="accountRole">The account role. Defaults to User.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A TestUserInfo object containing user details and API key.</returns>
    public static async Task<TestUserInfo> CreateTestUserWithApiKeyAsync(
        IDatabase redis,
        string userId,
        string username,
        string apiKeyName,
        string projectKey,
        ProjectRole projectRole = ProjectRole.Client,
        UserStatus userStatus = UserStatus.Active,
        AccountRole accountRole = AccountRole.User,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GenerateApiKey();
        await CreateUserAsync(redis, userId, username, accountRole, userStatus, cancellationToken);
        var hash = await CreateApiKeyAsync(redis, userId, apiKeyName, apiKey, cancellationToken);
        await CreateMembershipAsync(redis, userId, projectKey, projectRole, cancellationToken);

        return new TestUserInfo
        {
            UserId = userId,
            Username = username,
            ApiKeyName = apiKeyName,
            ApiKey = apiKey,
            ApiKeyHash = hash,
            ProjectKey = projectKey
        };
    }

    /// <summary>
    ///     Cleans up all Redis data for a user (user, API keys, memberships).
    /// </summary>
    /// <param name="redis">The Redis database instance.</param>
    /// <param name="userId">The user ID to clean up.</param>
    /// <param name="projectKey">The project key to clean up membership for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CleanupUserDataAsync(
        IDatabase redis,
        string userId,
        string projectKey,
        CancellationToken cancellationToken = default)
    {
        // Get all API key names
        var apiKeyNames = await redis.SetMembersAsync(RedisKeys.AdminUserApiKeys(userId));

        // Delete all API keys
        foreach (var keyName in apiKeyNames)
        {
            var keyNameStr = keyName.ToString();
            var json = await redis.StringGetAsync(RedisKeys.AdminUserApiKey(userId, keyNameStr));
            if (!json.IsNullOrEmpty)
            {
                using var doc = JsonDocument.Parse(json.ToString());
                if (doc.RootElement.TryGetProperty("hash", out var hashProp))
                {
                    await redis.KeyDeleteAsync(RedisKeys.AdminApiKeyHashIndex(hashProp.GetString()!));
                }
            }
            await redis.KeyDeleteAsync(RedisKeys.AdminUserApiKey(userId, keyNameStr));
        }

        // Delete API key set
        await redis.KeyDeleteAsync(RedisKeys.AdminUserApiKeys(userId));

        // Delete project membership
        await redis.KeyDeleteAsync(RedisKeys.AdminMembership(projectKey, userId));
        await redis.SetRemoveAsync(RedisKeys.AdminMembersByProject(projectKey), userId);
        await redis.SetRemoveAsync(RedisKeys.AdminProjectsByUser(userId), projectKey);

        // Delete user
        var userJson = await redis.StringGetAsync(RedisKeys.AdminUser(userId));
        if (!userJson.IsNullOrEmpty)
        {
            var user = JsonSerializer.Deserialize<User>(userJson!);
            if (user != null)
            {
                await redis.KeyDeleteAsync(RedisKeys.AdminUserByUsername(user.Username.ToLowerInvariant()));
                if (!string.IsNullOrEmpty(user.Email))
                {
                    await redis.KeyDeleteAsync(RedisKeys.AdminUserByEmail(user.Email.ToLowerInvariant()));
                }
            }
        }
        await redis.KeyDeleteAsync(RedisKeys.AdminUser(userId));
        await redis.SetRemoveAsync(RedisKeys.AdminUsersSet(), userId);
    }

    /// <summary>
    ///     Generates a random API key value for testing.
    /// </summary>
    /// <param name="prefix">The prefix for the API key. Defaults to "test-key-".</param>
    /// <returns>A random API key string.</returns>
    public static string GenerateApiKey(string prefix = "test-key-")
    {
        return prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }
}
