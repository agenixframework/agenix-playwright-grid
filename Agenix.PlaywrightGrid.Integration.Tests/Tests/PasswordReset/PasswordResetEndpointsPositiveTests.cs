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
using NUnit.Framework;
using StackExchange.Redis;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.PasswordReset;

[TestFixture]
public class PasswordResetEndpointsPositiveTests : ApiTestBase
{
    [Test]
    public async Task ForgotPassword_ShouldAlwaysReturnOk()
    {
        // Arrange
        var request = new { email = "test@example.com" };

        // Act
        var response = await HttpClient.PostAsJsonAsync("/admin/auth/forgot-password", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("message").GetString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task FullResetFlow_WithRedisSeeding_ShouldSucceed()
    {
        // Arrange
        var email = "integration-test@example.com";
        var token = Guid.NewGuid().ToString("N");

        // Manually seed Redis to bypass the actual email sending and user existence check
        var tokenKey = RedisKeys.AdminPasswordResetToken(token);
        var emailKey = RedisKeys.AdminPasswordResetByEmail(email.ToLowerInvariant());

        await Redis.StringSetAsync(tokenKey, email, TimeSpan.FromMinutes(60));
        await Redis.StringSetAsync(emailKey, token, TimeSpan.FromMinutes(60));

        // Act: Validate Token
        var validateResponse = await HttpClient.GetAsync($"/admin/auth/reset-password/{token}");
        Assert.That(validateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var validateResult = await validateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(validateResult.GetProperty("email").GetString(), Is.EqualTo(email));

        // Act: Reset Password
        var resetRequest = new { newPassword = "SecurePassword123!" };
        var resetResponse = await HttpClient.PostAsJsonAsync($"/admin/auth/reset-password/{token}", resetRequest);

        // Assert
        Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify Redis keys are cleaned up
        Assert.That(await Redis.KeyExistsAsync(tokenKey), Is.False);
        Assert.That(await Redis.KeyExistsAsync(emailKey), Is.False);
    }
}
