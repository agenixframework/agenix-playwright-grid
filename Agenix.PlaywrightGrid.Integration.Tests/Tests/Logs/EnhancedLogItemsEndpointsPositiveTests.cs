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
public class EnhancedLogItemsEndpointsPositiveTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task GetEnhancedLogsAndStats_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Enhanced Log Test");
        await CreateLogItemAsync(testId, "Enhanced Log 1");

        // Act: Use retry because of async ingestion
        bool logsIngested = false;
        for (int i = 0; i < 15; i++)
        {
            var flatResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/flat");
            if (flatResponse.StatusCode == HttpStatusCode.OK)
            {
                var result = await flatResponse.Content.ReadFromJsonAsync<JsonElement>();
                if (result.GetProperty("totalCount").GetInt32() >= 1)
                {
                    logsIngested = true;
                    break;
                }
            }
            await Task.Delay(1000);
        }

        Assert.That(logsIngested, Is.True, "Logs were not ingested in time");

        // Act: Stats
        var statsResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/stats");
        Assert.That(statsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var statsResult = await statsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(statsResult.GetProperty("itemId").GetString(), Is.EqualTo(testId.ToString()));

        // Act: Hierarchical
        var hierarchicalResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/hierarchical?useCache=false");
        Assert.That(hierarchicalResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var hierarchicalResult = await hierarchicalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(hierarchicalResult.GetProperty("itemId").GetString(), Is.EqualTo(testId.ToString()));
    }

    [Test]
    public async Task SearchAndExportLogs_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Search Export Test");
        await CreateLogItemAsync(testId, "FindMeInLogs");

        // Wait for ingestion
        bool ingested = false;
        for (int i = 0; i < 15; i++)
        {
            var flatResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/flat");
            if (flatResponse.StatusCode == HttpStatusCode.OK)
            {
                var result = await flatResponse.Content.ReadFromJsonAsync<JsonElement>();
                if (result.GetProperty("totalCount").GetInt32() >= 1)
                {
                    ingested = true;
                    break;
                }
            }
            await Task.Delay(1000);
        }
        Assert.That(ingested, Is.True, "Logs were not ingested in time for search/export");

        // Act: Search
        var searchResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/search?query=FindMe");
        Assert.That(searchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act: Export JSON
        var exportJsonResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/export?format=json");
        Assert.That(exportJsonResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(exportJsonResponse.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));

        // Act: Export CSV
        var exportCsvResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/logs/export?format=csv");
        Assert.That(exportCsvResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(exportCsvResponse.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/csv"));
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new { name = $"Enhanced Logs Launch {Guid.NewGuid():N}" };
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
