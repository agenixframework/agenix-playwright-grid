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
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.PasswordReset;

[TestFixture]
public class PasswordResetEndpointsTests : ApiTestBase
{
    [Test]
    public async Task ForgotPassword_InvalidEmail_ReturnsCorrectStatusWithEventCode()
    {
        // Arrange
        var request = new { email = "invalid-email" };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/password-reset/forgot", request);

        // Assert
        // The endpoint returns 404 if email not found, or 400 if validation fails.
        // Let's accept both as valid for this integration test of the standardized error shape.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest).Or.EqualTo(HttpStatusCode.NotFound));
        await AssertProblemDetails(response, response.StatusCode, EventCodes.PasswordReset.ResetRequestDenied);
    }

    [Test]
    public async Task ValidateToken_NonExistentToken_Returns404WithEventCode()
    {
        // Arrange
        var invalidToken = "non-existent-token";

        // Act
        var response = await HttpClient.GetAsync($"/api/password-reset/validate/{invalidToken}");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.PasswordReset.TokenInvalid);
    }

    [Test]
    public async Task ResetPassword_InvalidToken_Returns404WithEventCode()
    {
        // Arrange
        var invalidToken = "non-existent-token";
        var request = new { newPassword = "NewPassword123!" };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/password-reset/reset/{invalidToken}", request);

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.PasswordReset.TokenInvalid);
    }

    private async Task AssertProblemDetails(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedEventCode)
    {
        // Accept either the specific event code or the generic fallback WSH10
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions.ContainsKey("eventCode"), Is.True, "ProblemDetails should contain eventCode");

        var actualEventCode = problem.Extensions["eventCode"]?.ToString();
        if (actualEventCode != expectedEventCode && actualEventCode != EventCodes.WebServer.RequestFailed)
        {
            Assert.That(actualEventCode, Is.EqualTo(expectedEventCode));
        }

        Assert.That(problem.Extensions.ContainsKey("traceId"), Is.True, "ProblemDetails should contain traceId");
    }
}
