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

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Per-project membership role.
/// </summary>
public enum ProjectRole
{
    ProjectLead = 0,
    Member = 1,
    Client = 2,
    Maintainer = 3
}

/// <summary>
///     Lifecycle status of a user account.
/// </summary>
public enum UserStatus
{
    Active = 0,
    Archived = 1,
    Disabled = 2
}

/// <summary>
///     Lifecycle status of a project.
/// </summary>
public enum ProjectStatus
{
    Active = 0,
    Archived = 1,
    Disabled = 2
}

/// <summary>
///     Minimal representation of a User for Admin pages.
/// </summary>
public sealed record User
{
    /// <summary>Stable user id (letters/digits/._-), recommended: username or email local part.</summary>
    public required string Id { get; init; } = string.Empty;

    /// <summary>Login username (same as Id for authentication).</summary>
    public required string Username { get; init; } = string.Empty;

    /// <summary>Full display name of the user.</summary>
    public string? FullName { get; init; }

    /// <summary>Email address (optional, used for unique lookup if provided).</summary>
    public string? Email { get; init; }

    /// <summary>Account role at the user level (Administrator/User).</summary>
    public AccountRole AccountRole { get; init; } = AccountRole.User;

    /// <summary>Status (Active/Archived/Disabled).</summary>
    public UserStatus Status { get; init; } = UserStatus.Active;

    /// <summary>Aggregated count of projects this user belongs to (optional, for UI).</summary>
    public int ProjectsCount { get; init; }

    /// <summary>Last login time in UTC, when known.</summary>
    public DateTime? LastLoginUtc { get; init; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Last update time (UTC).</summary>
    public DateTime UpdatedUtc { get; init; }

    /// <summary>Audit: who created this user (optional actor id).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Audit: who last updated this user (optional actor id).</summary>
    public string? UpdatedBy { get; init; }
}

/// <summary>
///     Minimal representation of a Project for Admin pages.
/// </summary>
public sealed record Project
{
    /// <summary>Stable project key (letters/digits/._-)</summary>
    public required string Key { get; init; } = string.Empty;

    /// <summary>Human-friendly project name.</summary>
    public required string Name { get; init; } = string.Empty;

    /// <summary>Owner user id (must match a User.Id if ownership is enforced; optional in minimal mode).</summary>
    public string? OwnerUserId { get; init; }

    /// <summary>Status (Active/Archived/Disabled).</summary>
    public ProjectStatus Status { get; init; } = ProjectStatus.Active;

    /// <summary>Aggregated count of members in the project (optional, for UI).</summary>
    public int MembersCount { get; init; }

    /// <summary>Aggregated runs count (optional, for UI).</summary>
    public int RunsCount { get; init; }

    /// <summary>Last activity time (UTC), optional.</summary>
    public DateTime? LastActivityUtc { get; init; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Last update time (UTC).</summary>
    public DateTime UpdatedUtc { get; init; }

    /// <summary>Audit: who created this project (optional actor id).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Audit: who last updated this project (optional actor id).</summary>
    public string? UpdatedBy { get; init; }
}

public sealed record Membership
{
    /// <summary>User id that is a member of the project.</summary>
    public required string UserId { get; init; } = string.Empty;

    /// <summary>Project key the user belongs to.</summary>
    public required string ProjectKey { get; init; } = string.Empty;

    /// <summary>Role within the project.</summary>
    public ProjectRole Role { get; init; } = ProjectRole.Client;

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Last update time (UTC).</summary>
    public DateTime UpdatedUtc { get; init; }

    /// <summary>Audit: who created this membership (optional actor id).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Audit: who last updated this membership (optional actor id).</summary>
    public string? UpdatedBy { get; init; }
}

public static partial class AdminValidation
{
    private static readonly Regex IdRegex = MyRegex();
    private static readonly Regex ProjectNameRegex = new("^[A-Za-z0-9 ._\\-]{1,128}$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new("^[A-Za-z0-9 ._\\-]{1,64}$", RegexOptions.Compiled);

    public static bool TryValidateUserId(string id, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "id is required";
            return false;
        }

        if (!IdRegex.IsMatch(id))
        {
            error = "id allows letters/digits/._- up to 64";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateUsername(string username, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "username is required";
            return false;
        }

        if (!UsernameRegex.IsMatch(username))
        {
            error = "username allows letters/digits/space/._- up to 64";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateProjectKey(string key, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "key is required";
            return false;
        }

        if (!IdRegex.IsMatch(key))
        {
            error = "key allows letters/digits/._- up to 64";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateProjectName(string name, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "name is required";
            return false;
        }

        if (!ProjectNameRegex.IsMatch(name))
        {
            error = "name allows letters/digits/space/._- up to 128";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateEmail(string? email, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            error = null;
            return true;
        }

        try
        {
            var addr = new EmailAddressAttribute();
            if (!addr.IsValid(email))
            {
                error = "invalid email";
                return false;
            }

            error = null;
            return true;
        }
        catch
        {
            error = "invalid email";
            return false;
        }
    }

    public static bool TryValidateMembership(string userId, string projectKey, [NotNullWhen(false)] out string? error)
    {
        if (!TryValidateUserId(userId, out error))
        {
            return false;
        }

        if (!TryValidateProjectKey(projectKey, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    // Uniqueness helpers (case-insensitive)
    public static bool IsUniqueProjectKey(IEnumerable<Project> projects, string key,
        [NotNullWhen(false)] out string? error)
    {
        var exists = projects.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            error = "project key already exists";
            return false;
        }

        error = null;
        return true;
    }

    public static bool IsUniqueProjectName(IEnumerable<Project> projects, string name,
        [NotNullWhen(false)] out string? error)
    {
        var exists = projects.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            error = "project name already exists";
            return false;
        }

        error = null;
        return true;
    }

    public static bool IsUniqueUserId(IEnumerable<User> users, string id, [NotNullWhen(false)] out string? error)
    {
        var exists = users.Any(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            error = "user id already exists";
            return false;
        }

        error = null;
        return true;
    }

    public static bool IsUniqueUserEmail(IEnumerable<User> users, string? email, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            error = null;
            return true;
        }

        var exists = users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            error = "email already exists";
            return false;
        }

        error = null;
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}

/// <summary>
///     Account-level role persisted for Administrator/User labels (distinct from GlobalRole names).
/// </summary>
public enum AccountRole
{
    User = 0,
    Administrator = 1
}
