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

using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using NUnit.Framework;

namespace GridTests;

[SetUpFixture]
public sealed class TestEnvironment
{
    private TestcontainersContainer? _hub;
    private IDockerNetwork? _network;
    private TestcontainersContainer? _redis;
    private TestcontainersContainer? _postgres;
    private TestcontainersContainer? _workerChromium;
    private TestcontainersContainer? _workerFxWk;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        var skip = Environment.GetEnvironmentVariable("GRID_TESTS_SKIP_CONTAINERS");
        if (string.Equals(skip, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase))
        {
            await TestContext.Progress.WriteLineAsync(
                "[GridTests] Skipping Test-containers per GRID_TESTS_SKIP_CONTAINERS.");
            return;
        }

        // Local mode: use already running local grid (e.g., docker-compose up) without managing containers
        var useLocal = IsTruthy(Environment.GetEnvironmentVariable("GRID_TESTS_USE_LOCAL"))
                       || IsTruthy(TestContext.Parameters.Get("GRID_TESTS_USE_LOCAL", null));
        if (useLocal)
        {
            var hubUrl = Environment.GetEnvironmentVariable("HUB_URL");
            if (string.IsNullOrWhiteSpace(hubUrl))
            {
                hubUrl = "http://127.0.0.1:5100";
            }

            Environment.SetEnvironmentVariable("HUB_URL", hubUrl);

            var runnerSecret = Environment.GetEnvironmentVariable("HUB_RUNNER_SECRET");
            if (string.IsNullOrWhiteSpace(runnerSecret))
            {
                runnerSecret = "runner-secret";
            }

            Environment.SetEnvironmentVariable("HUB_RUNNER_SECRET", runnerSecret);

            var healthTimeoutSecondsStrLocal = Environment.GetEnvironmentVariable("GRID_TESTS_HEALTH_TIMEOUT_SECONDS");
            var healthTimeoutSecondsLocal = 120;
            if (!string.IsNullOrWhiteSpace(healthTimeoutSecondsStrLocal) &&
                int.TryParse(healthTimeoutSecondsStrLocal, out var parsedLocal) && parsedLocal > 0)
            {
                healthTimeoutSecondsLocal = parsedLocal;
            }

            await TestContext.Progress.WriteLineAsync(
                $"[GridTests] Using local grid at {hubUrl} per GRID_TESTS_USE_LOCAL (env or test parameter).");
            await WaitForHubHealth(hubUrl.TrimEnd('/') + "/health", TimeSpan.FromSeconds(healthTimeoutSecondsLocal),
                TestContext.Progress);
            return;
        }

        // Preflight: auto-skip if Docker is not available/reachable
        if (!IsDockerAvailable())
        {
            await TestContext.Progress.WriteLineAsync(
                "[GridTests] Docker engine not detected. Skipping containerized test setup. Set GRID_TESTS_SKIP_CONTAINERS=1 to skip explicitly.");
            Assert.Inconclusive("Docker engine not available for Testcontainers.");
            return;
        }

        try
        {
            // Disable Testcontainers' resource reaper (Ryuk) to avoid conflicts with orphaned/stopped reaper containers.
            // We perform explicit teardown in [OneTimeTearDown], so this is safe for CI/local runs.
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
            try { TestcontainersSettings.ResourceReaperEnabled = false; }
            catch
            {
                /* fallback to env var only */
            }

            await TestContext.Progress.WriteLineAsync("[GridTests] Disabled Testcontainers Resource Reaper (Ryuk)");

            // Pre-flight cleanup of orphaned containers (can be skipped via GRID_TESTS_SKIP_CLEANUP=1)
            var skipCleanup = IsTruthy(Environment.GetEnvironmentVariable("GRID_TESTS_SKIP_CLEANUP"));
            if (!skipCleanup)
            {
                await CleanupGridtestsContainersAsync(TestContext.Progress);
            }
            else
            {
                await TestContext.Progress.WriteLineAsync(
                    "[GridTests] Skipping pre-flight cleanup per GRID_TESTS_SKIP_CLEANUP.");
            }

            var root = FindFileUpwards("docker-compose.yml");
            if (root is null)
            {
                throw new FileNotFoundException("Cannot find docker-compose.yml from test working directory.");
            }

            root = Path.GetDirectoryName(root);

            var hubDir = Path.Combine(root ?? throw new InvalidOperationException(), "hub");
            var workerDir = Path.Combine(root, "worker");
            if (!Directory.Exists(hubDir) || !Directory.Exists(workerDir))
            {
                throw new DirectoryNotFoundException("Expected hub/ and worker/ directories next to compose file.");
            }

            // Build or reuse images with stable tags for speed
            var hubImageName = "gridtests/hub:dev";
            var workerImageName = "gridtests/worker:dev";

            // Backend selection for results store used by Hub during tests
            var resultsBackend = (Environment.GetEnvironmentVariable("GRID_TESTS_RESULTS_BACKEND") ?? "redis").Trim();
            var pgImage = Environment.GetEnvironmentVariable("GRID_TESTS_POSTGRES_IMAGE") ?? "postgres:16-alpine";
            var pgDb = Environment.GetEnvironmentVariable("GRID_TESTS_POSTGRES_DB") ?? "playwrightgrid";
            var pgUser = Environment.GetEnvironmentVariable("GRID_TESTS_POSTGRES_USER") ?? "postgres";
            var pgPassword = Environment.GetEnvironmentVariable("GRID_TESTS_POSTGRES_PASSWORD") ?? "postgres";

            var forceBuildEnv = Environment.GetEnvironmentVariable("GRID_TESTS_FORCE_BUILD");
            var forceBuild = string.IsNullOrWhiteSpace(forceBuildEnv) || IsTruthy(forceBuildEnv);

            var dockerfileHub = Path.Combine(hubDir, "Dockerfile");
            var dockerfileWorker = Path.Combine(workerDir, "Dockerfile");

            // Build using repository root as context to include shared projects (e.g., Agenix.PlaywrightGrid.Domain)
            await EnsureImageAsync(hubImageName, root!, dockerfileHub, TestContext.Progress, forceBuild);
            await EnsureImageAsync(workerImageName, root!, dockerfileWorker, TestContext.Progress, forceBuild);

            // Reuse mode: stable names to allow fast subsequent runs
            var reuse = IsTruthy(Environment.GetEnvironmentVariable("GRID_TESTS_REUSE"));
            var nameSuffix = reuse ? string.Empty : $"-{Guid.NewGuid():N}";

            // If reuse requested and all containers appear to be running, skip setup
            if (reuse)
            {
                var runningRedis = await IsContainerRunningAsync("gridtests-redis");
                var runningHub = await IsContainerRunningAsync("gridtests-hub");
                var runningW1 = await IsContainerRunningAsync("gridtests-worker1");
                var runningW3 = await IsContainerRunningAsync("gridtests-worker3");
                var needPostgres = string.Equals(resultsBackend, "postgres", StringComparison.OrdinalIgnoreCase);
                var runningPg = !needPostgres || await IsContainerRunningAsync("gridtests-postgres");

                if (runningRedis && runningHub && runningW1 && runningW3 && runningPg)
                {
                    await TestContext.Progress.WriteLineAsync(
                        "[GridTests] Reuse mode: Found running containers, skipping setup.");
                    Environment.SetEnvironmentVariable("HUB_URL", "http://127.0.0.1:5100");
                    Environment.SetEnvironmentVariable("HUB_RUNNER_SECRET", "runner-secret");

                    var healthTimeoutSecondsStr2 =
                        Environment.GetEnvironmentVariable("GRID_TESTS_HEALTH_TIMEOUT_SECONDS");
                    var healthTimeoutSeconds2 = 60;
                    if (!string.IsNullOrWhiteSpace(healthTimeoutSecondsStr2) &&
                        int.TryParse(healthTimeoutSecondsStr2, out var parsed2) && parsed2 > 0)
                    {
                        healthTimeoutSeconds2 = parsed2;
                    }

                    await WaitForHubHealth("http://127.0.0.1:5100/health", TimeSpan.FromSeconds(healthTimeoutSeconds2),
                        TestContext.Progress);
                    return;
                }
            }

            // Create isolated network for inter-container communication
            _network = new TestcontainersNetworkBuilder()
                .WithName($"gridtests-net{nameSuffix}")
                .Build();
            try { await _network.CreateAsync(); }
            catch
            {
                /* allow reuse if already exists */
            }

            // Start Redis (internal network only)
            _redis = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage("redis:7")
                .WithName($"gridtests-redis{nameSuffix}")
                .WithNetwork(_network)
                .WithHostname("redis")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
                .WithOutputConsumer(Consume.RedirectStdoutAndStderrToStream(
                    new ProgressStream(TestContext.Progress, "[redis][stdout] "),
                    new ProgressStream(TestContext.Progress, "[redis][stderr] ")
                ))
                .Build();
            await _redis.StartAsync();

            // Optionally start Postgres if requested as results backend
            var usePostgres = string.Equals(resultsBackend, "postgres", StringComparison.OrdinalIgnoreCase);
            if (usePostgres)
            {
                _postgres = new TestcontainersBuilder<TestcontainersContainer>()
                    .WithImage(pgImage)
                    .WithName($"gridtests-postgres{nameSuffix}")
                    .WithNetwork(_network)
                    .WithHostname("postgres")
                    .WithEnvironment("POSTGRES_PASSWORD", pgPassword)
                    .WithEnvironment("POSTGRES_USER", pgUser)
                    .WithEnvironment("POSTGRES_DB", pgDb)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                    .WithOutputConsumer(Consume.RedirectStdoutAndStderrToStream(
                        new ProgressStream(TestContext.Progress, "[postgres][stdout] "),
                        new ProgressStream(TestContext.Progress, "[postgres][stderr] ")
                    ))
                    .Build();
                await _postgres.StartAsync();
            }

            // Start Hub (use Redis via network alias and optional Postgres)
            var hubBuilder = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage(hubImageName)
                .WithName($"gridtests-hub{nameSuffix}")
                .WithNetwork(_network)
                .WithHostname("hub")
                .WithPortBinding(5100, 5000)
                .WithEnvironment("REDIS_URL", "redis:6379")
                .WithEnvironment("HUB_NODE_SECRET", "node-secret")
                .WithEnvironment("HUB_RUNNER_SECRET", "runner-secret")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5000))
                .WithOutputConsumer(Consume.RedirectStdoutAndStderrToStream(
                    new ProgressStream(TestContext.Progress, "[hub][stdout] "),
                    new ProgressStream(TestContext.Progress, "[hub][stderr] ")
                ));

            if (usePostgres)
            {
                var pgConn = $"Host=postgres;Port=5432;Username={pgUser};Password={pgPassword};Database={pgDb}";
                hubBuilder = hubBuilder
                    .WithEnvironment("HUB_RESULTS_BACKEND", "postgres")
                    .WithEnvironment("HUB_RESULTS_POSTGRES", pgConn);
            }

            _hub = hubBuilder.Build();
            await _hub.StartAsync();

            // Prepare Workers (start in parallel after Hub is up)
            _workerChromium = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage(workerImageName)
                .WithName($"gridtests-worker1{nameSuffix}")
                .WithNetwork(_network)
                .WithHostname("worker1")
                .WithPortBinding(5200, 5000)
                .WithEnvironment("HUB_URL", "http://hub:5000")
                .WithEnvironment("REDIS_URL", "redis:6379")
                .WithEnvironment("NODE_ID", "worker1")
                .WithEnvironment("NODE_SECRET", "node-secret")
                .WithEnvironment("NODE_NODE_SECRET", "node-node-secret")
                .WithEnvironment("POOL_CONFIG", "AppB:Chromium:UAT=2")
                .WithEnvironment("PUBLIC_WS_HOST", "127.0.0.1")
                .WithEnvironment("PUBLIC_WS_PORT", "5200")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5000))
                .WithOutputConsumer(Consume.RedirectStdoutAndStderrToStream(
                    new ProgressStream(TestContext.Progress, "[worker1][stdout] "),
                    new ProgressStream(TestContext.Progress, "[worker1][stderr] ")
                ))
                .Build();

            _workerFxWk = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage(workerImageName)
                .WithName($"gridtests-worker3{nameSuffix}")
                .WithNetwork(_network)
                .WithHostname("worker3")
                .WithPortBinding(5202, 5000)
                .WithEnvironment("HUB_URL", "http://hub:5000")
                .WithEnvironment("REDIS_URL", "redis:6379")
                .WithEnvironment("NODE_ID", "worker3")
                .WithEnvironment("NODE_SECRET", "node-secret")
                .WithEnvironment("NODE_NODE_SECRET", "node-node-secret")
                .WithEnvironment("POOL_CONFIG", "AppB:Firefox:UAT=1,AppB:Webkit:UAT=1")
                .WithEnvironment("PUBLIC_WS_HOST", "127.0.0.1")
                .WithEnvironment("PUBLIC_WS_PORT", "5202")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5000))
                .WithOutputConsumer(Consume.RedirectStdoutAndStderrToStream(
                    new ProgressStream(TestContext.Progress, "[worker3][stdout] "),
                    new ProgressStream(TestContext.Progress, "[worker3][stderr] ")
                ))
                .Build();

            await Task.WhenAll(_workerChromium.StartAsync(), _workerFxWk.StartAsync());

            // Export vars for tests
            Environment.SetEnvironmentVariable("HUB_URL", "http://127.0.0.1:5100");
            Environment.SetEnvironmentVariable("HUB_RUNNER_SECRET", "runner-secret");

            // Ensure hub is healthy from host (configurable timeout)
            var healthTimeoutSecondsStr = Environment.GetEnvironmentVariable("GRID_TESTS_HEALTH_TIMEOUT_SECONDS");
            var healthTimeoutSeconds = 120;
            if (!string.IsNullOrWhiteSpace(healthTimeoutSecondsStr) &&
                int.TryParse(healthTimeoutSecondsStr, out var parsed) && parsed > 0)
            {
                healthTimeoutSeconds = parsed;
            }

            await WaitForHubHealth("http://127.0.0.1:5100/health", TimeSpan.FromSeconds(healthTimeoutSeconds),
                TestContext.Progress);

            await TestContext.Progress.WriteLineAsync("[GridTests] Testcontainers environment is ready.");
        }
        catch (Exception ex)
        {
            await TestContext.Progress.WriteLineAsync($"[GridTests] Containerized setup failed: {ex.Message}");

            // Attempt automatic fallback to local hub if reachable
            try
            {
                var hubUrl = Environment.GetEnvironmentVariable("HUB_URL");
                if (string.IsNullOrWhiteSpace(hubUrl))
                {
                    hubUrl = "http://127.0.0.1:5100";
                }

                Environment.SetEnvironmentVariable("HUB_URL", hubUrl);

                var runnerSecret = Environment.GetEnvironmentVariable("HUB_RUNNER_SECRET");
                if (string.IsNullOrWhiteSpace(runnerSecret))
                {
                    runnerSecret = "runner-secret";
                }

                Environment.SetEnvironmentVariable("HUB_RUNNER_SECRET", runnerSecret);

                var healthTimeoutSecondsStrLocal =
                    Environment.GetEnvironmentVariable("GRID_TESTS_HEALTH_TIMEOUT_SECONDS");
                var healthTimeoutSecondsLocal = 60;
                if (!string.IsNullOrWhiteSpace(healthTimeoutSecondsStrLocal) &&
                    int.TryParse(healthTimeoutSecondsStrLocal, out var parsedLocal) && parsedLocal > 0)
                {
                    healthTimeoutSecondsLocal = parsedLocal;
                }

                await TestContext.Progress.WriteLineAsync(
                    $"[GridTests] Falling back to local hub at {hubUrl} after container setup failure...");
                await WaitForHubHealth(hubUrl.TrimEnd('/') + "/health", TimeSpan.FromSeconds(healthTimeoutSecondsLocal),
                    TestContext.Progress);
                await TestContext.Progress.WriteLineAsync(
                    "[GridTests] Local hub is healthy. Proceeding with tests in local mode.");
                return;
            }
            catch (Exception ex2)
            {
                await TestContext.Progress.WriteLineAsync($"[GridTests] Local fallback not available: {ex2.Message}");
            }

            Assert.Inconclusive(
                $"Testcontainers environment not available: {ex.Message}. Set GRID_TESTS_SKIP_CONTAINERS=1 or run docker-compose and set GRID_TESTS_USE_LOCAL=1.");
        }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        var reuse = IsTruthy(Environment.GetEnvironmentVariable("GRID_TESTS_REUSE"));
        if (reuse)
        {
            await TestContext.Progress.WriteLineAsync(
                "[GridTests] Reuse mode enabled: skipping teardown to speed subsequent runs.");
            return;
        }

        await StopAsync(_workerFxWk);
        await StopAsync(_workerChromium);
        await StopAsync(_hub);
        await StopAsync(_postgres);
        await StopAsync(_redis);

        // Delete network after containers are stopped
        if (_network != null)
        {
            try { await _network.DeleteAsync(); }
            catch
            {
                /* ignore */
            }

            _network = null;
        }

        async Task StopAsync(TestcontainersContainer? c)
        {
            if (c == null)
            {
                return;
            }

            try
            {
                await c.StopAsync();
                await c.DisposeAsync();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static async Task CleanupGridtestsContainersAsync(TextWriter log)
    {
        try
        {
            await log.WriteLineAsync("[GridTests] Pre-flight: cleaning up orphaned 'gridtests' containers...");

            var docker = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
            var listArgs =
                "ps -a --filter name=^/gridtests --filter status=exited --filter status=dead --filter status=created -q";
            var (code, outText, errText) = await RunCommandAsync(docker, listArgs, 20000);
            if (code != 0)
            {
                await log.WriteLineAsync(
                    $"[GridTests] 'docker ps' failed (ignored): exit={code} err={errText?.Trim()}");
                return;
            }

            var ids = (outText ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                await log.WriteLineAsync("[GridTests] No orphaned 'gridtests' containers found.");
                return;
            }

            const int batchSize = 50;
            for (var i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var rmArgs = "rm -f " + string.Join(" ", batch);
                var (rmCode, rmOut, rmErr) = await RunCommandAsync(docker, rmArgs, 60000);
                if (rmCode == 0)
                {
                    await log.WriteLineAsync(
                        $"[GridTests] Removed {batch.Count} container(s): {string.Join(",", batch.Select(x => x[..Math.Min(12, x.Length)]))}");
                }
                else
                {
                    await log.WriteLineAsync(
                        $"[GridTests] 'docker rm' failed for batch (ignored): exit={rmCode} err={rmErr?.Trim()}");
                }
            }

            await log.WriteLineAsync("[GridTests] Pre-flight cleanup complete.");
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[GridTests] Pre-flight cleanup failed (ignored): {ex.Message}");
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCommandAsync(string fileName,
        string arguments, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var outTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    outTcs.TrySetResult(true);
                }
                else
                {
                    stdout.AppendLine(e.Data);
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    errTcs.TrySetResult(true);
                }
                else
                {
                    stderr.AppendLine(e.Data);
                }
            };

            if (!proc.Start())
            {
                return (-1, "", "Failed to start process");
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var waitTask = proc.WaitForExitAsync();
            var timeoutTask = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(waitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                    }
                }
                catch
                {
                    /* ignore */
                }

                return (-1, stdout.ToString(), "Timeout");
            }

            // Ensure streams are consumed
            try
            {
                await Task.WhenAll(outTcs.Task, errTcs.Task)
                    .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs / 10)));
            }
            catch
            {
                /* ignore */
            }

            return (proc.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private static async Task WaitForHubHealth(string url, TimeSpan timeout, TextWriter log)
    {
        var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(10);
        var start = DateTime.UtcNow;
        Exception? last = null;
        while (DateTime.UtcNow - start < timeout)
        {
            try
            {
                var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    return;
                }

                last = new Exception($"Status {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            await log.WriteLineAsync("[GridTests] Waiting for hub /health...");
        }

        throw new TimeoutException(
            $"Hub health endpoint did not become ready in {timeout}. Last error: {last?.Message}");
    }

    private static string? FindFileUpwards(string fileName)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
            if (!string.IsNullOrWhiteSpace(dockerHost))
            {
                if (dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
                {
                    var path = dockerHost["unix://".Length..];
                    return File.Exists(path);
                }

                if (dockerHost.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
                {
                    return CanConnectNamedPipe("docker_engine", TimeSpan.FromMilliseconds(250));
                }

                if (dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ||
                    dockerHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    dockerHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var normalized = dockerHost.Replace("tcp://", "http://", StringComparison.OrdinalIgnoreCase);
                    var uri = new Uri(normalized);
                    return CanConnectTcp(uri.Host, uri.Port, TimeSpan.FromMilliseconds(300));
                }
            }

            // No DOCKER_HOST provided: use platform defaults
            if (OperatingSystem.IsWindows())
            {
                if (CanConnectNamedPipe("docker_engine", TimeSpan.FromMilliseconds(250)))
                {
                    return true;
                }

                if (CanConnectTcp("127.0.0.1", 2375, TimeSpan.FromMilliseconds(150)))
                {
                    return true;
                }

                return false;
            }

            const string defaultSock = "/var/run/docker.sock";
            if (File.Exists(defaultSock))
            {
                return true;
            }

            if (CanConnectTcp("127.0.0.1", 2375, TimeSpan.FromMilliseconds(150)))
            {
                return true;
            }

            if (CanConnectTcp("127.0.0.1", 2376, TimeSpan.FromMilliseconds(150)))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanConnectTcp(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            var success = ar.AsyncWaitHandle.WaitOne(timeout);
            if (!success)
            {
                return false;
            }

            client.EndConnect(ar);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanConnectNamedPipe(string pipeName, TimeSpan timeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect((int)timeout.TotalMilliseconds);
            return pipe.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    // Helpers
    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        return v.Equals("1", StringComparison.OrdinalIgnoreCase)
               || v.Equals("true", StringComparison.OrdinalIgnoreCase)
               || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || v.Equals("y", StringComparison.OrdinalIgnoreCase)
               || v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsureImageAsync(string imageName, string contextDir, string dockerfilePath,
        TextWriter log, bool forceBuild)
    {
        var docker = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
        try
        {
            if (!Directory.Exists(contextDir))
            {
                throw new DirectoryNotFoundException($"Build context directory not found: {contextDir}");
            }

            if (!File.Exists(dockerfilePath))
            {
                throw new FileNotFoundException($"Dockerfile not found: {dockerfilePath}");
            }

            if (!forceBuild)
            {
                var (code, outText, _) = await RunCommandAsync(docker, $"image inspect {imageName} -f {{.Id}}", 20000);
                if (code == 0 && !string.IsNullOrWhiteSpace(outText))
                {
                    await log.WriteLineAsync($"[GridTests] Using cached image {imageName} ({outText.Trim()})");
                    return;
                }
            }

            var quotedContext = contextDir.Contains(' ') ? $"\"{contextDir}\"" : contextDir;
            var quotedDockerfile = dockerfilePath.Contains(' ') ? $"\"{dockerfilePath}\"" : dockerfilePath;
            await log.WriteLineAsync(
                $"[GridTests] Building image {imageName} with {dockerfilePath} (context {contextDir})...");
            var (buildCode, buildOut, buildErr) =
                await RunCommandAsync(docker, $"build -t {imageName} -f {quotedDockerfile} {quotedContext}",
                    60 * 60 * 1000);
            if (buildCode != 0)
            {
                await log.WriteLineAsync(
                    $"[GridTests] 'docker build' failed: exit={buildCode} err={(buildErr ?? string.Empty).Trim()}");
                throw new InvalidOperationException($"docker build failed for {imageName} (exit {buildCode})");
            }

            // Verify the image exists and is tagged
            var (inspectCode, inspectOut, _) =
                await RunCommandAsync(docker, $"image inspect {imageName} -f {{.Id}}", 20000);
            if (inspectCode == 0 && !string.IsNullOrWhiteSpace(inspectOut))
            {
                await log.WriteLineAsync($"[GridTests] Built image {imageName} ({inspectOut.Trim()})");
            }
            else
            {
                await log.WriteLineAsync(
                    $"[GridTests] Warning: built image {imageName} but could not verify with 'docker image inspect'.");
            }
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[GridTests] Image build for {imageName} failed: {ex.Message}");
            throw;
        }
    }

    private static async Task<bool> IsContainerRunningAsync(string containerName)
    {
        try
        {
            var docker = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
            var (code, outText, _) = await RunCommandAsync(docker,
                $"ps --filter name=^/{containerName}$ --filter status=running -q", 15000);
            if (code != 0)
            {
                return false;
            }

            return (outText ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    // Stream adapter to forward container stdout/stderr to NUnit's TestContext.Progress.
    private sealed class ProgressStream(TextWriter writer, string prefix = "") : Stream
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly object _gate = new();
        private readonly string _prefix = prefix ?? string.Empty;
        private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer is null || count <= 0)
            {
                return;
            }

            var charCount = _decoder.GetCharCount(buffer, offset, count, false);
            if (charCount == 0)
            {
                return;
            }

            var chars = new char[charCount];
            _decoder.GetChars(buffer, offset, count, chars, 0, false);

            lock (_gate)
            {
                _writer.WriteLine(_prefix + new string(chars));
            }
        }
    }
}
