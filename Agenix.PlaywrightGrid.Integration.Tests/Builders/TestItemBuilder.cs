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

using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Database;
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using Npgsql;

namespace Agenix.PlaywrightGrid.Integration.Tests.Builders;

/// <summary>
///     Fluent builder for creating test item test data.
///     Provides a chainable API for setting test item properties and creating test items in the database.
/// </summary>
public class TestItemBuilder
{
    private string? _computedStatus = TestConstants.ComputedStatus.InProgress;
    private DateTimeOffset? _finishTime;
    private bool _hasStats = true;
    private string _itemType = TestConstants.ItemType.Test;
    private Guid _launchId;
    private string _name = "Test Item";
    private Guid? _parentItemId;
    private Guid _runId = Guid.NewGuid();
    private string _sessionStatus = TestConstants.SessionStatus.Running;
    private DateTimeOffset? _startTime;

    /// <summary>
    ///     Creates a new TestItemBuilder with required parameters.
    /// </summary>
    /// <param name="launchId">The launch ID this test item belongs to.</param>
    public TestItemBuilder(Guid launchId)
    {
        _launchId = launchId;
    }

    /// <summary>
    ///     Sets the run ID (test item UUID).
    /// </summary>
    /// <param name="runId">The run ID.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithRunId(Guid runId)
    {
        _runId = runId;
        return this;
    }

    /// <summary>
    ///     Sets the launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithLaunchId(Guid launchId)
    {
        _launchId = launchId;
        return this;
    }

    /// <summary>
    ///     Sets the parent item ID (for hierarchical test structures).
    /// </summary>
    /// <param name="parentItemId">The parent item ID, or null for root items.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithParent(Guid? parentItemId)
    {
        _parentItemId = parentItemId;
        return this;
    }

    /// <summary>
    ///     Sets the item type.
    /// </summary>
    /// <param name="itemType">The item type (e.g., Test, Suite, Step, Scenario).</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithItemType(string itemType)
    {
        _itemType = itemType;
        return this;
    }

    /// <summary>
    ///     Sets the name of the test item.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    ///     Sets the session status (browser/infrastructure lifecycle).
    /// </summary>
    /// <param name="sessionStatus">The session status (e.g., Queued, Running, Completed).</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithSessionStatus(string sessionStatus)
    {
        _sessionStatus = sessionStatus;
        return this;
    }

    /// <summary>
    ///     Sets the computed status (test execution outcome).
    /// </summary>
    /// <param name="computedStatus">The computed status (e.g., Passed, Failed, InProgress), or null.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithComputedStatus(string? computedStatus)
    {
        _computedStatus = computedStatus;
        return this;
    }

    /// <summary>
    ///     Sets the start time.
    /// </summary>
    /// <param name="startTime">The start time, or null to use current time.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithStartTime(DateTimeOffset? startTime)
    {
        _startTime = startTime;
        return this;
    }

    /// <summary>
    ///     Sets the finish time.
    /// </summary>
    /// <param name="finishTime">The finish time, or null if not finished.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithFinishTime(DateTimeOffset? finishTime)
    {
        _finishTime = finishTime;
        return this;
    }

    /// <summary>
    ///     Sets whether this item contributes to statistics.
    /// </summary>
    /// <param name="hasStats">True if item contributes to statistics.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder WithHasStats(bool hasStats)
    {
        _hasStats = hasStats;
        return this;
    }

    /// <summary>
    ///     Convenience method to mark the test item as finished (Completed session, Passed computed status).
    /// </summary>
    /// <param name="computedStatus">The computed status. Defaults to Passed.</param>
    /// <returns>This builder for chaining.</returns>
    public TestItemBuilder Finished(string computedStatus = TestConstants.ComputedStatus.Passed)
    {
        _sessionStatus = TestConstants.SessionStatus.Completed;
        _computedStatus = computedStatus;
        _finishTime = DateTimeOffset.UtcNow;
        return this;
    }

    /// <summary>
    ///     Creates the test item in the database using the configured parameters.
    /// </summary>
    /// <param name="dataSource">The database connection source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A TestItemCreateResult containing the run ID and database ID.</returns>
    public async Task<TestItemCreateResult> CreateAsync(NpgsqlDataSource dataSource,
        CancellationToken cancellationToken = default)
    {
        var dbId = await DatabaseHelpers.CreateTestItemAsync(
            dataSource,
            _runId,
            _launchId,
            _parentItemId,
            _itemType,
            _name,
            _sessionStatus,
            _computedStatus,
            _startTime,
            _finishTime,
            _hasStats,
            cancellationToken);

        return new TestItemCreateResult { RunId = _runId, DbId = dbId };
    }

    /// <summary>
    ///     Creates the test item in the database using the singleton PostgresTestFixture.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A TestItemCreateResult containing the run ID and database ID.</returns>
    public Task<TestItemCreateResult> CreateAsync(CancellationToken cancellationToken = default)
    {
        return CreateAsync(PostgresTestFixture.Instance.DataSource, cancellationToken);
    }
}
