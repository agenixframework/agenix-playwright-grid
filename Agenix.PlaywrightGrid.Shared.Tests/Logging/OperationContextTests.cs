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

using System.Diagnostics;
using Agenix.PlaywrightGrid.Shared.Logging;
using FluentAssertions;
using Xunit;

namespace Agenix.PlaywrightGrid.Shared.Tests.Logging;

public class OperationContextTests : IDisposable
{
    public OperationContextTests()
    {
        // Clear any existing context before each test
        OperationContext.Current = null;
        Activity.Current = null;
    }

    public void Dispose()
    {
        // Clean up after each test
        OperationContext.Current = null;
        Activity.Current = null;
    }

    [Fact]
    public void Constructor_WithValidOperationName_CreatesContext()
    {
        // Arrange & Act
        var context = new OperationContext("TestOperation");

        // Assert
        context.OperationId.Should().NotBe(Guid.Empty);
        context.OperationName.Should().Be("TestOperation");
        context.ParentOperationId.Should().BeNull();
        context.StartTime.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        context.Properties.Should().NotBeNull().And.BeEmpty();
        context.KeyEvents.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullOperationName_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new OperationContext(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithParentOperationId_SetsParentId()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        // Act
        var context = new OperationContext("ChildOperation", parentId);

        // Assert
        context.ParentOperationId.Should().Be(parentId);
    }

    [Fact]
    public void Constructor_WithProperties_SetsProperties()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["userId"] = "user123",
            ["projectKey"] = "project1"
        };

        // Act
        var context = new OperationContext("TestOperation", properties: properties);

        // Assert
        context.Properties.Should().BeSameAs(properties);
        context.Properties["userId"].Should().Be("user123");
        context.Properties["projectKey"].Should().Be("project1");
    }

    [Fact]
    public void Constructor_WithActivity_CapturesTraceAndSpanIds()
    {
        // Arrange
        using var activity = new Activity("TestActivity").Start();

        // Act
        var context = new OperationContext("TestOperation");

        // Assert
        context.TraceId.Should().NotBeNull().And.Be(activity.TraceId.ToString());
        context.SpanId.Should().NotBeNull().And.Be(activity.SpanId.ToString());
    }

    [Fact]
    public void Constructor_WithoutActivity_LeavesTraceAndSpanIdsNull()
    {
        // Act
        var context = new OperationContext("TestOperation");

        // Assert
        context.TraceId.Should().BeNull();
        context.SpanId.Should().BeNull();
    }

    [Fact]
    public void Current_GetSet_WorksCorrectly()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        OperationContext.Current = context;

        // Assert
        OperationContext.Current.Should().BeSameAs(context);
    }

    [Fact]
    public void Begin_CreatesContextAndSetsAsCurrent()
    {
        // Act
        using var scope = OperationContext.Begin("TestOperation");

        // Assert
        OperationContext.Current.Should().NotBeNull();
        OperationContext.Current!.OperationName.Should().Be("TestOperation");
    }

    [Fact]
    public void Begin_WithParentOperationId_SetsParentId()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        // Act
        using var scope = OperationContext.Begin("ChildOperation", parentId);

        // Assert
        OperationContext.Current.Should().NotBeNull();
        OperationContext.Current!.ParentOperationId.Should().Be(parentId);
    }

    [Fact]
    public void Begin_WithProperties_SetsProperties()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            ["userId"] = "user123"
        };

        // Act
        using var scope = OperationContext.Begin("TestOperation", properties: properties);

        // Assert
        OperationContext.Current.Should().NotBeNull();
        OperationContext.Current!.Properties["userId"].Should().Be("user123");
    }

    [Fact]
    public void Begin_DisposalRestoresPreviousContext()
    {
        // Arrange
        var outerContext = new OperationContext("OuterOperation");
        OperationContext.Current = outerContext;

        // Act
        using (var scope = OperationContext.Begin("InnerOperation"))
        {
            OperationContext.Current.Should().NotBeSameAs(outerContext);
            OperationContext.Current!.OperationName.Should().Be("InnerOperation");
        }

        // Assert
        OperationContext.Current.Should().BeSameAs(outerContext);
    }

    [Fact]
    public void Begin_NestedContexts_RestoresCorrectly()
    {
        // Arrange
        OperationContext.Current = null;

        // Act & Assert
        using (var scope1 = OperationContext.Begin("Level1"))
        {
            OperationContext.Current!.OperationName.Should().Be("Level1");

            using (var scope2 = OperationContext.Begin("Level2"))
            {
                OperationContext.Current!.OperationName.Should().Be("Level2");

                using (var scope3 = OperationContext.Begin("Level3"))
                {
                    OperationContext.Current!.OperationName.Should().Be("Level3");
                }

                OperationContext.Current!.OperationName.Should().Be("Level2");
            }

            OperationContext.Current!.OperationName.Should().Be("Level1");
        }

        OperationContext.Current.Should().BeNull();
    }

    [Fact]
    public void RecordKeyEvent_WithValidEventCode_AddsToKeyEvents()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        context.RecordKeyEvent("ITEM01");
        context.RecordKeyEvent("POOL03");

        // Assert
        context.KeyEvents.Should().HaveCount(2)
            .And.Contain("ITEM01")
            .And.Contain("POOL03");
    }

    [Fact]
    public void RecordKeyEvent_WithDuplicateEventCode_OnlyAddsOnce()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        context.RecordKeyEvent("ITEM01");
        context.RecordKeyEvent("ITEM01");
        context.RecordKeyEvent("ITEM01");

        // Assert
        context.KeyEvents.Should().HaveCount(1)
            .And.Contain("ITEM01");
    }

    [Fact]
    public void RecordKeyEvent_WithNullEventCode_DoesNotAdd()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        context.RecordKeyEvent(null!);

        // Assert
        context.KeyEvents.Should().BeEmpty();
    }

    [Fact]
    public void RecordKeyEvent_WithEmptyEventCode_DoesNotAdd()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        context.RecordKeyEvent("");
        context.RecordKeyEvent("   ");

        // Assert
        context.KeyEvents.Should().BeEmpty();
    }

    [Fact]
    public void GetDuration_ReturnsElapsedTime()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        Thread.Sleep(50); // Wait 50ms
        var duration = context.GetDuration();

        // Assert
        duration.TotalMilliseconds.Should().BeGreaterOrEqualTo(50)
            .And.BeLessThan(1000); // Sanity check
    }

    [Fact]
    public async Task Begin_FlowsAcrossAsyncBoundaries()
    {
        // Arrange
        using var scope = OperationContext.Begin("ParentOperation");
        var parentContext = OperationContext.Current;

        // Act
        await Task.Run(() =>
        {
            // Assert - context should flow to async task
            OperationContext.Current.Should().BeSameAs(parentContext);
        });

        // Assert - context should still be set after await
        OperationContext.Current.Should().BeSameAs(parentContext);
    }

    [Fact]
    public async Task Begin_NestedAsync_FlowsCorrectly()
    {
        // Arrange & Act
        using var scope1 = OperationContext.Begin("Level1");
        var context1 = OperationContext.Current;

        await Task.Run(async () =>
        {
            OperationContext.Current.Should().BeSameAs(context1);

            using var scope2 = OperationContext.Begin("Level2");
            var context2 = OperationContext.Current;

            await Task.Run(() =>
            {
                OperationContext.Current.Should().BeSameAs(context2);
            });

            OperationContext.Current.Should().BeSameAs(context2);
        });

        // Assert
        OperationContext.Current.Should().BeSameAs(context1);
    }

    [Fact]
    public void OperationId_IsUnique()
    {
        // Act
        var context1 = new OperationContext("Operation1");
        var context2 = new OperationContext("Operation2");
        var context3 = new OperationContext("Operation3");

        // Assert
        context1.OperationId.Should().NotBe(context2.OperationId);
        context1.OperationId.Should().NotBe(context3.OperationId);
        context2.OperationId.Should().NotBe(context3.OperationId);
    }

    [Fact]
    public void Properties_AreMutable()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        context.Properties["key1"] = "value1";
        context.Properties["key2"] = 123;
        context.Properties["key1"] = "updated";

        // Assert
        context.Properties.Should().HaveCount(2);
        context.Properties["key1"].Should().Be("updated");
        context.Properties["key2"].Should().Be(123);
    }

    [Fact]
    public void KeyEvents_AreMutable()
    {
        // Arrange
        var context = new OperationContext("TestOperation");

        // Act
        context.RecordKeyEvent("ITEM01");
        context.KeyEvents.Add("POOL03"); // Direct add (bypasses duplicate check)
        context.KeyEvents.Add("POOL03"); // Allows duplicate when added directly

        // Assert
        context.KeyEvents.Should().HaveCount(3);
    }
}
