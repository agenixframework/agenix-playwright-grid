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

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.LaunchFilters;

[TestFixture]
public class LaunchFiltersEndpointsPositiveTests : ApiTestBase
{
    [Test]
    public async Task CreateUpdateDeleteFilter_ShouldSucceed()
    {
        // Arrange
        var filterName = $"Filter {Guid.NewGuid():N}";
        var saveRequest = new
        {
            name = filterName,
            description = "Test Description",
            projectKey = ProjectKey,
            criteria = new[]
            {
                new { field = "name", @operator = "contains", value = "test" }
            },
            sortBy = "start_time",
            isShared = false,
            displayOnLaunches = true
        };

        // Act: Create
        var createResponse = await HttpClient.PostAsJsonAsync($"/api/launch-filters?userId={TestUser.UserId}", saveRequest);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK));

        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var filterId = Guid.Parse(createResult.GetProperty("id").GetString()!);
        Assert.That(createResult.GetProperty("name").GetString(), Is.EqualTo(filterName));

        // Act: Update
        var updateRequest = new
        {
            name = filterName + " Updated",
            description = "Updated Description",
            projectKey = ProjectKey,
            criteria = new[]
            {
                new { field = "name", @operator = "contains", value = "updated" }
            },
            sortBy = "start_time",
            isShared = false,
            displayOnLaunches = true
        };
        var updateResponse = await HttpClient.PutAsJsonAsync($"/api/launch-filters/{filterId}?userId={TestUser.UserId}", updateRequest);
        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act: Get All
        var getResponse = await HttpClient.GetAsync($"/api/launch-filters?projectKey={ProjectKey}&userId={TestUser.UserId}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var filters = await getResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(filters, Is.Not.Null);
        Assert.That(filters.Any(f => f.GetProperty("id").GetString() == filterId.ToString()), Is.True);

        // Act: Delete
        var deleteResponse = await HttpClient.DeleteAsync($"/api/launch-filters/{filterId}?userId={TestUser.UserId}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify deleted
        var getAfterDeleteResponse = await HttpClient.GetAsync($"/api/launch-filters?projectKey={ProjectKey}&userId={TestUser.UserId}");
        var filtersAfterDelete = await getAfterDeleteResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.That(filtersAfterDelete!.Any(f => f.GetProperty("id").GetString() == filterId.ToString()), Is.False);
    }

    [Test]
    public async Task FilterPreferences_ShouldSucceed()
    {
        // Arrange: Create a filter first
        var filterName = $"Pref Filter {Guid.NewGuid():N}";
        var saveRequest = new
        {
            name = filterName,
            projectKey = ProjectKey
        };
        var createResponse = await HttpClient.PostAsJsonAsync($"/api/launch-filters?userId={TestUser.UserId}", saveRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var filterId = Guid.Parse(createResult.GetProperty("id").GetString()!);

        // Act: Update Preference
        var prefRequest = new { projectKey = ProjectKey, selectedFilterId = filterId };
        var updatePrefResponse = await HttpClient.PutAsJsonAsync($"/api/launch-filters/preference?userId={TestUser.UserId}", prefRequest);
        Assert.That(updatePrefResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act: Get Preference
        var getPrefResponse = await HttpClient.GetAsync($"/api/launch-filters/preference?projectKey={ProjectKey}&userId={TestUser.UserId}");
        Assert.That(getPrefResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prefResult = await getPrefResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(prefResult.GetProperty("selectedFilterId").GetString(), Is.EqualTo(filterId.ToString()));
    }

    [Test]
    public async Task ToggleFilterDisplay_ShouldSucceed()
    {
        // Arrange
        var filterName = $"Toggle Filter {Guid.NewGuid():N}";
        var saveRequest = new
        {
            name = filterName,
            projectKey = ProjectKey,
            displayOnLaunches = true
        };
        var createResponse = await HttpClient.PostAsJsonAsync($"/api/launch-filters?userId={TestUser.UserId}", saveRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var filterId = Guid.Parse(createResult.GetProperty("id").GetString()!);

        // Act
        var toggleRequest = new { displayOnLaunches = false };
        var response = await HttpClient.PutAsJsonAsync($"/api/launch-filters/{filterId}/display?userId={TestUser.UserId}", toggleRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
