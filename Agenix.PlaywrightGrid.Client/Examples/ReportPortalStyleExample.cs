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

namespace Agenix.PlaywrightGrid.Client.Examples;

/// <summary>
///     Example demonstrating ReportPortal-style test reporting with hierarchical test items.
/// </summary>
/// <remarks>
///     This example uses standard ReportPortal Client API which supports:
///     - Hierarchical test items (Launch → Test → Step)
///     - ReportPortal item types (Test, Scenario, Step, Suite, Story, hooks)
///     - Attributes and parameters
///     - Nested steps with configurable statistics (HasStats)
///     Note: Suites are represented as TestItems with Type = Suite, not a separate resource.
/// </remarks>
public class ReportPortalStyleExample
{
    public static async Task RunAsync()
    {
        // Initialize the client
        var hubUri = new Uri("https://your-playwright-grid-hub.com");
        var projectKey = "myapp";
        var apiKey = "your-api-key-here";

        using var client = new Service(hubUri, projectKey, apiKey);

        // 1. Start Launch
        var launchResponse = await client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = "CI Build #123",
            Description = "Regression tests for release 2.0",
            StartTime = DateTime.UtcNow,
            Attributes = new List<ItemAttribute>
            {
                new() { Value = "build:2.0.123" },
                new() { Value = "environment:staging" },
                new() { Value = "smoke" }
            }
        });
        var launchUuid = launchResponse.Uuid;
        Console.WriteLine($"Launch started: {launchUuid}");

        // 2. Start Suite (as TestItem with Type = Suite)
        var suiteResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Login Feature",
            Type = TestItemType.Suite,
            Description = "Tests for user authentication",
            StartTime = DateTime.UtcNow,
            Attributes = new List<ItemAttribute> { new() { Key = "feature", Value = "authentication" } },
            HasStats = true
        });
        var suiteId = suiteResponse.Uuid;
        Console.WriteLine($"Suite started: {suiteId}");

        // 3. Start Test Item
        var testItemResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Login with valid credentials",
            Type = TestItemType.Test,
            Description = "Verify user can log in with correct username and password",
            StartTime = DateTime.UtcNow,
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "priority", Value = "high" }, new() { Key = "author", Value = "john.doe" }
            },
            CodeReference = "tests/auth/login.spec.ts:42",
            HasStats = true
        });
        var testItemId = testItemResponse.Uuid;
        Console.WriteLine($"Test item started: {testItemId}");

        // 4. Add log entry with a screenshot
        await client.LogItem.CreateAsync(new CreateLogItemRequest
        {
            LaunchUuid = launchUuid,
            TestItemUuid = testItemResponse.Uuid,
            Level = "INFO",
            Time = DateTime.UtcNow,
            Text = "Test execution started"
        });

        // 5. Start Step (uses LaunchUuid to associate with hierarchy)
        var stepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Navigate to login page",
            Type = TestItemType.Step,
            Description = "Open https://myapp.com/login",
            StartTime = DateTime.UtcNow,
            HasStats = false // Steps don't affect pass/fail statistics
        });
        var stepId = stepResponse.Uuid;
        Console.WriteLine($"Step started: {stepId}");

        // Simulate test execution...
        await Task.Delay(1000);

        // 6. Finish Step
        await client.TestItem.FinishAsync(Guid.Parse(stepId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("Step finished");

        // 7. Finish Test Item
        await client.TestItem.FinishAsync(Guid.Parse(testItemId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("Test item finished");

        // 8. Finish Suite
        await client.TestItem.FinishAsync(Guid.Parse(suiteId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("Suite finished");

        // 9. Finish Launch
        var finishResponse = await client.Launch.FinishAsync(Guid.Parse(launchUuid),
            new FinishLaunchRequest { EndTime = DateTime.UtcNow });
        Console.WriteLine($"Launch finished: {finishResponse.Info}");
    }

    /// <summary>
    ///     Example demonstrating BDD/Gherkin-style reporting with scenarios and steps.
    /// </summary>
    public static async Task RunBddStyleAsync()
    {
        var hubUri = new Uri("https://your-playwright-grid-hub.com");
        var projectKey = "myapp";
        var apiKey = "your-api-key-here";

        using var client = new Service(hubUri, projectKey, apiKey);

        // Start Launch
        var launchResponse = await client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = "BDD Test Run",
            StartTime = DateTime.UtcNow
        });
        var launchUuid = launchResponse.Uuid;

        // Start Story (Feature) - using TestItem with Type = Suite
        var storyResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Feature: User Authentication",
            Type = TestItemType.Suite,
            StartTime = DateTime.UtcNow,
            HasStats = true
        });
        var storyId = storyResponse.Uuid;

        // Start Scenario
        var scenarioResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Scenario: User logs in with valid credentials",
            Type = TestItemType.Scenario,
            StartTime = DateTime.UtcNow,
            HasStats = true
        });
        var scenarioId = scenarioResponse.Uuid;
        Console.WriteLine($"Scenario started: {scenarioId}");

        // Given step
        var givenStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Given the user is on the login page",
            Type = TestItemType.Step,
            HasStats = false,
            StartTime = DateTime.UtcNow
        });
        await Task.Delay(500);
        await client.TestItem.FinishAsync(Guid.Parse(givenStepResponse.Uuid),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

        // When step
        var whenStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "When the user enters valid credentials",
            Type = TestItemType.Step,
            HasStats = false,
            StartTime = DateTime.UtcNow
        });
        await Task.Delay(500);
        await client.TestItem.FinishAsync(Guid.Parse(whenStepResponse.Uuid),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

        // Then step
        var thenStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Then the user should be redirected to the dashboard",
            Type = TestItemType.Step,
            HasStats = false,
            StartTime = DateTime.UtcNow
        });
        await Task.Delay(500);
        await client.TestItem.FinishAsync(Guid.Parse(thenStepResponse.Uuid),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

        // Finish Scenario
        await client.TestItem.FinishAsync(Guid.Parse(scenarioId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("Scenario finished");

        // Finish Story
        await client.TestItem.FinishAsync(Guid.Parse(storyId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

        // Finish Launch
        await client.Launch.FinishAsync(Guid.Parse(launchUuid), new FinishLaunchRequest { EndTime = DateTime.UtcNow });
        Console.WriteLine("BDD test run completed");
    }

    /// <summary>
    ///     Example demonstrating test hooks (BeforeClass, AfterClass, etc.).
    /// </summary>
    public static async Task RunWithHooksAsync()
    {
        var hubUri = new Uri("https://your-playwright-grid-hub.com");
        var projectKey = "myapp";
        var apiKey = "your-api-key-here";

        using var client = new Service(hubUri, projectKey, apiKey);

        // Start Launch
        var launchResponse = await client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = "Test with Hooks",
            StartTime = DateTime.UtcNow
        });
        var launchUuid = launchResponse.Uuid;

        // Start Suite (as TestItem)
        var suiteResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "User Tests",
            Type = TestItemType.Suite,
            StartTime = DateTime.UtcNow,
            HasStats = true
        });
        var suiteId = suiteResponse.Uuid;

        // BeforeClass hook
        var beforeClassResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Setup test database",
            Type = TestItemType.BeforeClass,
            HasStats = false,
            StartTime = DateTime.UtcNow
        });
        await Task.Delay(300);
        await client.TestItem.FinishAsync(Guid.Parse(beforeClassResponse.Uuid),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("BeforeClass hook executed");

        // Test 1
        var test1Response = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Create user test",
            Type = TestItemType.Test,
            StartTime = DateTime.UtcNow,
            HasStats = true
        });
        await Task.Delay(1000);
        await client.TestItem.FinishAsync(Guid.Parse(test1Response.Uuid),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("Test 1 completed");

        // AfterClass hook
        var afterClassResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Cleanup test database",
            Type = TestItemType.AfterClass,
            HasStats = false,
            StartTime = DateTime.UtcNow
        });
        await Task.Delay(300);
        await client.TestItem.FinishAsync(Guid.Parse(afterClassResponse.Uuid),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });
        Console.WriteLine("AfterClass hook executed");

        // Finish Suite
        await client.TestItem.FinishAsync(Guid.Parse(suiteId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

        // Finish Launch
        await client.Launch.FinishAsync(Guid.Parse(launchUuid), new FinishLaunchRequest { EndTime = DateTime.UtcNow });
        Console.WriteLine("Test run with hooks completed");
    }

    /// <summary>
    ///     Example demonstrating nested test items with parameters.
    /// </summary>
    public static async Task RunWithParametersAsync()
    {
        var hubUri = new Uri("https://your-playwright-grid-hub.com");
        var projectKey = "myapp";
        var apiKey = "your-api-key-here";

        using var client = new Service(hubUri, projectKey, apiKey);

        // Start Launch
        var launchResponse = await client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = "Parameterized Test Run",
            StartTime = DateTime.UtcNow
        });
        var launchUuid = launchResponse.Uuid;

        // Start Suite (as TestItem)
        var suiteResponse = await client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = "Data-Driven Tests",
            Type = TestItemType.Suite,
            StartTime = DateTime.UtcNow,
            HasStats = true
        });
        var suiteId = suiteResponse.Uuid;

        // Run parameterized test
        var browsers = new[] { "chromium", "firefox", "webkit" };
        foreach (var browser in browsers)
        {
            var testItemResponse = await client.TestItem.StartAsync(new StartTestItemRequest
            {
                LaunchUuid = launchUuid,
                Name = $"Login test with {browser}",
                Type = TestItemType.Test,
                StartTime = DateTime.UtcNow,
                HasStats = true,
                Parameters =
                    new List<KeyValuePair<string, string>> { new("browser", browser), new("headless", "true") },
                Attributes = new List<ItemAttribute> { new() { Key = "browser", Value = browser } }
            });

            await Task.Delay(500);

            await client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid),
                new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

            Console.WriteLine($"Test with {browser} completed");
        }

        // Finish Suite
        await client.TestItem.FinishAsync(Guid.Parse(suiteId),
            new FinishTestItemRequest { EndTime = DateTime.UtcNow, Status = Status.Passed });

        // Finish Launch
        await client.Launch.FinishAsync(Guid.Parse(launchUuid), new FinishLaunchRequest { EndTime = DateTime.UtcNow });
        Console.WriteLine("Parameterized test run completed");
    }
}
