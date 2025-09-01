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

using Dashboard.Application.Ports;
using Microsoft.AspNetCore.SignalR.Client;

namespace Dashboard;

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
                    {
                        logger.LogWarning(ex, "SignalR connection closed.");
                    }
                    else
                    {
                        logger.LogWarning("SignalR connection closed.");
                    }

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

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void EnsureHandlersWired(IPoolStateWriter writer)
    {
        if (_poolStateSubscription != null)
        {
            return; // already wired
        }

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
