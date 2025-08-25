# Test Client Usage — Using HubClient in C# test runners

This guide shows how to use Agenix.PlaywrightGrid.HubClient from your .NET test runner to:
- Borrow a Playwright browser session for a specific label key
- Attribute actions to a runId for grouping on the Dashboard
- Optionally forward client-side API log lines to the Hub (runner → hub)
- Return the session when done

If you’re looking for a deeper explanation of how the Dashboard assembles data and how protocol/API logs appear, see: docs/TestResultsDashboard-Approach.md


## Prerequisites
- HUB_URL: the Hub base URL (for local compose this is typically http://127.0.0.1:5100)
- HUB_RUNNER_SECRET: must match the hub’s HUB_RUNNER_SECRET; the client sends it as header x-hub-secret

You can pass HUB_URL explicitly to HubClient or set it via environment variables when constructing from configuration.


## Minimal NUnit example
The example below borrows a browser, connects with Playwright, performs an action, optionally forwards a client-side API log line, and returns the session.

```csharp
using Agenix.PlaywrightGrid.HubClient;
using Microsoft.Playwright;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

[TestFixture]
public class ExampleSuite
{
    [Test]
    public async Task ExampleTest()
    {
        // A run groups multiple tests within one execution.
        var runId = Guid.NewGuid().ToString("N");

        // HUB_URL can be supplied explicitly or via env/config.
        using var client = new HubClient("http://127.0.0.1:5100");

        // 1) Borrow a browser for a label and pass runId so the hub can attribute logs to this run
        var (browserId, wsEndpoint, labelKey, browserType) = await client.BorrowAsync("AppB:Chromium:UAT", runId);

        var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.ConnectAsync(wsEndpoint);
        var ctx = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        var page = await ctx.NewPageAsync();

        try
        {
            // Optional: send a client-side API log line before/after key actions
            await client.SendApiLogAsync(browserId, "Page.Goto https://example.com");

            await page.GotoAsync("https://example.com", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 20000 });

            await client.SendApiLogAsync(browserId, "Page.Goto done");
        }
        finally
        {
            await ctx.CloseAsync();
            await browser.CloseAsync();
            await client.ReturnAsync(labelKey, browserId, runId);
        }
    }
}
```

Notes
- labelKey identifies which pool to borrow from (for example App:Browser:Env[:Region[:OS…]]).
- Passing runId to BorrowAsync lets the Hub associate all logs with a specific run and browser session.
- The worker proxies the Playwright WebSocket and mirrors protocol messages to the Hub automatically.
- SendApiLogAsync/SendApiLogsAsync are optional for forwarding additional runner-side log lines (like your own “pw:api” style messages) to the Hub.


## xUnit/MSTest sketch
The same pattern applies: create a runId for your execution, borrow in test setup (or per test), perform actions, and ReturnAsync in teardown.


## Forwarding your own client-side logs
If you already capture human-readable API-level logs in your test framework (e.g., wrappers around page actions, or your own logger), you can forward them to the Hub using:

```csharp
await client.SendApiLogAsync(browserId, "<your log message>");
// or batch
await client.SendApiLogsAsync(browserId, new[] { "log 1", "log 2" });
```

Note: The client automatically tags these entries as coming from the runner (direction = "runner"). You don’t need to specify the direction yourself.

These logs are stored alongside worker protocol messages and appear in the Dashboard associated with the browser session/run.


## Environment variables summary
- HUB_URL: hub base URL (e.g., http://127.0.0.1:5100)
- HUB_RUNNER_SECRET: required by the hub for runner endpoints; the client sends it as x-hub-secret


## Troubleshooting
- Unauthorized (401): HUB_RUNNER_SECRET mismatch between your client and the hub.
- “No API logs yet”: Worker may not be forwarding protocol logs. Secrets may also be misconfigured.
- Not seeing your custom client logs: make sure you are calling SendApiLogAsync with the correct browserId after you BorrowAsync.


## Related docs
- Enabling pw:api logs in Playwright .NET: docs/PlaywrightDotNet-pw-api.md
- Dashboard & how logs appear: docs/TestResultsDashboard-Approach.md
- Root overview and configuration: README.md


## Optional: Auto-forward via Playwright event listeners
You can automatically forward useful Playwright events from the runner to the Hub using the helper provided by the HubClient package.

Example (NUnit):

```csharp
var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.ConnectAsync(wsEndpoint);
var ctx = await browser.NewContextAsync();
var page = await ctx.NewPageAsync();

// Start auto-forwarding selected events (console, request/response, pageerror, etc.)
using var forwarder = page.ForwardApiLogs(client, browserId);

await page.GotoAsync("https://example.com");
```

Customization:

```csharp
using var forwarder = page.ForwardApiLogs(
    client,
    browserId,
    new PlaywrightEventForwarder.Options { Console = true, RequestFinished = true }
);
```

Notes
- These logs are tagged direction = "runner" automatically.
- This does not replace protocol mirroring from workers; it complements it with runner-side, human-readable messages (console, request lifecycle, errors).
