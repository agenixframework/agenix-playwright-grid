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
    IConnectionStatusWriter status,
    ILogger<SignalRClientService> logger)
    : BackgroundService
{
    private const string PoolStateMessageName = "PoolState";

    // Keep subscription so we can avoid duplicates and dispose on shutdown
    private IDisposable? _poolStateSubscription;
    private bool _connectionEventsWired;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureHandlersWired(writer);
        EnsureConnectionEventsWired();

        var attempt = 0;
        var rng = new Random();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                status.Update(ConnectionStatus.Connecting());
                await conn.StartAsync(stoppingToken);
                attempt = 0;
                logger.LogInformation("SignalR connection started (ConnectionId={Id})", conn.ConnectionId);
                status.Update(ConnectionStatus.Connected());

                // Wait until closed or cancellation
                var closedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                Task ClosedHandler(Exception? ex)
                {
                    var msg = ex?.Message;
                    logger.LogWarning(ex, "SignalR connection closed.");
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
                status.Update(ConnectionStatus.Disconnected("Shutting down"));
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to start SignalR connection; will retry.");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                status.Update(ConnectionStatus.Disconnected("Cancelled"));
                break;
            }

            // exponential backoff with jitter: 1s * 2^attempt (max 30s)
            var baseDelayMs = (int)Math.Min(30000, 1000 * Math.Pow(2, Math.Min(6, attempt)));
            var jitterFactor = 0.8 + (rng.NextDouble() * 0.4); // 0.8 .. 1.2
            var delay = TimeSpan.FromMilliseconds(Math.Max(500, baseDelayMs * jitterFactor));
            attempt++;

            var nextRetryAt = DateTimeOffset.UtcNow + delay;
            status.Update(ConnectionStatus.Retrying(nextRetryAt, attempt, "Disconnected from hub"));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                status.Update(ConnectionStatus.Disconnected("Cancelled"));
                break;
            }
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

    private void EnsureConnectionEventsWired()
    {
        if (_connectionEventsWired)
        {
            return;
        }

        conn.Reconnecting += error =>
        {
            var msg = error?.Message;
            status.Update(ConnectionStatus.Disconnected(msg));
            return Task.CompletedTask;
        };

        conn.Reconnected += connectionId =>
        {
            status.Update(ConnectionStatus.Connected());
            return Task.CompletedTask;
        };

        _connectionEventsWired = true;
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
