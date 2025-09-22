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

public class WorkerOptionsCompressionTests
{
    [Test]
    public void FromEnvironment_DefaultAuto_CompressionEnabled()
    {
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        var prevComp = Environment.GetEnvironmentVariable("WS_COMPRESSION");
        var prevMin = Environment.GetEnvironmentVariable("WS_COMPRESSION_MIN_BYTES");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", "AppA:Chromium:Staging=1");
            Environment.SetEnvironmentVariable("NODE_REGION", "local");
            Environment.SetEnvironmentVariable("WS_COMPRESSION", null);
            Environment.SetEnvironmentVariable("WS_COMPRESSION_MIN_BYTES", null);

            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.WebSocketCompressionEnabled, Is.True);
            Assert.That(opts.WebSocketCompressionMinBytes, Is.EqualTo(1024));
        }
        finally
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
            Environment.SetEnvironmentVariable("WS_COMPRESSION", prevComp);
            Environment.SetEnvironmentVariable("WS_COMPRESSION_MIN_BYTES", prevMin);
        }
    }

    [Test]
    public void FromEnvironment_ExplicitOff_DisablesCompression()
    {
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        var prevComp = Environment.GetEnvironmentVariable("WS_COMPRESSION");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", "AppA:Chromium:Staging=1");
            Environment.SetEnvironmentVariable("NODE_REGION", "local");
            Environment.SetEnvironmentVariable("WS_COMPRESSION", "off");

            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.WebSocketCompressionEnabled, Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
            Environment.SetEnvironmentVariable("WS_COMPRESSION", prevComp);
        }
    }

    [Test]
    public void FromEnvironment_Auto_WithHighThreshold_DisablesCompression()
    {
        var prevPool = Environment.GetEnvironmentVariable("POOL_CONFIG");
        var prevRegion = Environment.GetEnvironmentVariable("NODE_REGION");
        var prevComp = Environment.GetEnvironmentVariable("WS_COMPRESSION");
        var prevMin = Environment.GetEnvironmentVariable("WS_COMPRESSION_MIN_BYTES");
        try
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", "AppA:Chromium:Staging=1");
            Environment.SetEnvironmentVariable("NODE_REGION", "local");
            Environment.SetEnvironmentVariable("WS_COMPRESSION", "auto");
            Environment.SetEnvironmentVariable("WS_COMPRESSION_MIN_BYTES", "5000000"); // 5,000,000 > default 2 MiB limit

            var opts = WorkerOptions.FromEnvironment();
            Assert.That(opts.WebSocketCompressionEnabled, Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POOL_CONFIG", prevPool);
            Environment.SetEnvironmentVariable("NODE_REGION", prevRegion);
            Environment.SetEnvironmentVariable("WS_COMPRESSION", prevComp);
            Environment.SetEnvironmentVariable("WS_COMPRESSION_MIN_BYTES", prevMin);
        }
    }
}
