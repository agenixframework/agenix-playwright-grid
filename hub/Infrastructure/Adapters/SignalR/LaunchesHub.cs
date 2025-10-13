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

using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Infrastructure.Adapters.SignalR;

/// <summary>
///     Defines a contract for SignalR clients to receive launch updates in real-time.
/// </summary>
public interface ILaunchesClient
{
    /// <summary>
    ///     Notifies clients that a launch has been created, updated, or its status has changed.
    /// </summary>
    /// <param name="projectKey">The project key the launch belongs to</param>
    /// <param name="launchId">The unique identifier of the launch</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LaunchUpdated(string projectKey, Guid launchId);

    /// <summary>
    ///     Notifies clients that a launch has been deleted.
    /// </summary>
    /// <param name="projectKey">The project key the launch belongs to</param>
    /// <param name="launchId">The unique identifier of the deleted launch</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LaunchDeleted(string projectKey, Guid launchId);

    /// <summary>
    ///     Notifies clients that a test item has been created or updated (Phase 9 - Real-time updates).
    /// </summary>
    /// <param name="testItem">The updated test item data</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task TestItemUpdated(TestItemDto testItem);

    /// <summary>
    ///     Notifies clients that a test item's status has changed (Phase 9 - Real-time updates).
    /// </summary>
    /// <param name="itemId">The test item ID</param>
    /// <param name="sessionStatus">The browser session status</param>
    /// <param name="computedStatus">The computed test outcome status</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task TestItemStatusChanged(Guid itemId, string? sessionStatus, string? computedStatus);

    /// <summary>
    ///     Notifies clients that a test item's child items have been updated (Phase 9 - Real-time updates).
    /// </summary>
    /// <param name="parentItemId">The parent item ID</param>
    /// <param name="children">The updated list of child items</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task TestItemChildrenUpdated(Guid parentItemId, List<TestItemDto> children);
}

/// <summary>
///     SignalR hub for real-time launch notifications to dashboard clients.
/// </summary>
public sealed class LaunchesHub : Hub<ILaunchesClient>
{
    /// <summary>
    ///     Allows clients to join a project-specific group to receive updates for that project only.
    /// </summary>
    /// <param name="projectKey">The project key to subscribe to</param>
    public Task JoinProject(string projectKey)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectKey}");
    }

    /// <summary>
    ///     Allows clients to leave a project-specific group.
    /// </summary>
    /// <param name="projectKey">The project key to unsubscribe from</param>
    public Task LeaveProject(string projectKey)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project:{projectKey}");
    }

    /// <summary>
    ///     Allows clients to join a launch-specific group to receive updates for that launch only.
    /// </summary>
    /// <param name="launchId">The launch ID to subscribe to</param>
    public Task JoinLaunch(Guid launchId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, $"launch:{launchId}");
    }

    /// <summary>
    ///     Allows clients to leave a launch-specific group.
    /// </summary>
    /// <param name="launchId">The launch ID to unsubscribe from</param>
    public Task LeaveLaunch(Guid launchId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"launch:{launchId}");
    }

    /// <summary>
    ///     Allows clients to join a test item-specific group to receive real-time updates (Phase 9).
    /// </summary>
    /// <param name="itemId">The test item ID to subscribe to</param>
    public Task JoinTestItem(Guid itemId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, $"test-item:{itemId}");
    }

    /// <summary>
    ///     Allows clients to leave a test item-specific group (Phase 9).
    /// </summary>
    /// <param name="itemId">The test item ID to unsubscribe from</param>
    public Task LeaveTestItem(Guid itemId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"test-item:{itemId}");
    }
}
