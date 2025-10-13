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
using Npgsql;
using NpgsqlTypes;

namespace IngestionService.Infrastructure;

/// <summary>
///     PostgreSQL bulk insert writer using COPY protocol for maximum performance.
///     Handles test items, commands, and log items.
/// </summary>
public sealed class PostgresBatchWriter : IPostgresBatchWriter
{
    private readonly ICommandTokenCache _commandTokenCache;
    private readonly IConfiguration _config;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresBatchWriter> _logger;
    private readonly ChunkedLogger<PostgresBatchWriter> _chunkedLogger;
    private readonly ILogTokenCache _logTokenCache;
    private readonly MinioStorageService? _minioStorage;
    private readonly bool _useCommandTokenOptimization;
    private readonly bool _useLogTokenOptimization;

    public PostgresBatchWriter(
        IConfiguration config,
        NpgsqlDataSource dataSource,
        ILogger<PostgresBatchWriter> logger,
        ChunkedLogger<PostgresBatchWriter> chunkedLogger,
        ILogTokenCache logTokenCache,
        ICommandTokenCache commandTokenCache,
        MinioStorageService? minioStorage = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger;
        _chunkedLogger = chunkedLogger;
        _logTokenCache = logTokenCache;
        _commandTokenCache = commandTokenCache;
        _useLogTokenOptimization = config.GetValue("AGENIX_INGESTION_LOG_TOKEN_OPTIMIZATION_ENABLED", true);
        _useCommandTokenOptimization = config.GetValue("AGENIX_INGESTION_COMMAND_TOKEN_OPTIMIZATION_ENABLED", true);
        _config = config;
        _minioStorage = minioStorage;
    }

    public async Task WriteTestItemsAsync(List<TestItemEvent> events, CancellationToken ct)
    {
        using var op = _chunkedLogger.BeginOperation(
            "WriteTestItemsBatch",
            inputs: new Dictionary<string, object>
            {
                ["batchSize"] = events.Count,
                ["optimizationEnabled"] = _useLogTokenOptimization
            });
        try
        {
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Starting batch write of {Count} test items", events.Count);
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var writer = await conn.BeginBinaryImportAsync(
                "COPY test_items (run_id, launch_id, parent_item_id, item_type, name, start_time, session_status, computed_status, browser_type, worker_node_id) " +
                "FROM STDIN (FORMAT BINARY)", ct);
            var skippedCount = 0;
            var insertedCount = 0;
            foreach (var evt in events)
            {
                // Skip events with null GUID (indicates invalid event upstream)
                if (evt.ItemId == Guid.Empty)
                {
                    _chunkedLogger.LogWarning(
                        EventCodes.Ingestion.BatchFailed,
                        "Skipping test item event with empty ItemId (00000000-0000-0000-0000-000000000000)");
                    skippedCount++;
                    continue;
                }
                // Skip events with empty or null DataJson
                if (string.IsNullOrWhiteSpace(evt.DataJson))
                {
                    _chunkedLogger.LogWarning(
                        EventCodes.Ingestion.BatchFailed,
                        "Skipping test item event {ItemId} with empty DataJson", evt.ItemId);
                    skippedCount++;
                    continue;
                }
                Dictionary<string, JsonElement>? data;
                try
                {
                    data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(evt.DataJson);
                }
                catch (JsonException ex)
                {
                    _chunkedLogger.LogError(
                        ex,
                        EventCodes.Ingestion.BatchFailed,
                        "Failed to parse DataJson for test item {ItemId}: {Json}", evt.ItemId,
                        evt.DataJson);
                    skippedCount++;
                    continue;
                }
                if (data == null)
                {
                    skippedCount++;
                    continue;
                }
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(evt.ItemId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(evt.LaunchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(GetGuidOrNull(data, "parentItemId"), NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(GetString(data, "itemType", "Test"), NpgsqlDbType.Text, ct);
                await writer.WriteAsync(GetString(data, "name", ""), NpgsqlDbType.Text, ct);
                await writer.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct);
                await writer.WriteAsync(GetString(data, "sessionStatus", "Queued"), NpgsqlDbType.Text, ct);
                await writer.WriteAsync(GetStringOrNull(data, "computedStatus"), NpgsqlDbType.Text, ct);
                await writer.WriteAsync(GetStringOrNull(data, "browserType"), NpgsqlDbType.Text, ct);
                await writer.WriteAsync(GetStringOrNull(data, "workerNodeId"), NpgsqlDbType.Text, ct);
                insertedCount++;

                // Log progress milestone every 100 items
                if (insertedCount % 100 == 0)
                {
                    _chunkedLogger.LogMilestone(
                        EventCodes.Ingestion.TestItemsWritten,
                        "Written {Written}/{Total} test items to COPY stream",
                        insertedCount, events.Count);
                }
            }
            await writer.CompleteAsync(ct);
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchCompleted,
                "Completed COPY operation: inserted {InsertedCount}, skipped {SkippedCount}",
                insertedCount, skippedCount);
            op.SetOutputs(new Dictionary<string, object>
            {
                ["insertedCount"] = insertedCount,
                ["skippedCount"] = skippedCount,
                ["success"] = true
            });
            op.Complete();
        }
        catch (PostgresException ex)
        {
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Database error in WriteTestItemsAsync: {SqlState}", ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            op.Fail(ex, ErrorType.Unexpected);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Unexpected error in WriteTestItemsAsync");
            throw;
        }
    }

    public async Task WriteCommandsAsync(List<CommandEvent> events, CancellationToken ct)
    {
        if (_useCommandTokenOptimization)
        {
            await WriteCommandsWithTokensAsync(events, ct);
        }
        else
        {
            await WriteCommandsLegacyAsync(events, ct);
        }
    }

    private async Task WriteCommandsWithTokensAsync(List<CommandEvent> events, CancellationToken ct)
    {
        using var op = _chunkedLogger.BeginOperation(
            "WriteCommandsBatch",
            inputs: new Dictionary<string, object>
            {
                ["batchSize"] = events.Count,
                ["optimizationMode"] = "token"
            });
        try
        {
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Starting batch write of {Count} commands with token optimization", events.Count);

            // Parse all events and extract message/kind
            var parsedEvents =
                new List<(CommandEvent evt, Dictionary<string, JsonElement> data, string? message, string? kind)>();
            foreach (var evt in events)
            {
                try
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(evt.DataJson);
                    if (data == null)
                    {
                        continue;
                    }

                    var message = GetStringOrNull(data, "message");
                    var kind = GetStringOrNull(data, "kind");

                    parsedEvents.Add((evt, data, message, kind));
                }
                catch (JsonException ex)
                {
                    _chunkedLogger.LogWarning(
                        ex,
                        EventCodes.Ingestion.BatchFailed,
                        "Failed to deserialize command data JSON: {Data}", evt.DataJson);
                }
            }

            if (parsedEvents.Count == 0)
            {
                _chunkedLogger.LogWarning(
                    EventCodes.Ingestion.BatchFailed,
                    "No valid commands to insert (all have null DataJson)");
                return;
            }

            // Get tokens for all commands (hash = SHA256(kind + message))
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Fetching tokens for {Count} commands", parsedEvents.Count);

            var tokenTasks = parsedEvents.Select(x =>
                _commandTokenCache.GetOrCreateTokenAsync(
                    x.message ?? "",
                    x.kind,
                    GetStringOrNull(x.data, "propsJson"),
                    ct));
            var tokens = await Task.WhenAll(tokenTasks);
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.TokenCreated,
                "Retrieved {Count} tokens from cache", tokens.Length);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Try initial COPY - if successful, return immediately
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Attempting COPY insert for commands");

            if (await TryWriteCommandsCopyAsync(conn, parsedEvents, tokens, ct))
            {
                op.SetOutputs(new Dictionary<string, object>
                {
                    ["insertedCount"] = parsedEvents.Count,
                    ["retryRequired"] = false,
                    ["success"] = true
                });
                op.Complete();
                return;
            }

            // FK violation - validate tokens exist and retry with valid items only
            _chunkedLogger.LogWarning(
                EventCodes.Ingestion.BatchFailed,
                "FK violation detected - validating tokens and retrying with valid items only");
            var distinctTokens = tokens.Distinct().ToArray();
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Checking {TokenCount} distinct tokens against database", distinctTokens.Length);

            var existingTokens = await GetExistingCommandTokensAsync(conn, distinctTokens, ct);
            var validSet = new HashSet<string>(existingTokens);

            var validIdx = new List<int>();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (validSet.Contains(tokens[i]))
                {
                    validIdx.Add(i);
                }
            }

            if (validIdx.Count == 0)
            {
                _chunkedLogger.LogError(
                    EventCodes.Ingestion.BatchFailed,
                    "All {Count} commands have missing tokens - skipping batch", parsedEvents.Count);
                op.SetOutputs(new Dictionary<string, object>
                {
                    ["insertedCount"] = 0,
                    ["retryRequired"] = false,
                    ["skippedAll"] = true,
                    ["success"] = false
                });
                op.Complete();
                return;
            }

            // Retry COPY with valid items
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Retrying COPY with {ValidCount} valid commands (skipping {InvalidCount})",
                validIdx.Count, parsedEvents.Count - validIdx.Count);

            await using var w = await conn.BeginBinaryImportAsync(
                "COPY commands (run_id, timestamp_utc, kind, token_hash, props_json, test_id, expires_at) FROM STDIN (FORMAT BINARY)",
                ct);

            foreach (var i in validIdx)
            {
                var (evt, data, _, kind) = parsedEvents[i];
                await w.StartRowAsync(ct);
                await w.WriteAsync(Guid.Parse(evt.RunId), NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct);
                await w.WriteAsync((object?)kind ?? DBNull.Value, NpgsqlDbType.Text, ct);
                await w.WriteAsync(tokens[i], NpgsqlDbType.Text, ct);
                await w.WriteAsync((object?)GetStringOrNull(data, "propsJson") ?? DBNull.Value, NpgsqlDbType.Text, ct);
                await w.WriteAsync((object?)GetStringOrNull(data, "testId") ?? DBNull.Value, NpgsqlDbType.Text, ct);
                await w.WriteAsync(DBNull.Value, NpgsqlDbType.TimestampTz, ct);
            }

            await w.CompleteAsync(ct);
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchCompleted,
                "Inserted {Valid} commands (skipped {Invalid} with missing tokens)",
                validIdx.Count, parsedEvents.Count - validIdx.Count);

            op.SetOutputs(new Dictionary<string, object>
            {
                ["insertedCount"] = validIdx.Count,
                ["skippedCount"] = parsedEvents.Count - validIdx.Count,
                ["retryRequired"] = true,
                ["success"] = true
            });
            op.Complete();
        }
        catch (PostgresException ex)
        {
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Database error in WriteCommandsWithTokensAsync: {SqlState}", ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            op.Fail(ex, ErrorType.Unexpected);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Unexpected error in WriteCommandsWithTokensAsync");
            throw;
        }
    }

    private async Task WriteCommandsLegacyAsync(List<CommandEvent> events, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY commands (run_id, timestamp_utc, kind, message, props_json, test_id, expires_at) " +
            "FROM STDIN (FORMAT BINARY)", ct);

        foreach (var evt in events)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(evt.DataJson);
            if (data == null)
            {
                continue;
            }

            await writer.StartRowAsync(ct);
            await writer.WriteAsync(Guid.Parse(evt.RunId), NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync((object?)GetStringOrNull(data, "kind") ?? DBNull.Value, NpgsqlDbType.Text, ct);
            await writer.WriteAsync((object?)GetStringOrNull(data, "message") ?? DBNull.Value, NpgsqlDbType.Text, ct);
            await writer.WriteAsync((object?)GetStringOrNull(data, "propsJson") ?? DBNull.Value, NpgsqlDbType.Text, ct);
            await writer.WriteAsync((object?)GetStringOrNull(data, "testId") ?? DBNull.Value, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(DBNull.Value, NpgsqlDbType.TimestampTz, ct);
        }

        await writer.CompleteAsync(ct);
        _chunkedLogger.LogMilestone(EventCodes.Ingestion.CommandsWritten, "Inserted {Count} commands (legacy mode) via COPY", events.Count);
    }

    public async Task WriteLogItemsAsync(List<LogItemEvent> events, CancellationToken ct)
    {
        if (_useLogTokenOptimization)
        {
            await WriteLogItemsWithTokensAsync(events, ct);
        }
        else
        {
            await WriteLogItemsLegacyAsync(events, ct);
        }
    }

    private async Task WriteLogItemsWithTokensAsync(List<LogItemEvent> events, CancellationToken ct)
    {
        using var op = _chunkedLogger.BeginOperation(
            "WriteLogItemsBatch",
            inputs: new Dictionary<string, object>
            {
                ["batchSize"] = events.Count,
                ["optimizationMode"] = "token",
                ["hasAttachments"] = events.Any(e => !string.IsNullOrWhiteSpace(e.MetadataJson) && e.MetadataJson.Contains("attachment"))
            });
        try
        {
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Starting batch write of {Count} log items with token optimization", events.Count);

            // Get tokens for all events (pass level and metadataJson for fingerprinting)
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.TokenCreated,
                "Fetching tokens for {Count} log items", events.Count);
            var tokenTasks = events.Select(evt =>
                _logTokenCache.GetOrCreateTokenAsync(evt.Message, evt.Level, evt.MetadataJson, ct));
            var tokens = await Task.WhenAll(tokenTasks);
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.TokenCreated,
                "Retrieved {Count} tokens from cache", tokens.Length);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // Process attachments if present (extract from MetadataJson and store in test_artifacts)
            var attachmentIds = await ProcessAttachmentsAsync(conn, events, ct);

            // Try initial COPY - if successful, return immediately
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Attempting COPY insert for log items");

            if (await TryWriteLogItemsCopyAsync(conn, events, tokens, attachmentIds, ct))
            {
                op.SetOutputs(new Dictionary<string, object>
                {
                    ["insertedCount"] = events.Count,
                    ["attachmentCount"] = attachmentIds.Count(a => a.HasValue),
                    ["retryRequired"] = false,
                    ["success"] = true
                });
                op.Complete();
                return;
            }

            // FK violation - validate tokens exist and retry with valid items only
            _chunkedLogger.LogWarning(
                EventCodes.Ingestion.BatchFailed,
                "FK violation detected - validating tokens and retrying with valid items only");
            var distinctTokens = tokens.Distinct().ToArray();
            var existingTokens = await GetExistingLogTokensAsync(conn, distinctTokens, ct);
            var validSet = new HashSet<string>(existingTokens);

            var validIdx = new List<int>();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (validSet.Contains(tokens[i]))
                {
                    validIdx.Add(i);
                }
            }

            if (validIdx.Count == 0)
            {
                _chunkedLogger.LogError(
                    EventCodes.Ingestion.BatchFailed,
                    "All {Count} log items have missing tokens - skipping batch", events.Count);
                op.SetOutputs(new Dictionary<string, object>
                {
                    ["insertedCount"] = 0,
                    ["retryRequired"] = false,
                    ["skippedAll"] = true,
                    ["success"] = false
                });
                op.Complete();
                return;
            }

            // Retry COPY with valid items
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchStarted,
                "Retrying COPY with {ValidCount} valid log items (skipping {InvalidCount})",
                validIdx.Count, events.Count - validIdx.Count);

            await using var w = await conn.BeginBinaryImportAsync(
                "COPY log_items (id, test_item_uuid, launch_uuid, time, level, token_hash, attachment_id, created_at) FROM STDIN (FORMAT BINARY)",
                ct);

            foreach (var i in validIdx)
            {
                var evt = events[i];
                await w.StartRowAsync(ct);
                await w.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.ItemId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.LaunchId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct);
                await w.WriteAsync(NormalizeLogLevel(evt.Level), NpgsqlDbType.Text, ct);
                await w.WriteAsync(tokens[i], NpgsqlDbType.Text, ct);
                await w.WriteAsync((object?)attachmentIds[i] ?? DBNull.Value, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz, ct);
            }

            await w.CompleteAsync(ct);
            _chunkedLogger.LogMilestone(
                EventCodes.Ingestion.BatchCompleted,
                "Inserted {Valid} log items (skipped {Invalid} with missing tokens)", validIdx.Count,
                events.Count - validIdx.Count);

            op.SetOutputs(new Dictionary<string, object>
            {
                ["insertedCount"] = validIdx.Count,
                ["skippedCount"] = events.Count - validIdx.Count,
                ["retryRequired"] = true,
                ["attachmentCount"] = attachmentIds.Where((id, idx) => id.HasValue && validIdx.Contains(idx)).Count(),
                ["success"] = true
            });
            op.Complete();
        }
        catch (PostgresException ex)
        {
            op.Fail(ex, ErrorType.DependencyFailure, DependencyName.Database);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Database error in WriteLogItemsAsync: {SqlState}", ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            op.Fail(ex, ErrorType.Unexpected);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Unexpected error in WriteLogItemsAsync");
            throw;
        }
    }

    private async Task<Guid?[]> ProcessAttachmentsAsync(NpgsqlConnection conn, List<LogItemEvent> events,
        CancellationToken ct)
    {
        if (!events.Any(e => !string.IsNullOrWhiteSpace(e.MetadataJson)))
            return new Guid?[events.Count];

        using var op = _chunkedLogger.BeginOperation(
            "ProcessAttachments",
            inputs: new Dictionary<string, object> { ["batchSize"] = events.Count });

        var attachmentIds = new Guid?[events.Count];
        var attachmentCount = 0;

        for (var i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (string.IsNullOrWhiteSpace(evt.MetadataJson)) continue;
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(evt.MetadataJson);
                if (metadata != null &&
                    metadata.TryGetValue("attachmentDataBase64", out var dataElement) &&
                    metadata.TryGetValue("attachmentName", out var nameElement) &&
                    metadata.TryGetValue("attachmentMimeType", out var mimeElement))
                {
                    var base64Data = dataElement.GetString();
                    var fileName = nameElement.GetString();
                    var mimeType = mimeElement.GetString();

                    if (!string.IsNullOrWhiteSpace(base64Data) && !string.IsNullOrWhiteSpace(fileName))
                    {
                        var fileData = Convert.FromBase64String(base64Data);
                        var artifactId = await InsertArtifactAsync(conn, evt.ItemId, fileName,
                            mimeType ?? "application/octet-stream", fileData, ct);
                        attachmentIds[i] = artifactId;
                        attachmentCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _chunkedLogger.LogWarning(ex, EventCodes.Ingestion.BatchFailed,
                    "Failed to process attachment metadata for log item {ItemId}", evt.ItemId);
            }
        }

        _chunkedLogger.LogMilestone(
            EventCodes.Ingestion.BatchCompleted,
            "Processed {Count} attachments for log items", attachmentCount);

        op.SetOutputs(new Dictionary<string, object>
        {
            ["attachmentCount"] = attachmentCount,
            ["success"] = true
        });
        op.Complete();

        return attachmentIds;
    }

    private async Task WriteLogItemsLegacyAsync(List<LogItemEvent> events, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY log_items (id, test_item_uuid, launch_uuid, time, level, message, attachment_id, created_at) " +
            "FROM STDIN (FORMAT BINARY)", ct);

        foreach (var evt in events)
        {
            // Normalize log level to uppercase (database constraint expects TRACE|DEBUG|INFO|WARN|ERROR|FATAL)
            var normalizedLevel = NormalizeLogLevel(evt.Level);

            await writer.StartRowAsync(ct);
            await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct); // id
            await writer.WriteAsync(evt.ItemId, NpgsqlDbType.Uuid, ct); // test_item_uuid
            await writer.WriteAsync(evt.LaunchId, NpgsqlDbType.Uuid, ct); // launch_uuid
            await writer.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct); // time
            await writer.WriteAsync(normalizedLevel, NpgsqlDbType.Text, ct); // level
            await writer.WriteAsync(evt.Message, NpgsqlDbType.Text, ct); // message
            await writer.WriteAsync(DBNull.Value, NpgsqlDbType.Uuid, ct); // attachment_id (NULL for now)
            await writer.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz, ct); // created_at
        }

        await writer.CompleteAsync(ct);

        _chunkedLogger.LogMilestone(EventCodes.Ingestion.LogItemsWritten, "Inserted {Count} log items (legacy mode) via COPY", events.Count);
    }

    private static string GetString(Dictionary<string, JsonElement> data, string key, string defaultValue)
    {
        return data.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? defaultValue
            : defaultValue;
    }

    private static string? GetStringOrNull(Dictionary<string, JsonElement> data, string key)
    {
        return data.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }

    private static Guid? GetGuidOrNull(Dictionary<string, JsonElement> data, string key)
    {
        return data.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String &&
               Guid.TryParse(val.GetString(), out var guid)
            ? guid
            : null;
    }

    /// <summary>
    ///     Normalizes log level to uppercase format expected by database constraint.
    ///     Database expects: TRACE, DEBUG, INFO, WARN, ERROR, FATAL
    /// </summary>
    private static string NormalizeLogLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return "INFO";
        }

        var normalized = level.Trim().ToUpperInvariant();

        // Map common variations to standard levels
        return normalized switch
        {
            "TRACE" => "TRACE",
            "DEBUG" => "DEBUG",
            "INFO" or "INFORMATION" => "INFO",
            "WARN" or "WARNING" => "WARN",
            "ERROR" => "ERROR",
            "FATAL" or "CRITICAL" => "FATAL",
            _ => "INFO" // Default to INFO for unknown levels
        };
    }

    /// <summary>
    ///     Inserts attachment file into test_artifacts table and persists file data to local filesystem.
    ///     Creates directory structure and writes file to disk.
    ///     Thread-safe for multiple ingestion service instances via atomic file operations.
    /// </summary>
    private async Task<Guid> InsertArtifactAsync(
        NpgsqlConnection conn,
        Guid testItemId,
        string fileName,
        string contentType,
        byte[] fileData,
        CancellationToken ct)
    {
        var artifactId = Guid.NewGuid();
        // V1 schema: Use simplified 2-level path (no artifacts/ prefix, no middle artifactId/)
        var storagePath = $"{testItemId}/{artifactId}_{fileName}";

        // Determine storage backend (local filesystem or MinIO S3-compatible storage)
        var storageBackend = _config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");

        if (storageBackend == "minio" && _minioStorage != null)
        {
            // Upload to MinIO (S3-compatible object storage)
            await _minioStorage.UploadArtifactAsync(storagePath, fileData, contentType, ct);
            _chunkedLogger.LogInformation(EventCodes.Storage.UploadCompleted, "Uploaded artifact to MinIO: {Path} ({Size} bytes)",
                storagePath, fileData.Length);
        }
        else
        {
            // Fallback to local filesystem storage
            // Get base storage directory from configuration (default: ./data/artifacts)
            // For multi-instance deployments, this should be a shared volume (NFS, EFS, etc.)
            var baseStoragePath = _config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts") ??
                                  "./data/artifacts";
            var fullPath = Path.Combine(baseStoragePath, storagePath);
            var directoryPath = Path.GetDirectoryName(fullPath);

            // Create directory structure if it doesn't exist (thread-safe)
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath); // CreateDirectory is idempotent
            }

            // Write file data to disk atomically (write to temp file, then move)
            // This prevents partial file writes if process crashes mid-write
            var tempPath = fullPath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, fileData, ct);

                // Atomic move - if another instance wrote the file first, this will fail gracefully
                if (!File.Exists(fullPath))
                {
                    File.Move(tempPath, fullPath, false);
                    _chunkedLogger.LogDebug(null, "Wrote artifact file to disk: {Path} ({Size} bytes)", fullPath, fileData.Length);
                }
                else
                {
                    // File already exists (another instance wrote it), delete temp file
                    File.Delete(tempPath);
                    _chunkedLogger.LogDebug(null, "Artifact file already exists (written by another instance): {Path}", fullPath);
                }
            }
            catch (IOException) when (File.Exists(fullPath))
            {
                // File was created by another instance between our check and move - this is OK
                try { File.Delete(tempPath); }
                catch
                {
                    /* Ignore cleanup errors */
                }

                _chunkedLogger.LogDebug(null, "Artifact file written by another instance: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                // Clean up temp file on error
                try { File.Delete(tempPath); }
                catch
                {
                    /* Ignore cleanup errors */
                }

                _chunkedLogger.LogError(ex, EventCodes.Storage.UploadFailed, "Failed to write artifact file to disk: {Path}", fullPath);
                throw;
            }
        }

        // Insert artifact metadata into database (storage_path is relative path)
        var sql = @"
            INSERT INTO test_artifacts (id, test_item_id, file_name, content_type, file_size, storage_path, uploaded_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(artifactId);
        cmd.Parameters.AddWithValue(testItemId);
        cmd.Parameters.AddWithValue(fileName);
        cmd.Parameters.AddWithValue(contentType);
        cmd.Parameters.AddWithValue((long)fileData.Length);
        cmd.Parameters.AddWithValue(storagePath); // Relative path (works with shared volumes)
        cmd.Parameters.AddWithValue(DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);

        _chunkedLogger.LogInformation(
            EventCodes.TestItem.ArtifactUploaded,
            "Created test artifact {ArtifactId} for test item {TestItemId}: {FileName} ({Size} bytes)",
            artifactId, testItemId, fileName, fileData.Length);

        return artifactId;
    }

    private async Task<bool> TryWriteLogItemsCopyAsync(NpgsqlConnection conn, List<LogItemEvent> events,
        string[] tokens, Guid?[] attachmentIds, CancellationToken ct)
    {
        try
        {
            await using var w = await conn.BeginBinaryImportAsync(
                "COPY log_items (id, test_item_uuid, launch_uuid, time, level, token_hash, attachment_id, created_at) FROM STDIN (FORMAT BINARY)",
                ct);

            for (var i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                await w.StartRowAsync(ct);
                await w.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.ItemId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.LaunchId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct);
                await w.WriteAsync(NormalizeLogLevel(evt.Level), NpgsqlDbType.Text, ct);
                await w.WriteAsync(tokens[i], NpgsqlDbType.Text, ct);
                await w.WriteAsync((object?)attachmentIds[i] ?? DBNull.Value, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz, ct);
            }

            await w.CompleteAsync(ct);
            _chunkedLogger.LogMilestone(EventCodes.Ingestion.LogItemsWritten, "Inserted {Count} log items with token optimization via COPY", events.Count);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _chunkedLogger.LogWarning(ex, EventCodes.Ingestion.BatchFailed, "FK violation inserting log items - will retry with valid tokens only");
            return false;
        }
    }

    private async Task<List<string>> GetExistingLogTokensAsync(NpgsqlConnection conn, string[] tokenHashes,
        CancellationToken ct)
    {
        var sql = "SELECT token_hash FROM log_tokens WHERE token_hash = ANY($1)";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(tokenHashes);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private async Task<bool> TryWriteCommandsCopyAsync(NpgsqlConnection conn,
        List<(CommandEvent evt, Dictionary<string, JsonElement> data, string? message, string? kind)> parsedEvents,
        string[] tokens, CancellationToken ct)
    {
        try
        {
            await using var w = await conn.BeginBinaryImportAsync(
                "COPY commands (run_id, timestamp_utc, kind, token_hash, props_json, test_id, expires_at) FROM STDIN (FORMAT BINARY)",
                ct);

            for (var i = 0; i < parsedEvents.Count; i++)
            {
                var (evt, data, _, kind) = parsedEvents[i];
                await w.StartRowAsync(ct);
                await w.WriteAsync(Guid.Parse(evt.RunId), NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(evt.TimestampUtc, NpgsqlDbType.TimestampTz, ct);
                await w.WriteAsync((object?)kind ?? DBNull.Value, NpgsqlDbType.Text, ct);
                await w.WriteAsync(tokens[i], NpgsqlDbType.Text, ct);
                await w.WriteAsync((object?)GetStringOrNull(data, "propsJson") ?? DBNull.Value, NpgsqlDbType.Text, ct);
                await w.WriteAsync((object?)GetStringOrNull(data, "testId") ?? DBNull.Value, NpgsqlDbType.Text, ct);
                await w.WriteAsync(DBNull.Value, NpgsqlDbType.TimestampTz, ct);
            }

            await w.CompleteAsync(ct);
            _chunkedLogger.LogMilestone(EventCodes.Ingestion.CommandsWritten, "Inserted {Count} commands with token optimization via COPY", parsedEvents.Count);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _chunkedLogger.LogWarning(ex, EventCodes.Ingestion.BatchFailed, "FK violation inserting commands - will retry with valid tokens only");
            return false;
        }
    }

    private async Task<List<string>> GetExistingCommandTokensAsync(NpgsqlConnection conn, string[] tokenHashes,
        CancellationToken ct)
    {
        var sql = "SELECT token_hash FROM command_tokens WHERE token_hash = ANY($1)";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(tokenHashes);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    /// <summary>
    ///     Writes audit entries in batch using PostgreSQL COPY BINARY for high throughput.
    /// </summary>
    public async Task WriteAuditEntriesAsync(List<AuditEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return;
        }

        using var op = _chunkedLogger.BeginOperation(
            "WriteAuditEntriesBatch",
            inputs: new Dictionary<string, object>
            {
                ["batchSize"] = events.Count
            });

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            const string copySql = @"
            COPY audit_entries (timestamp, category, action, actor, remote_ip, correlation_id, severity, details)
            FROM STDIN (FORMAT BINARY)";

            await using var writer = await conn.BeginBinaryImportAsync(copySql, ct);

            var skippedCount = 0;
            var insertedCount = 0;

            foreach (var evt in events)
            {
                // Skip events with missing required fields
                if (string.IsNullOrWhiteSpace(evt.Category) || string.IsNullOrWhiteSpace(evt.Action))
                {
                    _chunkedLogger.LogWarning(EventCodes.Ingestion.BatchFailed, "Skipping audit event {CorrelationId} with missing category or action",
                        evt.CorrelationId ?? "N/A");
                    skippedCount++;
                    continue;
                }

                try
                {
                    await writer.StartRowAsync(ct);

                    // Ensure timestamp is UTC for PostgreSQL timestamptz
                    var timestampUtc = evt.Timestamp.Kind == DateTimeKind.Utc
                        ? evt.Timestamp
                        : evt.Timestamp.ToUniversalTime();
                    await writer.WriteAsync(timestampUtc, NpgsqlDbType.TimestampTz, ct);

                    await writer.WriteAsync(evt.Category, NpgsqlDbType.Text, ct);
                    await writer.WriteAsync(evt.Action, NpgsqlDbType.Text, ct);
                    await writer.WriteAsync((object?)evt.Actor ?? DBNull.Value, NpgsqlDbType.Text, ct);
                    await writer.WriteAsync((object?)evt.RemoteIp ?? DBNull.Value, NpgsqlDbType.Text, ct);
                    await writer.WriteAsync((object?)evt.CorrelationId ?? DBNull.Value, NpgsqlDbType.Text, ct);
                    await writer.WriteAsync(evt.Severity ?? "Info", NpgsqlDbType.Text, ct);

                    // Serialize details dictionary to JSONB
                    var detailsJson = evt.Details != null
                        ? JsonSerializer.Serialize(evt.Details)
                        : "{}";
                    await writer.WriteAsync(detailsJson, NpgsqlDbType.Jsonb, ct);

                    insertedCount++;
                }
                catch (Exception ex)
                {
                    _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Failed to write audit event {CorrelationId} to COPY stream", evt.CorrelationId ?? "N/A");
                    skippedCount++;
                }
            }

            await writer.CompleteAsync(ct);

            if (skippedCount > 0)
            {
                _chunkedLogger.LogWarning(EventCodes.EventPublisher.AuditPublished, "Inserted {Inserted} audit entries via COPY, skipped {Skipped} invalid events",
                    insertedCount, skippedCount);
            }
            else
            {
                _chunkedLogger.LogDebug(EventCodes.EventPublisher.AuditPublished, "Inserted {Count} audit entries via COPY", insertedCount);
            }

            op.Complete();
        }
        catch (Exception ex)
        {
            op.Fail(ex, ErrorType.Unexpected);
            _chunkedLogger.LogError(ex, EventCodes.Ingestion.BatchFailed, "Unexpected error in WriteAuditEntriesAsync");
            throw;
        }
    }
}
