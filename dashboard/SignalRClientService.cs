using System;
using System.Threading;
using System.Threading.Tasks;
using Dashboard.Application.Ports;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dashboard.Infrastructure.Adapters.SignalR;

internal sealed class SignalRClientService(
    HubConnection conn,
    IPoolStateWriter writer,
    ILogger<SignalRClientService> logger)
    : BackgroundService
{
    private const string PoolStateMessageName = "PoolState";

    // Keep subscription so we can avoid duplicates and dispose on shutdown
    private IDisposable _poolStateSubscription;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureHandlersWired(writer);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await conn.StartAsync(stoppingToken);
                logger.LogInformation("SignalR connection started (ConnectionId={Id})", conn.ConnectionId);

                // Wait until closed or cancellation
                var closedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                Task ClosedHandler(Exception ex)
                {
                    if (ex != null)
                        logger.LogWarning(ex, "SignalR connection closed.");
                    else
                        logger.LogWarning("SignalR connection closed.");
                    closedTcs.TrySetResult(true);
                    return Task.CompletedTask;
                }

                conn.Closed += ClosedHandler;
                try
                {
                    await Task.WhenAny(closedTcs.Task, Task.Delay(Timeout.Infinite, stoppingToken));
                }
                finally
                {
                    conn.Closed -= ClosedHandler;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to start SignalR connection; will retry.");
            }

            if (stoppingToken.IsCancellationRequested) break;

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void EnsureHandlersWired(IPoolStateWriter writer)
    {
        if (_poolStateSubscription != null) return; // already wired

        _poolStateSubscription = conn.On<PoolStateDto>(PoolStateMessageName, writer.Update);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await conn.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while stopping SignalR connection.");
        }

        // Remove handler subscription to avoid leaks
        _poolStateSubscription?.Dispose();
        _poolStateSubscription = null;

        await base.StopAsync(cancellationToken);

        try
        {
            await conn.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while disposing SignalR connection.");
        }
    }
}
