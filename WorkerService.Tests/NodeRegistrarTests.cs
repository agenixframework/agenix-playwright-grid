using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOSTNAME", prevHost);
        }
    }
}
