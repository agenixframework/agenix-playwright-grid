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
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Worker;

/// <summary>
///     Integration tests for WorkerServiceRunner.EnsureRegisteredAsync method.
///     Tests the worker re-registration behavior when removed from hub.
///
///     These tests verify:
///     - Re-registration: Worker can recover after being removed from hub
///     - State preservation: Worker pool state is restored after re-registration
/// </summary>
[TestFixture]
public class EnsureRegisteredIntegrationTests : ApiTestBase
{
    [Test]
    public async Task EnsureRegisteredAsync_ReRegistersWorker_WhenRemovedFromHub()
    {
        // Arrange: Get first node from hub
        var nodes = await Redis.SetMembersAsync("nodes");
        Assert.That(nodes.Length, Is.GreaterThan(0), "At least one node must be registered");

        var firstNode = nodes[0].ToString();
        var nodeKey = $"node:{firstNode}";

        // Verify node exists in Redis
        var existsBefore = await Redis.KeyExistsAsync(nodeKey);
        Assert.That(existsBefore, Is.True, $"Node key {nodeKey} should exist before deletion");

        // Act: Delete node from Redis (simulate hub restart or node expiration)
        await Redis.KeyDeleteAsync(nodeKey);
        await Redis.SetRemoveAsync("nodes", firstNode);

        // Verify deletion
        var existsAfterDeletion = await Redis.KeyExistsAsync(nodeKey);
        Assert.That(existsAfterDeletion, Is.False, $"Node key {nodeKey} should not exist after deletion");

        // Wait for WorkerRegistrationVerifier to detect and re-register (uses periodic check)
        // Default interval is 300 seconds, but in tests we might have it set lower
        // For this test, we'll wait up to 10 seconds and check if the node re-appears
        const int maxWaitSeconds = 60;
        var reRegistered = false;

        for (var i = 0; i < maxWaitSeconds; i++)
        {
            await Task.Delay(1000);
            reRegistered = await Redis.KeyExistsAsync(nodeKey);
            if (reRegistered)
                break;
        }

        // Assert: Node re-registered (within a verification interval)
        Assert.That(reRegistered, Is.True,
            $"Node {firstNode} should re-register within {maxWaitSeconds} seconds");

        // Verify node is back in nodes set
        var nodesAfterReReg = await Redis.SetMembersAsync("nodes");
        Assert.That(nodesAfterReReg.Select(n => n.ToString()), Does.Contain(firstNode),
            "Node should be back in nodes set");
    }


    [Test]
    public async Task EnsureRegisteredAsync_PreservesWorkerState_AfterReRegistration()
    {
        // Arrange: Get first node and capture pool state before deletion
        var nodes = await Redis.SetMembersAsync("nodes");
        Assert.That(nodes.Length, Is.GreaterThan(0), "At least one node must be registered");

        var firstNode = nodes[0].ToString();
        var nodeKey = $"node:{firstNode}";

        // Get diagnostics and parse pool information before deletion
        var diagnosticsResponseBefore = await HttpClient.GetAsync("/diagnostics");
        diagnosticsResponseBefore.EnsureSuccessStatusCode();

        var diagnosticsJsonBefore = await diagnosticsResponseBefore.Content.ReadAsStringAsync();
        var diagnosticsBefore = JsonDocument.Parse(diagnosticsJsonBefore);

        // Extract pool count for the specific node
        var poolCountBefore = 0;
        if (diagnosticsBefore.RootElement.TryGetProperty("workers", out var workersArray) &&
            workersArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var worker in workersArray.EnumerateArray())
            {
                if (worker.TryGetProperty("id", out var workerId) &&
                    workerId.GetString() == firstNode &&
                    worker.TryGetProperty("pools", out var pools) &&
                    pools.ValueKind == JsonValueKind.Object)
                {
                    poolCountBefore = pools.EnumerateObject().Count();
                    break;
                }
            }
        }

        Assert.That(poolCountBefore, Is.GreaterThan(0), "Node should have at least one pool configured");

        // Act: Delete node from Redis to trigger re-registration
        await Redis.KeyDeleteAsync(nodeKey);
        await Redis.SetRemoveAsync("nodes", firstNode);

        // Wait for re-registration
        const int maxWaitSeconds = 60;
        var reRegistered = false;

        for (var i = 0; i < maxWaitSeconds; i++)
        {
            await Task.Delay(1000);
            reRegistered = await Redis.KeyExistsAsync(nodeKey);
            if (reRegistered)
                break;
        }

        Assert.That(reRegistered, Is.True,
            $"Node {firstNode} should re-register within {maxWaitSeconds} seconds");

        // Assert: Verify pool state preserved after re-registration
        var diagnosticsResponseAfter = await HttpClient.GetAsync("/diagnostics");
        diagnosticsResponseAfter.EnsureSuccessStatusCode();

        var diagnosticsJsonAfter = await diagnosticsResponseAfter.Content.ReadAsStringAsync();
        var diagnosticsAfter = JsonDocument.Parse(diagnosticsJsonAfter);

        var poolCountAfter = 0;
        if (diagnosticsAfter.RootElement.TryGetProperty("workers", out var workersArrayAfter) &&
            workersArrayAfter.ValueKind == JsonValueKind.Array)
        {
            foreach (var worker in workersArrayAfter.EnumerateArray())
            {
                if (worker.TryGetProperty("id", out var workerId) &&
                    workerId.GetString() == firstNode &&
                    worker.TryGetProperty("pools", out var pools) &&
                    pools.ValueKind == JsonValueKind.Object)
                {
                    poolCountAfter = pools.EnumerateObject().Count();
                    break;
                }
            }
        }

        Assert.That(poolCountAfter, Is.EqualTo(poolCountBefore),
            $"Node pool count should be preserved after re-registration (expected {poolCountBefore}, got {poolCountAfter})");
    }
}
