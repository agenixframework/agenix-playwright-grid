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

public class BorrowTtlSweeperServiceTests
{
    [Test]
    [Ignore("Flaky due to background timing and RedisResult mocking; covered by integration flows.")]
    public async Task Cleans_up_when_idle_expired_and_deletes_borrow_idle()
    {
        // Arrange mocks
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var server = new Mock<IServer>(MockBehavior.Strict);
        mux.Setup(m => m.GetEndPoints(false)).Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
        mux.Setup(m => m.GetServer(It.IsAny<EndPoint>(), null)).Returns(server.Object);

        // Config: make sweeps fast
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HUB_BORROW_TTL_SWEEP_SECONDS"] = "1"
        }).Build();

        // One session present
        server.Setup(s => s.Keys(It.IsAny<int>(), It.Is<RedisValue>(p => p.ToString() == "session:*"),
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new[] { (RedisKey)"session:bid1" });

        // TTL and idle keys: TTL present, idle missing to simulate idle expiry
        db.Setup(d => d.KeyExistsAsync("borrow_ttl:bid1", CommandFlags.None)).ReturnsAsync(true);
        db.Setup(d => d.KeyExistsAsync("borrow_idle:bid1", CommandFlags.None)).ReturnsAsync(false);

        // Session hash has labelKey
        var sessionEntries = new[]
        {
            new HashEntry("labelKey", "App:Chromium:UAT"), new HashEntry("browserId", "bid1")
        };
        db.Setup(d => d.HashGetAllAsync("session:bid1", CommandFlags.None)).ReturnsAsync(sessionEntries);

        // Lua to move from inuse -> available returns null (no inuse match) leading to cleanup branch
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(),
                It.Is<RedisKey[]>(keys =>
                    keys.Length == 2 && keys[0] == (RedisKey)"inuse:App:Chromium:UAT" &&
                    keys[1] == (RedisKey)"available:App:Chromium:UAT"),
                It.Is<RedisValue[]>(argv => argv.Length == 1 && argv[0] == (RedisValue)"bid1"),
                CommandFlags.None))
            .Returns(Task.FromResult<RedisResult>(default!));


        // Expect deletions to succeed
        db.Setup(d => d.KeyDeleteAsync("borrow_ttl:bid1", It.IsAny<CommandFlags>())).ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync("borrow_idle:bid1", It.IsAny<CommandFlags>())).ReturnsAsync(true);
        db.Setup(d => d.KeyDeleteAsync("session:bid1", It.IsAny<CommandFlags>())).ReturnsAsync(true);

        var logger = new Mock<ILogger<BorrowTtlSweeperService>>(MockBehavior.Loose);
        var chunkedLogger = new ChunkedLogger<BorrowTtlSweeperService>(logger.Object);
        var svc = new BorrowTtlSweeperService(db.Object, mux.Object, cfg, logger.Object, chunkedLogger);

        // Act: run briefly
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(1200);
        await svc.StopAsync(CancellationToken.None);

        // Assert: idle-expired path should delete borrow_idle
        db.Verify(d => d.KeyDeleteAsync("borrow_idle:bid1", CommandFlags.None), Times.AtLeastOnce());

        mux.VerifyAll();
        server.VerifyAll();
    }
}
