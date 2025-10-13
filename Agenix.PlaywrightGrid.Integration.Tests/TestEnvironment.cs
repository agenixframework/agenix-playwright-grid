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
using System.Text.RegularExpressions;
using Agenix.PlaywrightGrid.Shared.Logging;
using NUnit.Framework;
using Serilog;
using Serilog.Core;

namespace Agenix.PlaywrightGrid.Integration.Tests;

/// <summary>
///     NUnit SetUpFixture that prepares the test environment by waiting for
///     docker-compose services to be ready. Services must be started before running tests.
///     Prerequisites:
///     1. Start services: docker compose --profile infrastructure --profile core up -d
///     2. Or use the automated script: ./scripts/run-docker-compose-test.sh
///     This fixture waits for:
///     - Hub health endpoint to be ready
///     - Workers to register in the pool
///     - Browser pool capacity to be available
/// </summary>
[SetUpFixture]
public sealed class TestEnvironment
{
    private static Logger? _logger;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Configure Serilog to write to timestamped file
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var logFilePath = $"/tmp/pg-integration-tests-{timestamp}.log";

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.ChunkedConsole(
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message:l}{NewLine}{Exception}")
            .WriteTo.ChunkedFile(
                logFilePath,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message:l}{NewLine}{Exception}")
            .CreateLogger();

        // Write to both Serilog logger and NUnit test output
        _logger.Information("[IntegrationTests] Log file: {LogFilePath}", logFilePath);
        await TestContext.Progress.WriteLineAsync($"[IntegrationTests] Log file: {logFilePath}");

        _logger.Information("[IntegrationTests] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _logger.Information("[IntegrationTests] Integration Tests - Environment Setup");
        _logger.Information("[IntegrationTests] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        await TestContext.Progress.WriteLineAsync("[IntegrationTests] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        await TestContext.Progress.WriteLineAsync("[IntegrationTests] Integration Tests - Environment Setup");
        await TestContext.Progress.WriteLineAsync("[IntegrationTests] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        await TestContext.Progress.WriteLineAsync("");

        // 1. Get Hub URL (from environment or default)
        var hubUrl = Environment.GetEnvironmentVariable("HUB_URL")
                     ?? Environment.GetEnvironmentVariable("AGENIX_HUB_URL")
                     ?? "http://127.0.0.1:5100";

        // 2. Get health check configuration
        var healthTimeout = GetHealthTimeoutSeconds();
        var healthPoll = GetHealthPollInterval();
        var workerTimeout = GetWorkerTimeoutSeconds();
        var expectedWorkers = GetExpectedWorkers();

        _logger.Information("[IntegrationTests] Configuration:");
        _logger.Information("[IntegrationTests]   Hub URL:          {HubUrl}", hubUrl);
        _logger.Information("[IntegrationTests]   Health Timeout:   {HealthTimeout}s", healthTimeout);
        _logger.Information("[IntegrationTests]   Worker Timeout:   {WorkerTimeout}s", workerTimeout);

        await TestContext.Progress.WriteLineAsync("[IntegrationTests] Configuration:");
        await TestContext.Progress.WriteLineAsync($"[IntegrationTests]   Hub URL:          {hubUrl}");
        await TestContext.Progress.WriteLineAsync($"[IntegrationTests]   Health Timeout:   {healthTimeout}s");
        await TestContext.Progress.WriteLineAsync($"[IntegrationTests]   Worker Timeout:   {workerTimeout}s");

        // Show source of expected workers count
        var workersSource = GetExpectedWorkersSource();
        _logger.Information("[IntegrationTests]   Expected Workers: {ExpectedWorkers} ({WorkersSource})",
            expectedWorkers, workersSource);
        await TestContext.Progress.WriteLineAsync(
            $"[IntegrationTests]   Expected Workers: {expectedWorkers} ({workersSource})");

        await TestContext.Progress.WriteLineAsync("");

        // 3. Wait for Hub health
        _logger.Information("[IntegrationTests] Waiting for Hub at {HubUrl}/health...", hubUrl);
        await TestContext.Progress.WriteLineAsync($"[IntegrationTests] Waiting for Hub at {hubUrl}/health...");
        await WaitForHubHealth(hubUrl.TrimEnd('/') + "/health", TimeSpan.FromSeconds(healthTimeout), healthPoll,
            TestContext.Progress, _logger);
        await TestContext.Progress.WriteLineAsync("");

        // 4. Wait for workers to register
        _logger.Information("[IntegrationTests] Waiting for workers to join the pool...");
        await TestContext.Progress.WriteLineAsync("[IntegrationTests] Waiting for workers to join the pool...");
        var workersReady = await WaitForWorkers(hubUrl, expectedWorkers, TimeSpan.FromSeconds(workerTimeout),
            TestContext.Progress, _logger);

        if (!workersReady)
        {
            _logger.Warning(
                "[IntegrationTests] ⚠️  Warning: Not all workers are ready. Tests may fail with '503 No browser capacity' errors.");
            await TestContext.Progress.WriteLineAsync(
                "[IntegrationTests] ⚠️  Warning: Not all workers are ready. Tests may fail with '503 No browser capacity' errors.");
        }

        await TestContext.Progress.WriteLineAsync("");

        // 5. Set HUB_URL environment variable for tests
        Environment.SetEnvironmentVariable("HUB_URL", hubUrl);

        _logger.Information("[IntegrationTests] ✅ Environment ready for tests");
        _logger.Information("[IntegrationTests] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        await TestContext.Progress.WriteLineAsync("[IntegrationTests] ✅ Environment ready for tests");
        await TestContext.Progress.WriteLineAsync("[IntegrationTests] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        await TestContext.Progress.WriteLineAsync("");
    }

    [OneTimeTearDown]
    public Task GlobalTeardown()
    {
        // Dispose Serilog logger to flush any remaining logs
        _logger?.Dispose();

        // No cleanup needed - docker-compose handles container lifecycle
        // Services are managed by the run-docker-compose-test.sh script
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Get health check timeout from environment variable.
    ///     Default: 120 seconds
    /// </summary>
    private static int GetHealthTimeoutSeconds()
    {
        var env = Environment.GetEnvironmentVariable("AGENIX_TESTS_HEALTH_TIMEOUT_SECONDS");
        return int.TryParse(env, out var s) && s > 0 ? s : 120;
    }

    /// <summary>
    ///     Get health check poll interval from environment variable.
    ///     Default: 1.0 second
    ///     Clamped to range [0.2, 5.0] seconds
    /// </summary>
    private static TimeSpan GetHealthPollInterval()
    {
        var env = Environment.GetEnvironmentVariable("AGENIX_TESTS_HEALTH_POLL_INTERVAL_SECONDS");
        if (double.TryParse(env, out var d) && d > 0)
        {
            // Clamp to sane range: 0.2s to 5s
            d = Math.Min(Math.Max(d, 0.2), 5.0);
            return TimeSpan.FromSeconds(d);
        }

        return TimeSpan.FromSeconds(1.0);
    }

    /// <summary>
    ///     Get worker timeout from environment variable.
    ///     Default: 60 seconds
    /// </summary>
    private static int GetWorkerTimeoutSeconds()
    {
        var env = Environment.GetEnvironmentVariable("AGENIX_TESTS_WORKER_TIMEOUT_SECONDS");
        return int.TryParse(env, out var s) && s > 0 ? s : 60;
    }

    /// <summary>
    ///     Get expected number of workers from environment variable.
    ///     Defaults are read from .env.workers if available, otherwise:
    ///     - 10 workers (5 chromium + 3 firefox + 2 webkit from docker-compose.workers.yml defaults)
    /// </summary>
    private static int GetExpectedWorkers()
    {
        var env = Environment.GetEnvironmentVariable("AGENIX_TESTS_EXPECTED_WORKERS");
        if (int.TryParse(env, out var w) && w > 0)
        {
            return w;
        }

        // Try to find .env.workers file in multiple locations
        var searchPaths = new[]
        {
            Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(TestEnvironment).Assembly.Location),
            Path.Combine(Directory.GetCurrentDirectory(), ".."),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".."),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".."),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..")
        };

        foreach (var basePath in searchPaths.Where(p => p != null))
        {
            var envWorkersPath = Path.Combine(basePath!, ".env.workers");
            if (File.Exists(envWorkersPath))
            {
                try
                {
                    var lines = File.ReadAllLines(envWorkersPath);
                    var chromium = GetReplicaCount(lines, "WORKER_CHROMIUM_REPLICAS") ?? 5;
                    var firefox = GetReplicaCount(lines, "WORKER_FIREFOX_REPLICAS") ?? 3;
                    var webkit = GetReplicaCount(lines, "WORKER_WEBKIT_REPLICAS") ?? 2;
                    return chromium + firefox + webkit;
                }
                catch
                {
                    // Ignore file read errors, continue searching
                }
            }
        }

        // Default: 5 chromium + 3 firefox + 2 webkit = 10 (from docker-compose.workers.yml)
        return 10;
    }

    /// <summary>
    ///     Parse replica count from .env.workers lines.
    /// </summary>
    private static int? GetReplicaCount(string[] lines, string key)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (line == null)
        {
            return null;
        }

        var value = line.Split('=', 2)[1].Trim();
        return int.TryParse(value, out var count) ? count : null;
    }

    /// <summary>
    ///     Get description of where expected workers count came from.
    /// </summary>
    private static string GetExpectedWorkersSource()
    {
        var env = Environment.GetEnvironmentVariable("AGENIX_TESTS_EXPECTED_WORKERS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return "from AGENIX_TESTS_EXPECTED_WORKERS";
        }

        // Try to find .env.workers file in multiple locations
        var searchPaths = new[]
        {
            Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(TestEnvironment).Assembly.Location),
            Path.Combine(Directory.GetCurrentDirectory(), ".."),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".."),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".."),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..")
        };

        foreach (var basePath in searchPaths.Where(p => p != null))
        {
            var envWorkersPath = Path.Combine(basePath!, ".env.workers");
            if (File.Exists(envWorkersPath))
            {
                try
                {
                    var lines = File.ReadAllLines(envWorkersPath);
                    var chromium = GetReplicaCount(lines, "WORKER_CHROMIUM_REPLICAS") ?? 5;
                    var firefox = GetReplicaCount(lines, "WORKER_FIREFOX_REPLICAS") ?? 3;
                    var webkit = GetReplicaCount(lines, "WORKER_WEBKIT_REPLICAS") ?? 2;
                    return $"from .env.workers: {chromium} chromium + {firefox} firefox + {webkit} webkit";
                }
                catch
                {
                    // Ignore, continue searching
                }
            }
        }

        return "docker-compose.workers.yml defaults";
    }

    /// <summary>
    ///     Wait for Hub health endpoint to return success.
    ///     Polls the endpoint until it returns 200 OK or timeout is reached.
    /// </summary>
    private static async Task WaitForHubHealth(string healthUrl, TimeSpan timeout, TimeSpan pollInterval,
        TextWriter log, Logger? logger)
    {
        var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(10);

        var sw = Stopwatch.StartNew();
        Exception? lastError = null;
        var attempt = 0;

        while (sw.Elapsed < timeout)
        {
            attempt++;
            try
            {
                var resp = await http.GetAsync(healthUrl);
                if (resp.IsSuccessStatusCode)
                {
                    var message =
                        $"[IntegrationTests] ✅ Hub health OK after {sw.Elapsed.TotalSeconds:F1}s (attempt {attempt})";
                    logger?.Information(message);
                    await log.WriteLineAsync(message);
                    return;
                }

                lastError = new Exception($"HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            // Show progress every 10 seconds
            if (attempt > 1 && attempt % 10 == 0)
            {
                var message =
                    $"[IntegrationTests] Waiting for Hub health... elapsed={sw.Elapsed.TotalSeconds:F1}s attempts={attempt} lastError={lastError?.Message}";
                logger?.Information(message);
                await log.WriteLineAsync(message);
            }

            await Task.Delay(pollInterval);
        }

        var errorMsg = $"Hub health endpoint did not become ready within {timeout}. Last error: {lastError?.Message}";
        logger?.Error(errorMsg);
        throw new TimeoutException(errorMsg);
    }

    /// <summary>
    ///     Wait for workers to register in the pool.
    ///     Checks the /diagnostics endpoint to count registered workers.
    ///     Returns true if expected number of workers registered, false otherwise.
    /// </summary>
    private static async Task<bool> WaitForWorkers(string hubUrl, int expectedWorkers, TimeSpan timeout,
        TextWriter log, Logger? logger)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var diagnosticsUrl = hubUrl.TrimEnd('/') + "/diagnostics";
        var sw = Stopwatch.StartNew();
        var attempt = 0;

        while (sw.Elapsed < timeout)
        {
            attempt++;
            try
            {
                var response = await http.GetAsync(diagnosticsUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    // Count registered workers by finding any "id":"..." patterns
                    // Workers can have IDs like "agenix-reportportal-worker-chromium-1" in decoupled deployment
                    var matches = Regex.Matches(json, @"""id""\s*:\s*""[^""]+""");
                    var registeredWorkers = matches.Count;

                    if (registeredWorkers >= expectedWorkers)
                    {
                        var message =
                            $"[IntegrationTests] ✅ Workers ready! ({registeredWorkers}/{expectedWorkers} registered after {sw.Elapsed.TotalSeconds:F1}s)";
                        logger?.Information(message);
                        await log.WriteLineAsync(message);

                        // Give workers a bit more time to fully initialize browser pools
                        var waitMessage = "[IntegrationTests]    Waiting 3 seconds for browser pools to stabilize...";
                        logger?.Information(waitMessage);
                        await log.WriteLineAsync(waitMessage);
                        await Task.Delay(TimeSpan.FromSeconds(3));

                        // Verify browser capacity is available
                        await VerifyBrowserCapacity(hubUrl, log, logger);

                        return true;
                    }

                    // Show progress every 10 seconds
                    if (attempt > 1 && attempt % 10 == 0)
                    {
                        var progressMsg =
                            $"[IntegrationTests] Waiting for workers... ({registeredWorkers}/{expectedWorkers} registered, {sw.Elapsed.TotalSeconds:F1}s elapsed)";
                        logger?.Information(progressMsg);
                        await log.WriteLineAsync(progressMsg);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue waiting
                if (attempt == 1)
                {
                    var errorMsg = $"[IntegrationTests] Error checking diagnostics: {ex.Message} (will retry)";
                    logger?.Warning(errorMsg);
                    await log.WriteLineAsync(errorMsg);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var timeoutMsg = $"[IntegrationTests] ⚠️  Workers not ready after {timeout}. Expected {expectedWorkers} workers.";
        logger?.Warning(timeoutMsg);
        await log.WriteLineAsync(timeoutMsg);
        return false;
    }

    /// <summary>
    ///     Verify that browser pool capacity is available.
    ///     Checks the /diagnostics endpoint for totalBrowsers or capacity field.
    /// </summary>
    private static async Task VerifyBrowserCapacity(string hubUrl, TextWriter log, Logger? logger)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            var diagnosticsUrl = hubUrl.TrimEnd('/') + "/diagnostics";
            var response = await http.GetAsync(diagnosticsUrl);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();

                // Try to find totalBrowsers field first
                var totalBrowsersMatch =
                    Regex.Match(json, @"""totalBrowsers""\s*:\s*(\d+)");
                if (totalBrowsersMatch.Success)
                {
                    var capacity = int.Parse(totalBrowsersMatch.Groups[1].Value);
                    if (capacity > 0)
                    {
                        var message = $"[IntegrationTests] ✅ Browser capacity available: {capacity} browser(s)";
                        logger?.Information(message);
                        await log.WriteLineAsync(message);
                        return;
                    }
                }

                // Fallback: try capacity field
                var capacityMatch = Regex.Match(json, @"""capacity""\s*:\s*(\d+)");
                if (capacityMatch.Success)
                {
                    var capacity = int.Parse(capacityMatch.Groups[1].Value);
                    if (capacity > 0)
                    {
                        var message = $"[IntegrationTests] ✅ Browser capacity available: {capacity} browser(s)";
                        logger?.Information(message);
                        await log.WriteLineAsync(message);
                        return;
                    }
                }

                var warningMsg = "[IntegrationTests] ⚠️  Warning: Could not determine browser capacity";
                logger?.Warning(warningMsg);
                await log.WriteLineAsync(warningMsg);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"[IntegrationTests] ⚠️  Warning: Failed to verify browser capacity: {ex.Message}";
            logger?.Warning(errorMsg);
            await log.WriteLineAsync(errorMsg);
        }
    }
}
