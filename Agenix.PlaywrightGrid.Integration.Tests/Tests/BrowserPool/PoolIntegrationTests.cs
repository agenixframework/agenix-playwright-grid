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

using System.Collections.Concurrent;
using System.Diagnostics;
using Agenix.PlaywrightGrid.Client;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using NUnit.Framework;
using StackExchange.Redis;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.BrowserPool;

/// <summary>
///     Integration tests for browser pool integration with test run endpoints.
///     Tests verify browser borrowing, returning, and error scenarios.
/// </summary>
[TestFixture]
public class PoolIntegrationTests : ApiTestBase
{
    [OneTimeSetUp]
    public override async Task OneTimeSetup()
    {
        // Call base setup to create client, Redis, and Postgres connections
        await base.OneTimeSetup();
    }

    [SetUp]
    public async Task Setup()
    {
        // Ensure the maintenance mode is OFF before each test
        await ClearMaintenanceModeAsync();
        // Verify workers are healthy and the pool is stable
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [TearDown]
    public async Task Teardown()
    {
        // Clean up after each test
        await ClearMaintenanceModeAsync();
    }

    /// <summary>
    ///     Test 1: Start test item (Test type) → verify browser borrowed
    /// </summary>
    [Test]
    [Order(1)]
    public async Task StartTestItem_ShouldBorrowBrowser_AndStoreSessionDetails()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        Assert.That(Postgres, Is.Not.Null, "Postgres not initialized");
        // Arrange: Create launch and suite
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-Launch-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "integration-test" },
                new() { Key = "integration", Value = "" },
                new() { Key = "browser-pool", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        Assert.That(launchResponse.Uuid, Is.Not.Null.And.Not.Empty);
        TestContext.WriteLine($"[Test1] Created launch: {launchResponse.Uuid}");
        // Act: Start test item (Test type should borrow browser) - no suite needed
        var testItemRequest = new StartTestItemRequest
        {
            LaunchUuid = launchResponse.Uuid,
            LabelKey = LabelKey!,
            Name = $"Test-BrowserBorrow-{DateTime.UtcNow:HHmmss}",
            Description = "Integration test for browser borrowing",
            Type = TestItemType.Test,
            StartTime = DateTime.UtcNow
        };
        var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
        // Assert: Verify response contains browser details
        Assert.That(testItemResponse, Is.Not.Null);
        Assert.That(testItemResponse.Uuid, Is.Not.Null.And.Not.Empty);
        Assert.That(testItemResponse.SessionStatus, Is.EqualTo("Running"), "SessionStatus should be Running");
        Assert.That(testItemResponse.BrowserId, Is.Not.Null.And.Not.Empty, "BrowserId should be populated");
        Assert.That(testItemResponse.WebSocketEndpoint, Is.Not.Null.And.Not.Empty,
            "WebSocketEndpoint should be populated");
        Assert.That(testItemResponse.BrowserType, Is.Not.Null.And.Not.Empty, "BrowserType should be populated");
        Assert.That(testItemResponse.WorkerNodeId, Is.Not.Null, "WorkerNodeId should be populated");
        TestContext.WriteLine($"[Test1] Test item started: {testItemResponse.Uuid}");
        TestContext.WriteLine($"[Test1] Browser ID: {testItemResponse.BrowserId}");
        TestContext.WriteLine($"[Test1] WebSocket: {testItemResponse.WebSocketEndpoint}");
        TestContext.WriteLine($"[Test1] Browser Type: {testItemResponse.BrowserType}");
        TestContext.WriteLine($"[Test1] Node ID: {testItemResponse.WorkerNodeId}");
        // Verify: Browser is in Redis in-use list
        var inuseKey = $"inuse:{LabelKey}";
        var inuseItems = await Redis.ListRangeAsync(inuseKey);
        var foundInUse = inuseItems.Any(item => item.ToString().Contains(testItemResponse.BrowserId!));
        Assert.That(foundInUse, Is.True, $"Browser {testItemResponse.BrowserId} should be in Redis in-use list");
        TestContext.WriteLine("[Test1] Verified browser in Redis in-use list");
        // Verify: Test item is stored in database with browser details
        var testItemId = Guid.Parse(testItemResponse.Uuid);
        await using var cmd =
            Postgres.CreateCommand("SELECT browser_id, session_status, browser_type FROM test_items WHERE run_id = $1");
        cmd.Parameters.AddWithValue(testItemId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "Test item should be stored in database");
        var browserId = reader.GetString(0);
        var sessionStatus = reader.GetString(1);
        var browserType = reader.GetString(2);
        Assert.That(browserId, Is.EqualTo(testItemResponse.BrowserId), "Database browserId should match response");
        Assert.That(sessionStatus, Is.EqualTo("Running"), "Database session_status should be Running");
        Assert.That(browserType, Is.Not.Null.And.Not.Empty, "Database browser_type should be populated");
        TestContext.WriteLine("[Test1] Verified test item in database with browser details");
        // Cleanup: Finish the test item
        var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
        await Client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid), finishRequest);
        TestContext.WriteLine("[Test1] Cleaned up test item");
    }

    /// <summary>
    ///     Test 2: Finish test item → verify browser returned
    /// </summary>
    [Test]
    [Order(2)]
    public async Task FinishTestItem_ShouldReturnBrowser_AndClearSessionDetails()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        Assert.That(Postgres, Is.Not.Null, "Postgres not initialized");
        // Arrange: Create launch and start test item
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-Launch-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute> { new() { Key = "owner", Value = "integration-test" } }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        var testItemRequest = new StartTestItemRequest
        {
            LaunchUuid = launchResponse.Uuid,
            LabelKey = LabelKey!,
            Name = $"Test-BrowserReturn-{DateTime.UtcNow:HHmmss}",
            Type = TestItemType.Test,
            StartTime = DateTime.UtcNow
        };
        var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);

        Assert.That(testItemResponse.BrowserId, Is.Not.Null.And.Not.Empty);
        Assert.That(testItemResponse.Uuid, Is.Not.Null.And.Not.Empty);
        var browserId = testItemResponse.BrowserId!;
        TestContext.WriteLine($"[Test2] Started test item: {testItemResponse.Uuid} with browser: {browserId}");

        // Verify browser is in an in-use list before finish
        var inuseKey = $"inuse:{LabelKey}";
        var inuseBeforeFinish = await Redis.ListRangeAsync(inuseKey);
        var inUseBeforeCount = inuseBeforeFinish.Length;
        TestContext.WriteLine($"[Test2] Browsers in-use before finish: {inUseBeforeCount}");

        // Act: Finish test item (should return browser)
        var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
        await Client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid), finishRequest);
        TestContext.WriteLine($"[Test2] Finished test item: {testItemResponse.Uuid}");
        // Assert: Verify browser is returned to the available pool
        var availKey = $"available:{LabelKey}";
        var availableAfterFinish = await Redis.ListRangeAsync(availKey);
        var foundInAvailable = availableAfterFinish.Any(item => item.ToString().Contains(browserId));
        Assert.That(foundInAvailable, Is.True, $"Browser {browserId} should be returned to available pool");
        TestContext.WriteLine("[Test2] Verified browser returned to available pool");
        // Verify the browser is NOT in the in-use list after finish
        var inuseAfterFinish = await Redis.ListRangeAsync(inuseKey);
        var stillInUse = inuseAfterFinish.Any(item => item.ToString().Contains(browserId));
        Assert.That(stillInUse, Is.False, $"Browser {browserId} should NOT be in in-use list");
        TestContext.WriteLine("[Test2] Verified browser removed from in-use list");
        // Verify session metadata is cleaned up
        var sessionKey = $"session:{browserId}";
        var sessionExists = await Redis.KeyExistsAsync(sessionKey);
        Assert.That(sessionExists, Is.False, $"Session metadata for {browserId} should be deleted");
        TestContext.WriteLine("[Test2] Verified session metadata cleaned up");
        // Verify test item in a database has session status updated
        var testItemId = Guid.Parse(testItemResponse.Uuid);
        await using var cmd = Postgres.CreateCommand("SELECT session_status FROM test_items WHERE run_id = $1");
        cmd.Parameters.AddWithValue(testItemId);
        var sessionStatus = await cmd.ExecuteScalarAsync() as string;
        Assert.That(sessionStatus, Is.EqualTo("Completed"), "Session status should be Completed");
        TestContext.WriteLine("[Test2] Verified test item updated in database");
    }

    /// <summary>
    ///     Test 3: No capacity → verify 503 error
    /// </summary>
    [Test]
    [Order(3)]
    public async Task StartTestItem_WhenNoCapacity_ShouldReturn503ServiceUnavailable()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        // Arrange: Borrow ALL browsers from the pool to exhaust capacity
        var borrowedItems = new List<(string itemUuid, string browserId)>();
        const int maxAttempts = 50; // Safety limit
        try
        {
            // Create a launch for borrowing browsers
            var launchRequest = new StartLaunchRequest
            {
                Name = $"IntegrationTest-NoCapacity-{Guid.NewGuid():N}",
                Attributes = new List<ItemAttribute> { new() { Key = "owner", Value = "integration-test" } }
            };
            var launchResponse = await Client.Launch.StartAsync(launchRequest);
            TestContext.WriteLine("[Test3] Borrowing all browsers to exhaust capacity...");
            for (var i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var testItemRequest = new StartTestItemRequest
                    {
                        LaunchUuid = launchResponse.Uuid,
                        LabelKey = LabelKey!,
                        Name = $"ExhaustCapacity-{i}",
                        Type = TestItemType.Test,
                        StartTime = DateTime.UtcNow
                    };
                    var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
                    borrowedItems.Add((testItemResponse.Uuid, testItemResponse.BrowserId!));
                    TestContext.WriteLine($"[Test3] Borrowed browser {i + 1}: {testItemResponse.BrowserId}");
                }
                catch (ServiceException serviceEx) when (serviceEx.StatusCode == 503)
                {
                    TestContext.WriteLine($"[Test3] Pool exhausted after borrowing {borrowedItems.Count} browsers");
                    break;
                }
            }

            // Act: Try to start one more test item (should fail with 503)
            var finalItemRequest = new StartTestItemRequest
            {
                LaunchUuid = launchResponse.Uuid,
                LabelKey = LabelKey,
                Name = "ShouldFail-NoCapacity",
                Type = TestItemType.Test,
                StartTime = DateTime.UtcNow
            };
            // Assert: Should throw ServiceException with 503 status
            var ex = Assert.ThrowsAsync<ServiceException>(async () =>
            {
                await Client.TestItem.StartAsync(finalItemRequest);
            });
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(503), "Should return 503 Service Unavailable");
            Assert.That(ex.Message, Does.Contain("503").Or.Contains("Service Unavailable").Or.Contains("capacity"),
                "Error message should mention capacity issue");
            TestContext.WriteLine($"[Test3] Correctly received 503 error: {ex.Message}");
        }
        finally
        {
            // Cleanup: Return all borrowed browsers
            TestContext.WriteLine($"[Test3] Cleaning up {borrowedItems.Count} borrowed browsers...");
            foreach (var (itemUuid, browserId) in borrowedItems)
            {
                try
                {
                    var finishRequest = new FinishTestItemRequest
                    {
                        Status = Status.Cancelled,
                        EndTime = DateTime.UtcNow
                    };
                    await Client.TestItem.FinishAsync(Guid.Parse(itemUuid), finishRequest);
                }
                catch (Exception cleanupEx)
                {
                    TestContext.WriteLine($"[Test3] Cleanup failed for {browserId}: {cleanupEx.Message}");
                }
            }

            TestContext.WriteLine("[Test3] Cleanup complete");
        }
    }

    /// <summary>
    ///     Test 4: Maintenance mode → verify proper rejection
    /// </summary>
    [Test]
    [Order(4)]
    public async Task StartTestItem_WhenMaintenanceMode_ShouldReturn503WithMaintenanceMessage()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        try
        {
            // Arrange: Enable maintenance mode for the label
            var maintenanceKey = $"maintenance:{LabelKey}";
            await Redis.StringSetAsync(maintenanceKey, "1", TimeSpan.FromMinutes(5));
            TestContext.WriteLine($"[Test4] Enabled maintenance mode for {LabelKey}");
            // Verify maintenance mode is set
            var maintenanceSet = await Redis.KeyExistsAsync(maintenanceKey);
            Assert.That(maintenanceSet, Is.True, "Maintenance mode should be enabled");
            // Create launch
            var launchRequest = new StartLaunchRequest
            {
                Name = $"IntegrationTest-Maintenance-{Guid.NewGuid():N}",
                Attributes = new List<ItemAttribute> { new() { Key = "owner", Value = "integration-test" } }
            };
            var launchResponse = await Client.Launch.StartAsync(launchRequest);
            // Act: Try to start test item during maintenance mode
            var testItemRequest = new StartTestItemRequest
            {
                LaunchUuid = launchResponse.Uuid,
                LabelKey = LabelKey,
                Name = "ShouldFail-MaintenanceMode",
                Type = TestItemType.Test,
                StartTime = DateTime.UtcNow
            };
            // Assert: Should throw ServiceException with 503 status
            var ex = Assert.ThrowsAsync<ServiceException>(async () =>
            {
                await Client.TestItem.StartAsync(testItemRequest);
            });
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(503), "Should return 503 Service Unavailable");
            Assert.That(ex.Message,
                Does.Contain("503").Or.Contains("Service Unavailable").Or.Contains("capacity").Or
                    .Contains("maintenance"),
                "Error message should indicate service unavailability");
            TestContext.WriteLine($"[Test4] Correctly received 503 error during maintenance: {ex.Message}");
            // Verify: Test item was NOT created in database (rollback happened)
            await using var cmd = Postgres.CreateCommand("SELECT COUNT(*) FROM test_items WHERE launch_id = $1");
            cmd.Parameters.AddWithValue(Guid.Parse(launchResponse.Uuid));
            var itemCount = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            Assert.That(itemCount, Is.EqualTo(0), "No test items should be created during maintenance mode");
            TestContext.WriteLine("[Test4] Verified no test items created during maintenance");
        }
        finally
        {
            // Cleanup: Disable maintenance mode
            await ClearMaintenanceModeAsync();
            TestContext.WriteLine("[Test4] Disabled maintenance mode");
        }
    }

    /// <summary>
    ///     Test 5: Start N runs, finish N runs → verify pool size unchanged
    /// </summary>
    [Test]
    [Order(5)]
    public async Task BrowserPoolLeakDetection_MultipleStartFinishCycles_ShouldMaintainPoolSize()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        // Capture the initial pool state
        var availKey = $"available:{LabelKey}";
        var inuseKey = $"inuse:{LabelKey}";
        var initialAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var initialInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var initialTotal = initialAvailable + initialInUse;
        // Use all available browsers (or cap at 10)
        var numRuns = Math.Min(initialAvailable, 10);
        TestContext.WriteLine(
            $"[Test5] Initial pool state - Available: {initialAvailable}, In-use: {initialInUse}, Total: {initialTotal}");
        TestContext.WriteLine($"[Test5] Will start {numRuns} test runs");
        // Create launch for all test runs
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-LeakDetection-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "integration-test" },
                new() { Key = "leak-detection", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        var itemIds = new List<Guid>();
        try
        {
            // Act: Start N test items
            TestContext.WriteLine($"[Test5] Starting {numRuns} test items...");
            for (var i = 0; i < numRuns; i++)
            {
                var testItemRequest = new StartTestItemRequest
                {
                    LaunchUuid = launchResponse.Uuid,
                    LabelKey = LabelKey!,
                    Name = $"LeakTest-Run-{i}",
                    Type = TestItemType.Test,
                    StartTime = DateTime.UtcNow
                };
                var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
                var testItemId = Guid.Parse(testItemResponse.Uuid);
                itemIds.Add(testItemId);
                TestContext.WriteLine(
                    $"[Test5] Started item {i + 1}/{numRuns}: {testItemResponse.Uuid} (Browser: {testItemResponse.BrowserId})");
            }

            // Verify pool state after starts
            var midAvailable = (await Redis.ListRangeAsync(availKey)).Length;
            var midInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
            var midTotal = midAvailable + midInUse;
            TestContext.WriteLine(
                $"[Test5] Mid-test pool state - Available: {midAvailable}, In-use: {midInUse}, Total: {midTotal}");
            Assert.That(midInUse, Is.GreaterThanOrEqualTo(numRuns), $"At least {numRuns} browsers should be in-use");
            Assert.That(midTotal, Is.EqualTo(initialTotal), "Total pool size should remain constant after starts");
            // Act: Finish all test items
            TestContext.WriteLine($"[Test5] Finishing all {numRuns} test items...");
            for (var i = 0; i < itemIds.Count; i++)
            {
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await Client.TestItem.FinishAsync(itemIds[i], finishRequest);
                TestContext.WriteLine($"[Test5] Finished item {i + 1}/{numRuns}: {itemIds[i]}");
            }

            // Wait a moment for cleanup to complete
            await Task.Delay(500);
            // Assert: Verify pool size is unchanged
            var finalAvailable = (await Redis.ListRangeAsync(availKey)).Length;
            var finalInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
            var finalTotal = finalAvailable + finalInUse;
            TestContext.WriteLine(
                $"[Test5] Final pool state - Available: {finalAvailable}, In-use: {finalInUse}, Total: {finalTotal}");
            // Allow ±2 tolerance after multiple start/finish cycles (browsers may be recycling)
            Assert.That(Math.Abs(finalTotal - initialTotal), Is.LessThanOrEqualTo(2),
                $"Pool size should be stable (±2 allowed for recycling): initial={initialTotal}, final={finalTotal}");
            Assert.That(finalAvailable, Is.GreaterThanOrEqualTo(initialAvailable - 2),
                "Available browsers should be mostly restored (±2 tolerance for recycling)");
            TestContext.WriteLine(
                $"[Test5] ✓ No browser leaks detected after {numRuns} start/finish cycles - pool size stable");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[Test5] Test failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Test 6: Start run, crash before finish → verify timeout cleanup
    /// </summary>
    [Test]
    [Order(6)]
    public async Task BrowserPoolLeakDetection_CrashBeforeFinish_ShouldTimeoutAndCleanup()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        Assert.That(Postgres, Is.Not.Null, "Postgres not initialized");
        // Capture the initial pool state
        var availKey = $"available:{LabelKey}";
        var inuseKey = $"inuse:{LabelKey}";
        var initialAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var initialInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var initialTotal = initialAvailable + initialInUse;
        TestContext.WriteLine(
            $"[Test6] Initial pool state - Available: {initialAvailable}, In-use: {initialInUse}, Total: {initialTotal}");
        // Create launch and suite
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-TimeoutCleanup-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "integration-test" }, new() { Key = "timeout-test", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        // Act: Start test item and simulate crash (never call finish)
        var testItemRequest = new StartTestItemRequest
        {
            LaunchUuid = launchResponse.Uuid,
            LabelKey = LabelKey!,
            Name = $"TimeoutTest-CrashSimulation-{DateTime.UtcNow:HHmmss}",
            Type = TestItemType.Test,
            StartTime = DateTime.UtcNow
        };
        var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
        var browserId = testItemResponse.BrowserId;
        var itemId = testItemResponse.Uuid;
        var sessionKey = $"session:{browserId}";
        TestContext.WriteLine($"[Test6] Started item: {itemId} (Browser: {browserId})");
        TestContext.WriteLine("[Test6] Simulating crash - NOT calling finish endpoint");
        // Verify browser is in-use immediately after start
        var inuseAfterStart = await Redis.ListRangeAsync(inuseKey);
        var browserInUse = inuseAfterStart.Any(item => item.ToString().Contains(browserId));
        Assert.That(browserInUse, Is.True, "Browser should be in in-use list after start");
        // Set a short TTL on the borrow keys to simulate timeout
        var borrowTtlKey = $"borrow_ttl:{browserId}";
        var borrowIdleKey = $"borrow_idle:{browserId}";
        var hasBorrowTtl = await Redis.KeyExistsAsync(borrowTtlKey);
        if (hasBorrowTtl)
        {
            // Set the borrow lease and idle timeout to expire in 5 seconds for testing
            await Redis.KeyExpireAsync(borrowTtlKey, TimeSpan.FromSeconds(5));
            await Redis.KeyExpireAsync(borrowIdleKey, TimeSpan.FromSeconds(5));
            TestContext.WriteLine("[Test6] Set borrow timeout to 5 seconds");
        }
        else
        {
            TestContext.WriteLine("[Test6] Warning: borrow_ttl key not found, timeout cleanup may not work as expected");
        }

        // Wait for timeout + cleanup cycle (45s allows time for BorrowTtlSweeperService 30s startup delay + 10s sweep interval + 5s buffer)
        TestContext.WriteLine("[Test6] Waiting for timeout cleanup (45 seconds to accommodate sweeper startup delay)...");
        await Task.Delay(TimeSpan.FromSeconds(45));
        // Assert: Verify browser was returned to pool by timeout mechanism
        var finalAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var finalInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var finalTotal = finalAvailable + finalInUse;
        TestContext.WriteLine(
            $"[Test6] Final pool state - Available: {finalAvailable}, In-use: {finalInUse}, Total: {finalTotal}");
        // Check if a session key expired
        var sessionStillExists = await Redis.KeyExistsAsync((RedisKey)sessionKey);
        TestContext.WriteLine($"[Test6] Session key still exists: {sessionStillExists}");
        // Primary assertion: Browser should NOT be stuck in in-use state
        // After timeout + cleanup cycle, the browser should have been moved out of in-use
        Assert.That(finalInUse, Is.LessThanOrEqualTo(initialInUse),
            "Browser should not be stuck in in-use state after timeout cleanup");
        // Secondary assertion: The specific browser should not be in the in-use list
        var inuseAfterCleanup = await Redis.ListRangeAsync(inuseKey);
        var browserStillInUse = inuseAfterCleanup.Any(item => item.ToString().Contains(browserId));
        Assert.That(browserStillInUse, Is.False,
            $"Browser {browserId} should not be in in-use list after timeout");
        // Note: We don't assert exact pool size because:
        // 1. Browsers may be temporarily removed for recycling (expected behavior)
        // 2. Other background processes (node health checks) may affect the pool during the 10s wait
        // 3. The key verification is that browsers are not STUCK in in-use state (no leak)
        TestContext.WriteLine(
            $"[Test6] ✓ Timeout cleanup verified - browser not stuck in in-use (pool: {initialTotal}→{finalTotal})");
        // Additional verification: Check database for item status
        await using var cmd = Postgres.CreateCommand("SELECT session_status FROM test_items WHERE run_id = $1");
        cmd.Parameters.AddWithValue(Guid.Parse(itemId));
        var sessionStatus = await cmd.ExecuteScalarAsync() as string;
        if (sessionStatus != null)
        {
            TestContext.WriteLine(
                $"[Test6] Item still in database (expected for abandoned item): status={sessionStatus}");
            // Note: Depending on timeout implementation, the item might be marked as AutoStopped or Aborted
        }
    }

    /// <summary>
    ///     Test 7: Monitor Redis pool counters during operations
    /// </summary>
    [Test]
    [Order(7)]
    public async Task BrowserPoolLeakDetection_MonitorRedisCounters_ShouldAlwaysBalance()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        var availKey = $"available:{LabelKey}";
        var inuseKey = $"inuse:{LabelKey}";
        var counters = new List<(int available, int inuse, int total, string operation)>();

        // Helper to capture pool state
        async Task<(int available, int inuse, int total)> CapturePoolState()
        {
            var avail = (await Redis.ListRangeAsync(availKey)).Length;
            var inuse = (await Redis.ListRangeAsync(inuseKey)).Length;
            return (avail, inuse, avail + inuse);
        }

        // Capture initial state
        var initial = await CapturePoolState();
        counters.Add((initial.available, initial.inuse, initial.total, "Initial"));
        TestContext.WriteLine(
            $"[Test7] Initial: Available={initial.available}, In-use={initial.inuse}, Total={initial.total}");
        // Create launch
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-CounterMonitoring-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "integration-test" },
                new() { Key = "counter-monitoring", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        var itemIds = new List<Guid>();
        try
        {
            // Operation 1: Start 3 items
            for (var i = 0; i < 3; i++)
            {
                var testItemRequest = new StartTestItemRequest
                {
                    LaunchUuid = launchResponse.Uuid,
                    LabelKey = LabelKey,
                    Name = $"CounterTest-Run-{i}",
                    Type = TestItemType.Test,
                    StartTime = DateTime.UtcNow
                };
                var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
                var testItemId = Guid.Parse(testItemResponse.Uuid);
                itemIds.Add(testItemId);
                var state = await CapturePoolState();
                counters.Add((state.available, state.inuse, state.total, $"Start Item {i + 1}"));
                TestContext.WriteLine(
                    $"[Test7] After Start {i + 1}: Available={state.available}, In-use={state.inuse}, Total={state.total}");
            }

            // Operation 2: Finish 2 items
            for (var i = 0; i < 2; i++)
            {
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await Client.TestItem.FinishAsync(itemIds[i], finishRequest);
                var state = await CapturePoolState();
                counters.Add((state.available, state.inuse, state.total, $"Finish Item {i + 1}"));
                TestContext.WriteLine(
                    $"[Test7] After Finish {i + 1}: Available={state.available}, In-use={state.inuse}, Total={state.total}");
            }

            // Operation 3: Start 2 more items
            for (var i = 3; i < 5; i++)
            {
                var testItemRequest = new StartTestItemRequest
                {
                    LaunchUuid = launchResponse.Uuid,
                    LabelKey = LabelKey!,
                    Name = $"CounterTest-Run-{i}",
                    Type = TestItemType.Test,
                    StartTime = DateTime.UtcNow
                };
                var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
                var testItemId = Guid.Parse(testItemResponse.Uuid);
                itemIds.Add(testItemId);
                var state = await CapturePoolState();
                counters.Add((state.available, state.inuse, state.total, $"Start Item {i + 1}"));
                TestContext.WriteLine(
                    $"[Test7] After Start {i + 1}: Available={state.available}, In-use={state.inuse}, Total={state.total}");
            }

            // Operation 4: Finish all remaining items
            for (var i = 2; i < itemIds.Count; i++)
            {
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await Client.TestItem.FinishAsync(itemIds[i], finishRequest);
                var state = await CapturePoolState();
                counters.Add((state.available, state.inuse, state.total, $"Finish Run {i + 1}"));
                TestContext.WriteLine(
                    $"[Test7] After Finish {i + 1}: Available={state.available}, In-use={state.inuse}, Total={state.total}");
            }

            // Capture final state
            await Task.Delay(500); // Allow cleanup to complete
            var final = await CapturePoolState();
            counters.Add((final.available, final.inuse, final.total, "Final"));
            TestContext.WriteLine(
                $"[Test7] Final: Available={final.available}, In-use={final.inuse}, Total={final.total}");
            // Assert: Total should remain mostly stable (allow ±2 for recycling/health checks)
            var uniqueTotals = counters.Select(c => c.total).Distinct().OrderBy(t => t).ToList();
            var minTotal = uniqueTotals.Min();
            var maxTotal = uniqueTotals.Max();
            var totalRange = maxTotal - minTotal;
            Assert.That(totalRange, Is.LessThanOrEqualTo(2),
                $"Total pool size should remain stable (±2 allowed for recycling). Found totals: {string.Join(", ", uniqueTotals)}, range: {totalRange}");
            // Assert: Final state should be close to the initial state (within ±2 tolerance)
            Assert.That(Math.Abs(final.total - initial.total), Is.LessThanOrEqualTo(2),
                $"Final total should be close to initial: initial={initial.total}, final={final.total}");
            Assert.That(final.available, Is.GreaterThanOrEqualTo(initial.available - 2),
                "Available browsers should be restored (within 2 for tolerance)");
            TestContext.WriteLine("[Test7] ✓ Redis counters balanced throughout all operations");
            TestContext.WriteLine($"[Test7] Pool consistency verified across {counters.Count} state changes");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[Test7] Test failed: {ex.Message}");
            TestContext.WriteLine("[Test7] Counter history:");
            foreach (var counter in counters)
            {
                TestContext.WriteLine(
                    $"  {counter.operation}: Available={counter.available}, In-use={counter.inuse}, Total={counter.total}");
            }

            throw;
        }
    }

    /// <summary>
    ///     Test 8: Load testing with concurrent test runs (stress test)
    /// </summary>
    [Test]
    [Order(8)]
    [Category("LoadTest")]
    public async Task LoadTesting_ConcurrentRuns_ShouldHandleWithoutDeadlocksOrRaceConditions()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        Assert.That(Postgres, Is.Not.Null, "Postgres not initialized");
        // Capture the initial pool state
        var availKey = $"available:{LabelKey}";
        var inuseKey = $"inuse:{LabelKey}";
        var initialAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var initialInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var initialTotal = initialAvailable + initialInUse;
        TestContext.WriteLine(
            $"[Test8] Initial pool state - Available: {initialAvailable}, In-use: {initialInUse}, Total: {initialTotal}");
        // Reduce a concurrent load to 20 to avoid overwhelming the system
        // This still tests concurrency but is more realistic
        var concurrentRequests = 20;
        TestContext.WriteLine($"[Test8] Starting load test: {concurrentRequests} concurrent test run attempts");
        // Create launch for all test runs
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-LoadTest-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "integration-test" },
                new() { Key = "load-test", Value = "" },
                new() { Key = "stress-test", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        var successfulItems = new ConcurrentBag<(Guid itemId, string browserId)>();
        var failedItems = new ConcurrentBag<(int index, string error)>();
        var counterSnapshots = new ConcurrentBag<(DateTime timestamp, int available, int inuse, int total)>();
        var sw = Stopwatch.StartNew();
        // Act: Launch concurrent test item start attempts
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            try
            {
                // Start test item (will borrow from the pool if available)
                var testItemRequest = new StartTestItemRequest
                {
                    LaunchUuid = launchResponse.Uuid,
                    LabelKey = LabelKey!,
                    Name = $"LoadTest-Run-{i}",
                    Type = TestItemType.Test,
                    StartTime = DateTime.UtcNow
                };
                var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
                successfulItems.Add((Guid.Parse(testItemResponse.Uuid), testItemResponse.BrowserId!));
                // Capture counter-snapshot
                var avail = (await Redis.ListRangeAsync(availKey)).Length;
                var inuse = (await Redis.ListRangeAsync(inuseKey)).Length;
                counterSnapshots.Add((DateTime.UtcNow, avail, inuse, avail + inuse));
            }
            catch (ServiceException ex) when (ex.StatusCode == 503)
            {
                // Expected: Pool exhausted (only 6 browsers available)
                failedItems.Add((i, "503 Service Unavailable"));
            }
            catch (Exception ex)
            {
                failedItems.Add((i, ex.Message));
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        sw.Stop();
        TestContext.WriteLine($"[Test8] Load test completed in {sw.ElapsedMilliseconds}ms");
        TestContext.WriteLine($"[Test8] Successful items: {successfulItems.Count}");
        TestContext.WriteLine(
            $"[Test8] Failed items (503 expected): {failedItems.Count(r => r.error.Contains("503"))}");
        TestContext.WriteLine($"[Test8] Failed items (unexpected): {failedItems.Count(r => !r.error.Contains("503"))}");
        // Assert: Verify no unexpected failures (deadlocks, race conditions, etc.)
        var unexpectedFailures = failedItems.Where(r => !r.error.Contains("503")).ToList();
        Assert.That(unexpectedFailures.Count, Is.EqualTo(0),
            $"No unexpected failures should occur. Found: {string.Join(", ", unexpectedFailures.Select(f => $"Run {f.index}: {f.error}"))}");
        // Assert: Verify counter-accuracy - total should remain constant across all snapshots
        var uniqueTotals = counterSnapshots.Select(s => s.total).Distinct().ToList();
        Assert.That(uniqueTotals.Count, Is.LessThanOrEqualTo(1),
            $"Total pool size should remain constant. Found totals: {string.Join(", ", uniqueTotals)}");
        if (uniqueTotals.Count > 0)
        {
            Assert.That(uniqueTotals[0], Is.EqualTo(initialTotal),
                $"Total pool size should match initial: initial={initialTotal}, observed={uniqueTotals[0]}");
        }

        TestContext.WriteLine("[Test8] ✓ No deadlocks or race conditions detected");
        TestContext.WriteLine($"[Test8] ✓ Counter accuracy verified across {counterSnapshots.Count} snapshots");
        // Capture pool state during a max load
        var midAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var midInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var midTotal = midAvailable + midInUse;
        TestContext.WriteLine(
            $"[Test8] Pool state at peak load - Available: {midAvailable}, In-use: {midInUse}, Total: {midTotal}");
        Assert.That(midTotal, Is.EqualTo(initialTotal), "Pool size should remain constant even under load");
        // Cleanup: Finish all successful items
        TestContext.WriteLine($"[Test8] Cleaning up {successfulItems.Count} successful items...");
        var cleanupTasks = successfulItems.Select(async item =>
        {
            try
            {
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await Client.TestItem.FinishAsync(item.itemId, finishRequest);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"[Test8] Cleanup failed for {item.itemId}: {ex.Message}");
            }
        }).ToArray();
        await Task.WhenAll(cleanupTasks);
        // Wait for cleanup + browser replacement to complete (20s allows time for background services to recycle and replace browsers)
        await Task.Delay(20000);
        // Assert: Verify pool returns to initial state after cleanup (within ±1 tolerance for recycling)
        var finalAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var finalInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var finalTotal = finalAvailable + finalInUse;
        TestContext.WriteLine(
            $"[Test8] Final pool state - Available: {finalAvailable}, In-use: {finalInUse}, Total: {finalTotal}");
        Assert.That(Math.Abs(finalTotal - initialTotal), Is.LessThanOrEqualTo(1),
            $"Pool size should return to initial after cleanup: initial={initialTotal}, final={finalTotal}");
        Assert.That(finalAvailable, Is.GreaterThanOrEqualTo(initialAvailable - successfulItems.Count),
            "Available browsers should be restored");
        TestContext.WriteLine(
            $"[Test8] ✓ Load test complete - system handled {concurrentRequests} concurrent requests successfully");
    }

    /// <summary>
    ///     Test 9: Sustained load with counter-accuracy verification
    /// </summary>
    [Test]
    [Order(9)]
    [Category("LoadTest")]
    public async Task LoadTesting_SustainedLoad_ShouldMaintainCounterAccuracy()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");
        Assert.That(Redis, Is.Not.Null, "Redis not initialized");
        // Capture the initial pool state
        var availKey = $"available:{LabelKey}";
        var inuseKey = $"inuse:{LabelKey}";
        var initialAvailable = (await Redis.ListRangeAsync(availKey)).Length;
        var initialInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
        var initialTotal = initialAvailable + initialInUse;
        TestContext.WriteLine(
            $"[Test9] Initial pool state - Available: {initialAvailable}, In-use: {initialInUse}, Total: {initialTotal}");
        TestContext.WriteLine("[Test9] Starting sustained load test: 50 iterations of start/finish cycles");
        // Create launch
        var launchRequest = new StartLaunchRequest
        {
            Name = $"IntegrationTest-SustainedLoad-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "integration-test" },
                new() { Key = "sustained-load", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        var counterHistory = new List<(int iteration, int available, int inuse, int total, string operation)>();
        var errors = new List<string>();
        try
        {
            // Perform 50 iterations of: start run → wait → finish run
            for (var iteration = 0; iteration < 50; iteration++)
            {
                // Capture state before start
                var beforeAvail = (await Redis.ListRangeAsync(availKey)).Length;
                var beforeInuse = (await Redis.ListRangeAsync(inuseKey)).Length;
                counterHistory.Add((iteration, beforeAvail, beforeInuse, beforeAvail + beforeInuse, "Before Start"));
                // Start a test item
                var testItemRequest = new StartTestItemRequest
                {
                    LaunchUuid = launchResponse.Uuid,
                    LabelKey = LabelKey!,
                    Name = $"SustainedLoad-Run-{iteration}",
                    Type = TestItemType.Test,
                    StartTime = DateTime.UtcNow
                };
                var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
                // Capture state after start
                var afterStartAvail = (await Redis.ListRangeAsync(availKey)).Length;
                var afterStartInuse = (await Redis.ListRangeAsync(inuseKey)).Length;
                counterHistory.Add((iteration, afterStartAvail, afterStartInuse, afterStartAvail + afterStartInuse,
                    "After Start"));
                // Finish the test item immediately
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await Client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid), finishRequest);
                // Capture state after finish
                var afterFinishAvail = (await Redis.ListRangeAsync(availKey)).Length;
                var afterFinishInuse = (await Redis.ListRangeAsync(inuseKey)).Length;
                counterHistory.Add((iteration, afterFinishAvail, afterFinishInuse, afterFinishAvail + afterFinishInuse,
                    "After Finish"));
                if (iteration % 10 == 0)
                {
                    TestContext.WriteLine(
                        $"[Test9] Completed iteration {iteration}/50 - Available: {afterFinishAvail}, In-use: {afterFinishInuse}");
                }
            }

            // Assert: Total should remain mostly stable (allow ±2 for recycling/health checks during long test)
            var uniqueTotals = counterHistory.Select(h => h.total).Distinct().OrderBy(t => t).ToList();
            var minTotal = uniqueTotals.Min();
            var maxTotal = uniqueTotals.Max();
            var totalRange = maxTotal - minTotal;
            Assert.That(totalRange, Is.LessThanOrEqualTo(2),
                $"Total pool size should remain stable during sustained load (±2 allowed). Found totals: {string.Join(", ", uniqueTotals)}, range: {totalRange}");
            // Verify the final total is close to initial (within 2)
            var finalTotal = counterHistory.Last().total;
            Assert.That(Math.Abs(finalTotal - initialTotal), Is.LessThanOrEqualTo(2),
                $"Final total should be close to initial: initial={initialTotal}, final={finalTotal}");
            TestContext.WriteLine($"[Test9] ✓ Counter accuracy maintained across {counterHistory.Count} state changes");
            TestContext.WriteLine("[Test9] ✓ Total pool size stable within tolerance");
            // Verify the final state
            var finalAvailable = (await Redis.ListRangeAsync(availKey)).Length;
            var finalInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
            var finalTotalActual = finalAvailable + finalInUse;
            TestContext.WriteLine(
                $"[Test9] Final pool state - Available: {finalAvailable}, In-use: {finalInUse}, Total: {finalTotalActual}");
            Assert.That(Math.Abs(finalTotalActual - initialTotal), Is.LessThanOrEqualTo(2),
                $"Final total should be close to initial: initial={initialTotal}, final={finalTotalActual}");
            Assert.That(finalAvailable, Is.GreaterThanOrEqualTo(initialAvailable - 2),
                "All browsers should be returned to available pool (within ±2)");
            TestContext.WriteLine("[Test9] ✓ Sustained load test complete - no counter drift detected");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[Test9] Test failed: {ex.Message}");
            TestContext.WriteLine("[Test9] Counter history:");
            foreach (var entry in counterHistory.TakeLast(20))
            {
                TestContext.WriteLine(
                    $"  Iteration {entry.iteration} - {entry.operation}: Available={entry.available}, In-use={entry.inuse}, Total={entry.total}");
            }

            throw;
        }
    }

    /// <summary>
    ///     Helper: Clear maintenance mode for the test label.
    /// </summary>
    private async Task ClearMaintenanceModeAsync()
    {
        if (Redis == null)
        {
            return;
        }

        var maintenanceKey = $"maintenance:{LabelKey}";
        await Redis.KeyDeleteAsync(maintenanceKey);
    }
}
