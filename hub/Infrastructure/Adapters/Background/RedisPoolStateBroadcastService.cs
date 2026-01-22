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

using Microsoft.AspNetCore.SignalR;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

// Periodic broadcaster built on Redis-backed state reader
public sealed class RedisPoolStateBroadcastService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IHubContext<PoolHub, IPoolClient> _hub;
    private readonly IPoolStateReader _reader;

    public RedisPoolStateBroadcastService(IHubContext<PoolHub, IPoolClient> hub, IPoolStateReader reader,
        IConfiguration config)
    {
        _hub = hub;
        _reader = reader;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = int.TryParse(_config["AGENIX_HUB_POOL_BROADCAST_INTERVAL_SECONDS"], out var s)
            ? Math.Max(1, s)
            : 2;
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var state = await _reader.GetStateAsync();
                await _hub.Clients.All.PoolState(state);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // ignore transient errors; the next tick will retry
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
