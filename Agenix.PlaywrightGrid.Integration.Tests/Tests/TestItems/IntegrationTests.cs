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

using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.TestItems;

/// <summary>
///     Integration tests for the new TestItem hierarchy API endpoints.
///     Tests verify the ReportPortal-style hierarchical test item structure.
/// </summary>
[TestFixture]
public class IntegrationTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    [Order(1)]
    public async Task StartTestItem_WithTypeTest_ShouldBorrowBrowser()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Postgres, Is.Not.Null, "Postgres not initialized");

        // Arrange: Create launch and suite
        var launchRequest = new StartLaunchRequest
        {
            Name = $"TestItem-Launch-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "test-item-hierarchy", Value = "" }, new() { Key = "integration", Value = "" }
            }
        };

        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        TestContext.WriteLine($"[Test1] Created launch: {launchResponse.Uuid}");

        // Act: Start test item of type Test (should borrow browser)
        // Note: Suite API removed - test items created directly under launch
        var testItemRequest = new StartTestItemRequest
        {
            LaunchUuid = launchResponse.Uuid,
            Name = "Login Test",
            Type = TestItemType.Test,
            LabelKey = LabelKey,
            StartTime = DateTime.UtcNow,
            Attributes = [new ItemAttribute { Key = "feature", Value = "authentication" }]
        };

        var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);

        // Assert: Verify browser was borrowed
        Assert.That(testItemResponse, Is.Not.Null);
        Assert.That(testItemResponse.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(testItemResponse.SessionStatus, Is.EqualTo("Running"));
        Assert.That(testItemResponse.BrowserId, Is.Not.Null.And.Not.Empty,
            "BrowserId should be populated for Test type");
        Assert.That(testItemResponse.WebSocketEndpoint, Is.Not.Null.And.Not.Empty);
        Assert.That(testItemResponse.BrowserType, Is.Not.Null.And.Not.Empty);

        TestContext.WriteLine($"[Test1] Test item created: {testItemResponse.Uuid}");
        TestContext.WriteLine($"[Test1] Browser ID: {testItemResponse.BrowserId}");
        TestContext.WriteLine($"[Test1] Session Status: {testItemResponse.SessionStatus}");

        // Verify in database
        await using var cmd =
            Postgres.CreateCommand("SELECT browser_id, session_status FROM test_items WHERE run_id = $1");
        cmd.Parameters.AddWithValue(Guid.Parse(testItemResponse.Uuid));
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "Test item should exist in database");
        var browserId = reader.GetString(0);
        var sessionStatus = reader.GetString(1);
        Assert.That(browserId, Is.EqualTo(testItemResponse.BrowserId));
        Assert.That(sessionStatus, Is.EqualTo("Running"));

        TestContext.WriteLine("[Test1] Verified test item in database");

        // Cleanup
        await Client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid),
            new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow });
    }


    /// <summary>
    ///     Test 3: Get test item by ID
    /// </summary>
    [Test]
    [Order(3)]
    public async Task GetTestItem_ById_ShouldReturnCompleteData()
    {
        Assert.That(Client, Is.Not.Null);

        // Arrange: Create a test item
        var launchResponse = await Client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = $"Get-Launch-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute> { new() { Key = "get-test", Value = "" } }
        });

        var createdItem = await Client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchResponse.Uuid,
            Name = "Test to Get",
            Description = "Testing GET endpoint",
            Type = TestItemType.Test,
            LabelKey = LabelKey,
            StartTime = DateTime.UtcNow,
            Attributes =
            [
                new ItemAttribute { Key = "priority", Value = "high" },
                new ItemAttribute { Key = "team", Value = "qa" }
            ]
        });

        // Act: Get test item
        var retrieved = await Client.TestItem.GetAsync(Guid.Parse(createdItem.Uuid));

        // Debug: Check what's in the database
        await using var dbCmd = Postgres.CreateCommand("SELECT item_type, name FROM test_items WHERE run_id = $1");
        dbCmd.Parameters.AddWithValue(Guid.Parse(createdItem.Uuid));
        await using var dbReader = await dbCmd.ExecuteReaderAsync();
        if (await dbReader.ReadAsync())
        {
            var dbItemType = dbReader.GetString(0);
            var dbName = dbReader.GetString(1);
            TestContext.WriteLine($"[Test3] Database item_type: {dbItemType}, name: {dbName}");
        }

        // Assert: Verify all fields
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Id, Is.EqualTo(Guid.Parse(createdItem.Uuid)));
        Assert.That(retrieved.Name, Is.EqualTo("Test to Get"));
        Assert.That(retrieved.Description, Is.EqualTo("Testing GET endpoint"));

        TestContext.WriteLine($"[Test3] Retrieved Type: {retrieved.Type}, Expected: {TestItemType.Test}");
        Assert.That(retrieved.Type, Is.EqualTo(TestItemType.Test));
        // Note: TestItemResponse doesn't have SessionStatus/BrowserId - those are in TestItemCreatedResponse
        Assert.That(retrieved.Attributes, Is.Not.Null.And.Count.EqualTo(2));

        TestContext.WriteLine($"[Test3] Retrieved test item: {retrieved.Id}");
        TestContext.WriteLine(
            $"[Test3] Attributes: {string.Join(", ", retrieved.Attributes.Select(a => $"{a.Key}:{a.Value}"))}");

        // Cleanup
        await Client.TestItem.FinishAsync(Guid.Parse(createdItem.Uuid),
            new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow });
    }


    /// <summary>
    ///     Test 5: Finish test item → verify browser returned
    /// </summary>
    [Test]
    [Order(5)]
    public async Task FinishTestItem_ShouldReturnBrowserToPool()
    {
        Assert.That(Client, Is.Not.Null);
        Assert.That(Redis, Is.Not.Null);

        // Arrange: Create a test item
        var launchResponse = await Client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = $"Finish-Launch-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute> { new() { Key = "finish-test", Value = "" } }
        });

        var testItem = await Client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchResponse.Uuid,
            Name = "Test to Finish",
            Type = TestItemType.Test,
            LabelKey = LabelKey,
            StartTime = DateTime.UtcNow
        });

        var browserId = testItem.BrowserId;
        Assert.That(browserId, Is.Not.Null);

        TestContext.WriteLine($"[Test5] Test item started with browser: {browserId}");

        // Verify browser in use
        var inuseKey = $"inuse:{LabelKey}";
        var inuseItems = await Redis.ListRangeAsync(inuseKey);
        var foundInUse = inuseItems.Any(item => item.ToString().Contains(browserId!));
        Assert.That(foundInUse, Is.True, "Browser should be in use");

        // Act: Finish test item
        await Client.TestItem.FinishAsync(Guid.Parse(testItem.Uuid),
            new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow });

        TestContext.WriteLine("[Test5] Finished test item");

        // Assert: Browser returned to pool
        await Task.Delay(1000); // Allow time for cleanup

        inuseItems = await Redis.ListRangeAsync(inuseKey);
        foundInUse = inuseItems.Any(item => item.ToString().Contains(browserId!));
        Assert.That(foundInUse, Is.False, "Browser should be returned to pool");

        TestContext.WriteLine("[Test5] Verified browser returned to pool");

        // Verify session_status updated in a database
        await using var cmd = Postgres.CreateCommand(
            "SELECT session_status, computed_status FROM test_items WHERE run_id = $1");
        cmd.Parameters.AddWithValue(Guid.Parse(testItem.Uuid));
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        var sessionStatus = reader.GetString(0);
        var computedStatus = reader.GetString(1);
        Assert.That(sessionStatus, Is.EqualTo("Completed"));
        Assert.That(computedStatus, Is.EqualTo("Passed"));

        TestContext.WriteLine("[Test5] Verified status updated in database");
    }
}
