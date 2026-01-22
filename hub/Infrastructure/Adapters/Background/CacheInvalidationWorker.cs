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

using System.Threading.Channels;
using Npgsql;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Adapters.Background;

/// <summary>
///     Background service that processes the cache invalidation outbox.
///     Uses Postgres LISTEN/NOTIFY for near-instant invalidation and polling as a fallback.
/// </summary>
public sealed class CacheInvalidationWorker(
    ICacheInvalidationOutbox outbox,
    IDatabase redis,
    IConfiguration configuration,
    ILogger<CacheInvalidationWorker> logger)
    : BackgroundService
{
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CacheInvalidationWorker starting...");

        // Start listening for notifications in a separate task
        _ = ListenForNotificationsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing cache invalidation outbox");
            }

            try
            {
                // Wait for next poll (fallback) or notification signal
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10)); // Poll fallback every 10s

                await _signal.Reader.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Either periodic timeout or stoppingToken triggered
            }
        }

        logger.LogInformation("CacheInvalidationWorker stopped.");
    }

    private async Task ListenForNotificationsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Fix: Multiplexing doesn't support WaitAsync/LISTEN/NOTIFY.
                // We create a dedicated non-multiplexed connection for notifications.
                var connString = configuration["POSTGRES_CONNECTION_STRING"];
                if (string.IsNullOrEmpty(connString))
                {
                    throw new InvalidOperationException("POSTGRES_CONNECTION_STRING is required");
                }

                var builder = new NpgsqlConnectionStringBuilder(connString)
                {
                    Multiplexing = false,
                    KeepAlive = 30 // Recommended for long-running LISTEN connections
                };

                await using var conn = new NpgsqlConnection(builder.ConnectionString);
                await conn.OpenAsync(ct);

                conn.Notification += (o, e) =>
                {
                    if (e.Channel == "cache_invalidation")
                    {
                        _signal.Writer.TryWrite(true);
                    }
                };

                await using var cmd = new NpgsqlCommand("LISTEN cache_invalidation", conn);
                await cmd.ExecuteNonQueryAsync(ct);

                while (!ct.IsCancellationRequested)
                {
                    // This will wait until a notification arrives or connection is lost
                    await conn.WaitAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Postgres LISTEN connection failed, retrying in 5s...");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }
    }

    private async Task ProcessOutboxAsync(CancellationToken ct)
    {
        int processedCount = 0;
        while (!ct.IsCancellationRequested)
        {
            var pending = await outbox.GetPendingAsync(100, ct);
            if (pending.Count == 0) break;

            var successfullyProcessedIds = new List<long>();

            foreach (var (Id, Key) in pending)
            {
                try
                {
                    await redis.KeyDeleteAsync(Key);
                    successfullyProcessedIds.Add(Id);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to invalidate cache key: {Key}. Will retry.", Key);
                }
            }

            if (successfullyProcessedIds.Count > 0)
            {
                await outbox.DeleteAsync(successfullyProcessedIds, ct);
            }

            // If we processed a full batch, continue immediately to next batch
            if (pending.Count < 100) break;
        }

        if (processedCount > 0)
        {
            logger.LogDebug("Processed {Count} cache invalidation tasks from outbox", processedCount);
        }
    }
}
