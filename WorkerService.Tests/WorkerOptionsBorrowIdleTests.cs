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

using NUnit.Framework;
using WorkerService.Infrastructure;

namespace WorkerService.Tests;

public class WorkerOptionsBorrowIdleTests
{
    [Test]
    public void FromEnvironment_DefaultBorrowIdle_IfEnvMissing()
    {
        var prevIdle = Environment.GetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS");
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        var prevWsHost = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST");
        var prevWsPort = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT");
        try
        {
            Environment.SetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS", null);
            Environment.SetEnvironmentVariable("POOL_CONFIG", "AppA:Chromium:Staging=1");
            Environment.SetEnvironmentVariable("NODE_REGION", "local");
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);

            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.BorrowIdleTimeoutSeconds, Is.EqualTo(120));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS", prevIdle);
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", prevWsHost);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", prevWsPort);
        }
    }

    [Test]
    public void FromEnvironment_ParsesAndClampsBorrowIdle()
    {
        var prevIdle = Environment.GetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS");
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        var prevWsHost = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST");
        var prevWsPort = Environment.GetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", "AppA:Chromium:Staging=1");
            Environment.SetEnvironmentVariable("NODE_REGION", "local");
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);

            Environment.SetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS", "300");
            var opts1 = WorkerOptions.FromEnvironment();
            Assert.That(opts1.BorrowIdleTimeoutSeconds, Is.EqualTo(300));

            Environment.SetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS", "9"); // below min -> clamp to 10
            var opts2 = WorkerOptions.FromEnvironment();
            Assert.That(opts2.BorrowIdleTimeoutSeconds, Is.EqualTo(10));

            Environment.SetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS", "100000"); // above max -> clamp to 86400
            var opts3 = WorkerOptions.FromEnvironment();
            Assert.That(opts3.BorrowIdleTimeoutSeconds, Is.EqualTo(86400));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HUB_IDLE_TIMEOUT_SECONDS", prevIdle);
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", prevWsHost);
            Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", prevWsPort);
        }
    }
}
