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
using Agenix.PlaywrightGrid.Shared.Logging;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Launch;

/// <summary>
///     Integration tests for LaunchesEndpoints error handling.
///     Verifies correct HTTP status codes, EventCodes, and ProblemDetails responses.
/// </summary>
[TestFixture]
public class ErrorHandlingTests : ApiTestBase
{
    private const string ValidationFailedCode = "ADM91";
    private const string LaunchNotFoundCode = "LCH03";
    private const string LaunchAlreadyFinishedCode = "LCH07";

    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task GetLaunches_MissingProjectKey_Returns400WithEventCode()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(HubUrl) };
        // No X-Project-Key header, no projectKey query param

        // Act
        var response = await client.GetAsync("/api/launches");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "projectKey");
    }

    [Test]
    public async Task CreateLaunch_MissingProjectKeyHeader_Returns400WithEventCode()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(HubUrl) };
        var request = new { name = "Test Launch" };

        // Act
        var response = await client.PostAsJsonAsync("/api/launches", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "X-Project-Key");
    }

    [Test]
    public async Task GetLaunchById_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{nonExistentId}");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task GetLaunchById_MissingProjectKeyHeader_Returns400WithEventCode()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(HubUrl) };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestUser.ApiKey}");
        var id = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/launches/{id}");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "X-Project-Key");
    }

    [Test]
    public async Task UpdateLaunch_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new { name = "Updated Name" };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/launches/{id}", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task UpdateLaunch_EmptyRequest_Returns400WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var request = new { };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "request");
    }

    [Test]
    public async Task FinishLaunch_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new { endTime = DateTime.UtcNow };

        // Act
        var response = await HttpClient.PutAsJsonAsync($"/api/launches/{id}/finish", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task FinishLaunch_AlreadyFinished_Returns409WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var finishRequest = new { endTime = DateTime.UtcNow, status = "Finished" };

        var firstResponse = await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", finishRequest);
        Assert.That(firstResponse.IsSuccessStatusCode, Is.True);

        // Act
        var secondResponse = await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", finishRequest);

        // Assert
        await AssertProblemDetails(secondResponse, HttpStatusCode.Conflict, LaunchAlreadyFinishedCode);
    }

    [Test]
    public async Task DeleteLaunch_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await HttpClient.DeleteAsync($"/api/launches/{id}");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task BulkUpdate_EmptyIds_Returns400WithEventCode()
    {
        // Arrange
        var request = new { launchIds = Array.Empty<Guid>(), updates = new { name = "Bulk" } };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/launches/bulk-update", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "LaunchIds");
    }

    [Test]
    public async Task GetLaunchByNumber_NonExistent_Returns404WithEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync($"/api/launches/by-number/{ProjectKey}/999999");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task GetLaunchByDbId_NonExistent_Returns404WithEventCode()
    {
        // Act
        var response = await HttpClient.GetAsync($"/api/launches/by-db-id/{ProjectKey}/999999");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task GetLaunchRuns_NonExistentLaunch_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{id}/runs");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task GetLaunchSuites_NonExistentLaunch_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{id}/suites");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task CompareLaunches_TooFewIds_Returns400WithEventCode()
    {
        // Arrange
        var request = new { launchIds = new[] { Guid.NewGuid() } };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/launches/compare", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "LaunchIds");
    }

    [Test]
    public async Task CompareLaunches_TooManyIds_Returns400WithEventCode()
    {
        // Arrange
        var request = new { launchIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray() };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/launches/compare", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, ValidationFailedCode, "LaunchIds");
    }

    [Test]
    public async Task ForceFinish_NonExistent_Returns404WithEventCode()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/launches/{id}/force-finish", new { reason = "test" });

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, LaunchNotFoundCode);
    }

    [Test]
    public async Task ForceFinish_AlreadyFinished_Returns409WithEventCode()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();

        // Finish it normally
        var finishRequest = new { endTime = DateTime.UtcNow, status = "Finished" };
        await HttpClient.PutAsJsonAsync($"/api/launches/{launchId}/finish", finishRequest);

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/launches/{launchId}/force-finish", new { reason = "test" });

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.Conflict, LaunchAlreadyFinishedCode);
    }

    private async Task AssertProblemDetails(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedEventCode, string? expectedErrorField = null)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus), $"Expected {expectedStatus} but got {response.StatusCode}. Content: {content}");
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var pd = JsonSerializer.Deserialize<JsonElement>(content);

        // Standard ProblemDetails properties are usually camelCase in JSON
        Assert.That(GetPropertyIgnoringCase(pd, "status").GetInt32(), Is.EqualTo((int)expectedStatus));
        Assert.That(GetPropertyIgnoringCase(pd, "eventCode").GetString(), Is.EqualTo(expectedEventCode));
        Assert.That(TryGetPropertyIgnoringCase(pd, "traceId", out _), Is.True, "TraceId should be present in ProblemDetails");

        if (expectedErrorField != null)
        {
            var errors = GetPropertyIgnoringCase(pd, "errors");
            Assert.That(TryGetPropertyIgnoringCase(errors, expectedErrorField, out _), Is.True, $"Errors should contain field '{expectedErrorField}'");
        }
    }

    private JsonElement GetPropertyIgnoringCase(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var property)) return property;
        if (element.TryGetProperty(name.Substring(0, 1).ToUpper() + name.Substring(1), out property)) return property;
        throw new KeyNotFoundException($"Property '{name}' not found (tried both camelCase and PascalCase)");
    }

    private bool TryGetPropertyIgnoringCase(JsonElement element, string name, out JsonElement property)
    {
        if (element.TryGetProperty(name, out property)) return true;
        if (element.TryGetProperty(name.Substring(0, 1).ToUpper() + name.Substring(1), out property)) return true;
        return false;
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new
        {
            name = $"Error Test Launch {Guid.NewGuid():N}",
            description = "Integration test for error handling"
        };

        var response = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }
}
