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
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.ProjectSettings;

[TestFixture]
public class ProjectSettingsPositiveTests : ApiTestBase
{
    [Test]
    public async Task GetSettings_ReturnsDefaultSettings_WhenNoneExist()
    {
        // Arrange - Ensure we use a fresh project key to get defaults
        var freshProjectKey = $"fresh-{Guid.NewGuid():N}";

        // Act
        var response = await HttpClient.GetAsync($"/api/projects/{freshProjectKey}/settings");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var settings = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings["launchInactivityTimeout"], Is.EqualTo("1d"));
        Assert.That(settings["keepLaunches"], Is.EqualTo("30d"));
        Assert.That(settings["keepLogs"], Is.EqualTo("7d"));
        Assert.That(settings["keepAttachments"], Is.EqualTo("7d"));
    }

    [Test]
    public async Task UpdateSettings_SavesAndReturnsMergedSettings()
    {
        // Arrange
        var updateRequest = new
        {
            launchInactivityTimeout = "3h",
            keepLaunches = "90d"
        };

        // Act - Update settings
        var postResponse = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert Post
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var postResult = await postResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.That(postResult["ok"].ToString(), Is.EqualTo("True"));
        Assert.That(postResult["launchInactivityTimeout"].ToString(), Is.EqualTo("3h"));
        Assert.That(postResult["keepLaunches"].ToString(), Is.EqualTo("90d"));

        // Act - Get settings to verify persistence
        var getResponse = await HttpClient.GetAsync($"/api/projects/{ProjectKey}/settings");

        // Assert Get
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var settings = await getResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings["launchInactivityTimeout"], Is.EqualTo("3h"));
        Assert.That(settings["keepLaunches"], Is.EqualTo("90d"));
        // Defaults should be preserved for other fields
        Assert.That(settings["keepLogs"], Is.EqualTo("7d"));
        Assert.That(settings["keepAttachments"], Is.EqualTo("7d"));
    }

    [Test]
    public async Task UpdateSettings_NormalizesValuesWithoutSuffix()
    {
        // Arrange
        var updateRequest = new
        {
            keepLaunches = "180" // No 'd' suffix
        };

        // Act
        var response = await HttpClient.PostAsJsonAsync($"/api/projects/{ProjectKey}/settings", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.That(result["keepLaunches"].ToString(), Is.EqualTo("180d"));
    }
}
