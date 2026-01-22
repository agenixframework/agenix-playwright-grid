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
using Agenix.PlaywrightGrid.Client.Abstractions;
using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.BrowserPool;

/// <summary>
///     Smoke tests for browser pool operations using Playwright browser automation.
///     These tests verify end-to-end browser borrowing, navigation, and return.
/// </summary>
[TestFixture]
public class SmokeTests : ApiTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await EnsureWorkersHealthyAndPoolStableAsync();
    }

    [OneTimeSetUp]
    public override async Task OneTimeSetup()
    {
        await base.OneTimeSetup();

        // Get labels from the environment or use defaults
        var labelsEnv = Environment.GetEnvironmentVariable("LABELS");
        _labels = !string.IsNullOrWhiteSpace(labelsEnv)
            ? labelsEnv.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : ["AppB:Chromium:UAT", "AppB:Firefox:UAT", "AppB:Webkit:UAT"];

        await TestContext.Out.WriteLineAsync($"[Setup] Labels: {string.Join(", ", _labels)}");
    }

    private string[] _labels = null!;

    /// <summary>
    ///     Smoke test: Borrow browser, navigate to Google, verify navigation, return browser
    /// </summary>
    [Test]
    public async Task SmokeTest_BorrowNavigateReturn_AllLabels()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");

        var pw = await Playwright.CreateAsync();

        foreach (var label in _labels)
        {
            var sw = Stopwatch.StartNew();
            await TestContext.Out.WriteLineAsync($"[SmokeTest] Testing label: {label}");

            // Create launch and suite
            var launchRequest = new StartLaunchRequest
            {
                Name = $"SmokeTest-{label.Replace(':', '-')}-{Guid.NewGuid():N}",
                Attributes = new List<ItemAttribute>
                {
                    new() { Key = "owner", Value = "smoke-test" }, new() { Key = "automated", Value = "" }
                }
            };
            var launchResponse = await Client.Launch.StartAsync(launchRequest);
            await TestContext.Out.WriteLineAsync($"[SmokeTest] Created launch: {launchResponse.Uuid}");

            // Start test item (borrows browser) - no suite needed
            var testItemRequest = new StartTestItemRequest
            {
                LaunchUuid = launchResponse.Uuid,
                Name = $"SmokeTest-{label.Replace(':', '-')}-{DateTime.UtcNow:HHmmss}",
                Type = TestItemType.Test,
                LabelKey = label, // REQUIRED: Label key for browser borrowing
                StartTime = DateTime.UtcNow
            };

            var testItemResponse = await Client.TestItem.StartAsync(testItemRequest);
            Assert.That(testItemResponse.BrowserId, Is.Not.Null.And.Not.Empty, "Browser should be borrowed");
            Assert.That(testItemResponse.WebSocketEndpoint, Is.Not.Null.And.Not.Empty,
                "WebSocket endpoint should be provided");

            var borrowTime = sw.Elapsed;
            await TestContext.Out.WriteLineAsync(
                $"[SmokeTest] Borrowed browser {testItemResponse.BrowserId} in {borrowTime.TotalMilliseconds:0}ms");
            await TestContext.Out.WriteLineAsync($"[SmokeTest] WebSocket: {testItemResponse.WebSocketEndpoint}");
            await TestContext.Out.WriteLineAsync($"[SmokeTest] Browser Type: {testItemResponse.BrowserType}");

            IBrowser? browser = null;
            IBrowserContext? ctx = null;

            try
            {
                // Connect to a browser via WebSocket
                var browserType = (testItemResponse.BrowserType ?? "chromium").ToLowerInvariant();
                browser = browserType switch
                {
                    "chromium" => await pw.Chromium.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 }),
                    "firefox" => await pw.Firefox.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 }),
                    "webkit" => await pw.Webkit.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 }),
                    _ => await pw.Chromium.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 })
                };

                var connectTime = sw.Elapsed - borrowTime;
                await TestContext.Out.WriteLineAsync(
                    $"[SmokeTest] Connected to browser in {connectTime.TotalMilliseconds:0}ms");

                // Create context and page
                ctx = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
                ctx.SetDefaultTimeout(60000);
                ctx.SetDefaultNavigationTimeout(60000);

                var page = await ctx.NewPageAsync();
                var pageTime = sw.Elapsed - borrowTime - connectTime;
                await TestContext.Out.WriteLineAsync($"[SmokeTest] Created page in {pageTime.TotalMilliseconds:0}ms");

                // Navigate to Google
                await page.GotoAsync("https://google.com",
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 20000 });

                var navTime = sw.Elapsed - borrowTime - connectTime - pageTime;
                await TestContext.Out.WriteLineAsync(
                    $"[SmokeTest] Navigated to Google in {navTime.TotalMilliseconds:0}ms");

                // Verify navigation
                var url = page.Url.ToLowerInvariant();
                Assert.That(url, Does.Contain("google"), $"Expected Google URL, got: {url}");
                await TestContext.Out.WriteLineAsync($"[SmokeTest] ✓ Successfully navigated to: {page.Url}");
            }
            finally
            {
                // Close browser connection
                if (ctx != null)
                {
                    await ctx.CloseAsync();
                }

                if (browser != null)
                {
                    await browser.CloseAsync();
                }

                // Finish test item (returns browser to pool)
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await Client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid), finishRequest);

                await TestContext.Out.WriteLineAsync(
                    $"[SmokeTest] ✓ Browser {testItemResponse.BrowserId} returned to pool");
                await TestContext.Out.WriteLineAsync(
                    $"[SmokeTest] Total time for {label}: {sw.Elapsed.TotalMilliseconds:0}ms");
            }
        }
    }

    /// <summary>
    ///     Concurrent test: Multiple parallel browser borrows with navigation
    /// </summary>
    [Test]
    public async Task ConcurrentTest_ParallelBorrowsWithNavigation()
    {
        Assert.That(Client, Is.Not.Null, "Client not initialized");

        var concurrency = int.TryParse(Environment.GetEnvironmentVariable("CONCURRENCY"), out var c) ? c : 3;
        var iterations = int.TryParse(Environment.GetEnvironmentVariable("ITERATIONS"), out var i) ? i : 2;

        await TestContext.Out.WriteLineAsync(
            $"[ConcurrentTest] Starting concurrent test: {concurrency} concurrent, {iterations} iterations per label");

        var pw = await Playwright.CreateAsync();

        // Create launch for all concurrent runs
        var launchRequest = new StartLaunchRequest
        {
            Name = $"ConcurrentTest-{Guid.NewGuid():N}",
            Attributes = new List<ItemAttribute>
            {
                new() { Key = "owner", Value = "concurrent-test" }, new() { Key = "stress-test", Value = "" }
            }
        };
        var launchResponse = await Client.Launch.StartAsync(launchRequest);
        ArgumentNullException.ThrowIfNull(launchResponse);

        // Build tasks for all labels and iterations
        var allTasks = new List<Func<Task<bool>>>();
        foreach (var label in _labels)
        {
            for (var iter = 0; iter < iterations; iter++)
            {
                var labelCopy = label; // Capture for closure
                allTasks.Add(async () =>
                    await ExecuteSingleBorrowNavigateReturnAsync(Client, pw, launchResponse!.Uuid, labelCopy));
            }
        }

        // Execute with concurrency limit
        var sem = new SemaphoreSlim(concurrency);
        var running = allTasks.Select(async taskFunc =>
        {
            await sem.WaitAsync();
            try
            {
                return await taskFunc();
            }
            finally
            {
                sem.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(running);
        var successes = results.Count(r => r);

        await TestContext.Out.WriteLineAsync(
            $"[ConcurrentTest] Summary: total={results.Length} success={successes} fail={results.Length - successes}");

        // Require at least one success to demonstrate pool activity
        Assert.That(successes, Is.GreaterThan(0), "No successful borrows. Check pool capacity and labels.");
    }

    /// <summary>
    ///     Helper method to execute a single borrow-navigate-return cycle
    /// </summary>
    private static async Task<bool> ExecuteSingleBorrowNavigateReturnAsync(
        IClientService client,
        IPlaywright pw,
        string launchUuid,
        string label)
    {
        try
        {
            // Start a test item (no suite needed)
            var testItemRequest = new StartTestItemRequest
            {
                LaunchUuid = launchUuid,
                Name = $"Concurrent-{label.Replace(':', '-')}-{DateTime.UtcNow:HHmmss}",
                Type = TestItemType.Test,
                LabelKey = label, // REQUIRED: Label key for browser borrowing
                StartTime = DateTime.UtcNow
            };

            var testItemResponse = await client.TestItem.StartAsync(testItemRequest);
            await TestContext.Out.WriteLineAsync($"[Concurrent] Borrowed {testItemResponse.BrowserId} for {label}");

            IBrowser? browser = null;
            IBrowserContext? ctx = null;

            try
            {
                // Connect to browser
                var browserType = (testItemResponse.BrowserType ?? "chromium").ToLowerInvariant();
                browser = browserType switch
                {
                    "chromium" => await pw.Chromium.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 }),
                    "firefox" => await pw.Firefox.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 }),
                    "webkit" => await pw.Webkit.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 }),
                    _ => await pw.Chromium.ConnectAsync(testItemResponse.WebSocketEndpoint!,
                        new BrowserTypeConnectOptions { Timeout = 20000 })
                };

                ctx = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
                var page = await ctx.NewPageAsync();

                // Quick navigation
                await page.GotoAsync("https://google.com",
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 20000 });

                await TestContext.Out.WriteLineAsync($"[Concurrent] ✓ Navigated successfully for {label}");
                return true;
            }
            finally
            {
                if (ctx != null)
                {
                    await ctx.CloseAsync();
                }

                if (browser != null)
                {
                    await browser.CloseAsync();
                }

                // Finish the test item
                var finishRequest = new FinishTestItemRequest { Status = Status.Passed, EndTime = DateTime.UtcNow };
                await client.TestItem.FinishAsync(Guid.Parse(testItemResponse.Uuid), finishRequest);

                await TestContext.Out.WriteLineAsync($"[Concurrent] Returned {testItemResponse.BrowserId} for {label}");
            }
        }
        catch (Exception ex)
        {
            await TestContext.Out.WriteLineAsync($"[Concurrent] Failed for {label}: {ex.Message}");
            return false;
        }
    }
}
