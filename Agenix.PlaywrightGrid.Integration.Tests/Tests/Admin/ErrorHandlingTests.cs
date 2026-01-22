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
using Agenix.PlaywrightGrid.Domain;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;
using Agenix.PlaywrightGrid.Shared.Logging;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Admin;

[TestFixture]
public class ErrorHandlingTests : ApiTestBase
{
    private const string AdminUserId = "admin";
    private const string NonExistentProjectId = "non-existent-project";
    private const string NonExistentUserId = "non-existent-user";

    [OneTimeSetUp]
    public override async Task OneTimeSetup()
    {
        await base.OneTimeSetup();

        // Seed admin user for tests that use AdminUserId = "admin"
        await RedisHelpers.CreateUserAsync(Redis, AdminUserId, "admin", AccountRole.Administrator);
    }

    [Test]
    public async Task LoginWithMissingCredentials_ShouldReturnBadRequest()
    {
        // Act
        var response = await HttpClient.PostAsJsonAsync("/admin/auth/login", new { });

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, EventCodes.AdminProjectsUsers.Validation.ValidationFailed);
    }

    [Test]
    public async Task LoginWithInvalidCredentials_ShouldReturnUnauthorized()
    {
        // Act
        var response = await HttpClient.PostAsJsonAsync("/admin/auth/login", new
        {
            id = "admin",
            password = "wrong-password"
        });

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.Unauthorized, EventCodes.AdminProjectsUsers.Authentication.LoginFailed);
    }

    [Test]
    public async Task UnauthenticatedAccess_ShouldReturnUnauthorized()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(HubUrl) };
        // No headers

        // Act
        var response = await client.GetAsync("/admin/projects");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.Unauthorized, EventCodes.AdminProjectsUsers.Authentication.LoginFailed);
    }

    [Test]
    public async Task GetNonExistentProject_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/projects/{NonExistentProjectId}");
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted);
    }

    [Test]
    public async Task CreateProjectWithInvalidKey_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/projects")
        {
            Content = JsonContent.Create(new
            {
                key = "Invalid Key!", // Spaces and symbols not allowed
                name = "Test Project"
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "key");
    }

    [Test]
    public async Task GetNonExistentUser_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/users/{NonExistentUserId}");
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.UserManagement.UserDeleted);
    }

    [Test]
    public async Task CreateUserWithInvalidEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/users")
        {
            Content = JsonContent.Create(new
            {
                id = "newuser",
                username = "newuser",
                email = "invalid-email",
                password = "Password123!"
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "email");
    }

    [Test]
    public async Task AddMembershipForNonExistentUser_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Put, $"/admin/projects/{ProjectKey}/members/{NonExistentUserId}")
        {
            Content = JsonContent.Create(new
            {
                role = "Member"
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.UserManagement.UserDeleted);
    }

    [Test]
    public async Task AddMembershipForNonExistentProject_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Put, $"/admin/projects/{NonExistentProjectId}/members/{AdminUserId}")
        {
            Content = JsonContent.Create(new
            {
                role = "Member"
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted);
    }

    [Test]
    public async Task ActivateNonExistentUser_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/users/{NonExistentUserId}/activate");
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.UserManagement.UserDeleted);
    }

    [Test]
    public async Task DeactivateNonExistentUser_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/users/{NonExistentUserId}/deactivate");
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.UserManagement.UserDeleted);
    }

    [Test]
    public async Task UpdateNonExistentProject_ShouldReturnNotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/admin/projects/{NonExistentProjectId}")
        {
            Content = JsonContent.Create(new
            {
                name = "Updated Name"
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.AdminProjectsUsers.ProjectManagement.ProjectDeleted);
    }

    [Test]
    public async Task ChangePasswordWithInvalidComplexity_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/users/{AdminUserId}/password")
        {
            Content = JsonContent.Create(new
            {
                oldPassword = "AnyPassword123!",
                newPassword = "short"
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.BadRequest, EventCodes.AdminProjectsUsers.Validation.ValidationFailed, "newPassword");
    }

    [Test]
    public async Task DeleteActiveUser_ShouldReturnConflict()
    {
        // Arrange
        // Create a temporary user that is active
        var userId = "active-user-to-delete";
        await RedisHelpers.CreateTestUserWithApiKeyAsync(Redis, userId, "activeuser", "integration-test", ProjectKey);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/admin/users/{userId}");
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.Conflict, EventCodes.AdminProjectsUsers.UserManagement.UserDeleted);

        // Cleanup
        await RedisHelpers.CleanupUserDataAsync(Redis, userId, ProjectKey);
    }

    [Test]
    public async Task UpdateProjectWithExistingName_ShouldReturnConflict()
    {
        // Arrange
        var otherProjectKey = "other-project";
        var otherProjectName = "Other Project";
        await RedisHelpers.CreateProjectAsync(Redis, otherProjectKey, otherProjectName);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/admin/projects/{ProjectKey}")
        {
            Content = JsonContent.Create(new
            {
                name = otherProjectName
            })
        };
        request.Headers.Add("x-user-id", AdminUserId);

        // Act
        var response = await HttpClient.SendAsync(request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.Conflict, EventCodes.AdminProjectsUsers.Validation.ValidationFailed);

        // Cleanup
        await Redis.KeyDeleteAsync($"admin:project:{otherProjectKey}");
        await Redis.KeyDeleteAsync($"admin:project:by-name:{otherProjectName.ToLowerInvariant()}");
    }

    private async Task AssertProblemDetails(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedEventCode, string? expectedErrorField = null)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus), $"Expected {expectedStatus} but got {response.StatusCode}. Content: {content}");
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var pd = JsonSerializer.Deserialize<JsonElement>(content);

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
        if (element.TryGetProperty(name.Substring(0, 1).ToLower() + name.Substring(1), out property)) return property;
        if (element.TryGetProperty(name.Substring(0, 1).ToUpper() + name.Substring(1), out property)) return property;
        throw new KeyNotFoundException($"Property '{name}' not found");
    }

    private bool TryGetPropertyIgnoringCase(JsonElement element, string name, out JsonElement property)
    {
        if (element.TryGetProperty(name, out property)) return true;
        if (element.TryGetProperty(name.Substring(0, 1).ToLower() + name.Substring(1), out property)) return true;
        if (element.TryGetProperty(name.Substring(0, 1).ToUpper() + name.Substring(1), out property)) return true;
        return false;
    }
}
