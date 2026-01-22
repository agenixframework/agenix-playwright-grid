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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Artifacts;

[TestFixture]
public class ArtifactCacheStatsEndpointsPositiveTests : ApiTestBase
{
    [Test]
    public async Task GetCacheStatsAndHealth_ShouldSucceed()
    {
        // Act: Stats
        var statsResponse = await HttpClient.GetAsync("/api/admin/cache/artifacts/stats");
        Assert.That(statsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var statsResult = await statsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(statsResult.GetProperty("enabled").GetBoolean(), Is.AnyOf(true, false));

        // Act: Health
        var healthResponse = await HttpClient.GetAsync("/api/admin/cache/artifacts/health");
        Assert.That(healthResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var healthResult = await healthResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(healthResult.GetProperty("healthStatus").GetString(), Is.Not.Null);
    }

    [Test]
    public async Task ClearCache_ShouldSucceed()
    {
        // Act
        var response = await HttpClient.PostAsync("/api/admin/cache/artifacts/clear", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
    }
}
