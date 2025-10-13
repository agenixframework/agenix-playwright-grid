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

using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Tests.Algorithms;

/// <summary>
///     Unit tests for recursive tree update algorithms used in real-time SignalR updates.
///     These algorithms update immutable TestItemDto records in a tree structure.
/// </summary>
public class TreeUpdateTests
{
    /// <summary>
    ///     Implementation of UpdateTestItemInTree for testing purposes.
    ///     This mirrors the logic from ResultsRun.razor Phase 9.
    /// </summary>
    private static void UpdateTestItemInTree(List<TestItemDto> testItems, TestItemDto updatedItem)
    {
        if (testItems == null) return;

        for (int i = 0; i < testItems.Count; i++)
        {
            if (testItems[i].Id == updatedItem.Id)
            {
                testItems[i] = updatedItem;
                return;
            }

            if (UpdateItemInChildren(testItems[i], updatedItem, testItems, i))
                return;
        }

        testItems.Add(updatedItem);
    }

    private static bool UpdateItemInChildren(
        TestItemDto parent,
        TestItemDto updatedItem,
        List<TestItemDto> rootList,
        int parentIndex)
    {
        if (parent.Children == null) return false;

        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Id == updatedItem.Id)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = updatedItem;
                rootList[parentIndex] = parent with { Children = newChildren };
                return true;
            }

            var childResult = UpdateItemInChildrenRecursive(parent.Children[i], updatedItem);
            if (childResult != null)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = childResult;
                rootList[parentIndex] = parent with { Children = newChildren };
                return true;
            }
        }

        return false;
    }

    private static TestItemDto? UpdateItemInChildrenRecursive(TestItemDto parent, TestItemDto updatedItem)
    {
        if (parent.Children == null) return null;

        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Id == updatedItem.Id)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = updatedItem;
                return parent with { Children = newChildren };
            }

            var childResult = UpdateItemInChildrenRecursive(parent.Children[i], updatedItem);
            if (childResult != null)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = childResult;
                return parent with { Children = newChildren };
            }
        }

        return null;
    }

    /// <summary>
    ///     Implementation of UpdateTestItemStatus for testing purposes.
    /// </summary>
    private static void UpdateTestItemStatus(
        List<TestItemDto> testItems,
        Guid itemId,
        string? sessionStatus,
        string? computedStatus)
    {
        if (testItems == null) return;

        for (int i = 0; i < testItems.Count; i++)
        {
            if (testItems[i].Id == itemId)
            {
                testItems[i] = testItems[i] with
                {
                    SessionStatus = sessionStatus ?? testItems[i].SessionStatus,
                    ComputedStatus = computedStatus ?? testItems[i].ComputedStatus
                };
                return;
            }

            if (UpdateStatusInChildren(testItems[i], itemId, sessionStatus, computedStatus, testItems, i))
                return;
        }
    }

    private static bool UpdateStatusInChildren(
        TestItemDto parent,
        Guid itemId,
        string? sessionStatus,
        string? computedStatus,
        List<TestItemDto> rootList,
        int parentIndex)
    {
        if (parent.Children == null) return false;

        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Id == itemId)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = newChildren[i] with
                {
                    SessionStatus = sessionStatus ?? newChildren[i].SessionStatus,
                    ComputedStatus = computedStatus ?? newChildren[i].ComputedStatus
                };
                rootList[parentIndex] = parent with { Children = newChildren };
                return true;
            }

            var childResult = UpdateStatusInChildrenRecursive(parent.Children[i], itemId, sessionStatus, computedStatus);
            if (childResult != null)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = childResult;
                rootList[parentIndex] = parent with { Children = newChildren };
                return true;
            }
        }

        return false;
    }

    private static TestItemDto? UpdateStatusInChildrenRecursive(
        TestItemDto parent,
        Guid itemId,
        string? sessionStatus,
        string? computedStatus)
    {
        if (parent.Children == null) return null;

        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Id == itemId)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = newChildren[i] with
                {
                    SessionStatus = sessionStatus ?? newChildren[i].SessionStatus,
                    ComputedStatus = computedStatus ?? newChildren[i].ComputedStatus
                };
                return parent with { Children = newChildren };
            }

            var childResult = UpdateStatusInChildrenRecursive(parent.Children[i], itemId, sessionStatus, computedStatus);
            if (childResult != null)
            {
                var newChildren = parent.Children.ToList();
                newChildren[i] = childResult;
                return parent with { Children = newChildren };
            }
        }

        return null;
    }

    [Fact]
    public void UpdateTestItemInTree_ShouldUpdateRootItem()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Original Name", ItemType = "Test", ComputedStatus = "InProgress" }
        };

        var updatedItem = new TestItemDto
        {
            Id = rootId,
            Name = "Updated Name",
            ItemType = "Test",
            ComputedStatus = "Passed"
        };

        // Act
        UpdateTestItemInTree(testItems, updatedItem);

        // Assert
        testItems.Should().HaveCount(1);
        testItems[0].Name.Should().Be("Updated Name");
        testItems[0].ComputedStatus.Should().Be("Passed");
    }

    [Fact]
    public void UpdateTestItemInTree_ShouldUpdateChildItem()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Root",
                ItemType = "Test",
                Children = new List<TestItemDto>
                {
                    new() { Id = childId, Name = "Child Original", ItemType = "Step", ComputedStatus = "InProgress" }
                }
            }
        };

        var updatedChild = new TestItemDto
        {
            Id = childId,
            Name = "Child Updated",
            ItemType = "Step",
            ComputedStatus = "Passed"
        };

        // Act
        UpdateTestItemInTree(testItems, updatedChild);

        // Assert
        testItems[0].Children.Should().HaveCount(1);
        testItems[0].Children![0].Name.Should().Be("Child Updated");
        testItems[0].Children![0].ComputedStatus.Should().Be("Passed");
    }

    [Fact]
    public void UpdateTestItemInTree_ShouldUpdateDeepNestedItem()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        var deepChildId = Guid.NewGuid();

        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Root",
                ItemType = "Test",
                Children = new List<TestItemDto>
                {
                    new()
                    {
                        Id = child1Id,
                        Name = "Child 1",
                        ItemType = "Step",
                        Children = new List<TestItemDto>
                        {
                            new()
                            {
                                Id = child2Id,
                                Name = "Child 2",
                                ItemType = "Step",
                                Children = new List<TestItemDto>
                                {
                                    new() { Id = deepChildId, Name = "Deep Child Original", ItemType = "Step" }
                                }
                            }
                        }
                    }
                }
            }
        };

        var updatedDeepChild = new TestItemDto
        {
            Id = deepChildId,
            Name = "Deep Child Updated",
            ItemType = "Step"
        };

        // Act
        UpdateTestItemInTree(testItems, updatedDeepChild);

        // Assert
        testItems[0].Children![0].Children![0].Children![0].Name.Should().Be("Deep Child Updated");
    }

    [Fact]
    public void UpdateTestItemInTree_ShouldAddNewItem_WhenNotFound()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new() { Id = existingId, Name = "Existing", ItemType = "Test" }
        };

        var newItem = new TestItemDto
        {
            Id = newId,
            Name = "New Item",
            ItemType = "Test"
        };

        // Act
        UpdateTestItemInTree(testItems, newItem);

        // Assert
        testItems.Should().HaveCount(2);
        testItems[1].Id.Should().Be(newId);
        testItems[1].Name.Should().Be("New Item");
    }

    [Fact]
    public void UpdateTestItemInTree_ShouldMaintainImmutability()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var originalChild = new TestItemDto
        {
            Id = childId,
            Name = "Original",
            ItemType = "Step"
        };

        var originalRoot = new TestItemDto
        {
            Id = rootId,
            Name = "Root",
            ItemType = "Test",
            Children = new List<TestItemDto> { originalChild }
        };

        var testItems = new List<TestItemDto> { originalRoot };

        var updatedChild = originalChild with { Name = "Updated" };

        // Act
        UpdateTestItemInTree(testItems, updatedChild);

        // Assert
        // Original objects should not be modified (immutability)
        originalChild.Name.Should().Be("Original");
        originalRoot.Children![0].Name.Should().Be("Original");

        // But the list should have updated instances
        testItems[0].Children![0].Name.Should().Be("Updated");
    }

    [Fact]
    public void UpdateTestItemStatus_ShouldUpdateRootStatus()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Test",
                ItemType = "Test",
                SessionStatus = "Running",
                ComputedStatus = "InProgress"
            }
        };

        // Act
        UpdateTestItemStatus(testItems, rootId, "Completed", "Passed");

        // Assert
        testItems[0].SessionStatus.Should().Be("Completed");
        testItems[0].ComputedStatus.Should().Be("Passed");
    }

    [Fact]
    public void UpdateTestItemStatus_ShouldUpdateChildStatus()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Root",
                ItemType = "Test",
                Children = new List<TestItemDto>
                {
                    new()
                    {
                        Id = childId,
                        Name = "Child",
                        ItemType = "Step",
                        SessionStatus = null,
                        ComputedStatus = "InProgress"
                    }
                }
            }
        };

        // Act
        UpdateTestItemStatus(testItems, childId, null, "Passed");

        // Assert
        testItems[0].Children![0].SessionStatus.Should().BeNull();
        testItems[0].Children![0].ComputedStatus.Should().Be("Passed");
    }

    [Fact]
    public void UpdateTestItemStatus_ShouldUpdateDeepNestedStatus()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Root",
                ItemType = "Test",
                Children = new List<TestItemDto>
                {
                    new()
                    {
                        Id = childId,
                        Name = "Child",
                        ItemType = "Step",
                        Children = new List<TestItemDto>
                        {
                            new()
                            {
                                Id = grandchildId,
                                Name = "Grandchild",
                                ItemType = "Step",
                                ComputedStatus = "InProgress"
                            }
                        }
                    }
                }
            }
        };

        // Act
        UpdateTestItemStatus(testItems, grandchildId, null, "Passed");

        // Assert
        testItems[0].Children![0].Children![0].ComputedStatus.Should().Be("Passed");
    }

    [Fact]
    public void UpdateTestItemStatus_ShouldPreserveExistingStatus_WhenNullPassed()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Test",
                ItemType = "Test",
                SessionStatus = "Running",
                ComputedStatus = "InProgress"
            }
        };

        // Act - Only update computed status, keep session status
        UpdateTestItemStatus(testItems, rootId, null, "Passed");

        // Assert
        testItems[0].SessionStatus.Should().Be("Running", "should preserve existing session status");
        testItems[0].ComputedStatus.Should().Be("Passed", "should update computed status");
    }

    [Fact]
    public void UpdateTestItemStatus_ShouldHandleMultipleSiblings()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        var child3Id = Guid.NewGuid();

        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                Name = "Root",
                ItemType = "Test",
                Children = new List<TestItemDto>
                {
                    new() { Id = child1Id, Name = "Child 1", ItemType = "Step", ComputedStatus = "InProgress" },
                    new() { Id = child2Id, Name = "Child 2", ItemType = "Step", ComputedStatus = "InProgress" },
                    new() { Id = child3Id, Name = "Child 3", ItemType = "Step", ComputedStatus = "InProgress" }
                }
            }
        };

        // Act - Update only the second child
        UpdateTestItemStatus(testItems, child2Id, null, "Passed");

        // Assert
        testItems[0].Children![0].ComputedStatus.Should().Be("InProgress", "sibling 1 should not change");
        testItems[0].Children![1].ComputedStatus.Should().Be("Passed", "target child should update");
        testItems[0].Children![2].ComputedStatus.Should().Be("InProgress", "sibling 3 should not change");
    }

    [Fact]
    public void UpdateTestItemStatus_ShouldNotAddItem_WhenNotFound()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var nonExistentId = Guid.NewGuid();
        var testItems = new List<TestItemDto>
        {
            new() { Id = existingId, Name = "Existing", ItemType = "Test" }
        };

        // Act
        UpdateTestItemStatus(testItems, nonExistentId, "Completed", "Passed");

        // Assert
        testItems.Should().HaveCount(1, "should not add new items");
    }

    [Fact]
    public void TreeUpdateAlgorithms_ShouldWorkTogether_InRealisticScenario()
    {
        // Arrange - Build a realistic test tree
        var testId = Guid.NewGuid();
        var step1Id = Guid.NewGuid();
        var step2Id = Guid.NewGuid();

        var testItems = new List<TestItemDto>
        {
            new()
            {
                Id = testId,
                Name = "Login Test",
                ItemType = "Test",
                SessionStatus = "Running",
                ComputedStatus = "InProgress",
                Children = new List<TestItemDto>
                {
                    new() { Id = step1Id, Name = "Navigate", ItemType = "Step", ComputedStatus = "InProgress" },
                    new() { Id = step2Id, Name = "Fill form", ItemType = "Step", ComputedStatus = "Queued" }
                }
            }
        };

        // Act - Simulate a real-time test execution sequence
        // 1. Step 1 completes
        UpdateTestItemStatus(testItems, step1Id, null, "Passed");

        // 2. Step 2 starts
        UpdateTestItemStatus(testItems, step2Id, null, "InProgress");

        // 3. Step 2 completes
        UpdateTestItemStatus(testItems, step2Id, null, "Passed");

        // 4. Test completes
        UpdateTestItemStatus(testItems, testId, "Completed", "Passed");

        // Assert
        testItems[0].SessionStatus.Should().Be("Completed");
        testItems[0].ComputedStatus.Should().Be("Passed");
        testItems[0].Children![0].ComputedStatus.Should().Be("Passed");
        testItems[0].Children![1].ComputedStatus.Should().Be("Passed");
    }
}
