# Playwright .NET — Enable pw:api logs

This page explains how to turn on Playwright API logs ("pw:api") when you run Playwright from .NET test runners (NUnit, xUnit, MSTest, etc.).

Quick answer
- pw:api is enabled by setting environment variable DEBUG=pw:api before Playwright.CreateAsync runs.
- These logs are printed by the Playwright driver (Node) to stdout/stderr of the spawned driver process.
- They are not automatically sent to the Grid Hub; if you want runner-side messages on the Dashboard, forward your own logs via HubClient (see below).

Why env var?
- The logs are produced by the underlying Playwright driver (Node). It reads DEBUG to decide which categories to print (pw:api, pw:protocol, etc.).

Ways to enable pw:api
1) Set env var when invoking tests (CLI)
- macOS/Linux (bash/zsh):
  DEBUG=pw:api dotnet test

- Windows PowerShell:
  $env:DEBUG = "pw:api"; dotnet test; Remove-Item Env:DEBUG

- Windows cmd.exe:
  set DEBUG=pw:api && dotnet test

2) Set env var in code (before CreateAsync)
- Make sure this runs before you call Playwright.CreateAsync so the child driver process inherits the variable.

C# example (NUnit):

using NUnit.Framework;
using System;

[SetUpFixture]
public class GlobalPlaywrightDebug
{
    [OneTimeSetUp]
    public void EnablePwApi()
    {
        // Apply to current process; child Playwright driver inherits it.
        Environment.SetEnvironmentVariable("DEBUG", "pw:api", EnvironmentVariableTarget.Process);
        // Optional alternative categories/examples:
        // Environment.SetEnvironmentVariable("DEBUG", "pw:api,pw:browser,pw:channel");
        // Environment.SetEnvironmentVariable("PWDEBUG", "console"); // stepwise console debug
    }
}

3) IDE run configuration
- Rider/Visual Studio: add environment variable DEBUG with value pw:api to your test run configuration.
- GitHub Actions/Azure Pipelines: set env: DEBUG: pw:api for the step that runs tests.

Notes on PWDEBUG
- PWDEBUG=console enables stepwise console mode and prints additional details; it is separate from DEBUG=pw:api. You can use both.

Where do the logs appear?
- They are emitted to the Playwright driver process’ stdout/stderr. With dotnet test you’ll see them in the test output/console. Some test runners collapse stdout; ensure console output is visible in your CI/IDE.

Sending runner-side logs to the Grid Hub Dashboard
- pw:api output is produced by the driver, not by your test code, so the HubClient cannot automatically intercept it.
- If you want human-readable, API-level messages to appear under the Dashboard’s Tests tab, you have two options:
  1) Use the Grid’s worker-protocol mirroring (already enabled): protocol messages from the worker are grouped under the current test when you call HubClient.SetCurrentTestAsync.
  2) Forward your own runner logs (e.g., from your wrappers or your logger) using HubClient:

C# forwarding example:

await client.SetCurrentTestAsync(browserId, testId, nunit.Name, "Running", runId: runId);
await client.SendApiLogAsync(browserId, "Page.Goto https://example.com");
// ... your actions ...
await client.SendApiLogAsync(browserId, "Page.Goto done");

- SendApiLogAsync/SendApiLogsAsync automatically tag direction = "runner". No need to set it yourself.

Troubleshooting
- No logs after setting DEBUG:
  - Ensure the variable is set before Playwright.CreateAsync is called.
  - Verify the value is exactly pw:api (case-sensitive) or include it among comma-separated categories.
  - Some CI systems mute stdout/stderr; enable verbose logging.
- I want to capture and forward the driver’s pw:api lines:
  - This requires intercepting the child process’ stdout/stderr. Playwright .NET does not expose this directly. In practice, prefer your own runner-side logs forwarded via HubClient or rely on the worker’s protocol mirroring.

Related
- Test client usage and forwarding runner logs: docs/TestClient-Usage.md
