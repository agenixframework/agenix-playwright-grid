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
///     Integration tests for error handling in TestItemsEndpoints.
///     Verifies RFC 7807 ProblemDetails responses and EventCode integration.
/// </summary>
[TestFixture]
public class ErrorHandlingTests : ApiTestBase
{
    private const string ValidationFailedCode = "ADM91";
    private const string TestItemNotFoundCode = "ITEM03";
    private const string LaunchAlreadyFinishedCode = "LCH07";

    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task StartTestItem_MissingLaunchUuid_Returns400WithEventCode()
    {
        // Arrange
        var request = new { name = "Test", type = "Test" };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "LaunchUuid");
    }

    [Test]
    public async Task StartTestItem_MissingName_Returns400WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var request = new { launchUuid = launchId.ToString(), type = "Test" };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "Name");
    }

    [Test]
    public async Task StartTestItem_NonExistentLaunch_Returns404WithEventCode()
    {
        // Arrange
        var request = new { launchUuid = Guid.NewGuid().ToString(), name = "Test", type = "Test" };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, "LCH03");
    }

    [Test]
    public async Task StartTestItem_FinishedLaunch_Returns409WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", new { });

        // Delay for cache
        await Task.Delay(200);

        var request = new { launchUuid = launchId.ToString(), name = "Test", type = "Test" };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.Conflict, LaunchAlreadyFinishedCode);
    }

    [Test]
    public async Task GetTestItem_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{id}");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, TestItemNotFoundCode);
    }

    [Test]
    public async Task UpdateTestItem_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new { name = "Update" };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/test-items/{id}", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, TestItemNotFoundCode);
    }

    [Test]
    public async Task UpdateTestItemStatus_MissingStatus_Returns400WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Status Test");
        var request = new { computedStatus = "" };

        // Act
        var response = await HttpClient.PatchAsJsonAsync($"/api/test-items/{testId}/status", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "computedStatus");
    }

    [Test]
    public async Task GetTestItemByNumber_MissingProjectKey_Returns400WithEventCode()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(HubUrl) };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestUser.ApiKey}");
        var launchId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/test-items/by-number/{launchId}/1");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "X-Project-Key");
    }

    [Test]
    public async Task GetTestItemByNumber_NonExistent_Returns404WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/by-number/{launchId}/999999");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, TestItemNotFoundCode);
    }

    private async Task AssertProblemDetails(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedEventCode,
        string? expectedErrorField = null)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var content = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<JsonElement>(content);

        Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo((int)expectedStatus));
        Assert.That(problem.GetProperty("eventCode").GetString(), Is.EqualTo(expectedEventCode));
        Assert.That(problem.TryGetProperty("traceId", out _), Is.True);

        if (expectedErrorField != null)
        {
            Assert.That(problem.TryGetProperty("errors", out var errors), Is.True, "Should contain 'errors' property");
            Assert.That(errors.TryGetProperty(expectedErrorField, out _), Is.True, $"Should contain error for field '{expectedErrorField}'");
        }
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new { name = $"Error Test Launch {Guid.NewGuid():N}" };
        var response = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
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
            type = "Test"
        };
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }
}
