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
public class ArtifactsEndpointsPositiveTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [Test]
    public async Task UploadListAndDownloadArtifact_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Artifact Test");

        var fileName = "test-artifact.txt";
        var content = "This is a test artifact content";

        // Act: Upload (with retry)
        var artifactId = await UploadTestArtifactAsync(testId, fileName, content);

        // Act: List
        List<JsonElement>? artifactsList = null;
        for (int i = 0; i < 10; i++)
        {
            var listResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/artifacts");
            if (listResponse.StatusCode == HttpStatusCode.OK)
            {
                artifactsList = await listResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
                if (artifactsList != null && artifactsList.Any(a => a.GetProperty("id").GetString() == artifactId.ToString()))
                {
                    break;
                }
            }
            await Task.Delay(1000);
        }

        Assert.That(artifactsList, Is.Not.Null);
        Assert.That(artifactsList!.Any(a => a.GetProperty("id").GetString() == artifactId.ToString()), Is.True);

        // Act: Download
        // Note: In integration tests, the async processing (saving to disk) might take a moment.
        // We might need a small retry or wait if the storage is async.
        // ArtifactsEndpoints.UploadArtifact publishes ArtifactUploadEvent.

        HttpResponseMessage downloadResponse = null!;
        for (int i = 0; i < 30; i++)
        {
            downloadResponse = await HttpClient.GetAsync($"/api/artifacts/{artifactId}");
            if (downloadResponse.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(2000);
        }

        Assert.That(downloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var downloadedContent = await downloadResponse.Content.ReadAsStringAsync();
        Assert.That(downloadedContent, Is.EqualTo(content));
    }

    [Test]
    public async Task GetArtifactUrl_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Artifact URL Test");
        var artifactId = await UploadTestArtifactAsync(testId, "url-test.txt", "url content");

        // Act
        var response = await HttpClient.GetAsync($"/api/artifacts/{artifactId}/url?expirySeconds=60");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("artifactId").GetString(), Is.EqualTo(artifactId.ToString()));
        Assert.That(result.GetProperty("url").GetString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task DownloadArtifactsAsZip_ShouldSucceed()
    {
        // Arrange
        var launchId = await CreateActiveLaunchAsync();
        var testId = await CreateTestItemAsync(launchId, null, "Artifact Zip Test");
        await UploadTestArtifactAsync(testId, "file1.txt", "content 1");
        await UploadTestArtifactAsync(testId, "file2.txt", "content 2");

        // Wait for processing
        await Task.Delay(15000);

        // Act
        var response = await HttpClient.GetAsync($"/api/test-items/{testId}/artifacts/download-zip");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/zip"));
        var zipBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.That(zipBytes.Length, Is.GreaterThan(0));
    }

    private async Task<Guid> CreateActiveLaunchAsync()
    {
        var launchRequest = new { name = $"Artifacts Launch {Guid.NewGuid():N}" };
        var response = await HttpClient.PostAsJsonAsync("/api/launches", launchRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateTestItemAsync(Guid launchId, Guid? parentId, string name)
    {
        var request = new
        {
            launchUuid = launchId.ToString(),
            parentItemId = parentId?.ToString(),
            name = name,
            type = "Test",
            labelKey = LabelKey
        };
        var response = await HttpClient.PostAsJsonAsync("/api/test-items", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private async Task<Guid> UploadTestArtifactAsync(Guid testId, string fileName, string content)
    {
        var logRequest = new
        {
            itemUuid = testId.ToString(),
            time = DateTime.UtcNow,
            level = "INFO",
            message = $"Attachment: {fileName}",
            file = new
            {
                name = fileName,
                data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)),
                contentType = "text/plain"
            }
        };

        var response = await HttpClient.PostAsJsonAsync($"/v1/{ProjectKey}/log", logRequest);
        response.EnsureSuccessStatusCode();

        // Find the artifact ID
        for (int i = 0; i < 30; i++)
        {
            var listResponse = await HttpClient.GetAsync($"/api/test-items/{testId}/artifacts");
            if (listResponse.StatusCode == HttpStatusCode.OK)
            {
                var artifactsList = await listResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
                var artifact = artifactsList?.FirstOrDefault(a => a.TryGetProperty("name", out var nameProp) && nameProp.GetString() == fileName);
                if (artifact != null && artifact.Value.ValueKind != JsonValueKind.Undefined)
                {
                    if (artifact.Value.TryGetProperty("id", out var idProp))
                    {
                        return Guid.Parse(idProp.GetString()!);
                    }
                }
            }
            await Task.Delay(1000);
        }

        throw new Exception("Artifact not found after upload via log item");
    }
}
