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

using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared;
using Agenix.PlaywrightGrid.Shared.Logging;
using IngestionService.Application;
using IngestionService.Infrastructure;

namespace IngestionService.Workers;

/// <summary>
///     Background worker that consumes events from RabbitMQ and processes them via batch writers.
/// </summary>
public sealed class IngestionWorker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IRabbitMqConsumer _consumer;
    private readonly List<Task> _consumerTasks = new();
    private readonly ILogger<IngestionWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ChunkedLogger<IngestionWorker> _chunkedLogger;
    private readonly IPostgresBatchWriter _pgWriter;

    public IngestionWorker(
        IRabbitMqConsumer consumer,
        IPostgresBatchWriter pgWriter,
        IConfiguration config,
        ILogger<IngestionWorker> logger,
        ILoggerFactory loggerFactory,
        ChunkedLogger<IngestionWorker> chunkedLogger)
    {
        _consumer = consumer;
        _pgWriter = pgWriter;
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _chunkedLogger = chunkedLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // NEW: Startup milestone
        using var startupOp = _chunkedLogger.BeginOperation(
            "StartIngestionWorker",
            inputs: new Dictionary<string, object>
            {
                ["concurrency"] = _config.GetValue("AGENIX_INGESTION_CONSUMER_CONCURRENCY", 4)
            });
        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "Starting ingestion workers with concurrency={Concurrency}",
            _config.GetValue("AGENIX_INGESTION_CONSUMER_CONCURRENCY", 4));
        startupOp.Complete();

        // Create batch writers for each event type
        var testItemBatcher = new BatchWriter<TestItemEvent>(
            _pgWriter.WriteTestItemsAsync,
            _config.GetValue("AGENIX_INGESTION_BATCH_SIZE_TEST_ITEMS", 200),
            TimeSpan.FromMilliseconds(_config.GetValue("AGENIX_INGESTION_BATCH_TIMEOUT_MS", 1000)),
            _logger,
            new ChunkedLogger<BatchWriter<TestItemEvent>>(_loggerFactory.CreateLogger<BatchWriter<TestItemEvent>>(), _chunkedLogger.Options));

        var commandBatcher = new BatchWriter<CommandEvent>(
            _pgWriter.WriteCommandsAsync,
            _config.GetValue("AGENIX_INGESTION_BATCH_SIZE_COMMANDS", 500),
            TimeSpan.FromMilliseconds(_config.GetValue("AGENIX_INGESTION_BATCH_TIMEOUT_MS", 1000)),
            _logger,
            new ChunkedLogger<BatchWriter<CommandEvent>>(_loggerFactory.CreateLogger<BatchWriter<CommandEvent>>(), _chunkedLogger.Options));

        var logItemBatcher = new BatchWriter<LogItemEvent>(
            _pgWriter.WriteLogItemsAsync,
            _config.GetValue("AGENIX_INGESTION_BATCH_SIZE_LOG_ITEMS", 300),
            TimeSpan.FromMilliseconds(_config.GetValue("AGENIX_INGESTION_BATCH_TIMEOUT_MS", 1000)),
            _logger,
            new ChunkedLogger<BatchWriter<LogItemEvent>>(_loggerFactory.CreateLogger<BatchWriter<LogItemEvent>>(), _chunkedLogger.Options));

        // Start concurrent consumers for each queue
        var concurrency = _config.GetValue("AGENIX_INGESTION_CONSUMER_CONCURRENCY", 4);
        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchReceived,
            "Starting {ConsumerCount} concurrent consumers for each queue",
            concurrency * 3); // 3 queues (test-items, commands, log-items)

        for (var i = 0; i < concurrency; i++)
        {
            // NEW: Create an operation per consumer
            var consumerId = i;
            using var consumerOp = _chunkedLogger.BeginOperation(
                "StartRabbitMqConsumer",
                inputs: new Dictionary<string, object>
                {
                    ["consumerId"] = consumerId,
                    ["concurrency"] = concurrency
                });

            _consumerTasks.Add(Task.Run(async () =>
            {
                using var op = _chunkedLogger.BeginOperation(
                    "RabbitMqConsumerLoop",
                    inputs: new Dictionary<string, object>
                    {
                        ["consumerId"] = consumerId,
                        ["queue"] = "test-items"
                    });
                await _consumer.ConsumeAsync<TestItemEvent>(
                    "agenix-test-platform.test-items",
                    async (evt, ct) =>
                    {
                        // Log each event received
                        _chunkedLogger.LogMilestone(
                            EventCodes.Ingestion.TestItemsWritten,
                            "TestItem event received: ItemId={ItemId} LaunchId={LaunchId}",
                            evt.ItemId, evt.LaunchId);

                        await testItemBatcher.AddAsync(evt, ct);
                    },
                    stoppingToken);
            }, stoppingToken));

            _consumerTasks.Add(Task.Run(async () =>
            {
                using var op = _chunkedLogger.BeginOperation(
                    "RabbitMqConsumerLoop",
                    inputs: new Dictionary<string, object>
                    {
                        ["consumerId"] = consumerId,
                        ["queue"] = "commands"
                    });
                await _consumer.ConsumeAsync<CommandEvent>(
                    "agenix-test-platform.commands",
                    async (evt, ct) =>
                    {
                        _chunkedLogger.LogMilestone(
                            EventCodes.Ingestion.CommandsWritten,
                            "Command event received: RunId={RunId} Kind={Kind}",
                            evt.RunId, evt.DataJson?.Length ?? 0);

                        await commandBatcher.AddAsync(evt, ct);
                    },
                    stoppingToken);
            }, stoppingToken));

            _consumerTasks.Add(Task.Run(async () =>
            {
                using var op = _chunkedLogger.BeginOperation(
                    "RabbitMqConsumerLoop",
                    inputs: new Dictionary<string, object>
                    {
                        ["consumerId"] = consumerId,
                        ["queue"] = "log-items"
                    });
                await _consumer.ConsumeAsync<LogItemEvent>(
                    "agenix-test-platform.log-items",
                    async (evt, ct) =>
                    {
                        _chunkedLogger.LogMilestone(
                            EventCodes.Ingestion.LogItemsWritten,
                            "LogItem event received: ItemId={ItemId} Level={Level}",
                            evt.ItemId, evt.Level);

                        await logItemBatcher.AddAsync(evt, ct);
                    },
                    stoppingToken);
            }, stoppingToken));

            consumerOp.Complete();
        }

        _chunkedLogger.LogInformation(EventCodes.Ingestion.BatchStarted, "Started {Count} consumer tasks", _consumerTasks.Count);

        // Wait for all consumers (existing logic)
        try
        {
            using var op = _chunkedLogger.BeginOperation("WaitForConsumers");
            await Task.WhenAll(_consumerTasks);
            op.SetOutputs(new Dictionary<string, object> { ["completed"] = true });
            op.Complete();
        }
        catch (OperationCanceledException)
        {
            _chunkedLogger.LogInformation(EventCodes.Ingestion.BatchCompleted, "Ingestion worker stopped by cancellation");
        }
        finally
        {
            // Flush remaining batches with chunked logging
            using var flushOp = _chunkedLogger.BeginOperation(
                "FlushBatches",
                inputs: new Dictionary<string, object> { ["reason"] = "shutdown" });

            await testItemBatcher.FlushAsync(CancellationToken.None);
            _chunkedLogger.LogMilestone(EventCodes.Ingestion.BatchCompleted, "Flushed test item batch");

            await commandBatcher.FlushAsync(CancellationToken.None);
            _chunkedLogger.LogMilestone(EventCodes.Ingestion.BatchCompleted, "Flushed command batch");

            await logItemBatcher.FlushAsync(CancellationToken.None);
            _chunkedLogger.LogMilestone(EventCodes.Ingestion.BatchCompleted, "Flushed log item batch");

            testItemBatcher.Dispose();
            commandBatcher.Dispose();
            logItemBatcher.Dispose();

            flushOp.SetOutputs(new Dictionary<string, object> { ["flushed"] = 3 });
            flushOp.Complete();
        }
    }
}
