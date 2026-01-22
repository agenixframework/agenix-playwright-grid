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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.TestItems;

/// <summary>
///     Integration tests for positive scenarios of TestItemsEndpoints.
/// </summary>
[TestFixture]
public class TestItemsPositiveTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task StartAndGetTestItem_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Positive Suite");

        var testItemRequest = new
        {
            launchUuid = launchId.ToString(),
            parentItemId = suiteId.ToString(),
            name = "Start Test Item",
            type = "Test",
            labelKey = LabelKey,
            description = "Detailed test description",
            attributes = new[] { new { key = "tier", value = "smoke" } }
        };

        // Act: Start
        var startResponse = await HttpClient.PostAsJsonAsync("/api/test-items", testItemRequest);
        Assert.That(startResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));

        var startResult = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = Guid.Parse(startResult.GetProperty("id").GetString()!);

        // Assert: Get by ID
        var getResponse = await HttpClient.GetAsync($"/api/test-items/{itemId}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var item = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(item.GetProperty("id").GetString(), Is.EqualTo(itemId.ToString()));
        Assert.That(item.GetProperty("name").GetString(), Is.EqualTo("Start Test Item"));
        Assert.That(item.GetProperty("description").GetString(), Is.EqualTo("Detailed test description"));
        Assert.That(item.GetProperty("sessionStatus").GetString(), Is.EqualTo("Running"));
    }

    [Test]
    public async Task GetChildItems_ShouldReturnChildren()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Parent Suite");
        var testId = await CreateTestItemAsync(launchId, suiteId, "Child Test");

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{suiteId}/children");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("parentId").GetString(), Is.EqualTo(suiteId.ToString()));

        var children = result.GetProperty("children").EnumerateArray().ToList();
        Assert.That(children.Any(c => c.GetProperty("id").GetString() == testId.ToString()), Is.True);
    }

    [Test]
    public async Task GetTestItemTree_ShouldReturnHierarchy()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Tree Root Suite");
        var testId = await CreateTestItemAsync(launchId, suiteId, "Tree Leaf Test");

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{suiteId}/tree?maxDepth=2");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rootItem = result.GetProperty("item");
        Assert.That(rootItem.GetProperty("id").GetString(), Is.EqualTo(suiteId.ToString()));

        var children = rootItem.GetProperty("children").EnumerateArray().ToList();
        Assert.That(children.Any(c => c.GetProperty("id").GetString() == testId.ToString()), Is.True);

        var stats = result.GetProperty("statistics");
        Assert.That(stats.GetProperty("totalItems").GetInt32(), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task GetTestItemByNumber_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Number Suite");

        // Get dbId from the created item
        var getByIdResponse = await HttpClient.GetAsync($"/api/test-items/{suiteId}");
        var itemData = await getByIdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var dbId = itemData.GetProperty("dbId").GetInt64();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/by-number/{launchId}/{dbId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var item = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(item.GetProperty("id").GetString(), Is.EqualTo(suiteId.ToString()));
        Assert.That(item.GetProperty("dbId").GetInt64(), Is.EqualTo(dbId));
    }

    [Test]
    public async Task UpdateTestItem_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Original Name");

        var updateRequest = new
        {
            name = "Updated Name",
            description = "Updated Description",
            attributes = new[] { new { key = "status", value = "ready" } }
        };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/test-items/{suiteId}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await HttpClient.GetAsync($"/api/test-items/{suiteId}");
        var item = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(item.GetProperty("name").GetString(), Is.EqualTo("Updated Name"));
        Assert.That(item.GetProperty("description").GetString(), Is.EqualTo("Updated Description"));
    }

    [Test]
    public async Task UpdateTestItemStatus_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Status Update Suite");

        var updateRequest = new { computedStatus = "Passed" };

        // Act
        var response = await HttpClient.PatchAsJsonAsync($"/api/test-items/{suiteId}/status", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await HttpClient.GetAsync($"/api/test-items/{suiteId}");
        var item = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(item.GetProperty("computedStatus").GetString(), Is.EqualTo("Passed"));
    }

    [Test]
    public async Task FinishTestItem_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Finish Suite");
        var testId = await CreateTestItemAsync(launchId, suiteId, "Finish Test");

        var finishRequest = new
        {
            status = "Passed",
            endTime = DateTime.UtcNow,
            description = "Finished successfully"
        };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/test-items/{testId}/finish", finishRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await HttpClient.GetAsync($"/api/test-items/{testId}");
        var item = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(item.GetProperty("computedStatus").GetString(), Is.EqualTo("Passed"));
        Assert.That(item.GetProperty("sessionStatus").GetString(), Is.EqualTo("Completed"));
    }

    [Test]
    public async Task GetTestItemHistory_ShouldReturnData()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testName = $"History Test {Guid.NewGuid():N}";
        var testId = await CreateTestItemAsync(launchId, null, testName);

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{testId}/history?limit=5");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var history = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(history, Is.Not.Null);
        // Should contain at least the current item
        Assert.That(history.Any(h => h.GetProperty("testItemId").GetString() == testId.ToString()), Is.True);
    }

    [Test]
    public async Task GetTestItemLogs_ShouldReturnEmptyList()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Log Test");

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{testId}/logs");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var logs = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(logs, Is.Not.Null);
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new { name = $"Test Items Launch {Guid.NewGuid():N}" };
        var response = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateSuiteAsync(Guid launchId, string name)
    {
        var request = new { launchUuid = launchId.ToString(), name = name, type = "Suite" };
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateTestItemAsync(Guid launchId, Guid? parentId, string name)
    {
        var request = new
        {
            launchUuid = launchId.ToString(),
            parentItemId = parentId?.ToString(),
            name = name,
            type = "Test",
            labelKey = LabelKey
        };
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }
}
