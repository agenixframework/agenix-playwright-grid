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
    public async Task Expired_node_prunes_inuse_and_mappings()
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var server = new Mock<IServer>(MockBehavior.Strict);
        mux.Setup(m => m.GetEndPoints(false)).Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
        mux.Setup(m => m.GetServer(It.IsAny<EndPoint>(), null)).Returns(server.Object);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HUB_NODE_TIMEOUT"] = "1",
            ["HUB_SWEEPER_EXPIRE"] = "true"
        }).Build();

        // nodes
        db.Setup(d => d.SetMembers("nodes", CommandFlags.None)).Returns(new RedisValue[] { "n1" });
        db.Setup(d => d.KeyExistsAsync("node_alive:n1", CommandFlags.None)).ReturnsAsync(false);
        db.Setup(d => d.HashGetAsync("node:n1", "LastSeen", CommandFlags.None))
            .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5).ToString("o"));

        // keys and lists
        server.Setup(s => s.Keys(It.IsAny<int>(), It.Is<RedisValue>(p => p.ToString() == "available:*"),
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new[] { (RedisKey)"available:App:Chromium:UAT" });
        server.Setup(s => s.Keys(It.IsAny<int>(), It.Is<RedisValue>(p => p.ToString() == "inuse:*"), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new[] { (RedisKey)"inuse:App:Chromium:UAT" });

        var inuseItem = new RedisValue("{\"nodeId\":\"n1\",\"browserId\":\"b2\"}");
        db.Setup(d => d.ListRangeAsync("available:App:Chromium:UAT", 0, -1, CommandFlags.None))
            .ReturnsAsync(Array.Empty<RedisValue>());
        db.Setup(d => d.ListRange("available:App:Chromium:UAT", 0, -1, CommandFlags.None))
            .Returns(Array.Empty<RedisValue>());
        db.Setup(d => d.ListRangeAsync("inuse:App:Chromium:UAT", 0, -1, CommandFlags.None))
            .ReturnsAsync(new[] { inuseItem });

        db.Setup(d => d.SetRemoveAsync("nodes", "n1", CommandFlags.None)).ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync("node:n1", CommandFlags.None)).ReturnsAsync(true);
        db.Setup(d => d.ListRemoveAsync("inuse:App:Chromium:UAT", inuseItem, 0, CommandFlags.None))
            .ReturnsAsync(1);
        db.Setup(d => d.KeyDeleteAsync("browser_run:b2", CommandFlags.None)).ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync("browser_test:b2", CommandFlags.None)).ReturnsAsync(true);

        var logger = new Moq.Mock<ILogger<NodeSweeperService>>(MockBehavior.Loose);
        var svc = new NodeSweeperService(db.Object, mux.Object, cfg, logger.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await svc.StartAsync(cts.Token);
        try { await Task.Delay(100, cts.Token); }
        catch { }

        await svc.StopAsync(CancellationToken.None);

        db.VerifyAll();
        mux.VerifyAll();
        server.VerifyAll();
    }
}
