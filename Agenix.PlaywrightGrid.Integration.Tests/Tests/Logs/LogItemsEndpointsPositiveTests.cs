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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Logs;

[TestFixture]
public class LogItemsEndpointsPositiveTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task CreateAndGetLogItem_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Log Test");

        var logRequest = new
        {
            itemUuid = testId.ToString(),
            time = DateTime.UtcNow,
            level = "INFO",
            message = "Test log message"
        };

        // Act: Create
        var createResponse = await HttpClient.PostAsJsonAsync($"/v1/{ProjectKey}/log", logRequest);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));

        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var logId = Guid.Parse(createResult.GetProperty("id").GetString()!);

        // Act: Get by ID (with retry because of async ingestion)
        JsonElement? logItem = null;
        for (int i = 0; i < 15; i++)
        {
            var getResponse = await HttpClient.GetAsync($"/v1/{ProjectKey}/log/{logId}");
            if (getResponse.StatusCode == HttpStatusCode.OK)
            {
                logItem = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
                break;
            }
            await Task.Delay(1000);
        }

        Assert.That(logItem, Is.Not.Null);
        Assert.That(logItem!.Value.GetProperty("id").GetString(), Is.EqualTo(logId.ToString()));
        Assert.That(logItem!.Value.GetProperty("message").GetString(), Is.EqualTo("Test log message"));
    }

    [Test]
    public async Task CreateLogItemBatch_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Batch Log Test");

        var batchRequest = new[]
        {
            new { itemUuid = testId.ToString(), level = "INFO", message = "Message 1", time = DateTime.UtcNow },
            new { itemUuid = testId.ToString(), level = "DEBUG", message = "Message 2", time = DateTime.UtcNow }
        };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/v1/{ProjectKey}/log/batch", batchRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("responses").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task GetLogItemsForTestItem_ShouldReturnLogs()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "List Log Test");
        await CreateLogItemAsync(testId, "Log 1");
        await CreateLogItemAsync(testId, "Log 2");

        // Act: Use retry because of async ingestion
        List<JsonElement>? logs = null;
        for (int i = 0; i < 10; i++)
        {
            var response = await HttpClient.GetAsync($"/v1/{ProjectKey}/log/test-item/{testId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                logs = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
                if (logs != null && logs.Count >= 2) break;
            }
            await Task.Delay(1000);
        }

        // Assert
        Assert.That(logs, Is.Not.Null);
        Assert.That(logs!.Count, Is.GreaterThanOrEqualTo(2));
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new { name = $"Logs Launch {Guid.NewGuid():N}" };
        var response = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
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

    private async Task CreateLogItemAsync(Guid testId, string message)
    {
        var logRequest = new
        {
            itemUuid = testId.ToString(),
            time = DateTime.UtcNow,
            level = "INFO",
            message = message
        };
        var response = await HttpClient.PostAsJsonAsync($"/v1/{ProjectKey}/log", logRequest);
        response.EnsureSuccessStatusCode();
    }
}
