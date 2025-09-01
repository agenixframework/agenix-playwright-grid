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

public class WorkerOptionsTests
{
    [Test]
    public void FromEnvironment_Defaults_WhenNoEnvVars()
    {
        // Arrange
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", null);
            Environment.SetEnvironmentVariable("NODE_REGION", null);

            // Act
            var opts = WorkerOptions.FromEnvironment();

            // Assert
            Assert.That(opts.PoolConfig.ContainsKey("AppA:Chromium:Staging"), Is.True);
            Assert.That(opts.PoolConfig["AppA:Chromium:Staging"], Is.EqualTo(3));
            Assert.That(opts.Labels.ContainsKey("region"), Is.True);
            Assert.That(opts.Labels["region"], Is.EqualTo("local"));
            Assert.That(string.IsNullOrWhiteSpace(opts.NodeId), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
        }
    }

    [Test]
    public void FromEnvironment_ParsesPools_AndRegionOverride()
    {
        // Arrange
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", "X=5, Y=2, Z=bad, =3, W= 7");
            Environment.SetEnvironmentVariable("NODE_REGION", "eu");

            // Act
            var opts = WorkerOptions.FromEnvironment();

            // Assert
            Assert.That(opts.PoolConfig.TryGetValue("X", out var x) && x == 5, Is.True);
            Assert.That(opts.PoolConfig.TryGetValue("Y", out var y) && y == 2, Is.True);
            Assert.That(opts.PoolConfig.TryGetValue("W", out var w) && w == 7, Is.True);
            Assert.That(opts.PoolConfig.ContainsKey("Z"), Is.False);
            Assert.That(opts.PoolConfig.ContainsKey(string.Empty), Is.False);
            Assert.That(opts.Labels["region"], Is.EqualTo("eu"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
        }
    }
}
