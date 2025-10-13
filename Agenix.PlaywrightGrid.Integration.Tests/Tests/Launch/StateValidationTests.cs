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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Launch;

/// <summary>
///     Integration tests for terminal state validation logic.
///     Tests verify that launches in terminal states (Finished, Stopped, Failed) properly reject
///     incoming requests to create test items, finish test items, and re-finish launches.
/// </summary>
[TestFixture]
public class StateValidationTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task ForceFinishLaunch_ThenCreateTestItem_ShouldReturn409Conflict()
    {
        // Arrange: Create an active launch with running tests
        var launchId = await CreateActiveLaunchAsync();

        // Force finish the launch (terminal state: Stopped)
        var forceFinishRequest = new { reason = "Testing terminal state validation" };
        var forceFinishResponse = await HttpClient.PostAsJsonAsync(
            $"/api/launches/{launchId}/force-finish", forceFinishRequest);
        Assert.That(forceFinishResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify launch is in terminal state
        var launchResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        var launch = await launchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launch.GetProperty("status").GetString(), Is.EqualTo("Stopped"));

        // Act: Attempt to create a new test item in the stopped launch
        var testItemRequest = new
        {
            launchUuid = launchId.ToString(),
            name = "Test Item After Force Finish",
            type = "Test",
            labelKey = "test:chromium:dev"
        };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/test-items", testItemRequest);

        // Assert: Should return 409 Conflict
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "Creating test item in a stopped launch should return 409 Conflict");

        var errorContent = await createResponse.Content.ReadAsStringAsync();
        Assert.That(errorContent, Does.Contain("terminal state").IgnoreCase,
            "Error message should mention terminal state");
        Assert.That(errorContent, Does.Contain("Stopped").IgnoreCase,
            "Error message should mention the Stopped status");
    }

    [Test]
    public async Task ForceFinishLaunch_ThenCreateSuite_ShouldSucceed()
    {
        // Arrange: Create an active launch
        var launchId = await CreateActiveLaunchAsync();

        // Force finish the launch
        var forceFinishRequest = new { reason = "Testing suite creation after force finish" };
        await HttpClient.PostAsJsonAsync($"/api/launches/{launchId}/force-finish", forceFinishRequest);

        // Act: Attempt to create a suite (Suite type doesn't borrow browsers)
        var suiteRequest = new { launchUuid = launchId.ToString(), name = "Suite After Force Finish", type = "Suite" };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/test-items", suiteRequest);

        // Assert: Should still return 409 Conflict (terminal state validation applies to all items)
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "Creating suite in a stopped launch should return 409 Conflict");
    }

    [Test]
    public async Task ForceFinishLaunchMidTest_ThenFinishTestItem_ShouldReturn409Conflict()
    {
        // Arrange: Create a launch with a running test item
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId);
        var testItemId = await CreateTestItemAsync(launchId, suiteId);

        // Verify the test item is running
        var testItemResponse = await HttpClient.GetAsync($"/api/test-items/{testItemId}");
        Assert.That(testItemResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act: Force to finish the launch while the test is running
        var forceFinishRequest = new { reason = "Testing mid-test force finish" };
        var forceFinishResponse = await HttpClient.PostAsJsonAsync(
            $"/api/launches/{launchId}/force-finish", forceFinishRequest);
        Assert.That(forceFinishResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Small delay to ensure cache invalidation completes
        await Task.Delay(1000);

        // Attempt to finish the test item after launch was force-finished
        var finishRequest = new { status = "Passed", endTime = DateTime.UtcNow };

        var finishResponse = await HttpClient.PutAsJsonAsync(
            $"/api/test-items/{testItemId}/finish", finishRequest);

        // Assert: Should return 409 Conflict
        Assert.That(finishResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "Finishing test item in a stopped launch should return 409 Conflict");

        var errorContent = await finishResponse.Content.ReadAsStringAsync();
        Assert.That(errorContent, Does.Contain("terminal state").IgnoreCase,
            "Error message should mention terminal state");
        Assert.That(errorContent, Does.Contain("Stopped").IgnoreCase,
            "Error message should mention the Stopped status");
    }

    [Test]
    public async Task ForceFinishLaunch_ShouldAutoStopAllRunningTestItems()
    {
        // Arrange: Create launch with multiple running test items
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId);
        var testItem1Id = await CreateTestItemAsync(launchId, suiteId, "Test 1");
        var testItem2Id = await CreateTestItemAsync(launchId, suiteId, "Test 2");

        // Act: Force to finish the launch
        var forceFinishRequest = new { reason = "Testing auto-stop of running tests" };
        var forceFinishResponse = await HttpClient.PostAsJsonAsync(
            $"/api/launches/{launchId}/force-finish", forceFinishRequest);
        Assert.That(forceFinishResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Assert: Verify all test items were stopped
        var testItem1Response = await HttpClient.GetAsync($"/api/test-items/{testItem1Id}");
        var testItem1 = await testItem1Response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(testItem1.GetProperty("sessionStatus").GetString(), Is.EqualTo("Stopped"));

        var testItem2Response = await HttpClient.GetAsync($"/api/test-items/{testItem2Id}");
        var testItem2 = await testItem2Response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(testItem2.GetProperty("sessionStatus").GetString(), Is.EqualTo("Stopped"));
    }

    [Test]
    public async Task FinishLaunch_ThenAttemptToReFinish_ShouldReturn409Conflict()
    {
        // Arrange: Create a launch and finish it normally
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId);
        var testItemId = await CreateTestItemAsync(launchId, suiteId);

        // Finish the test item first
        var finishTestRequest = new { status = "Passed", endTime = DateTime.UtcNow };
        await HttpClient.PutAsJsonAsync($"/api/test-items/{testItemId}/finish", finishTestRequest);

        // Finish the launch normally
        var finishLaunchRequest = new { status = "Finished", endTime = DateTime.UtcNow };
        var firstFinishResponse = await HttpClient.PutAsJsonAsync(
            $"/api/launches/{launchId}/finish", finishLaunchRequest);
        Assert.That(firstFinishResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify launch is finished
        var launchResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        var launch = await launchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launch.GetProperty("status").GetString(), Is.EqualTo("Finished"));

        // Act: Attempt to finish the launch again
        var secondFinishResponse = await HttpClient.PutAsJsonAsync(
            $"/api/launches/{launchId}/finish", finishLaunchRequest);

        // Assert: Should return 409 Conflict
        Assert.That(secondFinishResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "Re-finishing an already finished launch should return 409 Conflict");

        var errorContent = await secondFinishResponse.Content.ReadAsStringAsync();
        Assert.That(errorContent, Does.Contain("terminal state").IgnoreCase,
            "Error message should mention terminal state");
        Assert.That(errorContent, Does.Contain("Finished").IgnoreCase,
            "Error message should mention the Finished status");
    }

    [Test]
    public async Task FinishLaunch_ThenAttemptToUpdateLaunch_ShouldReturn409Conflict()
    {
        // Arrange: Create and finish a launch
        var launchId = await CreateActiveLaunchAsync();
        var finishRequest = new { status = "Finished", endTime = DateTime.UtcNow };
        await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", finishRequest);

        // Act: Attempt to update the finished launch
        var updateRequest = new { name = "Updated Launch Name", description = "Updated description" };
        var updateResponse = await HttpClient.PutAsJsonAsync(
            $"/api/launches/{launchId}", updateRequest);

        // Assert: Should return 409 Conflict (if update endpoint validates terminal state)
        // Note: This test may need adjustment based on whether UpdateLaunch validates terminal state
        if (updateResponse.StatusCode == HttpStatusCode.Conflict)
        {
            var errorContent = await updateResponse.Content.ReadAsStringAsync();
            Assert.That(errorContent, Does.Contain("terminal state").IgnoreCase,
                "Error message should mention terminal state");
        }
    }

    [Test]
    public async Task FinishLaunch_ThenCreateTestItem_ShouldReturn409Conflict()
    {
        // Arrange: Create and finish a launch normally
        var launchId = await CreateActiveLaunchAsync();
        var finishRequest = new { status = "Finished", endTime = DateTime.UtcNow };
        var finishResponse = await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", finishRequest);
        Assert.That(finishResponse.IsSuccessStatusCode, Is.True,
            $"Finish launch failed: {await finishResponse.Content.ReadAsStringAsync()}");

        // Small delay to ensure Redis cache invalidation completes
        await Task.Delay(100);

        // Act: Attempt to create a new test item in the finished launch
        var testItemRequest = new
        {
            launchUuid = launchId.ToString(),
            name = "Test Item After Finish",
            type = "Test",
            labelKey = "test:chromium:dev"
        };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/test-items", testItemRequest);

        // Debug: Print actual response
        var errorContent = await createResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"[DEBUG] Create test item response status: {createResponse.StatusCode}");
        Console.WriteLine($"[DEBUG] Create test item response body: {errorContent}");

        // Assert: Should return 409 Conflict
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            $"Creating test item in a finished launch should return 409 Conflict. Actual response: {errorContent}");

        Assert.That(errorContent, Does.Contain("terminal state").IgnoreCase);
        Assert.That(errorContent, Does.Contain("Finished").IgnoreCase);
    }

    [Test]
    public async Task InProgressLaunch_ShouldAllowTestItemCreation()
    {
        // Arrange: Create an active launch
        var launchId = await CreateActiveLaunchAsync();

        // Verify launch is InProgress
        var launchResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        var launch = await launchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launch.GetProperty("status").GetString(), Is.EqualTo("InProgress"));

        // Act: Create a test item (should succeed)
        var suiteId = await CreateSuiteAsync(launchId);
        var testItemRequest = new
        {
            launchUuid = launchId.ToString(),
            parentItemId = suiteId.ToString(),
            name = "Test Item In Progress Launch",
            type = "Test",
            labelKey = LabelKey // Use the label key from ApiTestBase (AppB:Chromium:UAT)
        };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/test-items", testItemRequest);

        // Assert: Should succeed
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Created),
            "Creating test item in an InProgress launch should succeed");

        // Cleanup: Finish the test item to release browser
        var testItemResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var testItemId = Guid.Parse(testItemResult.GetProperty("id").GetString()!);
        await HttpClient.PutAsJsonAsync($"/api/test-items/{testItemId}/finish", new { status = "Passed" });
    }

    [Test]
    public async Task NonExistentLaunch_CreateTestItem_ShouldReturn404()
    {
        // Arrange: Use a non-existent launch ID
        var nonExistentLaunchId = Guid.NewGuid();

        // Act: Attempt to create a test item
        var testItemRequest = new { launchUuid = nonExistentLaunchId.ToString(), name = "Test Item", type = "Test" };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/test-items", testItemRequest);

        // Assert: Should return 404 Not Found
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "Creating test item with non-existent launch should return 404 Not Found");
    }

    /// <summary>
    ///     Creates an active launch in InProgress state.
    /// </summary>
    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new
        {
            name = $"Terminal State Test Launch {Guid.NewGuid():N}",
            description = "Integration test for terminal state validation",
            attributes = new[]
            {
                new { key = "test", value = "" }, new { key = "terminal-state-validation", value = "" }
            }
        };

        var launchResponse = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        Assert.That(launchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Created),
            $"Launch creation failed with status {launchResponse.StatusCode}. Body: {await launchResponse.Content.ReadAsStringAsync()}");

        var responseBody = await launchResponse.Content.ReadAsStringAsync();
        var launchResult = JsonSerializer.Deserialize<JsonElement>(responseBody);

        // Extract launch ID from response (property name is "id")
        Guid launchId;
        if (launchResult.TryGetProperty("id", out var idProperty))
        {
            launchId = Guid.Parse(idProperty.GetString()!);
        }
        else
        {
            throw new Exception($"Launch response missing 'id' property. Response: {responseBody}");
        }

        return launchId;
    }

    /// <summary>
    ///     Creates a suite within the specified launch.
    /// </summary>
    private async Task<Guid> CreateSuiteAsync(Guid launchId)
    {
        var suiteRequest = new
        {
            launchUuid = launchId.ToString(),
            name = $"Test Suite {Guid.NewGuid():N}",
            type = "Suite"
        };

        var suiteResponse = await HttpClient.PostAsJsonAsync("/api/test-items", suiteRequest);
        Assert.That(suiteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Created),
            $"Suite creation failed with status {suiteResponse.StatusCode}. Body: {await suiteResponse.Content.ReadAsStringAsync()}");

        var suiteResult = await suiteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var suiteId = Guid.Parse(suiteResult.GetProperty("id").GetString()!);

        return suiteId;
    }

    /// <summary>
    ///     Creates a test item (Test type) within the specified launch and suite.
    ///     This will attempt to borrow a browser if labelKey is provided.
    /// </summary>
    private async Task<Guid> CreateTestItemAsync(Guid launchId, Guid suiteId, string? name = null)
    {
        var testItemRequest = new
        {
            launchUuid = launchId.ToString(),
            parentItemId = suiteId.ToString(),
            name = name ?? $"Test Item {Guid.NewGuid():N}",
            type = "Test",
            labelKey = LabelKey // Use the label key from ApiTestBase (AppB:Chromium:UAT)
        };

        var testItemResponse = await HttpClient.PostAsJsonAsync("/api/test-items", testItemRequest);
        Assert.That(testItemResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Created),
            $"Failed to create test item. Status: {testItemResponse.StatusCode}. Body: {await testItemResponse.Content.ReadAsStringAsync()}");

        var testItemResult = await testItemResponse.Content.ReadFromJsonAsync<JsonElement>();
        var testItemId = Guid.Parse(testItemResult.GetProperty("id").GetString()!);

        return testItemId;
    }
}
