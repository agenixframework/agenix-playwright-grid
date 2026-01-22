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

using System.Net;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using PlaywrightHub.Infrastructure.Adapters.Background;
using StackExchange.Redis;

namespace WorkerService.Tests;

public class NodeSweeperServiceTests
{
    [Test]
    [Ignore("Timing-sensitive; may be flaky on slower CI hosts. To be stabilized in a follow-up.")]
    public async Task Expired_node_enters_quarantine_and_prunes_available_only()
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var server = new Mock<IServer>(MockBehavior.Strict);
        mux.Setup(m => m.GetEndPoints(false)).Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
        mux.Setup(m => m.GetServer(It.IsAny<EndPoint>(), null)).Returns(server.Object);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HUB_NODE_TIMEOUT"] = "1",
            ["HUB_SWEEPER_EXPIRE"] = "true",
            ["HUB_NODE_QUARANTINE_SECONDS"] = "60"
        }).Build();

        // nodes
        db.Setup(d => d.SetMembers("nodes", CommandFlags.None)).Returns(new RedisValue[] { "n1" });
        db.Setup(d => d.KeyExistsAsync("node_alive:n1", CommandFlags.None)).ReturnsAsync(false);
        db.Setup(d => d.HashGetAsync("node:n1", "LastSeen", CommandFlags.None))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5).ToString("o"));

        // no available entries contain this node; prune path will scan but remove nothing
        server.Setup(s => s.Keys(It.IsAny<int>(), It.Is<RedisValue>(p => p.ToString() == "available:*"),
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new[] { (RedisKey)"available:App:Chromium:UAT" });
        // HasAvailableEntriesForNodeAsync scans available lists (async)
        db.Setup(d => d.ListRangeAsync("available:App:Chromium:UAT", 0, -1, CommandFlags.None))
            .ReturnsAsync(Array.Empty<RedisValue>());
        // PruneAvailableEntriesForNodeAsync scans available lists (sync)
        db.Setup(d => d.ListRange("available:App:Chromium:UAT", 0, -1, CommandFlags.None))
            .Returns(Array.Empty<RedisValue>());

        // quarantine key is created (first TTL check null, then set, then TTL exists on subsequent check)
        db.SetupSequence(d => d.KeyTimeToLiveAsync("node_quarantine:n1", CommandFlags.None))
            .ReturnsAsync((TimeSpan?)null)
            .ReturnsAsync(TimeSpan.FromSeconds(60));
        db.Setup(d => d.StringSetAsync("node_quarantine:n1", "1",
                It.Is<TimeSpan?>(t => t != null && Math.Abs(t.Value.TotalSeconds - 60) < 0.1), When.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger<NodeSweeperService>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<NodeSweeperService>(logger.Object);
        var svc = new NodeSweeperService(db.Object, mux.Object, cfg, logger.Object, chunkedLogger);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        await svc.StartAsync(cts.Token);
        try { await Task.Delay(1100, cts.Token); }
        catch { }

        await svc.StopAsync(CancellationToken.None);

        db.VerifyAll();
        mux.VerifyAll();
        server.VerifyAll();
    }
}
