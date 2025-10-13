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
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Launch;

/// <summary>
///     Integration tests for positive scenarios of LaunchesEndpoints.
/// </summary>
[TestFixture]
public class LaunchPositiveTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task CreateAndGetLaunch_ShouldSucceed()
    {
        // 1. Create Launch
        var launchName = $"Positive Test Launch {Guid.NewGuid():N}";
        var launchRequest = new
        {
            name = launchName,
            description = "Positive test description",
            attributes = new[] { new { key = "env", value = "test" } }
        };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));

        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var launchId = Guid.Parse(createResult.GetProperty("id").GetString()!);

        // We need to get the dbId from the created launch to test by-db-id lookup
        var initialGet = await HttpClient.GetAsync($"/api/launches/{launchId}");
        var launchData = await initialGet.Content.ReadFromJsonAsync<JsonElement>();
        var dbId = launchData.GetProperty("dbId").GetInt64();

        Assert.That(launchId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(dbId, Is.GreaterThan(0));

        // 2. Get by ID
        var getByIdResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        Assert.That(getByIdResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var launchById = await getByIdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launchById.GetProperty("name").GetString(), Is.EqualTo(launchName));

        // 3. Get by DbId
        var getByDbIdResponse = await HttpClient.GetAsync($"/api/launches/by-db-id/{ProjectKey}/{dbId}");
        Assert.That(getByDbIdResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var launchByDbId = await getByDbIdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launchByDbId.GetProperty("id").GetString(), Is.EqualTo(launchId.ToString()));
    }

    [Test]
    public async Task UpdateLaunch_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var updateRequest = new
        {
            name = "Updated Launch Name",
            description = "Updated Description",
            isImportant = true
        };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        var launch = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launch.GetProperty("name").GetString(), Is.EqualTo("Updated Launch Name"));
        Assert.That(launch.GetProperty("description").GetString(), Is.EqualTo("Updated Description"));
        Assert.That(launch.GetProperty("isImportant").GetBoolean(), Is.True);
    }

    [Test]
    public async Task FinishLaunch_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var finishRequest = new
        {
            endTime = DateTime.UtcNow,
            status = "Finished"
        };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", finishRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        var launch = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(launch.GetProperty("status").GetString(), Is.EqualTo("Finished"));
        Assert.That(launch.TryGetProperty("finishTime", out var ft) && ft.ValueKind != JsonValueKind.Null, Is.True);
    }

    [Test]
    public async Task BulkUpdate_ShouldSucceed()
    {
        // Arrange
        var launchId1 = await CreateActiveLaunchAsync();
        var launchId2 = await CreateActiveLaunchAsync();
        var request = new
        {
            launchIds = new[] { launchId1, launchId2 },
            updates = new { description = "Bulk Updated Description" }
        };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/launches/bulk-update", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var launch1 = await (await HttpClient.GetAsync($"/api/launches/{launchId1}")).Content.ReadFromJsonAsync<JsonElement>();
        var launch2 = await (await HttpClient.GetAsync($"/api/launches/{launchId2}")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.That(launch1.GetProperty("description").GetString(), Is.EqualTo("Bulk Updated Description"));
        Assert.That(launch2.GetProperty("description").GetString(), Is.EqualTo("Bulk Updated Description"));
    }

    [Test]
    public async Task GetLaunches_ShouldReturnList()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches?projectKey={ProjectKey}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var launches = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(launches, Is.Not.Null);
        Assert.That(launches!.Any(l => l.GetProperty("id").GetString() == launchId.ToString()), Is.True);
    }

    [Test]
    public async Task GetLaunchByNumber_ShouldReturnLaunch()
    {
        // Arrange
        var uniqueName = $"Number Test {Guid.NewGuid():N}";
        var launchRequest = new { name = uniqueName };

        var createResponse = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var launchId = Guid.Parse(createResult.GetProperty("id").GetString()!);
        var number = createResult.GetProperty("number").GetInt32();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/by-number/{ProjectKey}/{number}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var launch = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Since number is NOT globally unique per project (it's per name),
        // we check that we got SOME launch with this number.
        Assert.That(launch.GetProperty("launchNumber").GetInt32(), Is.EqualTo(number));
    }

    [Test]
    public async Task GetLaunchRunsAndSuites_ShouldReturnData()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Positive Suite");
        var testItemId = await CreateTestItemAsync(launchId, suiteId, "Positive Test");

        // Act & Assert for Runs
        var runsResponse = await HttpClient.GetAsync($"/api/launches/{launchId}/runs");
        Assert.That(runsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var runs = await runsResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(runs, Is.Not.Null);
        // Note: After fixing the bug in LaunchesEndpoints, runId is now correctly populated
        Assert.That(runs!.Any(r => r.GetProperty("runId").GetString() == testItemId.ToString()), Is.True);

        // Cleanup: Finish the test item to release browser
        await HttpClient.PutAsJsonAsync($"/api/test-items/{testItemId}/finish", new { status = "Passed" });

        // Act & Assert for Suites
        var suitesResponse = await HttpClient.GetAsync($"/api/launches/{launchId}/suites");
        Assert.That(suitesResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var suites = await suitesResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(suites, Is.Not.Null);
        Assert.That(suites!.Any(s => s.GetProperty("id").GetString() == suiteId.ToString()), Is.True);
    }

    [Test]
    public async Task GetLaunchTestItems_ShouldReturnData()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Tree Suite");
        var testItemId = await CreateTestItemAsync(launchId, suiteId, "Tree Test");

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{launchId}/test-items");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var items = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(items, Is.Not.Null);
        Assert.That(items!.Any(i => i.GetProperty("id").GetString() == suiteId.ToString()), Is.True);
        Assert.That(items!.Any(i => i.GetProperty("id").GetString() == testItemId.ToString()), Is.True);

        // Cleanup: Finish the test item to release browser
        await HttpClient.PutAsJsonAsync($"/api/test-items/{testItemId}/finish", new { status = "Passed" });
    }

    [Test]
    public async Task CompareLaunches_ShouldReturnComparison()
    {
        // Arrange
        var launchId1 = await CreateActiveLaunchAsync();
        var launchId2 = await CreateActiveLaunchAsync();
        var request = new { launchIds = new[] { launchId1, launchId2 } };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/launches/compare", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var comparisons = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(comparisons, Is.Not.Null);
        Assert.That(comparisons!.Count, Is.EqualTo(2));
        Assert.That(comparisons.Any(c => c.GetProperty("launchId").GetString() == launchId1.ToString()), Is.True);
        Assert.That(comparisons.Any(c => c.GetProperty("launchId").GetString() == launchId2.ToString()), Is.True);
    }

    [Test]
    public async Task GetLaunchParentItemsHistory_ShouldReturnData()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{launchId}/parent-items-history?depth=5");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.TryGetProperty("columns", out _), Is.True);
        Assert.That(result.TryGetProperty("rows", out _), Is.True);
    }

    [Test]
    public async Task DeleteLaunch_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();

        // Act: Delete
        var deleteResponse = await HttpClient.DeleteAsync($"/api/launches/{launchId}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Assert: Verify it's gone
        var getResponse = await HttpClient.GetAsync($"/api/launches/{launchId}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new
        {
            name = $"Positive Test Launch {Guid.NewGuid():N}",
            description = "Integration test"
        };

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

    private async Task<Guid> CreateTestItemAsync(Guid launchId, Guid parentId, string name)
    {
        var request = new
        {
            launchUuid = launchId.ToString(),
            parentItemId = parentId.ToString(),
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
