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

using Agenix.PlaywrightGrid.Client;
using Agenix.PlaywrightGrid.Client.Abstractions;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Database;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using Npgsql;
using NUnit.Framework;
using StackExchange.Redis;

namespace Agenix.PlaywrightGrid.Integration.Tests.Fixtures;

/// <summary>
///     Base class for API integration tests that need client access.
///     Provides shared setup for Hub client, Redis, and PostgresSQL connections.
/// </summary>
public abstract class ApiTestBase
{
    /// <summary>
    ///     Gets the Hub URL for API calls.
    /// </summary>
    protected string HubUrl { get; private set; } = null!;

    /// <summary>
    ///     Gets the project key for tests.
    /// </summary>
    protected virtual string ProjectKey =>
        Environment.GetEnvironmentVariable("PROJECT_KEY") ?? TestConstants.DefaultProjectKey;

    /// <summary>
    ///     Gets the label key for browser pool tests.
    /// </summary>
    protected virtual string LabelKey => Environment.GetEnvironmentVariable("TEST_LABEL") ?? "AppB:Chromium:UAT";

    /// <summary>
    ///     Gets the PlaywrightGrid client service.
    /// </summary>
    protected IClientService Client { get; private set; } = null!;

    /// <summary>
    ///     Gets the raw HttpClient for direct HTTP endpoint testing.
    /// </summary>
    protected HttpClient HttpClient { get; private set; } = null!;

    /// <summary>
    ///     Gets the Redis database instance.
    /// </summary>
    protected IDatabase Redis => RedisTestFixture.Instance.GetDatabase();

    /// <summary>
    ///     Gets the PostgreSQL data source.
    /// </summary>
    protected NpgsqlDataSource Postgres => PostgresTestFixture.Instance.DataSource;

    /// <summary>
    ///     Gets the test user information (created during OneTimeSetup).
    /// </summary>
    protected TestUserInfo TestUser { get; private set; } = null!;

    [OneTimeSetUp]
    public virtual async Task OneTimeSetup()
    {
        HubUrl = TestConfiguration.HubUrl;

        // Log test environment configuration
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] ==========================================");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] {TestConfiguration.EnvironmentDescription}");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] ==========================================");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Hub URL: {HubUrl}");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] PostgresSQL: {TestConfiguration.PostgresHost}:{TestConfiguration.PostgresPort}");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Redis: {TestConfiguration.RedisConnection}");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Project Key: {ProjectKey}");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Label Key: {LabelKey}");

        // Create project in Redis (required for authentication)
        await RedisHelpers.CreateProjectAsync(Redis, ProjectKey, $"Test Project {GetType().Name}");

        // Create test user with API key
        var userId = $"test-user-{GetType().Name}";
        TestUser = await RedisHelpers.CreateTestUserWithApiKeyAsync(
            Redis,
            userId,
            $"testuser{GetType().Name}",
            "integration-test",
            ProjectKey
        );

        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Created test user: {TestUser.UserId}");

        // Create client with an API key
        var baseUri = new Uri(HubUrl);
        Client = new Service(baseUri, ProjectKey, TestUser.ApiKey);

        // Create HttpClient for direct HTTP endpoint testing
        HttpClient = new HttpClient { BaseAddress = baseUri };
        HttpClient.DefaultRequestHeaders.Add("X-Project-Key", ProjectKey);
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestUser.ApiKey}");

        // Wait for hub to be healthy
        await WaitForHubHealthyAsync();
    }

    /// <summary>
    ///     Ensures that the workers are healthy and the browser pool is stable.
    ///     Calculates expected capacity and waits for it to be reached and stay stable.
    /// </summary>
    protected async Task EnsureWorkersHealthyAndPoolStableAsync()
    {
        if (Redis == null)
        {
            return;
        }

        var availKey = $"available:{LabelKey}";
        var inuseKey = $"inuse:{LabelKey}";

        // Calculate expected capacity based on .env configuration
        // WORKER_CHROMIUM_REPLICAS=2 × AGENIX_WORKER_CHROMIUM_POOL_CONFIG=AppB:Chromium:UAT=3 = 6 browsers
        const int expectedCapacity = 6;
        const int maxAttempts = 60; // Wait up to 30s for capacity
        const int requiredStableChecks = 6; // Increased from 3 to 6 for more robust stability (3 seconds)

        // Phase 0: Cleanup any leaked borrows from previous test runs
        var inuseItems = await Redis.ListRangeAsync(inuseKey);
        if (inuseItems.Length > 0)
        {
            TestContext.WriteLine($"[Setup] Found {inuseItems.Length} leaked borrow(s) in inuse list. Force returning to pool...");

            foreach (var inuseItem in inuseItems)
            {
                var inuseItemStr = inuseItem.ToString();
                TestContext.WriteLine($"[Setup] Force returning: {inuseItemStr}");

                try
                {
                    // Atomically move from inuse to available (same as /session/return endpoint)
                    await Redis.ListRemoveAsync(inuseKey, inuseItem);
                    await Redis.ListRightPushAsync(availKey, inuseItem);
                    TestContext.WriteLine("[Setup] Successfully returned leaked browser");
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine($"[Setup] Error returning browser: {ex.Message}");
                }
            }

            // Wait for Redis propagation
            TestContext.WriteLine("[Setup] Waiting 1 second for pool to update...");
            await Task.Delay(1000);

            // Verify cleanup worked
            var remainingInUse = await Redis.ListRangeAsync(inuseKey);
            if (remainingInUse.Length == 0)
            {
                TestContext.WriteLine("[Setup] Cleanup successful - all leaked browsers returned");
            }
            else
            {
                TestContext.WriteLine($"[Setup] Warning: {remainingInUse.Length} browser(s) still in inuse list after cleanup");
            }
        }

        // Phase 1: Wait for the pool to reach expected capacity
        var capacityReached = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var currentAvailable = (await Redis.ListRangeAsync(availKey)).Length;
            var currentInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
            var currentTotal = currentAvailable + currentInUse;

            if (currentAvailable == expectedCapacity)
            {
                TestContext.WriteLine(
                    $"[Setup] Expected available capacity reached in {sw.ElapsedMilliseconds}ms - Available: {currentAvailable}, In-use: {currentInUse}, Total: {currentTotal}/{expectedCapacity}");
                capacityReached = true;
                break;
            }

            TestContext.WriteLine(
                $"[Setup] Waiting for available capacity {attempt + 1}/{maxAttempts} - Available: {currentAvailable}/{expectedCapacity}, In-use: {currentInUse}, Total: {currentTotal}");
            await Task.Delay(500);
        }

        if (!capacityReached)
        {
            TestContext.WriteLine(
                $"[Setup] Pool did not reach expected capacity after {maxAttempts} checks. Attempting to force close all borrows for {LabelKey}...");

            // Call admin restart endpoint to clear leaked browsers
            try
            {
                var response = await HttpClient.PostAsync($"/admin/pools/{LabelKey}/restart", null);
                if (response.IsSuccessStatusCode)
                {
                    TestContext.WriteLine($"[Setup] Pool restart requested. Waiting for pool to recover (second attempt)...");

                    // Phase 1 retry: Wait for the pool to reach expected capacity again
                    for (var attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        var currentAvailable = (await Redis.ListRangeAsync(availKey)).Length;
                        var currentInUse = (await Redis.ListRangeAsync(inuseKey)).Length;

                        if (currentAvailable == expectedCapacity)
                        {
                            TestContext.WriteLine(
                                $"[Setup] Expected available capacity reached after restart in {sw.ElapsedMilliseconds}ms - Available: {currentAvailable}, In-use: {currentInUse}, Total: {currentAvailable + currentInUse}/{expectedCapacity}");
                            capacityReached = true;
                            break;
                        }

                        if ((attempt + 1) % 10 == 0)
                        {
                            TestContext.WriteLine(
                                $"[Setup] Recovery check {attempt + 1}/{maxAttempts} - Available: {currentAvailable}/{expectedCapacity}, In-use: {currentInUse}");
                        }

                        await Task.Delay(500);
                    }
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    TestContext.WriteLine($"[Setup] Failed to call restart endpoint: {response.StatusCode} - {content}");
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"[Setup] Error during pool restart: {ex.Message}");
            }
        }

        if (!capacityReached)
        {
            var finalAvail = (await Redis.ListRangeAsync(availKey)).Length;
            var finalInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
            Assert.Fail(
                $"Pool did not reach expected available capacity of {expectedCapacity} browsers after {sw.Elapsed.TotalSeconds:F1}s (including restart attempt). " +
                $"Current state - Available: {finalAvail}, In-use: {finalInUse}, Total: {finalAvail + finalInUse}. " +
                "Workers may be offline, unhealthy, or browsers are leaked from previous runs.");
        }

        // Phase 2: Verify the pool is stable (no changes for consecutive checks)
        var stableCount = 0;
        int? lastAvailable = null;
        int? lastInUse = null;
        int? lastTotal = null;

        for (var attempt = 0; attempt < 60; attempt++) // Increased to 60 checks for stability
        {
            var currentAvailable = (await Redis.ListRangeAsync(availKey)).Length;
            var currentInUse = (await Redis.ListRangeAsync(inuseKey)).Length;
            var currentTotal = currentAvailable + currentInUse;

            // Check if pool is stable: available == expected capacity AND total pool size unchanged
            if (lastAvailable.HasValue && lastInUse.HasValue && lastTotal.HasValue &&
                currentAvailable == lastAvailable.Value &&
                currentInUse == lastInUse.Value &&
                currentTotal == lastTotal.Value &&
                currentAvailable == expectedCapacity &&
                currentTotal == expectedCapacity)
            {
                stableCount++;
                if (stableCount >= requiredStableChecks)
                {
                    TestContext.WriteLine(
                        $"[Setup] Pool stable after {sw.ElapsedMilliseconds}ms - Available: {currentAvailable}, In-use: {currentInUse}, Total: {currentTotal} (stable for {stableCount} consecutive checks)");
                    return;
                }
            }
            else
            {
                // Pool changed - reset stability counter and log what changed
                if (lastTotal.HasValue && currentTotal != lastTotal.Value)
                {
                    TestContext.WriteLine(
                        $"[Setup] Pool size changed at check {attempt + 1}: {lastTotal.Value}→{currentTotal} (browsers being recycled)");
                }
                stableCount = 0;
            }

            lastAvailable = currentAvailable;
            lastInUse = currentInUse;
            lastTotal = currentTotal;

            // Only log every 5th check to reduce noise, unless pool is unstable
            if ((attempt + 1) % 5 == 0 || stableCount > 0)
            {
                TestContext.WriteLine(
                    $"[Setup] Stability check {attempt + 1}/60 - Available: {currentAvailable}, In-use: {currentInUse}, Total: {currentTotal}, Stable: {stableCount}/{requiredStableChecks}");
            }
            await Task.Delay(500);
        }

        Assert.Fail(
            $"Pool reached expected capacity but did not stabilize after {sw.Elapsed.TotalSeconds:F1}s. Pool may be churning.");
    }

    [OneTimeTearDown]
    public virtual async Task OneTimeTeardown()
    {
        // Dispose HttpClient
        HttpClient.Dispose();

        // Cleanup test user
        await RedisHelpers.CleanupUserDataAsync(Redis, TestUser.UserId, ProjectKey);
    }

    /// <summary>
    ///     Waits for the Hub to be healthy before running tests.
    /// </summary>
    protected virtual async Task WaitForHubHealthyAsync()
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        const int maxRetries = 10;
        var delay = TimeSpan.FromSeconds(2);

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await httpClient.GetAsync($"{HubUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Hub is healthy");
                    return;
                }
            }
            catch
            {
                // Hub doesn't ready yet
            }

            if (i < maxRetries - 1)
            {
                await TestContext.Progress.WriteLineAsync(
                    $"[{GetType().Name}] Waiting for Hub... ({i + 1}/{maxRetries})");
                await Task.Delay(delay);
            }
        }

        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Warning: Hub health check did not succeed");
    }
}
