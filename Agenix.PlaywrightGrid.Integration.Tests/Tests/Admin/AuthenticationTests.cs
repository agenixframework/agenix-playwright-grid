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
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using NUnit.Framework;
using StackExchange.Redis;
using ProjectRole = Agenix.PlaywrightGrid.Domain.ProjectRole;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Admin;

/// <summary>
///     Integration tests for API key authentication and authorization.
///     Tests various authentication scenarios including missing/invalid tokens,
///     wrong project access, disabled users, and role-based access control.
/// </summary>
[TestFixture]
public class AuthenticationTests
{
    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _hubUrl = Environment.GetEnvironmentVariable("HUB_URL") ?? "http://localhost:5100";
        _projectKey = Environment.GetEnvironmentVariable("PROJECT_KEY") ?? TestConstants.DefaultProjectKey;

        // Use singleton Redis fixture
        _redis = RedisTestFixture.Instance.GetDatabase();

        // Create HTTP client
        _httpClient = new HttpClient { BaseAddress = new Uri(_hubUrl), Timeout = TimeSpan.FromSeconds(10) };

        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Hub URL: {_hubUrl}");
        await TestContext.Progress.WriteLineAsync($"[{GetType().Name}] Project Key: {_projectKey}");
    }

    [OneTimeTearDown]
    public void OneTimeTeardown()
    {
        _httpClient?.Dispose();
    }

    private IDatabase _redis = null!;
    private HttpClient _httpClient = null!;
    private string _hubUrl = null!;
    private string _projectKey = null!;

    [Test]
    public async Task MissingAuthorizationHeader_Returns401()
    {
        // Arrange
        var request = new StartLaunchRequest { Name = "Test Launch", Attributes = new List<ItemAttribute>() };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/launches")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Project-Key", _projectKey);
        // No Authorization header

        // Act
        var response = await _httpClient.SendAsync(httpRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task InvalidBearerToken_Returns401()
    {
        // Arrange
        var request = new StartLaunchRequest { Name = "Test Launch", Attributes = new List<ItemAttribute>() };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/launches")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-key-12345");
        httpRequest.Headers.Add("X-Project-Key", _projectKey);

        // Act
        var response = await _httpClient.SendAsync(httpRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ValidKeyButWrongProject_Returns403()
    {
        // Arrange - Create user with API key for a different project
        var userId = "test-user-wrong-project";
        var wrongProjectKey = "DifferentProject";

        // Create both projects (the one we're testing and the wrong one)
        await RedisHelpers.CreateProjectAsync(_redis, _projectKey, "Test Project");
        await RedisHelpers.CreateProjectAsync(_redis, wrongProjectKey, "Different Project");

        var userInfo = await RedisHelpers.CreateTestUserWithApiKeyAsync(
            _redis,
            userId,
            "testuserWrongProject",
            "test-key",
            wrongProjectKey
        );

        // Act - Try to access TestProject with key for DifferentProject
        var request = new StartLaunchRequest { Name = "Test Launch", Attributes = new List<ItemAttribute>() };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/launches")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userInfo.ApiKey);
        httpRequest.Headers.Add("X-Project-Key", _projectKey);

        var response = await _httpClient.SendAsync(httpRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // Cleanup
        await RedisHelpers.CleanupUserDataAsync(_redis, userId, wrongProjectKey);
    }

    [Test]
    public async Task DisabledUser_Returns401()
    {
        // Arrange - Create disabled user with API key
        var userId = "test-user-disabled";

        // Create project (required for authentication)
        await RedisHelpers.CreateProjectAsync(_redis, _projectKey, "Test Project");

        var userInfo = await RedisHelpers.CreateTestUserWithApiKeyAsync(
            _redis,
            userId,
            "testuserDisabled",
            "test-key",
            _projectKey,
            ProjectRole.Client,
            UserStatus.Disabled // Disabled user
        );

        // Act
        var request = new StartLaunchRequest { Name = "Test Launch", Attributes = new List<ItemAttribute>() };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/launches")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userInfo.ApiKey);
        httpRequest.Headers.Add("X-Project-Key", _projectKey);

        var response = await _httpClient.SendAsync(httpRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // Cleanup
        await RedisHelpers.CleanupUserDataAsync(_redis, userId, _projectKey);
    }

    [Test]
    public async Task AllRolesCanAccessApi_Returns201()
    {
        // Test that ProjectLead, Member, Client, and Maintainer roles all have access
        var roles = new[] { ProjectRole.ProjectLead, ProjectRole.Member, ProjectRole.Client, ProjectRole.Maintainer };

        // Create project once (required for authentication)
        await RedisHelpers.CreateProjectAsync(_redis, _projectKey, "Test Project");

        foreach (var role in roles)
        {
            // Arrange - Create user with API key for current role
            var userId = $"test-user-{role}";

            var userInfo = await RedisHelpers.CreateTestUserWithApiKeyAsync(
                _redis,
                userId,
                $"testuser{role}",
                "test-key",
                _projectKey,
                role
            );

            // Act
            var request = new StartLaunchRequest
            {
                Name = $"Test Launch {role}",
                Attributes = new List<ItemAttribute>()
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/launches")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userInfo.ApiKey);
            httpRequest.Headers.Add("X-Project-Key", _projectKey);

            var response = await _httpClient.SendAsync(httpRequest);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                $"Role {role} should have access (got {response.StatusCode})");

            // Cleanup user data
            await RedisHelpers.CleanupUserDataAsync(_redis, userId, _projectKey);

            // Cleanup - Delete created launch if successful
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var launchResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
                if (launchResponse != null && launchResponse.TryGetValue("id", out var idElement))
                {
                    var launchId = idElement.GetGuid();

                    // Delete launch using authenticated request
                    var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/launches/{launchId}");
                    deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userInfo.ApiKey);
                    deleteRequest.Headers.Add("X-Project-Key", _projectKey);
                    await _httpClient.SendAsync(deleteRequest);
                }
            }
        }
    }
}
