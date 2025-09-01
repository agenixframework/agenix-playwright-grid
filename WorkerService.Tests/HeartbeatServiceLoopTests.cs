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
using System.Collections.Concurrent;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

public class HeartbeatServiceLoopTests
{
    private static WorkerOptions CreateOptions()
    {
        return new WorkerOptions
        {
            NodeId = "node-loop",
            Labels =
                new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["region"] = "eu" },
            PoolConfig =
                new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AppA:Chromium:Staging"] = 1
                }
        };
    }

    [Test]
    public async Task HeartbeatLoopAsync_CancelsAndCompletes_WithoutThrowing()
    {
        var options = CreateOptions();
        var db = new Mock<IDatabase>(MockBehavior.Strict);

        // Setup all called operations to succeed
        db.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var svc = new HeartbeatService(options, db.Object);
        using var cts = new CancellationTokenSource();

        var loopTask = svc.HeartbeatLoopAsync(cts.Token);

        // Cancel quickly to prevent long waits
        cts.CancelAfter(50);

        var completed = await Task.WhenAny(loopTask, Task.Delay(2000));
        Assert.That(completed == loopTask, Is.True, "Heartbeat loop did not complete upon cancellation in time");
    }
}
