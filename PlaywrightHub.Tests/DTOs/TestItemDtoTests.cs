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

using System.Text.Json;
using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Tests.DTOs;

/// <summary>
///     Unit tests for TestItemDto serialization, deserialization, and immutability.
/// </summary>
public class TestItemDtoTests
{
    [Fact]
    public void TestItemDto_ShouldSerializeAndDeserialize_WithAllProperties()
    {
        // Arrange
        var originalDto = new TestItemDto
        {
            Id = Guid.NewGuid(),
            LaunchId = Guid.NewGuid(),
            ParentItemId = Guid.NewGuid(),
            ItemType = "Test",
            HasStats = true,
            Name = "Login Test",
            Description = "Test login functionality",
            Attributes = new[] { "smoke", "critical" },
            StartTime = DateTimeOffset.UtcNow,
            FinishTime = DateTimeOffset.UtcNow.AddMinutes(5),
            BrowserId = "browser-123",
            WebSocketEndpoint = "ws://localhost:3000",
            BrowserType = "chromium",
            WorkerNodeId = "worker-1",
            SessionStatus = "Completed",
            ComputedStatus = "Passed",
            TestTitle = "User can login successfully",
            TestFile = "tests/auth/login.spec.ts",
            LineNumber = 42,
            ErrorMessage = null,
            ErrorStack = null,
            CodeRef = "tests/auth/login.spec.ts:42",
            Parameters = new Dictionary<string, string> { { "username", "testuser" }, { "browser", "chromium" } },
            TestCaseId = "test-unique-id-123",
            TestCaseHash = 12345678,
            TotalTests = 5,
            PassedTests = 4,
            FailedTests = 1,
            SkippedTests = 0,
            TimedoutTests = 0,
            Children = new List<TestItemDto>()
        };

        // Act
        var json = JsonSerializer.Serialize(originalDto);
        var deserializedDto = JsonSerializer.Deserialize<TestItemDto>(json);

        // Assert
        deserializedDto.Should().NotBeNull();
        deserializedDto!.Id.Should().Be(originalDto.Id);
        deserializedDto.LaunchId.Should().Be(originalDto.LaunchId);
        deserializedDto.ParentItemId.Should().Be(originalDto.ParentItemId);
        deserializedDto.ItemType.Should().Be(originalDto.ItemType);
        deserializedDto.HasStats.Should().Be(originalDto.HasStats);
        deserializedDto.Name.Should().Be(originalDto.Name);
        deserializedDto.Description.Should().Be(originalDto.Description);
        deserializedDto.Attributes.Should().BeEquivalentTo(originalDto.Attributes);
        deserializedDto.BrowserId.Should().Be(originalDto.BrowserId);
        deserializedDto.SessionStatus.Should().Be(originalDto.SessionStatus);
        deserializedDto.ComputedStatus.Should().Be(originalDto.ComputedStatus);
        deserializedDto.TestTitle.Should().Be(originalDto.TestTitle);
        deserializedDto.CodeRef.Should().Be(originalDto.CodeRef);
        deserializedDto.TestCaseId.Should().Be(originalDto.TestCaseId);
        deserializedDto.TestCaseHash.Should().Be(originalDto.TestCaseHash);
        deserializedDto.TotalTests.Should().Be(originalDto.TotalTests);
        deserializedDto.Children.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void TestItemDto_ShouldSupportRecordWithExpression()
    {
        // Arrange
        var original = new TestItemDto
        {
            Id = Guid.NewGuid(),
            LaunchId = Guid.NewGuid(),
            Name = "Original Name",
            ItemType = "Test",
            ComputedStatus = "InProgress"
        };

        // Act
        var updated = original with { ComputedStatus = "Passed" };

        // Assert
        original.ComputedStatus.Should().Be("InProgress", "original should not be modified");
        updated.ComputedStatus.Should().Be("Passed", "updated should have new status");
        updated.Id.Should().Be(original.Id, "other properties should remain the same");
        updated.Name.Should().Be(original.Name, "other properties should remain the same");
    }

    [Fact]
    public void TestItemDto_ShouldSupportNestedChildren()
    {
        // Arrange
        var childStep = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Fill username field",
            ItemType = "Step",
            HasStats = false
        };

        var parentStep = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Login form interaction",
            ItemType = "Step",
            Children = new List<TestItemDto> { childStep }
        };

        var testItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            LaunchId = Guid.NewGuid(),
            Name = "Login Test",
            ItemType = "Test",
            Children = new List<TestItemDto> { parentStep }
        };

        // Assert
        testItem.Children.Should().HaveCount(1);
        testItem.Children!.Should().HaveCount(1);
        testItem.Children![0].Children.Should().HaveCount(1);
        testItem.Children![0].Children![0].Name.Should().Be("Fill username field");
        testItem.Children![0].Children![0].HasStats.Should().BeFalse();
    }

    [Fact]
    public void TestItemDto_ShouldHandleNullChildren()
    {
        // Arrange & Act
        var testItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Test without children",
            ItemType = "Test",
            Children = null
        };

        // Assert
        testItem.Children.Should().BeNull();
    }

    [Fact]
    public void TestItemDto_ShouldSupportDifferentItemTypes()
    {
        // Arrange & Act
        var itemTypes = new[]
        {
            "Test", "Scenario", "Step", "Suite", "Story",
            "BeforeTest", "AfterTest", "BeforeMethod", "AfterMethod",
            "BeforeClass", "AfterClass", "BeforeSuite", "AfterSuite"
        };

        // Assert
        foreach (var itemType in itemTypes)
        {
            var item = new TestItemDto
            {
                Id = Guid.NewGuid(),
                Name = $"Test {itemType}",
                ItemType = itemType
            };

            item.ItemType.Should().Be(itemType);
        }
    }

    [Fact]
    public void TestItemDto_ShouldSerializeParameters()
    {
        // Arrange
        var testItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Parameterized Test",
            ItemType = "Test",
            Parameters = new Dictionary<string, string>
            {
                { "username", "john.doe" },
                { "age", "30" },
                { "isActive", "true" },
                { "roles", "admin,user" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(testItem);
        var deserialized = JsonSerializer.Deserialize<TestItemDto>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Parameters.Should().NotBeNull();
        deserialized.Parameters.Should().ContainKey("username");
        deserialized.Parameters!["username"].ToString().Should().Be("john.doe");
    }

    [Fact]
    public void TestItemDto_ShouldHandleEmptyAggregations()
    {
        // Arrange & Act
        var testItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Test Item",
            ItemType = "Test",
            TotalTests = 0,
            PassedTests = 0,
            FailedTests = 0,
            SkippedTests = 0,
            TimedoutTests = 0
        };

        // Assert
        testItem.TotalTests.Should().Be(0);
        testItem.PassedTests.Should().Be(0);
        testItem.FailedTests.Should().Be(0);
        testItem.SkippedTests.Should().Be(0);
        testItem.TimedoutTests.Should().Be(0);
    }

    [Fact]
    public void TestItemDto_ShouldCalculateAggregationsCorrectly()
    {
        // Arrange
        var testItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Suite with tests",
            ItemType = "Suite",
            TotalTests = 10,
            PassedTests = 7,
            FailedTests = 2,
            SkippedTests = 1,
            TimedoutTests = 0
        };

        // Assert
        (testItem.PassedTests + testItem.FailedTests + testItem.SkippedTests + testItem.TimedoutTests)
            .Should().Be(testItem.TotalTests);
    }

    [Fact]
    public void TestItemDto_ShouldSupportStepWithoutBrowserSession()
    {
        // Arrange & Act
        var stepItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            LaunchId = Guid.NewGuid(),
            ParentItemId = Guid.NewGuid(),
            Name = "Click login button",
            ItemType = "Step",
            HasStats = false,
            BrowserId = null,
            WebSocketEndpoint = null,
            SessionStatus = null
        };

        // Assert
        stepItem.ItemType.Should().Be("Step");
        stepItem.HasStats.Should().BeFalse();
        stepItem.BrowserId.Should().BeNull();
        stepItem.WebSocketEndpoint.Should().BeNull();
        stepItem.SessionStatus.Should().BeNull();
    }

    [Fact]
    public void TestItemDto_ShouldSupportTestWithBrowserSession()
    {
        // Arrange & Act
        var testItem = new TestItemDto
        {
            Id = Guid.NewGuid(),
            LaunchId = Guid.NewGuid(),
            Name = "Login Test",
            ItemType = "Test",
            HasStats = true,
            BrowserId = "browser-123",
            WebSocketEndpoint = "ws://localhost:3000/browser-123",
            BrowserType = "chromium",
            WorkerNodeId = "worker-1",
            SessionStatus = "Running",
            ComputedStatus = "InProgress"
        };

        // Assert
        testItem.ItemType.Should().Be("Test");
        testItem.HasStats.Should().BeTrue();
        testItem.BrowserId.Should().NotBeNullOrEmpty();
        testItem.WebSocketEndpoint.Should().NotBeNullOrEmpty();
        testItem.SessionStatus.Should().Be("Running");
        testItem.ComputedStatus.Should().Be("InProgress");
    }

    [Fact]
    public void TestItemDto_WithExpression_ShouldUpdateChildren()
    {
        // Arrange
        var child1 = new TestItemDto { Id = Guid.NewGuid(), Name = "Child 1", ItemType = "Step" };
        var parent = new TestItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Parent",
            ItemType = "Test",
            Children = new List<TestItemDto> { child1 }
        };

        var child2 = new TestItemDto { Id = Guid.NewGuid(), Name = "Child 2", ItemType = "Step" };

        // Act
        var updated = parent with
        {
            Children = new List<TestItemDto> { child1, child2 }
        };

        // Assert
        parent.Children.Should().HaveCount(1, "original should not be modified");
        updated.Children.Should().HaveCount(2, "updated should have new children");
        updated.Children![1].Name.Should().Be("Child 2");
    }
}
