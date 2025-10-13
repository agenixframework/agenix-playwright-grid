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

using Agenix.PlaywrightGrid.Shared.Logging;
using PlaywrightHub.Infrastructure.Services;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
///     Extension methods for API key authorization.
/// </summary>
public static class AuthorizationExtensions
{
    /// <summary>
    ///     Authorizes an API key for accessing a project.
    ///     All project members (any role) are allowed to access client APIs.
    ///     Falls back to x-user-id header validation for dashboard requests.
    /// </summary>
    /// <param name="req">The HTTP request</param>
    /// <param name="projectKey">The project key to check access for</param>
    /// <param name="authService">The authentication service</param>
    /// <returns>Error result if authorization fails, null if successful</returns>
    public static async Task<IResult?> AuthorizeApiKeyAsync(
        this HttpRequest req,
        string projectKey,
        IApiKeyAuthenticationService authService)
    {
        // Check if authorization already performed for this request
        var cacheKey = $"Auth:{projectKey}";
        if (req.HttpContext.Items.ContainsKey(cacheKey))
        {
            // Authorization already performed, check if it succeeded
            return req.HttpContext.Items["AuthUserId"] == null
                ? ProblemDetailsHelpers.Unauthorized(
                    "Authorization already failed for this request",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier)
                : null; // Success
        }

        // Extract Bearer token from the Authorization header
        var authHeader = req.Headers.Authorization.FirstOrDefault();

        // If no Authorization header, check for x-user-id (dashboard authentication)
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // Try x-user-id header (dashboard forwarding authenticated user)
            var userId = req.Headers["x-user-id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                // Dashboard is forwarding an authenticated user - validate user exists and has project access
                var dashboardAuthResult = await authService.ValidateUserAccessAsync(userId, projectKey);

                if (!dashboardAuthResult.IsValid)
                {
                    // Map failure reasons to appropriate HTTP status codes and event codes
                    return dashboardAuthResult.FailureReason switch
                    {
                        AuthFailureReason.UserNotFound => ProblemDetailsHelpers.Unauthorized(
                            "Invalid user",
                            eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier),

                        AuthFailureReason.UserDisabled => ProblemDetailsHelpers.Unauthorized(
                            "User account is disabled",
                            eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier),

                        AuthFailureReason.ProjectNotFound => ProblemDetailsHelpers.NotFound(
                            $"Project {projectKey} not found",
                            eventCode: EventCodes.Launch.LaunchNotFound,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier),

                        AuthFailureReason.NoProjectAccess => ProblemDetailsHelpers.Forbidden(
                            $"User does not have access to project {projectKey}",
                            eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier),

                        _ => ProblemDetailsHelpers.Unauthorized(
                            "Authorization failed",
                            eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                            instance: req.Path,
                            traceId: req.HttpContext.TraceIdentifier)
                    };
                }

                // Store user context for later use (attribution, logging, etc.)
                req.HttpContext.Items["AuthUserId"] = dashboardAuthResult.UserId;
                req.HttpContext.Items["AuthUsername"] = dashboardAuthResult.Username;
                req.HttpContext.Items["AuthProjectRole"] = dashboardAuthResult.ProjectRole;
                req.HttpContext.Items[cacheKey] = true; // Mark authorization complete

                return null; // Authorization successful
            }

            return ProblemDetailsHelpers.Unauthorized(
                "Missing Authorization header or x-user-id header",
                eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                instance: req.Path,
                traceId: req.HttpContext.TraceIdentifier);
        }

        var apiKey = authHeader[7..].Trim(); // Remove "Bearer " prefix
        var result = await authService.ValidateApiKeyAsync(apiKey, projectKey);

        if (!result.IsValid)
        {
            // Map failure reasons to appropriate HTTP status codes and event codes
            return result.FailureReason switch
            {
                AuthFailureReason.InvalidApiKey => ProblemDetailsHelpers.Unauthorized(
                    "Invalid API key",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier),

                AuthFailureReason.RevokedApiKey => ProblemDetailsHelpers.Unauthorized(
                    "API key has been revoked",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier),

                AuthFailureReason.UserNotFound => ProblemDetailsHelpers.Unauthorized(
                    "Invalid user associated with API key",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier),

                AuthFailureReason.UserDisabled => ProblemDetailsHelpers.Unauthorized(
                    "User account associated with API key is disabled",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier),

                AuthFailureReason.ProjectNotFound => ProblemDetailsHelpers.NotFound(
                    $"Project {projectKey} not found",
                    eventCode: EventCodes.Launch.LaunchNotFound,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier),

                AuthFailureReason.NoProjectAccess => ProblemDetailsHelpers.Forbidden(
                    $"API key does not have access to project {projectKey}",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier),

                _ => ProblemDetailsHelpers.Unauthorized(
                    "API key authorization failed",
                    eventCode: EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
                    instance: req.Path,
                    traceId: req.HttpContext.TraceIdentifier)
            };
        }

        // Store user context for later use (attribution, logging, etc.)
        // Any project role is allowed - membership is enough
        req.HttpContext.Items["AuthUserId"] = result.UserId;
        req.HttpContext.Items["AuthUsername"] = result.Username;
        req.HttpContext.Items["AuthProjectRole"] = result.ProjectRole;
        req.HttpContext.Items[cacheKey] = true; // Mark authorization complete

        return null; // Authorization successful
    }
}
