using System;
using NUnit.Framework;
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class WorkerOptionsTimeoutTests
{
    [Test]
    public void FromEnvironment_DefaultTimeout_IfEnvMissing()
    {
        var prevTimeout = Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS");
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        try
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", null);
            Environment.SetEnvironmentVariable("POOL_CONFIG", null);
            Environment.SetEnvironmentVariable("NODE_REGION", null);

            var opts = WorkerOptions.FromEnvironment();

            Assert.That(opts.SidecarReadyTimeoutSeconds, Is.EqualTo(60));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", prevTimeout);
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
        }
    }

    [Test]
    public void FromEnvironment_ParsesAndClampsTimeout()
    {
        var prevTimeout = Environment.GetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS");
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", "AppA:Chromium:Staging=1");
            Environment.SetEnvironmentVariable("NODE_REGION", "local");

            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", "45");
            var opts1 = WorkerOptions.FromEnvironment();
            Assert.That(opts1.SidecarReadyTimeoutSeconds, Is.EqualTo(45));

            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", "3"); // below min -> clamp to 5
            var opts2 = WorkerOptions.FromEnvironment();
            Assert.That(opts2.SidecarReadyTimeoutSeconds, Is.EqualTo(5));

            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", "9999"); // above max -> clamp to 600
            var opts3 = WorkerOptions.FromEnvironment();
            Assert.That(opts3.SidecarReadyTimeoutSeconds, Is.EqualTo(600));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", prevTimeout);
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
        }
    }
}
