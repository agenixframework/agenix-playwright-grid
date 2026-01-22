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

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Service for validating API keys and authorizing access to projects.
/// </summary>
public interface IApiKeyAuthenticationService
{
    /// <summary>
    ///     Validates an API key and checks if the associated user has access to the specified project.
    /// </summary>
    /// <param name="apiKey">The API key to validate</param>
    /// <param name="projectKey">The project key to check access for</param>
    /// <returns>Authentication result with user information or failure reason</returns>
    Task<ApiKeyAuthResult> ValidateApiKeyAsync(string apiKey, string projectKey);

    /// <summary>
    ///     Validates a user ID (from dashboard x-user-id header) and checks if the user has access to the specified project.
    ///     This is used for dashboard requests where the user is already authenticated via cookies.
    /// </summary>
    /// <param name="userId">The user ID to validate</param>
    /// <param name="projectKey">The project key to check access for</param>
    /// <returns>Authentication result with user information or failure reason</returns>
    Task<ApiKeyAuthResult> ValidateUserAccessAsync(string userId, string projectKey);
}

/// <summary>
///     Result of API key authentication and authorization.
/// </summary>
public record ApiKeyAuthResult
{
    /// <summary>
    ///     Whether the API key is valid and the user has access to the project.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    ///     The user ID associated with the API key (if valid).
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    ///     The username associated with the API key (if valid).
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    ///     The user's role in the project (if valid).
    /// </summary>
    public ProjectRole? ProjectRole { get; init; }

    /// <summary>
    ///     The reason for authentication failure (if not valid).
    /// </summary>
    public AuthFailureReason? FailureReason { get; init; }
}

/// <summary>
///     Reasons why API key authentication might fail.
/// </summary>
public enum AuthFailureReason
{
    /// <summary>API key is invalid or does not exist</summary>
    InvalidApiKey,

    /// <summary>API key has been revoked</summary>
    RevokedApiKey,

    /// <summary>User associated with API key not found</summary>
    UserNotFound,

    /// <summary>User account is disabled or not active</summary>
    UserDisabled,

    /// <summary>Project does not exist</summary>
    ProjectNotFound,

    /// <summary>User is not a member of the project</summary>
    NoProjectAccess
}
