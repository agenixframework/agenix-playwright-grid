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
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.ProjectSettings;

[TestFixture]
public class ErrorHandlingTests : ApiTestBase
{
    private const string ValidationEventCode = "PRJ11";

    [Test]
    public async Task UpdateSettings_InvalidRequestBody_Returns400WithEventCode()
    {
        // Act - Send "null" JSON literal
        var response = await HttpClient.PostAsync($"/api/projects/{ProjectKey}/settings", new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("Request"), Is.True);
    }

    [Test]
    public async Task UpdateSettings_InvalidTimeout_Returns400WithEventCode()
    {
        // Arrange
        var updateRequest = new { launchInactivityTimeout = "invalid" };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("launchInactivityTimeout"), Is.True);
    }

    [Test]
    public async Task UpdateSettings_InvalidKeepLaunches_Returns400WithEventCode()
    {
        // Arrange
        var updateRequest = new { keepLaunches = "invalid" };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("keepLaunches"), Is.True);
    }

    [Test]
    public async Task UpdateSettings_InvalidKeepLogs_Returns400WithEventCode()
    {
        // Arrange
        var updateRequest = new { keepLogs = "invalid" };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("keepLogs"), Is.True);
    }

    [Test]
    public async Task UpdateSettings_InvalidKeepAttachments_Returns400WithEventCode()
    {
        // Arrange
        var updateRequest = new { keepAttachments = "invalid" };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("keepAttachments"), Is.True);
    }

    [Test]
    public async Task UpdateSettings_LogsGreaterThanLaunches_Returns400WithEventCode()
    {
        // Arrange
        var updateRequest = new
        {
            keepLaunches = "30d",
            keepLogs = "90d"
        };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("keepLogs"), Is.True);
        Assert.That(problem.Errors["keepLogs"][0], Contains.Substring("Logs retention must be ≤ Launches retention"));
    }

    [Test]
    public async Task UpdateSettings_AttachmentsGreaterThanLogs_Returns400WithEventCode()
    {
        // Arrange
        var updateRequest = new
        {
            keepLogs = "7d",
            keepAttachments = "30d"
        };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(ValidationEventCode));
        Assert.That(problem.Errors.ContainsKey("keepAttachments"), Is.True);
        Assert.That(problem.Errors["keepAttachments"][0], Contains.Substring("Attachments retention must be ≤ Logs retention"));
    }
}
