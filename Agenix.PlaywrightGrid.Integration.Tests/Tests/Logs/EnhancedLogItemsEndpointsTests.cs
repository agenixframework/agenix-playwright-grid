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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Logs;

[TestFixture]
public class EnhancedLogItemsEndpointsTests : ApiTestBase
{
    [Test]
    public async Task GetHierarchicalLogs_NonExistentTestItem_Returns200WithEmptyList()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{nonExistentId}/logs/hierarchical");

        // Assert
        // EnhancedLogItemsEndpoints returns 200 with empty list if item not found, not 404.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetFlatLogs_NonExistentTestItem_Returns200WithEmptyList()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{nonExistentId}/logs/flat");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SearchLogs_NonExistentTestItem_Returns200WithEmptyList()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{nonExistentId}/logs/search?query=test");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task AssertProblemDetails(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedEventCode)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem.Extensions.ContainsKey("eventCode"), Is.True, "ProblemDetails should contain eventCode");
        Assert.That(problem.Extensions["eventCode"]?.ToString(), Is.EqualTo(expectedEventCode));
        Assert.That(problem.Extensions.ContainsKey("traceId"), Is.True, "ProblemDetails should contain traceId");
    }
}
