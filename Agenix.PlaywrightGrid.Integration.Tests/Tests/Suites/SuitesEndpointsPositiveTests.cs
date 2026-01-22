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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Suites;

[TestFixture]
public class SuitesEndpointsPositiveTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task GetSuiteById_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Positive Suite");

        // Act
        var response = await HttpClient.GetAsync($"/api/suites/{suiteId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("id").GetString(), Is.EqualTo(suiteId.ToString()));
        Assert.That(result.GetProperty("type").GetString(), Is.EqualTo("Suite"));
    }

    [Test]
    public async Task GetSuiteRuns_ShouldReturnRuns()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var suiteId = await CreateSuiteAsync(launchId, "Suite for Runs");

        // Act
        var response = await HttpClient.GetAsync($"/api/suites/{suiteId}/runs");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(result, Is.Not.Null);
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new { name = $"Suites Launch {Guid.NewGuid():N}" };
        var response = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateSuiteAsync(Guid launchId, string name)
    {
        var request = new { launchUuid = launchId.ToString(), name = name, type = "Suite" };
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }
}
