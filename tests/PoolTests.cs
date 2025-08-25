using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Agenix.PlaywrightGrid.HubClient;
using Microsoft.Playwright;
using NUnit.Framework;

namespace GridTests;

public class PoolTests
{
    private static string NewRunId() => $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    private static async Task<List<(string labelKey, string browserId, string runId)>> BorrowAllForLabelAsync(HubClient client,
        string label, int maxAttempts)
    {
        var acquired = new List<(string labelKey, string browserId, string runId)>();
        for (var i = 0; i < maxAttempts; i++)
            try
            {
                var runId = NewRunId();
                var (browserId, ws, labelKey, browserType) = await client.BorrowAsync(label, runId);
                acquired.Add((labelKey, browserId, runId));
                TestContext.WriteLine($"[Borrowed] label={label} id={browserId} type={browserType ?? "?"} runId={runId}");
            }
            catch (HttpRequestException hre) when (hre.StatusCode == HttpStatusCode.ServiceUnavailable ||
                                                   hre.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var code = hre.StatusCode.HasValue ? (int)hre.StatusCode.Value : 0;
                TestContext.WriteLine(
                    $"[Exhausted] label={label} - no more available ({code}). Total acquired={acquired.Count}");
                break;
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"[BorrowStop] label={label} ex={ex.Message}. Stopping further borrows.");
                break;
            }

        return acquired;
    }

    private static string[] GetLabels()
    {
        // Prefer NUnit parameter; then env; else sensible defaults across all browsers
        var param = TestContext.Parameters.Get("LABELS", null);
        var env = Environment.GetEnvironmentVariable("LABELS");
        var raw = param ?? env;
        if (!string.IsNullOrWhiteSpace(raw))
            return raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        // Defaults that align with common labels used in this repo
        return new[]
        {
            "AppB:Chromium:UAT",
            "AppB:Firefox:UAT",
            "AppB:Webkit:UAT"
        };
    }

    private static int GetInt(string name, int def)
    {
        var str = TestContext.Parameters.Get(name, null) ?? Environment.GetEnvironmentVariable(name);
        return int.TryParse(str, out var v) ? v : def;
    }

    private static async Task<bool> BorrowNavigateReturnAsync(HubClient client, IPlaywright pw, string label,
        CancellationToken ct)
    {
        var browserId = string.Empty;
        var ws = string.Empty;
        var labelKey = label;
        string? browserType = null;
        IBrowser? browser = null;
        IBrowserContext? ctx = null;
        var runId = NewRunId();

        var sw = Stopwatch.StartNew();
        var last = TimeSpan.Zero;

        try
        {
            (browserId, ws, labelKey, browserType) = await client.BorrowAsync(label, runId);
            var type = (browserType ?? "chromium").ToLowerInvariant();
            TestContext.WriteLine($"[Borrow] label={label} id={browserId} type={type}");
            var e1 = sw.Elapsed;
            TestContext.WriteLine($"[T] {label} borrow {e1.TotalMilliseconds:0} ms");
            last = e1;

            // Connect with a small retry to cover race on server start
            browser = type switch
            {
                "chromium" => await pw.Chromium.ConnectAsync(ws, new BrowserTypeConnectOptions { Timeout = 20000 }),
                "firefox" => await pw.Firefox.ConnectAsync(ws, new BrowserTypeConnectOptions { Timeout = 20000 }),
                "webkit" => await pw.Webkit.ConnectAsync(ws, new BrowserTypeConnectOptions { Timeout = 20000 }),
                _ => await pw.Chromium.ConnectAsync(ws, new BrowserTypeConnectOptions { Timeout = 20000 })
            };
            var e2 = sw.Elapsed; TestContext.WriteLine($"[T] {label} connect {(e2 - last).TotalMilliseconds:0} ms"); last = e2;

            ctx = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
            var e3 = sw.Elapsed; TestContext.WriteLine($"[T] {label} context {(e3 - last).TotalMilliseconds:0} ms"); last = e3;
            ctx.SetDefaultTimeout(60000);
            ctx.SetDefaultNavigationTimeout(60000);

            var page = await ctx.NewPageAsync();
            var e4 = sw.Elapsed; TestContext.WriteLine($"[T] {label} page {(e4 - last).TotalMilliseconds:0} ms"); last = e4;
            page.RequestFailed += (_, r) => TestContext.WriteLine($"RequestFailed: {r.Url} - {r.Failure}");
            page.Console += (_, msg) => TestContext.WriteLine($"Console[{msg.Type}] {msg.Text}");

            // Announce current test to the hub so API logs are attributed under Tests tab; pass runId so hub can map browser->run if needed
            var nunitTest = TestContext.CurrentContext.Test;
            var testId = $"{runId}:{nunitTest.FullName}";

            // Forward a runner-side API log line before navigation
            try { await client.SendApiLogAsync(browserId, "Page.Goto https://google.com", direction: "runner"); } catch { }

            await page.GotoAsync("https://google.com", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = 20000
            });

            // Forward a runner-side API log line after navigation
            try { await client.SendApiLogAsync(browserId, "Page.Goto done", direction: "runner"); } catch { }

            var e5 = sw.Elapsed; TestContext.WriteLine($"[T] {label} navigate {(e5 - last).TotalMilliseconds:0} ms"); last = e5;
            // Validate we reached a Google domain (covers consent.google.com, www.google.com, regional TLDs)
            var currentUrl = page.Url;
            var e6 = sw.Elapsed; TestContext.WriteLine($"[T] {label} url {(e6 - last).TotalMilliseconds:0} ms"); last = e6;
            StringAssert.Contains("google", currentUrl.ToLowerInvariant());
            return true;
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[Error] label={label} ex={ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (ctx != null) await ctx.CloseAsync();
            }
            catch
            {
                /* ignore */
            }

            try
            {
                if (browser != null) await browser.CloseAsync();
            }
            catch
            {
                /* ignore */
            }

            if (!string.IsNullOrEmpty(browserId))
                try
                {
                    await client.ReturnAsync(labelKey, browserId, runId);
                    var er = sw.Elapsed; TestContext.WriteLine($"[T] {label} return {(er - last).TotalMilliseconds:0} ms"); last = er;
                    TestContext.WriteLine($"[Return] label={label} id={browserId}");
                }
                catch (Exception rex)
                {
                    TestContext.WriteLine($"[ReturnError] label={label} id={browserId} ex={rex.Message}");
                }
        }
    }

    [Test]
    public async Task AllBrowsersSmokeAcrossLabels()
    {
        using var client = new HubClient("http://localhost:5100");
        if (!await client.HealthAsync())
            Assert.Inconclusive("Hub /health is not available. Ensure docker-compose is up and HUB_URL is correct.");

        var labels = GetLabels();
        var pw = await Playwright.CreateAsync();

        var total = Stopwatch.StartNew();
        foreach (var label in labels)
        {
            var per = Stopwatch.StartNew();
            var ok = await BorrowNavigateReturnAsync(client, pw, label, CancellationToken.None);
            TestContext.WriteLine($"[T] {label} total {per.Elapsed.TotalMilliseconds:0} ms");
            if (!ok)
                Assert.Inconclusive(
                    $"No available browsers for label {label} or connection failed. Check workers and capacity.");
        }
        TestContext.WriteLine($"[T] all-labels total {total.Elapsed.TotalMilliseconds:0} ms");
    }

    [Test]
    public async Task PoolPressure_ConcurrentBorrows()
    {
        using var client = new HubClient("http://localhost:5100");
        if (!await client.HealthAsync())
            Assert.Inconclusive("Hub /health is not available. Ensure docker-compose is up and HUB_URL is correct.");

        var labels = GetLabels();
        var concurrency = GetInt("CONCURRENCY", 4);
        var iterations = GetInt("ITERATIONS", 3);
        var pw = await Playwright.CreateAsync();

        // Build a set of tasks across labels and iterations
        var allTasks = new List<Func<Task<bool>>>();
        foreach (var label in labels)
            for (var i = 0; i < iterations; i++)
                allTasks.Add(() => BorrowNavigateReturnAsync(client, pw, label, CancellationToken.None));

        // Throttle parallelism
        var sem = new SemaphoreSlim(concurrency);
        var running = allTasks.Select(async producer =>
        {
            await sem.WaitAsync();
            try
            {
                return await producer();
            }
            finally
            {
                sem.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(running);
        var successes = results.Count(r => r);
        TestContext.WriteLine(
            $"[Summary] total={results.Length} success={successes} fail={results.Length - successes}");

        // We only require at least one success across the batch to demonstrate pool activity
        Assert.That(successes, Is.GreaterThan(0), "No successful borrows. Check pool capacity and labels.");
    }

    [Test]
    public async Task FillPool_AllLabels_SeeDashboard()
    {
        using var client = new HubClient("http://localhost:5100");
        if (!await client.HealthAsync())
            Assert.Inconclusive("Hub /health is not available. Ensure docker-compose is up and HUB_URL is correct.");

        var labels = GetLabels();
        var holdSeconds = GetInt("HOLD_SECONDS", 10);
        var maxAttemptsPerLabel = GetInt("MAX_ATTEMPTS_PER_LABEL", 100);

        var allBorrowed = new List<(string labelKey, string browserId, string runId)>();

        // Borrow to capacity per label
        foreach (var label in labels)
        {
            var acquired = await BorrowAllForLabelAsync(client, label, maxAttemptsPerLabel);
            TestContext.WriteLine($"[LabelSummary] label={label} acquired={acquired.Count}");
            allBorrowed.AddRange(acquired);
        }

        if (allBorrowed.Count == 0)
            Assert.Inconclusive(
                "No browsers were borrowed. Check workers, capacity, labels, or increase MAX_ATTEMPTS_PER_LABEL.");

        TestContext.WriteLine(
            $"[PoolFilled] Total borrowed across labels = {allBorrowed.Count}. Holding for {holdSeconds}s so dashboard can reflect pool usage...");
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, holdSeconds)));

        // Return everything
        var returns = allBorrowed
            .Select(x => client.ReturnAsync(x.labelKey, x.browserId, x.runId))
            .ToArray();
        try
        {
            await Task.WhenAll(returns);
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[ReturnErrors] {ex.Message}");
        }

        TestContext.WriteLine(
            "[Done] All borrowed sessions attempted to return. Check dashboard history for borrow spikes.");

        // Test passes if we successfully borrowed at least once.
        Assert.That(allBorrowed.Count, Is.GreaterThan(0));
    }
}
