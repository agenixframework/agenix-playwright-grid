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

using System.Collections.Concurrent;
using Moq;
using NUnit.Framework;
using WorkerService.Application.Ports;
using WorkerService.Infrastructure;
using WorkerService.Services;

namespace WorkerService.Tests;

public class NodeRegistrarTests
{
    private static WorkerOptions CreateOptions()
    {
        return new WorkerOptions
        {
            HubUrl = "http://hub:5000",
            NodeId = "node-xyz",
            NodeSecret = "secret",
            Labels =
                new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["region"] = "eu" },
            PoolConfig = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppA:Chromium:Staging"] = 2,
                ["AppB:Firefox:Prod"] = 1
            }
        };
    }

    [Test]
    public async Task RegisterAsync_UsesHostName_WhenPresent()
    {
        var prevHost = Environment.GetEnvironmentVariable("HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("HOSTNAME", "my-host");
            var options = CreateOptions();
            var hub = new Mock<IHubClient>(MockBehavior.Strict);
            hub.Setup(h => h.RegisterAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var registrar = new NodeRegistrar(hub.Object, options);
            await registrar.RegisterAsync();

            hub.Verify(h => h.RegisterAsync(
                "http://hub:5000",
                "secret",
                "node-xyz",
                It.Is<string>(s => s == "http://my-host:5000"),
                It.Is<IEnumerable<string>>(apps =>
                    apps.OrderBy(x => x).SequenceEqual(options.PoolConfig.Keys.OrderBy(x => x))),
                It.Is<int>(cap => cap == options.PoolConfig.Values.Sum()),
                It.Is<IReadOnlyDictionary<string, string>>(lbl => lbl["region"] == "eu"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOSTNAME", prevHost);
        }
    }

    [Test]
    public async Task RegisterAsync_FallsBackToNodeId_WhenHostNameMissing()
    {
        var prevHost = Environment.GetEnvironmentVariable("HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("HOSTNAME", null);
            var options = CreateOptions();
            var hub = new Mock<IHubClient>(MockBehavior.Strict);
            hub.Setup(h => h.RegisterAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var registrar = new NodeRegistrar(hub.Object, options);
            await registrar.RegisterAsync();

            hub.Verify(h => h.RegisterAsync(
                "http://hub:5000",
                "secret",
                "node-xyz",
                It.Is<string>(s => s == "http://node-xyz:5000"),
                It.IsAny<IEnumerable<string>>(),
                It.Is<int>(cap => cap == 3),
                It.Is<IReadOnlyDictionary<string, string>>(lbl => lbl["region"] == "eu"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOSTNAME", prevHost);
        }
    }
}
