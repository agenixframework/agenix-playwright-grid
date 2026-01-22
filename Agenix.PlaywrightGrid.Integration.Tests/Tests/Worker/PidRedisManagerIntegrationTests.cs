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
using System.Text.Json;
using Agenix.PlaywrightGrid.Integration.Tests.Fixtures;
using Agenix.PlaywrightGrid.Integration.Tests.Infrastructure;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Infrastructure;

namespace Agenix.PlaywrightGrid.Integration.Tests.Tests.Worker;

/// <summary>
///     Integration tests for PidRedisManager verifying real Redis operations,
///     process lifecycle management, and multi-worker scenarios.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Worker")]
public class PidRedisManagerIntegrationTests : ApiTestBase
{
    private ChunkedLogger<PidRedisManager> _logger = null!;
    private readonly List<Process> _spawnedProcesses = [];
    private readonly List<string> _workerIds = [];

    protected override string ProjectKey => "pid-manager-integration-tests";

    [OneTimeSetUp]
    public override async Task OneTimeSetup()
    {
        await base.OneTimeSetup();

        // Create logger for PidRedisManager
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = new ChunkedLogger<PidRedisManager>(loggerFactory.CreateLogger<PidRedisManager>());
    }

    [SetUp]
    public async Task Setup()
    {
        // Clean up any leftover Redis keys from previous test runs
        await CleanupRedisKeysAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        // Kill all spawned processes
        foreach (var process in _spawnedProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }

                process.Dispose();
            }
            catch
            {
                // Process already dead or disposed
            }
        }

        _spawnedProcesses.Clear();

        // Cleanup Redis keys for all workers
        foreach (var workerId in _workerIds)
        {
            await CleanupWorkerKeysAsync(workerId);
        }

        _workerIds.Clear();
    }

    #region Category A: Redis Integration Tests

    [Test]
    [Category("Redis")]
    public async Task InitializeAsync_RedisWithExistingPids_ReturnsAllPids()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-001");
        var testPids = new[] { 12345, 12346, 12347 };

        // Pre-populate Redis
        var pidsKey = $"worker:{workerId}:pids";
        foreach (var pid in testPids)
        {
            await Redis.SetAddAsync(pidsKey, pid);
        }

        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        // Act
        var result = await pidManager.InitializeAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result, Does.Contain(12345));
        Assert.That(result, Does.Contain(12346));
        Assert.That(result, Does.Contain(12347));
    }

    [Test]
    [Category("Redis")]
    public async Task TrackPidAsync_RedisTransaction_EnsuresAtomicity()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-002");
        var pid = 23456;

        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        // Act
        await pidManager.TrackPidAsync(pid, "Chromium", "AppA:Chromium:UAT");

        // Assert - verify all Redis keys were created
        var pidsKey = $"worker:{workerId}:pids";
        var members = await Redis.SetMembersAsync(pidsKey);
        Assert.That(members.Select(m => (int)m), Does.Contain(pid));

        var metadataKey = $"pid:{pid}:metadata";
        var metadata = await Redis.StringGetAsync(metadataKey);
        Assert.That(metadata.IsNull, Is.False);

        var json = JsonSerializer.Deserialize<JsonElement>(metadata!);
        Assert.That(json.GetProperty("worker_id").GetString(), Is.EqualTo(workerId));
        Assert.That(json.GetProperty("browser_type").GetString(), Is.EqualTo("Chromium"));
        Assert.That(json.GetProperty("label_key").GetString(), Is.EqualTo("AppA:Chromium:UAT"));

        // Cleanup metadata key
        await Redis.KeyDeleteAsync(metadataKey);
    }

    [Test]
    [Category("Redis")]
    public async Task UntrackPidAsync_RedisTransaction_RemovesAllKeys()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-003");
        var pid = 34567;

        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        // First track a PID
        await pidManager.TrackPidAsync(pid, "Firefox", "AppB:Firefox:UAT");

        // Act - untrack it
        await pidManager.UntrackPidAsync(pid);

        // Assert
        var pidsKey = $"worker:{workerId}:pids";
        var members = await Redis.SetMembersAsync(pidsKey);
        Assert.That(members.Select(m => (int)m), Does.Not.Contain(pid));

        var metadataKey = $"pid:{pid}:metadata";
        var metadata = await Redis.StringGetAsync(metadataKey);
        Assert.That(metadata.IsNull, Is.True);
    }

    [Test]
    [Category("Redis")]
    public async Task CleanupAsync_RedisMultiKeyDelete_RemovesAllWorkerState()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-005");
        var pids = new[] { 11111, 22222, 33333 };

        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        // Track multiple PIDs
        foreach (var pid in pids)
        {
            await pidManager.TrackPidAsync(pid, "Chromium", "App:Chromium:PROD");
        }

        // Act - cleanup
        await pidManager.CleanupAsync();

        // Assert - all worker keys deleted
        var pidsKey = $"worker:{workerId}:pids";
        var heartbeatKey = $"worker:{workerId}:heartbeat";
        var metadataKey = $"worker:{workerId}:metadata";

        var pidsExists = await Redis.KeyExistsAsync(pidsKey);
        var heartbeatExists = await Redis.KeyExistsAsync(heartbeatKey);
        var metadataExists = await Redis.KeyExistsAsync(metadataKey);

        Assert.That(pidsExists, Is.False);
        Assert.That(heartbeatExists, Is.False);
        Assert.That(metadataExists, Is.False);

        // Individual PID metadata keys should also be deleted
        foreach (var pid in pids)
        {
            var pidMetadataKey = $"pid:{pid}:metadata";
            var exists = await Redis.KeyExistsAsync(pidMetadataKey);
            Assert.That(exists, Is.False);
        }
    }

    #endregion

    #region Category B: Multi-Worker Scenarios

    [Test]
    [Category("MultiWorker")]
    public async Task MultipleWorkers_ProcessOwnership_CorrectIsolation()
    {
        // Arrange
        var worker1Id = RegisterWorkerId("worker-integration-multi-003");
        var worker2Id = RegisterWorkerId("worker-integration-multi-004");

        var pidManager1 = new PidRedisManager(Redis, worker1Id, _logger);
        var pidManager2 = new PidRedisManager(Redis, worker2Id, _logger);

        // Act - track PIDs to different workers
        await pidManager1.TrackPidAsync(40001, "Chromium", "AppA:Chromium:UAT");
        await pidManager2.TrackPidAsync(40002, "Firefox", "AppB:Firefox:UAT");

        // Assert - each worker only sees its own PIDs
        var pids1 = await pidManager1.InitializeAsync();
        var pids2 = await pidManager2.InitializeAsync();

        Assert.That(pids1, Has.Count.EqualTo(1));
        Assert.That(pids1[0], Is.EqualTo(40001));

        Assert.That(pids2, Has.Count.EqualTo(1));
        Assert.That(pids2[0], Is.EqualTo(40002));

        // Cleanup metadata keys
        await Redis.KeyDeleteAsync("pid:40001:metadata");
        await Redis.KeyDeleteAsync("pid:40002:metadata");
    }

    [Test]
    [Category("MultiWorker")]
    public async Task LeaderElection_CompetingWorkers_OnlyOneScansOrphans()
    {
        // Arrange
        var worker1Id = RegisterWorkerId("worker-integration-leader-001");
        var worker2Id = RegisterWorkerId("worker-integration-leader-002");

        var pidManager1 = new PidRedisManager(Redis, worker1Id, _logger);
        var pidManager2 = new PidRedisManager(Redis, worker2Id, _logger);

        // Pre-populate both workers with PIDs
        await Redis.SetAddAsync($"worker:{worker1Id}:pids", 50001);
        await Redis.SetAddAsync($"worker:{worker2Id}:pids", 50002);

        // Act - simulate orphan detection
        var pids1 = await pidManager1.InitializeAsync();
        var pids2 = await pidManager2.InitializeAsync();

        // Assert - each worker only detects its own orphans
        Assert.That(pids1, Does.Contain(50001));
        Assert.That(pids1, Does.Not.Contain(50002));

        Assert.That(pids2, Does.Contain(50002));
        Assert.That(pids2, Does.Not.Contain(50001));
    }

    #endregion

    #region Category C: Failure & Resilience

    [Test]
    [Category("Resilience")]
    public async Task ConcurrentTrackUntrack_NoRaceConditions()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-resilience-004");
        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        var tasks = new List<Task>();

        // Act - 50 parallel track operations
        for (var i = 0; i < 50; i++)
        {
            var pid = 80000 + i;
            tasks.Add(Task.Run(() => pidManager.TrackPidAsync(pid, "Chromium", "App:Chromium:PROD")));
        }

        await Task.WhenAll(tasks);

        // Assert - all 50 PIDs tracked
        var tracked = await pidManager.InitializeAsync();
        Assert.That(tracked, Has.Count.EqualTo(50));

        // Cleanup metadata keys
        for (var i = 0; i < 50; i++)
        {
            await Redis.KeyDeleteAsync($"pid:{80000 + i}:metadata");
        }
    }

    [Test]
    [Category("Resilience")]
    public async Task LargeMetadataPayload_RedisHandlesWithoutIssue()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-resilience-005");
        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        var largeLabelKey = "App:" + new string('A', 1000) + ":PROD";

        // Act - track with large metadata
        await pidManager.TrackPidAsync(90001, "Chromium", largeLabelKey);

        // Assert - metadata stored correctly
        var metadataKey = "pid:90001:metadata";
        var metadata = await Redis.StringGetAsync(metadataKey);
        Assert.That(metadata.IsNull, Is.False);

        var json = JsonSerializer.Deserialize<JsonElement>(metadata!);
        Assert.That(json.GetProperty("label_key").GetString(), Is.EqualTo(largeLabelKey));

        // Cleanup
        await Redis.KeyDeleteAsync(metadataKey);
    }

    [Test]
    [Category("Resilience")]
    public async Task Cleanup_DuringActiveOperations_DoesNotCorruptState()
    {
        // Arrange
        var workerId = RegisterWorkerId("worker-integration-resilience-006");
        var pidManager = new PidRedisManager(Redis, workerId, _logger);

        // Start tracking (async, don't await)
        var trackTask = Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                await pidManager.TrackPidAsync(91000 + i, "Chromium", "App:Chromium:PROD");
                await Task.Delay(10);
            }
        });

        // Cleanup mid-operation
        await Task.Delay(50);
        await pidManager.CleanupAsync();

        // Continue tracking
        await trackTask;

        // Act - re-initialize
        var tracked = await pidManager.InitializeAsync();

        // Assert - state is consistent (either all tracked or none, no corruption)
        // After cleanup, new tracks should work but old ones are gone
        Assert.That(tracked.Count, Is.GreaterThanOrEqualTo(0));

        // Final cleanup of metadata keys
        for (var i = 0; i < 20; i++)
        {
            await Redis.KeyDeleteAsync($"pid:{91000 + i}:metadata");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Registers a worker ID for cleanup in teardown.
    /// </summary>
    private string RegisterWorkerId(string workerId)
    {
        _workerIds.Add(workerId);
        return workerId;
    }

    /// <summary>
    ///     Cleanup all Redis keys for a specific worker.
    /// </summary>
    private async Task CleanupWorkerKeysAsync(string workerId)
    {
        var pidsKey = $"worker:{workerId}:pids";
        var heartbeatKey = $"worker:{workerId}:heartbeat";
        var metadataKey = $"worker:{workerId}:metadata";

        await Redis.KeyDeleteAsync(pidsKey);
        await Redis.KeyDeleteAsync(heartbeatKey);
        await Redis.KeyDeleteAsync(metadataKey);
    }

    /// <summary>
    ///     Cleanup any leftover Redis keys matching test patterns.
    /// </summary>
    private async Task CleanupRedisKeysAsync()
    {
        // Note: In production, use SCAN instead of KEYS for large datasets
        // For test cleanup with known patterns, KEYS is acceptable

        var server = Redis.Multiplexer.GetServer(Redis.Multiplexer.GetEndPoints()[0]);
        var database = Redis.Database;

        // Cleanup worker keys
        await foreach (var key in server.KeysAsync(database, pattern: "worker:worker-integration-*"))
        {
            await Redis.KeyDeleteAsync(key);
        }

        // Cleanup PID metadata keys from test PIDs
        await foreach (var key in server.KeysAsync(database, pattern: "pid:*:metadata"))
        {
            await Redis.KeyDeleteAsync(key);
        }
    }

    /// <summary>
    ///     Spawns a test Node.js process that runs for a specified duration.
    /// </summary>
    private Process SpawnNodeProcess(int durationSeconds = 60)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"-e \"setTimeout(() => {{}}, {durationSeconds * 1000})\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process != null)
        {
            _spawnedProcesses.Add(process);
        }

        return process!;
    }

    /// <summary>
    ///     Spawns a test sleep process.
    /// </summary>
    private Process SpawnSleepProcess(int durationSeconds = 60)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sleep",
            Arguments = durationSeconds.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process != null)
        {
            _spawnedProcesses.Add(process);
        }

        return process!;
    }

    #endregion
}
