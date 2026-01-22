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

using WorkerService.Application.Ports;
using WorkerService.Infrastructure;

namespace WorkerService.Services;

public sealed class NodeRegistrar(IHubClient hub, WorkerOptions options)
{
    public async Task RegisterAsync()
    {
        var port = options.PublicWsPort ?? "5000";
        var baseUrl = $"http://{Environment.GetEnvironmentVariable("HOSTNAME") ?? options.NodeId}:{port}";
        var playwrightVersion = Environment.GetEnvironmentVariable("PLAYWRIGHT_VERSION");
        await hub.RegisterAsync(
            options.HubUrl,
            options.NodeSecret,
            options.NodeId,
            baseUrl,
            options.PoolConfig.Keys.ToArray(),
            options.PoolConfig.Values.Sum(),
            options.Labels.ToDictionary(k => k.Key, v => v.Value),
            playwrightVersion);
    }
}
