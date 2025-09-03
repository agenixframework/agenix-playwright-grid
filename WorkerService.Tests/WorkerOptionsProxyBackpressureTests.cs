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

public class WorkerOptionsProxyBackpressureTests
{
    [Test]
    public void FromEnvironment_Defaults_ForProxyBackpressure()
    {
        // arrange
        var prevCap = Environment.GetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY");
        var prevPolicy = Environment.GetEnvironmentVariable("WS_PROXY_DROP_POLICY");
        try
        {
            Environment.SetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY", null);
            Environment.SetEnvironmentVariable("WS_PROXY_DROP_POLICY", null);

            // act
            var opts = WorkerOptions.FromEnvironment();

            // assert
            Assert.That(opts.WebSocketProxyChannelCapacity, Is.EqualTo(1024));
            Assert.That(opts.WebSocketProxyDropPolicy, Is.EqualTo(WorkerOptions.WsDropPolicy.DropNewest));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY", prevCap);
            Environment.SetEnvironmentVariable("WS_PROXY_DROP_POLICY", prevPolicy);
        }
    }

    [Test]
    public void FromEnvironment_ParsesProxyBackpressure_AndClamps()
    {
        // arrange
        var prevCap = Environment.GetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY");
        var prevPolicy = Environment.GetEnvironmentVariable("WS_PROXY_DROP_POLICY");
        try
        {
            Environment.SetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY", "999999"); // should clamp to 65536
            Environment.SetEnvironmentVariable("WS_PROXY_DROP_POLICY", "DropOldest");

            // act
            var opts = WorkerOptions.FromEnvironment();

            // assert
            Assert.That(opts.WebSocketProxyChannelCapacity, Is.EqualTo(65536));
            Assert.That(opts.WebSocketProxyDropPolicy, Is.EqualTo(WorkerOptions.WsDropPolicy.DropOldest));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WS_PROXY_CHANNEL_CAPACITY", prevCap);
            Environment.SetEnvironmentVariable("WS_PROXY_DROP_POLICY", prevPolicy);
        }
    }
}
