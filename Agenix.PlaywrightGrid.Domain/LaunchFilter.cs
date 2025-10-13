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

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Represents a saved filter configuration for the launches page.
///     Filters can be private (user-specific) or shared (team-wide).
/// </summary>
public sealed class LaunchFilter
{
    /// <summary>
    ///     Unique identifier for the filter
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Display name of the filter (e.g., "My Active Launches", "Failed Tests Last Week")
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Optional description explaining what this filter shows
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     The project key this filter belongs to
    /// </summary>
    public required string ProjectKey { get; init; }

    /// <summary>
    ///     The user ID who owns this filter (from auth claims: preferred_username)
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    ///     Array of filter criteria (stored as JSON in database)
    /// </summary>
    public required FilterCriterion[] Criteria { get; init; }

    /// <summary>
    ///     Sort field for results (e.g., "start_time", "name", "launch_number")
    /// </summary>
    public required string SortBy { get; init; }

    /// <summary>
    ///     Whether this filter is shared with all project members (default: false)
    /// </summary>
    public bool IsShared { get; init; }

    /// <summary>
    ///     When the filter was created
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///     When the filter was last updated
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
///     Represents a single filter criterion (field, operator, value).
///     Multiple criteria can be combined with AND/OR logic.
/// </summary>
public sealed class FilterCriterion
{
    /// <summary>
    ///     Field to filter on (e.g., "name", "owner", "launch_number", "start_time")
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    ///     Comparison operator (e.g., "contains", "equals", "range")
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    ///     Value to compare against
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    ///     Logical operator to combine with next criterion ("AND" or "OR")
    /// </summary>
    public required string LogicalOperator { get; init; }

    /// <summary>
    ///     For date range filters: preset name (e.g., "today", "last_7_days", "custom")
    /// </summary>
    public string? DateRangePreset { get; init; }

    /// <summary>
    ///     For date range filters: start date (YYYY-MM-DD format)
    /// </summary>
    public string? FromDate { get; init; }

    /// <summary>
    ///     For date range filters: end date (YYYY-MM-DD format)
    /// </summary>
    public string? ToDate { get; init; }
}

/// <summary>
///     Stores which filter is currently selected for a user in a specific project.
///     This allows the UI to remember the user's last filter choice.
/// </summary>
public sealed class UserFilterPreference
{
    /// <summary>
    ///     The user ID (from auth claims: preferred_username)
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    ///     The project key
    /// </summary>
    public required string ProjectKey { get; init; }

    /// <summary>
    ///     The ID of the currently selected filter (null = "ALL LAUNCHES")
    /// </summary>
    public Guid? SelectedFilterId { get; init; }

    /// <summary>
    ///     When the preference was last updated
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
///     Stores per-user display preference for a filter.
///     Allows each user to control whether a filter appears in their launches dropdown,
///     independent of the filter owner's settings.
/// </summary>
public sealed class UserFilterDisplayPreference
{
    /// <summary>
    ///     The user ID (from auth claims: preferred_username)
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    ///     The filter ID
    /// </summary>
    public required Guid FilterId { get; init; }

    /// <summary>
    ///     Whether this user wants to display this filter in launches dropdown
    /// </summary>
    public required bool DisplayOnLaunches { get; init; }

    /// <summary>
    ///     When the preference was last updated
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
