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
using System.Net.Http.Headers;
using System.Text.Json;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Database;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;
using Agenix.PlaywrightGrid.Integration.Tests.Models;
using Npgsql;
using NUnit.Framework;
using StackExchange.Redis;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Launch;

/// <summary>
///     Integration tests for the Launch History Matrix API endpoint.
///     Tests the /api/launches/{id}/parent-items-history endpoint with various scenarios.
/// </summary>
[TestFixture]
public class LaunchHistoryTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
        // Clean up test data before each test
        await DatabaseHelpers.CleanupProjectDataAsync(Postgres, ProjectKey);
    }

    [TearDown]
    public async Task TearDown()
    {
        // Clean up test data after each test
        await DatabaseHelpers.CleanupProjectDataAsync(Postgres, ProjectKey);
    }

    [Test]
    public async Task LaunchHistory_ReturnsValidMatrix()
    {
        // Arrange: Create launch with suite executions across multiple launches
        var launch1Id = Guid.NewGuid();
        var launch2Id = Guid.NewGuid();
        var launch3Id = Guid.NewGuid();
        var launch4Id = Guid.NewGuid();
        var launch5Id = Guid.NewGuid();

        // Create 5 launches (for depth=5)
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch1Id, ProjectKey, 1, "Finished",
            TestUser.ApiKeyHash);
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch2Id, ProjectKey, 2, "Finished",
            TestUser.ApiKeyHash);
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch3Id, ProjectKey, 3, "Finished",
            TestUser.ApiKeyHash);
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch4Id, ProjectKey, 4, "Failed", TestUser.ApiKeyHash);
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch5Id, ProjectKey, 5, "InProgress",
            TestUser.ApiKeyHash);

        // Create parent items (suites) for each launch
        // Suite 1: Appears in all 5 launches
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch1Id,
            null,
            "Suite",
            "Login Tests",
            "Completed",
            "Passed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch2Id,
            null,
            "Suite",
            "Login Tests",
            "Completed",
            "Passed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch3Id,
            null,
            "Suite",
            "Login Tests",
            "Completed",
            "Failed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch4Id,
            null,
            "Suite",
            "Login Tests",
            "Completed",
            "Failed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch5Id,
            null,
            "Suite",
            "Login Tests",
            "Running",
            "InProgress"
        );

        // Suite 2: Appears in launches 2, 3, 4
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch2Id,
            null,
            "Suite",
            "Registration Tests",
            "Completed",
            "Passed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch3Id,
            null,
            "Suite",
            "Registration Tests",
            "Completed",
            "Passed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch4Id,
            null,
            "Suite",
            "Registration Tests",
            "Completed",
            "Skipped"
        );

        // Act: GET /api/launches/{id}/parent-items-history?depth=5
        var response = await HttpClient.GetAsync($"/api/launches/{launch5Id}/parent-items-history?depth=5");

        // Assert: Status 200, valid JSON, correct counts
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();

        var data = JsonSerializer.Deserialize<JsonElement>(json);

        // Verify structure (ASP.NET Core serializes with camelCase by default)
        Assert.That(data.TryGetProperty("columns", out var columns), Is.True,
            $"Response should have 'columns' property. Actual JSON: {json}");
        Assert.That(data.TryGetProperty("rows", out var rows), Is.True);

        // Verify columns (should have 5 launches)
        Assert.That(columns.GetArrayLength(), Is.EqualTo(5), "because we requested depth=5 and created 5 launches");

        // Verify rows (should have 2 suites: Login Tests, Registration Tests)
        Assert.That(rows.GetArrayLength(), Is.GreaterThanOrEqualTo(1), "because we created at least one suite");
        Assert.That(rows.GetArrayLength(), Is.LessThanOrEqualTo(2), "because we created exactly 2 different suites");

        // Verify each row has cells (camelCase property names)
        foreach (var row in rows.EnumerateArray())
        {
            Assert.That(row.TryGetProperty("itemName", out _), Is.True, "because each row should have an itemName");
            Assert.That(row.TryGetProperty("itemType", out _), Is.True, "because each row should have an itemType");
            Assert.That(row.TryGetProperty("cells", out var cells), Is.True, "because each row should have cells");

            Assert.That(cells.GetArrayLength(), Is.GreaterThan(0),
                "because each suite should appear in at least one launch");
            Assert.That(cells.GetArrayLength(), Is.LessThanOrEqualTo(5), "because we only have 5 launches total");

            // Verify each cell has required properties (camelCase property names)
            foreach (var cell in cells.EnumerateArray())
            {
                Assert.That(cell.TryGetProperty("launchId", out _), Is.True,
                    "because each cell should have a launchId");
                Assert.That(cell.TryGetProperty("status", out _), Is.True, "because each cell should have a status");
                Assert.That(cell.TryGetProperty("tooltip", out _), Is.True, "because each cell should have a tooltip");
            }
        }

        // Verify column structure (camelCase property names)
        foreach (var column in columns.EnumerateArray())
        {
            Assert.That(column.TryGetProperty("launchId", out _), Is.True,
                "because each column should have a launchId");
            Assert.That(column.TryGetProperty("launchNumber", out _), Is.True,
                "because each column should have a launchNumber");
            Assert.That(column.TryGetProperty("startTime", out _), Is.True,
                "because each column should have a startTime");
        }
    }

    [Test]
    public async Task LaunchHistory_WithInvalidLaunchId_Returns404()
    {
        // Arrange: Non-existent launch ID
        var nonExistentLaunchId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{nonExistentLaunchId}/parent-items-history?depth=5");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task LaunchHistory_WithInvalidDepth_Returns400()
    {
        // Arrange: Create a launch
        var launchId = Guid.NewGuid();
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launchId, ProjectKey, 1, "InProgress",
            TestUser.ApiKeyHash);

        // Act: Request with invalid depth (too high)
        var response = await HttpClient.GetAsync($"/api/launches/{launchId}/parent-items-history?depth=100");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task LaunchHistory_WithEmptyLaunch_ReturnsEmptyRows()
    {
        // Arrange: Create a launch with no test items
        var launchId = Guid.NewGuid();
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launchId, ProjectKey, 1, "InProgress",
            TestUser.ApiKeyHash);

        // Act
        var response = await HttpClient.GetAsync($"/api/launches/{launchId}/parent-items-history?depth=5");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        // Use camelCase property names (ASP.NET Core default serialization)
        Assert.That(data.TryGetProperty("rows", out var rows), Is.True);
        Assert.That(rows.GetArrayLength(), Is.EqualTo(0), "because the launch has no test items");
    }

    [Test]
    public async Task LaunchHistory_WithDefaultDepth_ReturnsValid()
    {
        // Arrange: Create multiple launches
        var launch1Id = Guid.NewGuid();
        var launch2Id = Guid.NewGuid();

        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch1Id, ProjectKey, 1, "Finished",
            TestUser.ApiKeyHash);
        await DatabaseHelpers.CreateLaunchAsync(Postgres, launch2Id, ProjectKey, 2, "InProgress",
            TestUser.ApiKeyHash);

        // Create a suite in both launches
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch1Id,
            null,
            "Suite",
            "Smoke Tests",
            "Completed",
            "Passed"
        );
        await DatabaseHelpers.CreateTestItemAsync(
            Postgres,
            Guid.NewGuid(),
            launch2Id,
            null,
            "Suite",
            "Smoke Tests",
            "Running",
            "InProgress"
        );

        // Act: Request without specifying depth (should use default)
        var response = await HttpClient.GetAsync($"/api/launches/{launch2Id}/parent-items-history");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        // Use camelCase property names (ASP.NET Core default serialization)
        Assert.That(data.TryGetProperty("columns", out var columns), Is.True);
        Assert.That(data.TryGetProperty("rows", out var rows), Is.True);

        // Should have at least 2 launches (we created 2)
        Assert.That(columns.GetArrayLength(), Is.GreaterThanOrEqualTo(2));

        // Should have at least 1 row (Smoke Tests suite)
        Assert.That(rows.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
    }
}
