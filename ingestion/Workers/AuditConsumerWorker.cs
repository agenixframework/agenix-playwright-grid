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
///     Background worker that consumes audit events from RabbitMQ and batch writes to PostgreSQL.
///     Provides async, high-throughput audit log persistence decoupled from the Hub service.
/// </summary>
public sealed class AuditConsumerWorker : BackgroundService
{
    private readonly IPostgresBatchWriter _batchWriter;
    private readonly IConfiguration _config;
    private readonly IRabbitMqConsumer _consumer;
    private readonly ILogger<AuditConsumerWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ChunkedLogger<AuditConsumerWorker> _chunkedLogger;

    public AuditConsumerWorker(
        IConfiguration config,
        ILogger<AuditConsumerWorker> logger,
        ILoggerFactory loggerFactory,
        ChunkedLogger<AuditConsumerWorker> chunkedLogger,
        IRabbitMqConsumer consumer,
        IPostgresBatchWriter batchWriter)
    {
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _chunkedLogger = chunkedLogger;
        _consumer = consumer;
        _batchWriter = batchWriter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var op = _chunkedLogger.BeginOperation("StartAuditConsumer");

        var batchSize = _config.GetValue("AGENIX_INGESTION_AUDIT_BATCH_SIZE", 500);
        var batchTimeout = TimeSpan.FromMilliseconds(_config.GetValue("AUDIT_BATCH_TIMEOUT", 750));

        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "Starting audit consumer (batchSize={BatchSize}, timeout={Timeout}ms)",
            batchSize, batchTimeout.TotalMilliseconds);

        // Create a batch writer using the same pattern as other workers
        var auditBatcher = new BatchWriter<AuditEvent>(
            _batchWriter.WriteAuditEntriesAsync,
            batchSize,
            batchTimeout,
            _logger,
            new ChunkedLogger<BatchWriter<AuditEvent>>(_loggerFactory.CreateLogger<BatchWriter<AuditEvent>>(), _chunkedLogger.Options));

        try
        {
            op.Complete();

            // Starts consumer using the same pattern as IngestionWorker
            await _consumer.ConsumeAsync<AuditEvent>(
                "agenix-test-platform.audit",
                async (evt, ct) =>
                {
                    _chunkedLogger.LogMilestone(
                        EventCodes.Ingestion.BatchReceived,
                        "Audit event received: Category={Category} Action={Action}",
                        evt.Category, evt.Action);
                    await auditBatcher.AddAsync(evt, ct);
                },
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _chunkedLogger.LogInformation(EventCodes.Ingestion.BatchCompleted, "Audit consumer shutdown requested");
        }
        catch (Exception ex)
        {
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Audit consumer fatal error");
            throw;
        }
        finally
        {
            _chunkedLogger.LogInformation(EventCodes.Ingestion.BatchCompleted, "Audit consumer stopped");
        }
    }
}
