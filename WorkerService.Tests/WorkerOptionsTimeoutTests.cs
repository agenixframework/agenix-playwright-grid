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

            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS",
                "3"); // below min -> clamp to 5
            var opts2 = WorkerOptions.FromEnvironment();
            Assert.That(opts2.SidecarReadyTimeoutSeconds, Is.EqualTo(5));

            Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS",
                "9999"); // above max -> clamp to 600
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
