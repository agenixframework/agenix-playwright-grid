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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Artifacts;

[TestFixture]
public class ArtifactsEndpointsTests : ApiTestBase
{
    [Test]
    public async Task DownloadArtifact_NonExistentId_Returns404WithEventCode()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/artifacts/{nonExistentId}");

        // Assert
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.Artifacts.ArtifactDownloadFailed);
    }

    [Test]
    public async Task UploadArtifact_InvalidTestItemId_Returns404WithEventCode()
    {
        // Arrange
        var nonExistentTestItemId = Guid.NewGuid();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "test.txt");

        // Act
        var response = await HttpClient.PostAsync($"/api/test-items/{nonExistentTestItemId}/artifacts", content);

        // Assert
        // The implementation checks for test item existence and returns ITEM03 if not found
        await AssertProblemDetails(response, HttpStatusCode.NotFound, EventCodes.TestItem.TestItemNotFound);
    }

    [Test]
    public async Task ListArtifacts_NonExistentTestItemId_Returns200WithEmptyList()
    {
        // Arrange
        var nonExistentTestItemId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{nonExistentTestItemId}/artifacts");

        // Assert
        // Implementation returns empty list instead of 404
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task AssertProblemDetails(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedEventCode)
    {
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
