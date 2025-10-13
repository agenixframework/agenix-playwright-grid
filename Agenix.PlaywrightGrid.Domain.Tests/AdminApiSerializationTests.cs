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

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Domain.Tests;

public class AdminApiSerializationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        // Explicitly mirror Web defaults: camelCase + ignore null
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    public void User_Serializes_CamelCase_And_Omits_Nulls_And_RoundTrips()
    {
        var now = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var u = new User
        {
            Id = "user-1",
            Username = "Jane Doe",
            Email = "jane@example.com",
            AccountRole = AccountRole.Administrator,
            Status = UserStatus.Active,
            ProjectsCount = 3,
            LastLoginUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            CreatedBy = "seed",
            UpdatedBy = "system"
        };

        var json = JsonSerializer.Serialize(u, WebJson);
        TestContext.WriteLine(json);
        Assert.That(json, Does.Contain("\"id\":\"user-1\""));
        Assert.That(json, Does.Contain("\"username\":\"Jane Doe\""));
        Assert.That(json, Does.Contain("\"email\":\"jane@example.com\""));
        Assert.That(json, Does.Contain("\"projectsCount\":3"));
        // Enums are numbers by default with System.Text.Json
        Assert.That(json, Does.Contain("\"accountRole\":1"));
        Assert.That(json, Does.Contain("\"status\":0"));

        var rt = JsonSerializer.Deserialize<User>(json, WebJson)!;
        Assert.That(rt.Id, Is.EqualTo(u.Id));
        Assert.That(rt.Username, Is.EqualTo(u.Username));
        Assert.That(rt.Email, Is.EqualTo(u.Email));
        Assert.That(rt.AccountRole, Is.EqualTo(u.AccountRole));
        Assert.That(rt.Status, Is.EqualTo(u.Status));
        Assert.That(rt.ProjectsCount, Is.EqualTo(u.ProjectsCount));
        Assert.That(rt.LastLoginUtc, Is.EqualTo(u.LastLoginUtc));
    }

    [Test]
    public void User_Omits_Null_Email_And_LastLogin()
    {
        var now = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var u = new User
        {
            Id = "user-2",
            Username = "John Smith",
            Email = null,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        var json = JsonSerializer.Serialize(u, WebJson);
        TestContext.WriteLine(json);
        Assert.That(json, Does.Contain("\"id\":\"user-2\""));
        Assert.That(json, Does.Contain("\"username\":\"John Smith\""));
        Assert.That(json, Does.Not.Contain("email"));
        Assert.That(json, Does.Not.Contain("lastLoginUtc"));
    }

    [Test]
    public void Project_Serializes_CamelCase_And_Omits_Null_Owner_And_RoundTrips()
    {
        var now = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var p = new Project
        {
            Key = "proj_key",
            Name = "Project Name",
            OwnerUserId = null,
            Status = ProjectStatus.Active,
            MembersCount = 5,
            RunsCount = 42,
            LastActivityUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            CreatedBy = "admin",
            UpdatedBy = "admin"
        };

        var json = JsonSerializer.Serialize(p, WebJson);
        TestContext.WriteLine(json);
        Assert.That(json, Does.Contain("\"key\":\"proj_key\""));
        Assert.That(json, Does.Contain("\"name\":\"Project Name\""));
        Assert.That(json, Does.Contain("\"membersCount\":5"));
        Assert.That(json, Does.Contain("\"runsCount\":42"));
        Assert.That(json, Does.Not.Contain("ownerUserId"));
        // Enums are numbers by default
        Assert.That(json, Does.Contain("\"status\":0"));

        var rt = JsonSerializer.Deserialize<Project>(json, WebJson)!;
        Assert.That(rt.Key, Is.EqualTo(p.Key));
        Assert.That(rt.Name, Is.EqualTo(p.Name));
        Assert.That(rt.OwnerUserId, Is.Null);
        Assert.That(rt.Status, Is.EqualTo(p.Status));
        Assert.That(rt.MembersCount, Is.EqualTo(p.MembersCount));
        Assert.That(rt.RunsCount, Is.EqualTo(p.RunsCount));
        Assert.That(rt.LastActivityUtc, Is.EqualTo(p.LastActivityUtc));
    }

    [Test]
    public void Membership_Serializes_And_RoundTrips()
    {
        var now = new DateTime(2025, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        var m = new Membership
        {
            UserId = "user-1",
            ProjectKey = "proj-1",
            Role = ProjectRole.Member,
            CreatedUtc = now,
            UpdatedUtc = now,
            CreatedBy = "admin",
            UpdatedBy = "admin"
        };

        var json = JsonSerializer.Serialize(m, WebJson);
        TestContext.WriteLine(json);
        Assert.That(json, Does.Contain("\"userId\":\"user-1\""));
        Assert.That(json, Does.Contain("\"projectKey\":\"proj-1\""));
        // Enums are numbers by default
        Assert.That(json, Does.Contain("\"role\":1"));

        var rt = JsonSerializer.Deserialize<Membership>(json, WebJson)!;
        Assert.That(rt.UserId, Is.EqualTo(m.UserId));
        Assert.That(rt.ProjectKey, Is.EqualTo(m.ProjectKey));
        Assert.That(rt.Role, Is.EqualTo(m.Role));
    }

    [Test]
    public void Launch_Serializes_CamelCase_And_Omits_Nulls_And_RoundTrips()
    {
        var startTime = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var finishTime = new DateTimeOffset(2025, 1, 15, 10, 30, 3, TimeSpan.Zero);

        var launch = new Launch
        {
            Id = Guid.Parse("12345678-1234-1234-1234-123456789012"),
            Name = "Demo Api Tests",
            Description = "Demonstration launch.",
            Attributes = new[] { "tag1", "tag2", "platform:x64", "build:3.4.7.47.10", "demo", "platform:macos" },
            OwnerApiKey = "test-api-key-123",
            ProjectKey = "demo-project",
            StartTime = startTime,
            FinishTime = finishTime,
            LaunchNumber = 5,
            TotalTestRuns = 30,
            FinishedTestRuns = 30,
            RunningTestRuns = 0,
            StoppedTestRuns = 0,
            ErroredTestRuns = 0
        };

        var json = JsonSerializer.Serialize(launch, WebJson);
        TestContext.WriteLine(json);

        Assert.That(json, Does.Contain("\"id\":\"12345678-1234-1234-1234-123456789012\""));
        Assert.That(json, Does.Contain("\"name\":\"Demo Api Tests\""));
        Assert.That(json, Does.Contain("\"description\":\"Demonstration launch.\""));
        Assert.That(json, Does.Contain("\"ownerApiKey\":\"test-api-key-123\""));
        Assert.That(json, Does.Contain("\"projectKey\":\"demo-project\""));
        Assert.That(json, Does.Contain("\"launchNumber\":5"));
        Assert.That(json, Does.Contain("\"totalTestRuns\":30"));
        Assert.That(json, Does.Contain("\"finishedTestRuns\":30"));
        Assert.That(json, Does.Contain("\"durationSeconds\":3"));
        Assert.That(json, Does.Contain("\"isRunning\":false"));
        Assert.That(json, Does.Contain("\"attributes\":[\"tag1\",\"tag2\",\"platform:x64\""));

        var rt = JsonSerializer.Deserialize<Launch>(json, WebJson)!;
        Assert.That(rt.Id, Is.EqualTo(launch.Id));
        Assert.That(rt.Name, Is.EqualTo(launch.Name));
        Assert.That(rt.Description, Is.EqualTo(launch.Description));
        Assert.That(rt.Attributes, Is.EqualTo(launch.Attributes));
        Assert.That(rt.OwnerApiKey, Is.EqualTo(launch.OwnerApiKey));
        Assert.That(rt.ProjectKey, Is.EqualTo(launch.ProjectKey));
        Assert.That(rt.StartTime, Is.EqualTo(launch.StartTime));
        Assert.That(rt.FinishTime, Is.EqualTo(launch.FinishTime));
        Assert.That(rt.LaunchNumber, Is.EqualTo(launch.LaunchNumber));
        Assert.That(rt.TotalTestRuns, Is.EqualTo(launch.TotalTestRuns));
        Assert.That(rt.DurationSeconds, Is.EqualTo(3.0));
        Assert.That(rt.IsRunning, Is.False);
    }

    [Test]
    public void Launch_Omits_Null_Description_And_FinishTime_For_Running_Launch()
    {
        var startTime = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var launch = new Launch
        {
            Id = Guid.NewGuid(),
            Name = "Running Tests",
            Description = null,
            Attributes = new[] { "platform:linux" },
            OwnerApiKey = "api-key-456",
            ProjectKey = "test-proj",
            StartTime = startTime,
            FinishTime = null,
            LaunchNumber = 1,
            TotalTestRuns = 10,
            FinishedTestRuns = 5,
            RunningTestRuns = 5,
            StoppedTestRuns = 0,
            ErroredTestRuns = 0
        };

        var json = JsonSerializer.Serialize(launch, WebJson);
        TestContext.WriteLine(json);

        Assert.That(json, Does.Contain("\"name\":\"Running Tests\""));
        Assert.That(json, Does.Not.Contain("\"description\""));
        Assert.That(json, Does.Not.Contain("\"finishTime\""));
        Assert.That(json, Does.Not.Contain("\"durationSeconds\""));
        Assert.That(json, Does.Contain("\"isRunning\":true"));
        Assert.That(json, Does.Contain("\"runningTestRuns\":5"));
    }
}
