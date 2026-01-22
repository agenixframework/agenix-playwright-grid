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

using System.Text.Json;
using Agenix.PlaywrightGrid.Integration.Tests.Builders;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Database;

/// <summary>
///     Integration tests for History Matrix database functions:
///     - get_launch_parent_items_history(project_key, depth)
///     - get_suite_child_items_history(suite_db_id, depth)
/// </summary>
[TestFixture]
public class HistoryMatrixTests : DatabaseTestBase
{
    protected override string ProjectKey => "test_project_history";

    [Test]
    public async Task GetLaunchParentItemsHistory_EmptyDatabase_ReturnsEmptyResult()
    {
        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 10);

        // Assert
        Assert.That(result, Is.Not.Null, "Result should not be null");
        Assert.That(result.Count, Is.EqualTo(0), "Empty database should return no rows");
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_SingleLaunchWithOneSuite_ReturnsCorrectData()
    {
        // Arrange
        var launchId = await new LaunchBuilder()
            .WithProjectKey(ProjectKey)
            .WithLaunchNumber(1)
            .WithStatus(TestConstants.LaunchStatus.InProgress)
            .CreateAsync();

        var suiteId = Guid.NewGuid();
        await new TestItemBuilder(launchId)
            .WithRunId(suiteId)
            .WithItemType(TestConstants.ItemType.Suite)
            .WithName("Login Tests")
            .WithSessionStatus(TestConstants.SessionStatus.Running)
            .WithComputedStatus(TestConstants.ComputedStatus.InProgress)
            .CreateAsync();

        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1), "Should return one row");
        Assert.That(result[0].ItemName, Is.EqualTo("Login Tests"));
        Assert.That(result[0].ItemType, Is.EqualTo("Suite"));
        Assert.That(result[0].Launches.Count, Is.EqualTo(1), "Should have one launch");

        var launch = result[0].Launches[0];
        Assert.That(launch.LaunchId, Is.EqualTo(launchId));
        Assert.That(launch.Status, Is.EqualTo(TestConstants.HistoryStatus.InProgress));
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_MultipleLaunches_OrderedByLaunchNumberDesc()
    {
        // Arrange - Create 3 launches with the same suite name
        for (var i = 1; i <= 3; i++)
        {
            var launchId = await new LaunchBuilder()
                .WithProjectKey(ProjectKey)
                .WithLaunchNumber(i)
                .WithStatus(TestConstants.LaunchStatus.Finished)
                .CreateAsync();

            await new TestItemBuilder(launchId)
                .WithItemType(TestConstants.ItemType.Suite)
                .WithName("API Tests")
                .Finished()
                .CreateAsync();
        }

        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1), "Should return one row (same suite across launches)");
        Assert.That(result[0].ItemName, Is.EqualTo("API Tests"));
        Assert.That(result[0].Launches.Count, Is.EqualTo(3), "Should have three launches");

        // Verify ordering: newest first (launch 3, 2, 1)
        Assert.That(result[0].Launches[0].LaunchNumber, Is.EqualTo(3));
        Assert.That(result[0].Launches[1].LaunchNumber, Is.EqualTo(2));
        Assert.That(result[0].Launches[2].LaunchNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_RespectsDepthLimit()
    {
        // Arrange - Create 15 launches with the same suite
        for (var i = 1; i <= 15; i++)
        {
            var launchId = await new LaunchBuilder()
                .WithProjectKey(ProjectKey)
                .WithLaunchNumber(i)
                .WithStatus(TestConstants.LaunchStatus.Finished)
                .CreateAsync();

            await new TestItemBuilder(launchId)
                .WithItemType(TestConstants.ItemType.Suite)
                .WithName("Regression Tests")
                .Finished()
                .CreateAsync();
        }

        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 5);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Launches.Count, Is.EqualTo(5), "Should return only 5 launches (depth limit)");

        // Verify it returned the 5 newest launches (15, 14, 13, 12, 11)
        Assert.That(result[0].Launches[0].LaunchNumber, Is.EqualTo(15));
        Assert.That(result[0].Launches[4].LaunchNumber, Is.EqualTo(11));
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_ExcludesStepItems()
    {
        // Arrange
        var launchId = await new LaunchBuilder()
            .WithProjectKey(ProjectKey)
            .WithLaunchNumber(1)
            .WithStatus(TestConstants.LaunchStatus.InProgress)
            .CreateAsync();

        // Create Suite (should be included)
        await new TestItemBuilder(launchId)
            .WithItemType(TestConstants.ItemType.Suite)
            .WithName("Login Tests")
            .CreateAsync();

        // Create Step (should be excluded)
        await new TestItemBuilder(launchId)
            .WithItemType(TestConstants.ItemType.Step)
            .WithName("Click login button")
            .CreateAsync();

        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1), "Should return only Suite (Step excluded)");
        Assert.That(result[0].ItemType, Is.EqualTo("Suite"));
        Assert.That(result.All(r => r.ItemType != "Step"), Is.True, "Steps should be excluded");
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_IncludesStoryAndScenarioTypes()
    {
        // Arrange
        var launchId = await new LaunchBuilder()
            .WithProjectKey(ProjectKey)
            .WithLaunchNumber(1)
            .WithStatus(TestConstants.LaunchStatus.InProgress)
            .CreateAsync();

        await new TestItemBuilder(launchId)
            .WithItemType(TestConstants.ItemType.Suite)
            .WithName("Feature Suite")
            .CreateAsync();

        await new TestItemBuilder(launchId)
            .WithItemType(TestConstants.ItemType.Story)
            .WithName("User Story 1")
            .CreateAsync();

        await new TestItemBuilder(launchId)
            .WithItemType(TestConstants.ItemType.Scenario)
            .WithName("Login Scenario")
            .CreateAsync();

        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(3), "Should return Suite, Story, and Scenario");
        Assert.That(result.Any(r => r.ItemType == "Suite"), Is.True);
        Assert.That(result.Any(r => r.ItemType == "Story"), Is.True);
        Assert.That(result.Any(r => r.ItemType == "Scenario"), Is.True);
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_TooltipContainsCorrectData()
    {
        // Arrange
        var launchId = await new LaunchBuilder()
            .WithProjectKey(ProjectKey)
            .WithLaunchNumber(1)
            .WithStatus(TestConstants.LaunchStatus.Finished)
            .CreateAsync();

        var startTime = DateTimeOffset.UtcNow;
        await new TestItemBuilder(launchId)
            .WithItemType(TestConstants.ItemType.Suite)
            .WithName("API Tests")
            .WithSessionStatus(TestConstants.SessionStatus.Completed)
            .WithComputedStatus(TestConstants.ComputedStatus.Failed)
            .WithStartTime(startTime)
            .WithFinishTime(startTime.AddMinutes(5))
            .CreateAsync();

        // Act
        var result = await GetLaunchParentItemsHistoryAsync(ProjectKey, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var tooltip = result[0].Launches[0].Tooltip;

        Assert.That(tooltip, Is.Not.Null, "Tooltip should not be null");
        Assert.That(tooltip!.ContainsKey("sessionStatus"), Is.True, "Tooltip should contain sessionStatus");
        Assert.That(tooltip.ContainsKey("computedStatus"), Is.True, "Tooltip should contain computedStatus");
        Assert.That(tooltip.ContainsKey("total"), Is.True, "Tooltip should contain total count");
        Assert.That(tooltip.ContainsKey("passed"), Is.True, "Tooltip should contain passed count");
        Assert.That(tooltip.ContainsKey("failed"), Is.True, "Tooltip should contain failed count");
        Assert.That(tooltip.ContainsKey("skipped"), Is.True, "Tooltip should contain skipped count");
    }

    [Test]
    public async Task GetSuiteChildItemsHistory_EmptyDatabase_ReturnsEmptyResult()
    {
        // Act
        var result = await GetSuiteChildItemsHistoryAsync(9999, 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0), "Non-existent suite should return empty result");
    }

    [Test]
    public async Task GetSuiteChildItemsHistory_SingleSuiteWithTests_ReturnsCorrectData()
    {
        // Arrange
        var launchId = await new LaunchBuilder()
            .WithProjectKey(ProjectKey)
            .WithLaunchNumber(1)
            .WithStatus(TestConstants.LaunchStatus.Finished)
            .CreateAsync();

        var suiteId = Guid.NewGuid();
        var suite = await new TestItemBuilder(launchId)
            .WithRunId(suiteId)
            .WithItemType(TestConstants.ItemType.Suite)
            .WithName("Login Suite")
            .Finished()
            .CreateAsync();

        await new TestItemBuilder(launchId)
            .WithParent(suiteId)
            .WithItemType(TestConstants.ItemType.Test)
            .WithName("Login with valid credentials")
            .Finished()
            .CreateAsync();

        // Act
        var result = await GetSuiteChildItemsHistoryAsync(suite.DbId, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1), "Should return one test item");
        Assert.That(result[0].ItemName, Is.EqualTo("Login with valid credentials"));
        Assert.That(result[0].ItemType, Is.EqualTo("Test"));
        Assert.That(result[0].Launches.Count, Is.EqualTo(1));
        Assert.That(result[0].Launches[0].Status, Is.EqualTo(TestConstants.HistoryStatus.Passed));
    }

    [Test]
    public async Task GetSuiteChildItemsHistory_MultipleLaunches_ShowsHistory()
    {
        // Arrange - Create 2 launches with same suite structure but different test outcomes
        var suiteDbId = 0L;

        for (var i = 1; i <= 2; i++)
        {
            var launchId = await new LaunchBuilder()
                .WithProjectKey(ProjectKey)
                .WithLaunchNumber(i)
                .WithStatus(TestConstants.LaunchStatus.Finished)
                .CreateAsync();

            var suiteId = Guid.NewGuid();
            var suite = await new TestItemBuilder(launchId)
                .WithRunId(suiteId)
                .WithItemType(TestConstants.ItemType.Suite)
                .WithName("Auth Suite")
                .Finished()
                .CreateAsync();

            if (i == 1)
            {
                suiteDbId = suite.DbId;
            }

            var outcome = i == 1 ? TestConstants.ComputedStatus.Passed : TestConstants.ComputedStatus.Failed;
            await new TestItemBuilder(launchId)
                .WithParent(suiteId)
                .WithItemType(TestConstants.ItemType.Test)
                .WithName("Login")
                .Finished(outcome)
                .CreateAsync();
        }

        // Act
        var result = await GetSuiteChildItemsHistoryAsync(suiteDbId, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1), "Should return one test item (Login)");
        Assert.That(result[0].ItemName, Is.EqualTo("Login"));
        Assert.That(result[0].Launches.Count, Is.EqualTo(2), "Should have two launches");

        // Verify ordering (launch 2 first, newest)
        Assert.That(result[0].Launches[0].Status, Is.EqualTo(TestConstants.HistoryStatus.Failed));
        Assert.That(result[0].Launches[1].Status, Is.EqualTo(TestConstants.HistoryStatus.Passed));
    }

    [Test]
    public async Task GetSuiteChildItemsHistory_ExcludesStepItems()
    {
        // Arrange
        var launchId = await new LaunchBuilder()
            .WithProjectKey(ProjectKey)
            .WithLaunchNumber(1)
            .WithStatus(TestConstants.LaunchStatus.InProgress)
            .CreateAsync();

        var suiteId = Guid.NewGuid();
        var suite = await new TestItemBuilder(launchId)
            .WithRunId(suiteId)
            .WithItemType(TestConstants.ItemType.Suite)
            .WithName("Suite 1")
            .CreateAsync();

        var testId = Guid.NewGuid();
        await new TestItemBuilder(launchId)
            .WithRunId(testId)
            .WithParent(suiteId)
            .WithItemType(TestConstants.ItemType.Test)
            .WithName("Test 1")
            .CreateAsync();

        // Create Step as child of Test (grandchild of Suite - should be excluded)
        await new TestItemBuilder(launchId)
            .WithParent(testId)
            .WithItemType(TestConstants.ItemType.Step)
            .WithName("Step 1")
            .CreateAsync();

        // Act
        var result = await GetSuiteChildItemsHistoryAsync(suite.DbId, 10);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1), "Should return only Test (Step excluded)");
        Assert.That(result[0].ItemType, Is.EqualTo("Test"));
    }

    [Test]
    public async Task GetSuiteChildItemsHistory_RespectsDepthLimit()
    {
        // Arrange - Create 15 launches with same test
        var suiteDbId = 0L;
        for (var i = 1; i <= 15; i++)
        {
            var launchId = await new LaunchBuilder()
                .WithProjectKey(ProjectKey)
                .WithLaunchNumber(i)
                .WithStatus(TestConstants.LaunchStatus.Finished)
                .CreateAsync();

            var suiteId = Guid.NewGuid();
            var suite = await new TestItemBuilder(launchId)
                .WithRunId(suiteId)
                .WithItemType(TestConstants.ItemType.Suite)
                .WithName("Suite")
                .Finished()
                .CreateAsync();

            await new TestItemBuilder(launchId)
                .WithParent(suiteId)
                .WithItemType(TestConstants.ItemType.Test)
                .WithName("Test 1")
                .Finished()
                .CreateAsync();

            if (i == 1)
            {
                suiteDbId = suite.DbId;
            }
        }

        // Act
        var result = await GetSuiteChildItemsHistoryAsync(suiteDbId, 5);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Launches.Count, Is.EqualTo(5), "Should return only 5 launches");
    }

    private async Task<List<HistoryRow>> GetLaunchParentItemsHistoryAsync(string projectKey, int depth)
    {
        var result = new List<HistoryRow>();

        await using var cmd = Db.CreateCommand(
            "SELECT item_name, item_type, launches FROM get_launch_parent_items_history($1, $2)");
        cmd.Parameters.AddWithValue(projectKey);
        cmd.Parameters.AddWithValue(depth);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var itemName = reader.GetString(0);
            var itemType = reader.GetString(1);
            var launchesJson = reader.GetString(2);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var launches = JsonSerializer.Deserialize<List<LaunchData>>(launchesJson, jsonOptions);

            result.Add(new HistoryRow
            {
                ItemName = itemName,
                ItemType = itemType,
                Launches = launches ?? new List<LaunchData>()
            });
        }

        return result;
    }

    private async Task<List<HistoryRow>> GetSuiteChildItemsHistoryAsync(long suiteDbId, int depth)
    {
        var result = new List<HistoryRow>();

        await using var cmd = Db.CreateCommand(
            "SELECT item_name, item_type, launches FROM get_suite_child_items_history($1, $2)");
        cmd.Parameters.AddWithValue(suiteDbId);
        cmd.Parameters.AddWithValue(depth);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var itemName = reader.GetString(0);
            var itemType = reader.GetString(1);
            var launchesJson = reader.GetString(2);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var launches = JsonSerializer.Deserialize<List<LaunchData>>(launchesJson, jsonOptions);

            result.Add(new HistoryRow
            {
                ItemName = itemName,
                ItemType = itemType,
                Launches = launches ?? []
            });
        }

        return result;
    }
}
