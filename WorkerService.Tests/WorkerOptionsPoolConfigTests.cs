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

public class WorkerOptionsPoolConfigTests
{
    [SetUp]
    public void ClearEnv()
    {
        // Ensure a predictable base
        Environment.SetEnvironmentVariable("NODE_REGION", "local");
        Environment.SetEnvironmentVariable("PLAYWRIGHT_SIDECAR_READY_TIMEOUT_SECONDS", null);
        Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_HOST", null);
        Environment.SetEnvironmentVariable("AGENIX_WORKER_PUBLIC_WS_PORT", null);
    }

    [Test]
    public void Parses_Multiple_Entries_With_Trim_And_BackCompat_Invalid_Keys()
    {
        var prev = Environment.GetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG");
        try
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG",
                "  AppA:Chromium:UAT = 2 , X = 1 , AppB:Firefox:UAT= 5  ");
            var opts = WorkerOptions.FromEnvironment();

            Assert.That(opts.PoolConfig, Does.ContainKey("AppA:Chromium:UAT"));
            Assert.That(opts.PoolConfig["AppA:Chromium:UAT"], Is.EqualTo(2));

            // Back-compat: un-parseable key should be preserved as-is
            Assert.That(opts.PoolConfig, Does.ContainKey("X"));
            Assert.That(opts.PoolConfig["X"], Is.EqualTo(1));

            Assert.That(opts.PoolConfig, Does.ContainKey("AppB:Firefox:UAT"));
            Assert.That(opts.PoolConfig["AppB:Firefox:UAT"], Is.EqualTo(5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", prev);
        }
    }

    [Test]
    public void Duplicate_Keys_Last_Wins()
    {
        var prev = Environment.GetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG");
        try
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", "AppA:Chromium:UAT=1,AppA:Chromium:UAT=3");
            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.PoolConfig["AppA:Chromium:UAT"], Is.EqualTo(3));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", prev);
        }
    }

    [Test]
    public void Empty_Or_Invalid_Entries_Fall_Back_To_Default()
    {
        var prev = Environment.GetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG");
        try
        {
            // Nothing valid -> default applied inside FromEnvironment
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", " , ,  ");
            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.PoolConfig.Count, Is.EqualTo(1));
            Assert.That(opts.PoolConfig.ContainsKey("AppA:Chromium:Staging"), Is.True);
            Assert.That(opts.PoolConfig["AppA:Chromium:Staging"], Is.EqualTo(3));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", prev);
        }
    }

    [Test]
    public void Normalization_Retains_Original_Case_By_Default()
    {
        var prev = Environment.GetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG");
        try
        {
            // LabelKey default CasePolicy.Keep will preserve case in Normalized
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", "appA:Chromium:UAT=2");
            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.PoolConfig.ContainsKey("appA:Chromium:UAT"), Is.True);
            Assert.That(opts.PoolConfig["appA:Chromium:UAT"], Is.EqualTo(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENIX_WORKER_POOL_CONFIG", prev);
        }
    }
}
