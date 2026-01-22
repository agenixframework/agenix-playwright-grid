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

using Agenix.PlaywrightGrid.Client.Abstractions.Responses;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Requests;

/// <summary>
///     Resource for managing test items (ReportPortal-aligned).
///     Test items can be tests, scenarios, steps, or hooks.
/// </summary>
public interface ITestItemResource
{
    /// <summary>
    ///     Starts a new test item.
    ///     For Type=Test or Type=Scenario, automatically borrows a browser from the grid.
    /// </summary>
    /// <param name="request">Test item start request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test item created response including browser session details if applicable</returns>
    Task<TestItemCreatedResponse> StartAsync(StartTestItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a test item by ID.
    /// </summary>
    /// <param name="itemId">Test item ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test item response</returns>
    Task<TestItemResponse> GetAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finishes a test item.
    ///     For items that borrowed a browser, automatically returns it to the pool.
    /// </summary>
    /// <param name="itemId">Test item ID</param>
    /// <param name="request">Test item finish request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message response</returns>
    Task<MessageResponse> FinishAsync(Guid itemId, FinishTestItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all test items for a launch.
    /// </summary>
    /// <param name="launchId">Launch ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of test items</returns>
    Task<List<TestItemResponse>> GetByLaunchAsync(Guid launchId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all test items for a suite.
    /// </summary>
    /// <param name="suiteId">Suite ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of test items</returns>
    Task<List<TestItemResponse>> GetBySuiteAsync(Guid suiteId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all child test items for a parent item (for nested steps).
    /// </summary>
    /// <param name="parentItemId">Parent test item ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of child test items</returns>
    Task<List<TestItemResponse>> GetChildrenAsync(Guid parentItemId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a test item with its full child hierarchy up to specified depth.
    /// </summary>
    /// <param name="itemId">Test item ID</param>
    /// <param name="maxDepth">Maximum recursion depth (default 5, max 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test item response with nested children populated</returns>
    Task<TestItemResponse> GetTreeAsync(Guid itemId, int maxDepth = 5, CancellationToken cancellationToken = default);
}
