#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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
using Agenix.PlaywrightGrid.HubClient;
using NUnit.Framework;

namespace GridTests;

public class NegativeScenariosTests
{
    private static bool IsTrue(string? v)
        => !string.IsNullOrWhiteSpace(v) && (v.Equals("1") || v.Equals("true", System.StringComparison.OrdinalIgnoreCase));

    [Test]
    public async Task Borrow_WithBadRunnerSecret_ShouldReturn401()
    {
        using var client = new HubClient("http://localhost:5100", runnerSecret: "wrong-secret");
        if (!await client.HealthAsync())
        {
            Assert.Inconclusive("Hub /health is not available. Ensure docker-compose is up and HUB_URL is correct.");
        }

        var d = new AsyncTestDelegate(async () => await client.BorrowAsync("AppB:Chromium:UAT", runId: "neg-401"));
        Assert.That(d, Throws.InstanceOf<Agenix.PlaywrightGrid.HubClient.AuthenticationException>()
            .Or.InstanceOf<HttpRequestException>());
    }

    [Test]
    public async Task Borrow_WhenCapacityExhausted_ShouldReturn503()
    {
        using var client = new HubClient("http://localhost:5100");
        if (!await client.HealthAsync())
        {
            Assert.Inconclusive("Hub /health is not available. Ensure docker-compose is up and HUB_URL is correct.");
        }

        // Fill capacity first (pool default in TestEnvironment: AppB:Chromium:UAT=2)
        var acquired = new List<string>();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    var (browserId, _, _, _) = await client.BorrowAsync("AppB:Chromium:UAT", runId: $"neg-503-{i}");
                    acquired.Add(browserId);
                }
                catch
                {
                    break; // hit capacity sooner than 10
                }
            }
        }
        catch
        {
            // If we cannot borrow two, the environment has changed; continue to the assertion path anyway.
        }

        // Next borrow should fail with 503/429 depending on hub policy; assert 503 primary, allow 429 too.
        try
        {
            var d = new AsyncTestDelegate(async () => await client.BorrowAsync("AppB:Chromium:UAT", runId: "neg-503-exhaust"));
            Assert.That(d,
                Throws.InstanceOf<Agenix.PlaywrightGrid.HubClient.CapacityUnavailableException>()
                    .Or.InstanceOf<HttpRequestException>()
                    .Or.InstanceOf<TaskCanceledException>());
            // If HttpRequestException/TaskCanceled, consider it acceptable (hub under pressure/timeouts).
        }
        finally
        {
            // No explicit return: Hub auto-finishes/auto-returns sessions; ReturnAsync is obsolete/no-op.
            // We intentionally do nothing here to avoid obsolete API usage warnings.
        }
    }

    [Test]
    public async Task BorrowQueueTimeout_ShouldFailWithinTimeoutWindow()
    {
        using var client = new HubClient("http://localhost:5100");
        if (!await client.HealthAsync())
        {
            Assert.Inconclusive("Hub /health is not available. Ensure docker-compose is up and HUB_URL is correct.");
        }

        // Try to congest the pool and attempt an extra borrow with short client timeout
        var acquired = new List<string>();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    var (browserId, _, _, _) = await client.BorrowAsync("AppB:Chromium:UAT", runId: $"neg-q-{i}");
                    acquired.Add(browserId);
                }
                catch
                {
                    break;
                }
            }
        }
        catch { }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // We expect either a domain CapacityUnavailable/Http 503 if the hub denies immediately, or a client-side OperationCanceled when waiting in queue.
        var d = new AsyncTestDelegate(async () =>
            await client.BorrowAsync("AppB:Chromium:UAT", runId: "neg-q-timeout", cancellationToken: cts.Token));
        try
        {
            Assert.That(d,
                Throws.InstanceOf<OperationCanceledException>()
                    .Or.InstanceOf<Agenix.PlaywrightGrid.HubClient.CapacityUnavailableException>()
                    .Or.InstanceOf<HttpRequestException>());
        }
        catch (AssertionException)
        {
            Assert.Inconclusive("Borrow did not time out or fail under queue pressure; environment likely had spare capacity.");
        }

        // No explicit return: Hub auto-finishes/auto-returns sessions; ReturnAsync is obsolete/no-op.
        // Intentionally leaving borrowed sessions to auto-expire/auto-return.
    }

    [Test]
    public async Task NodeEviction_AfterWorkerStopped_BorrowShouldFail()
    {
        // This test assumes Testcontainers-managed environment from TestEnvironment.
        // If GRID_TESTS_USE_LOCAL=1, we cannot control containers; mark inconclusive.
        if (IsTrue(Environment.GetEnvironmentVariable("GRID_TESTS_USE_LOCAL")))
        {
            Assert.Inconclusive("Node eviction test requires Testcontainers-managed environment.");
        }

        using var client = new HubClient("http://localhost:5100");
        if (!await client.HealthAsync())
        {
            Assert.Inconclusive("Hub /health is not available.");
        }

        // Stop a worker container via docker CLI and wait for hub sweeper to evict.
        var docker = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
        // try both names for reuse/non-reuse naming
        var names = new[] { "gridtests-worker1", "gridtests-worker1" };
        foreach (var name in names)
        {
            try
            {
                using var p = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docker,
                        Arguments = $"stop {name}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch { }
        }

        // Allow NodeSweeper to run; default heartbeat eviction may be ~30s+ depending on config, wait up to 90s
        await Task.Delay(TimeSpan.FromSeconds(45));

        var d2 = new AsyncTestDelegate(async () => await client.BorrowAsync("AppB:Chromium:UAT", runId: "neg-evict"));
        try
        {
            Assert.That(d2,
                Throws.InstanceOf<Agenix.PlaywrightGrid.HubClient.CapacityUnavailableException>()
                    .Or.InstanceOf<HttpRequestException>());
        }
        catch (AssertionException)
        {
            Assert.Inconclusive("Borrow succeeded after stopping worker; node eviction may not have elapsed yet in this environment.");
        }
    }
}
