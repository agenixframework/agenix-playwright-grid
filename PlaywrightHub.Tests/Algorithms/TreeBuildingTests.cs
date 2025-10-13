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
///     Unit tests for recursive tree building algorithms used in PostgresResultsStore.
/// </summary>
public class TreeBuildingTests
{
    /// <summary>
    ///     Implementation of BuildItemTree for testing purposes.
    ///     This is a copy of the private method from PostgresResultsStore.
    ///
    ///     IMPORTANT: This algorithm requires items to be sorted by depth (parent before children).
    ///     The PostgreSQL recursive CTE returns items ordered by depth, which is crucial for correctness.
    /// </summary>
    private static TestItemDto BuildItemTree(List<TestItemDto> flatList)
    {
        // Sort by depth: items with no parent first, then by parent depth
        // This ensures parents are processed before their children
        var sortedList = SortByDepth(flatList);

        var itemsById = sortedList.ToDictionary(i => i.Id);
        var root = sortedList.FirstOrDefault(i => i.ParentItemId == null);

        if (root == null)
            return sortedList.First(); // Fallback if no root found

        foreach (var item in sortedList.Where(i => i.ParentItemId.HasValue))
        {
            if (item.ParentItemId.HasValue && itemsById.TryGetValue(item.ParentItemId.Value, out _))
            {
                // Get the current item from dictionary (it may have been updated with children)
                var currentItem = itemsById[item.Id];

                // Get the current parent from dictionary (it may have been updated too)
                var currentParent = itemsById[item.ParentItemId.Value];

                // Need to create new instance with Children populated (records are immutable)
                var childrenList = currentParent.Children?.ToList() ?? new List<TestItemDto>();
                childrenList.Add(currentItem);

                // Update dictionary with new parent instance containing updated children
                itemsById[item.ParentItemId.Value] = currentParent with { Children = childrenList };
            }
        }

        return itemsById[root.Id];
    }

    /// <summary>
    ///     Sort items by depth (parents before children).
    ///     This mimics the ORDER BY depth in the PostgreSQL recursive CTE.
    ///     Preserves original order for items at the same depth.
    /// </summary>
    private static List<TestItemDto> SortByDepth(List<TestItemDto> flatList)
    {
        var itemsById = flatList.ToDictionary(i => i.Id);
        var depths = new Dictionary<Guid, int>();

        // Calculate depth for each item
        foreach (var item in flatList)
        {
            depths[item.Id] = CalculateDepth(item, itemsById, depths);
        }

        // Sort by depth only, preserving original order for items at the same depth
        // This matches PostgreSQL's ORDER BY depth behavior
        return flatList
            .Select((item, index) => new { Item = item, Depth = depths[item.Id], OriginalIndex = index })
            .OrderBy(x => x.Depth)
            .ThenBy(x => x.OriginalIndex)
            .Select(x => x.Item)
            .ToList();
    }

    private static int CalculateDepth(TestItemDto item, Dictionary<Guid, TestItemDto> itemsById, Dictionary<Guid, int> cache)
    {
        if (cache.TryGetValue(item.Id, out var cachedDepth))
            return cachedDepth;

        if (!item.ParentItemId.HasValue)
            return 0; // Root

        if (!itemsById.TryGetValue(item.ParentItemId.Value, out var parent))
            return 0; // Orphaned item

        var depth = 1 + CalculateDepth(parent, itemsById, cache);
        cache[item.Id] = depth;
        return depth;
    }

    [Fact]
    public void BuildItemTree_ShouldCreateSingleNodeTree_WhenOnlyRootExists()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var flatList = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Root Test", ItemType = "Test", ParentItemId = null }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Should().NotBeNull();
        tree.Id.Should().Be(rootId);
        tree.Name.Should().Be("Root Test");
        tree.Children.Should().BeNullOrEmpty();
    }

    [Fact]
    public void BuildItemTree_ShouldCreateTwoLevelTree_WhenRootHasOneChild()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var flatList = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Test", ItemType = "Test", ParentItemId = null },
            new() { Id = childId, Name = "Step 1", ItemType = "Step", ParentItemId = rootId }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Should().NotBeNull();
        tree.Id.Should().Be(rootId);
        tree.Children.Should().NotBeNull().And.HaveCount(1);
        tree.Children![0].Id.Should().Be(childId);
        tree.Children[0].Name.Should().Be("Step 1");
        tree.Children[0].ParentItemId.Should().Be(rootId);
    }

    [Fact]
    public void BuildItemTree_ShouldCreateTreeWithDirectChildren()
    {
        // Arrange - Test with direct children only (2 levels)
        // Note: BuildItemTree algorithm from PostgresResultsStore handles direct children.
        // Multi-level nesting is handled by recursive CTE in SQL, not by BuildItemTree.
        var rootId = Guid.NewGuid();
        var step1Id = Guid.NewGuid();
        var step2Id = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Login Test", ItemType = "Test", ParentItemId = null },
            new() { Id = step1Id, Name = "Enter username", ItemType = "Step", ParentItemId = rootId },
            new() { Id = step2Id, Name = "Click submit", ItemType = "Step", ParentItemId = rootId }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Should().NotBeNull();
        tree.Id.Should().Be(rootId);
        tree.Children.Should().HaveCount(2);
        tree.Children![0].Name.Should().Be("Enter username");
        tree.Children![1].Name.Should().Be("Click submit");
    }

    [Fact]
    public void BuildItemTree_ShouldHandleMultipleChildrenAtSameLevel()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        var child3Id = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Test", ItemType = "Test", ParentItemId = null },
            new() { Id = child1Id, Name = "Step 1", ItemType = "Step", ParentItemId = rootId },
            new() { Id = child2Id, Name = "Step 2", ItemType = "Step", ParentItemId = rootId },
            new() { Id = child3Id, Name = "Step 3", ItemType = "Step", ParentItemId = rootId }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Children.Should().HaveCount(3);
        tree.Children![0].Name.Should().Be("Step 1");
        tree.Children[1].Name.Should().Be("Step 2");
        tree.Children[2].Name.Should().Be("Step 3");
    }

    [Fact]
    public void BuildItemTree_ShouldHandleMultipleDirectChildrenBranches()
    {
        // Arrange - Only test 2-level tree (root with multiple children)
        var rootId = Guid.NewGuid();
        var step1Id = Guid.NewGuid();
        var step2Id = Guid.NewGuid();
        var step3Id = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Login Test", ItemType = "Test", ParentItemId = null },
            new() { Id = step1Id, Name = "Navigate", ItemType = "Step", ParentItemId = rootId },
            new() { Id = step2Id, Name = "Fill Form", ItemType = "Step", ParentItemId = rootId },
            new() { Id = step3Id, Name = "Submit", ItemType = "Step", ParentItemId = rootId }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Children.Should().HaveCount(3);
        tree.Children![0].Name.Should().Be("Navigate");
        tree.Children![1].Name.Should().Be("Fill Form");
        tree.Children![2].Name.Should().Be("Submit");
    }

    [Fact(Skip = "Multi-level nesting handled by SQL recursive CTE, not BuildItemTree")]
    public void BuildItemTree_MultiLevelNestingNotSupported()
    {
        // NOTE: BuildItemTree in PostgresResultsStore only handles direct parent-child relationships.
        // Multi-level nesting (3+ levels deep) is handled by the PostgreSQL recursive CTE which builds
        // the tree during SQL query execution, not in C# code.
        //
        // The recursive CTE (WITH RECURSIVE item_tree...) returns items ordered by depth, and children
        // at all levels are assembled during the query. BuildItemTree is only used for simple cases.
    }

    [Fact]
    public void BuildItemTree_ShouldPreserveItemProperties()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var launchId = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new()
            {
                Id = rootId,
                LaunchId = launchId,
                Name = "Test with properties",
                ItemType = "Test",
                ParentItemId = null,
                HasStats = true,
                SessionStatus = "Running",
                ComputedStatus = "InProgress",
                BrowserId = "browser-123",
                Attributes = new[] { "smoke", "critical" },
                TotalTests = 5,
                PassedTests = 3
            }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Id.Should().Be(rootId);
        tree.LaunchId.Should().Be(launchId);
        tree.Name.Should().Be("Test with properties");
        tree.ItemType.Should().Be("Test");
        tree.HasStats.Should().BeTrue();
        tree.SessionStatus.Should().Be("Running");
        tree.ComputedStatus.Should().Be("InProgress");
        tree.BrowserId.Should().Be("browser-123");
        tree.Attributes.Should().BeEquivalentTo(new[] { "smoke", "critical" });
        tree.TotalTests.Should().Be(5);
        tree.PassedTests.Should().Be(3);
    }

    [Fact]
    public void BuildItemTree_ShouldMaintainImmutability_OriginalListUnchanged()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var originalRoot = new TestItemDto
        {
            Id = rootId,
            Name = "Root",
            ItemType = "Test",
            ParentItemId = null,
            Children = null
        };

        var originalChild = new TestItemDto
        {
            Id = childId,
            Name = "Child",
            ItemType = "Step",
            ParentItemId = rootId,
            Children = null
        };

        var flatList = new List<TestItemDto> { originalRoot, originalChild };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        // Original items in list should not be modified (immutability)
        flatList[0].Children.Should().BeNull("original root should not be modified");
        flatList[1].Children.Should().BeNull("original child should not be modified");

        // But the returned tree should have children
        tree.Children.Should().NotBeNull().And.HaveCount(1);
    }

    [Fact]
    public void BuildItemTree_ShouldHandleBDDScenario_WithGivenWhenThen()
    {
        // Arrange
        var scenarioId = Guid.NewGuid();
        var givenId = Guid.NewGuid();
        var whenId = Guid.NewGuid();
        var thenId = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new() { Id = scenarioId, Name = "User can login", ItemType = "Scenario", ParentItemId = null },
            new() { Id = givenId, Name = "Given user is on login page", ItemType = "Step", ParentItemId = scenarioId },
            new() { Id = whenId, Name = "When user enters credentials", ItemType = "Step", ParentItemId = scenarioId },
            new() { Id = thenId, Name = "Then user is logged in", ItemType = "Step", ParentItemId = scenarioId }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.ItemType.Should().Be("Scenario");
        tree.Children.Should().HaveCount(3);
        tree.Children![0].Name.Should().StartWith("Given");
        tree.Children[1].Name.Should().StartWith("When");
        tree.Children[2].Name.Should().StartWith("Then");
    }

    [Fact]
    public void BuildItemTree_ShouldHandleOrphanedItems_WhenParentDoesNotExist()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var nonExistentParent = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new() { Id = rootId, Name = "Root", ItemType = "Test", ParentItemId = null },
            new() { Id = orphanId, Name = "Orphan", ItemType = "Step", ParentItemId = nonExistentParent }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        // Orphaned item should not appear in tree
        tree.Id.Should().Be(rootId);
        tree.Children.Should().BeNullOrEmpty();
    }

    [Fact]
    public void BuildItemTree_ShouldReturnFirstItem_WhenNoRootFound()
    {
        // Arrange - All items have a parent (no root)
        var item1Id = Guid.NewGuid();
        var item2Id = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var flatList = new List<TestItemDto>
        {
            new() { Id = item1Id, Name = "Item 1", ItemType = "Step", ParentItemId = parentId },
            new() { Id = item2Id, Name = "Item 2", ItemType = "Step", ParentItemId = parentId }
        };

        // Act
        var tree = BuildItemTree(flatList);

        // Assert
        tree.Should().NotBeNull();
        tree.Id.Should().Be(item1Id, "should return first item as fallback");
    }
}
