using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Adapters.SignalR;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

// Periodic broadcaster built on Redis-backed state reader
public sealed class RedisPoolStateBroadcastService : BackgroundService
{
    private readonly IHubContext<PoolHub, IPoolClient> _hub;
    private readonly IPoolStateReader _reader;

    public RedisPoolStateBroadcastService(IHubContext<PoolHub, IPoolClient> hub, IPoolStateReader reader)
    {
        _hub = hub;
        _reader = reader;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(2);

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
