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

using System.Text.Json;
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared;
using Agenix.PlaywrightGrid.Shared.Logging;
using IngestionService.Infrastructure;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace IngestionService.Workers;

/// <summary>
///     Background worker that consumes artifact upload events from RabbitMQ
///     and stores artifacts in configured backend (local filesystem or MinIO).
/// </summary>
public sealed class ArtifactUploadWorker : BackgroundService
{
    private readonly int _batchSize;
    private readonly IConfiguration _config;
    private readonly IRabbitMqConsumer _consumer;
    private readonly string _localBasePath;
    private readonly ILogger<ArtifactUploadWorker> _logger;
    private readonly ChunkedLogger<ArtifactUploadWorker> _chunkedLogger;
    private readonly MinioStorageService? _minioStorage;
    private readonly NpgsqlDataSource _pgDataSource;
    private readonly string _storageBackend;

    public ArtifactUploadWorker(
        IConfiguration config,
        IRabbitMqConsumer consumer,
        ILogger<ArtifactUploadWorker> logger,
        ChunkedLogger<ArtifactUploadWorker> chunkedLogger,
        NpgsqlDataSource pgDataSource,
        MinioStorageService? minioStorage = null)
    {
        _config = config;
        _consumer = consumer;
        _logger = logger;
        _chunkedLogger = chunkedLogger;
        _pgDataSource = pgDataSource;
        _minioStorage = minioStorage;

        _storageBackend = config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local")!;
        _localBasePath = config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts")!;
        _batchSize = config.GetValue("AGENIX_INGESTION_ARTIFACTS_BATCH_SIZE", 10);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var op = _chunkedLogger.BeginOperation(
            "StartArtifactUploadWorker",
            inputs: new Dictionary<string, object>
            {
                ["backend"] = _storageBackend,
                ["batchSize"] = _batchSize
            });

        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "Starting artifact upload worker with backend={Backend}, localBasePath={Path}, batchSize={BatchSize}",
            _storageBackend, _localBasePath, _batchSize);

        // Ensure local storage directory exists if using local backend
        if (_storageBackend == "local" && !string.IsNullOrEmpty(_localBasePath))
        {
            Directory.CreateDirectory(_localBasePath);
            _chunkedLogger.LogInformation(
                EventCodes.Ingestion.BatchStarted,
                "Ensured local storage directory exists: {Path}", _localBasePath);
        }

        // Get RabbitMQ connection and create channel
        var connection = _consumer.GetConnection();
        var channel = connection.CreateModel();

        // Declare queue with dead-letter queue configuration (must match Hub's declaration)
        var dlqArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" }, { "x-dead-letter-routing-key", "agenix-test-platform.dlq" }
        };

        channel.QueueDeclare(
            "agenix-test-platform.artifacts",
            true,
            false,
            false,
            dlqArgs);

        // Set prefetch count for batch processing
        channel.BasicQos(0, (ushort)_batchSize, false);

        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "Declared queue 'agenix-test-platform.artifacts' with prefetch={Prefetch}", _batchSize);

        // Create async consumer
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            using var msgOp = _chunkedLogger.BeginOperation("ProcessArtifactMessage");
            try
            {
                var evt = JsonSerializer.Deserialize<ArtifactUploadEvent>(ea.Body.Span);
                if (evt == null)
                {
                    _chunkedLogger.LogWarning(
                        EventCodes.Ingestion.BatchFailed,
                        "Received null event, skipping");
                    channel.BasicNack(ea.DeliveryTag, false, false);
                    return;
                }

                _chunkedLogger.LogMilestone(
                    EventCodes.Ingestion.BatchReceived,
                    "Received artifact upload event: ArtifactId={ArtifactId} TestItemId={TestItemId}",
                    evt.ArtifactId, evt.TestItemId);

                await ProcessArtifactUploadAsync(evt, ct);
                channel.BasicAck(ea.DeliveryTag, false);

                _chunkedLogger.LogMilestone(
                    EventCodes.Ingestion.BatchCompleted,
                    "Successfully processed artifact {ArtifactId} for test item {TestItemId}",
                    evt.ArtifactId, evt.TestItemId);

                msgOp.SetOutputs(new Dictionary<string, object>
                {
                    ["artifactId"] = evt.ArtifactId,
                    ["testItemId"] = evt.TestItemId,
                    ["success"] = true
                });
                msgOp.Complete();
            }
            catch (Exception ex)
            {
                msgOp.Fail(ex, ErrorType.Unexpected);
                _logger.LogError(ex, "Failed to process artifact upload event, moving to DLQ");
                channel.BasicNack(ea.DeliveryTag, false, false);
            }
        };

        channel.BasicConsume(
            "agenix-test-platform.artifacts",
            false,
            consumer);

        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "Started consuming from 'agenix-test-platform.artifacts' queue");

        op.Complete();

        // Keep worker running
        await Task.Delay(Timeout.Infinite, ct);
    }

    internal async Task ProcessArtifactUploadAsync(ArtifactUploadEvent evt, CancellationToken ct)
    {
        // Basic validation - if IDs are empty or content is null, this is an unrecoverable invalid message
        if (evt.ArtifactId == Guid.Empty || evt.TestItemId == Guid.Empty || evt.Content == null)
        {
            _chunkedLogger.LogWarning(
                EventCodes.Ingestion.BatchFailed,
                "Received invalid artifact upload event: ArtifactId={ArtifactId}, TestItemId={TestItemId}, HasContent={HasContent}. Skipping.",
                evt.ArtifactId, evt.TestItemId, evt.Content != null);
            return;
        }

        try
        {
            string storagePath;
            if (_storageBackend == "minio" && _minioStorage != null)
            {
                // Upload to MinIO
                var bucketName = _config.GetValue("MINIO_BUCKET_NAME", "playwright-artifacts")!;
                var objectKey = $"{evt.TestItemId}/{evt.ArtifactId}_{SanitizeFileName(evt.FileName)}";

                await _minioStorage.UploadArtifactAsync(objectKey, evt.Content, evt.ContentType, ct);
                storagePath = $"minio://{bucketName}/{objectKey}";

                _chunkedLogger.LogInformation(
                    EventCodes.Storage.UploadCompleted,
                    "Uploaded artifact {ArtifactId} to MinIO: {Path}",
                    evt.ArtifactId, storagePath);
            }
            else
            {
                // Upload to local filesystem
                var sanitizedFileName = SanitizeFileName(evt.FileName);
                var relativePath = Path.Combine(evt.TestItemId.ToString(), $"{evt.ArtifactId}_{sanitizedFileName}");
                var fullPath = Path.Combine(_localBasePath, relativePath);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write file
                await File.WriteAllBytesAsync(fullPath, evt.Content, ct);
                storagePath = relativePath;

                _chunkedLogger.LogInformation(
                    EventCodes.Storage.UploadCompleted,
                    "Uploaded artifact {ArtifactId} to local filesystem: {Path}",
                    evt.ArtifactId, fullPath);
            }

            // Update database with storage path and status
            await UpdateArtifactMetadataAsync(evt.ArtifactId, storagePath, "uploaded", ct);
        }
        catch (Exception ex)
        {
            _chunkedLogger.LogError(ex,
                EventCodes.Storage.UploadFailed,
                "Failed to upload artifact {ArtifactId}, marking as failed",
                evt.ArtifactId);

            // Mark as failed in database
            await UpdateArtifactMetadataAsync(evt.ArtifactId, null, "failed", ct);
            throw;
        }
    }

    private async Task UpdateArtifactMetadataAsync(
        Guid artifactId,
        string? storagePath,
        string status,
        CancellationToken ct)
    {
        await using var conn = await _pgDataSource.OpenConnectionAsync(ct);

        var sql = storagePath != null
            ? "UPDATE test_artifacts SET storage_path = $1, status = $2 WHERE id = $3"
            : "UPDATE test_artifacts SET status = $1 WHERE id = $2";

        await using var cmd = new NpgsqlCommand(sql, conn);

        if (storagePath != null)
        {
            cmd.Parameters.AddWithValue(storagePath);
            cmd.Parameters.AddWithValue(status);
            cmd.Parameters.AddWithValue(artifactId);
        }
        else
        {
            cmd.Parameters.AddWithValue(status);
            cmd.Parameters.AddWithValue(artifactId);
        }

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

        if (rowsAffected == 0)
        {
            _chunkedLogger.LogWarning(
                EventCodes.Ingestion.BatchFailed,
                "Artifact {ArtifactId} not found in database, may have been deleted",
                artifactId);
        }
    }

    internal static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "artifact";
        }

        // Remove invalid file name characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrEmpty(sanitized) ? "artifact" : sanitized;
    }

    public override void Dispose()
    {
        _logger.LogInformation("[ArtifactUploadWorker] Disposing artifact upload worker");
        base.Dispose();
    }
}
