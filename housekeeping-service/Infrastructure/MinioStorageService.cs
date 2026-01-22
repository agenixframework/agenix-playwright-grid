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

using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;

namespace HousekeepingService.Infrastructure;

/// <summary>
///     MinIO-based artifact storage service with S3-compatible API.
///     Handles file uploads, downloads, and pre-signed URL generation for test artifacts.
/// </summary>
public class MinioStorageService : IMinioStorageService
{
    private readonly string _bucketName;
    private readonly IMinioClient _client;
    private readonly ChunkedLogger<MinioStorageService> _logger;

    public MinioStorageService(IConfiguration config, ChunkedLogger<MinioStorageService> logger)
        : this(config, logger, null)
    {
    }

    internal MinioStorageService(IConfiguration config, ChunkedLogger<MinioStorageService> logger, IMinioClient? client)
    {
        _logger = logger;
        _bucketName = config.GetValue("MINIO_BUCKET_NAME", "playwright-artifacts")!;

        var endpoint = config["MINIO_ENDPOINT"] ?? "localhost:9000";
        var accessKey = config["MINIO_ACCESS_KEY"] ?? "minioadmin";
        var secretKey = config["MINIO_SECRET_KEY"] ?? "minioadmin";
        var useSSL = config.GetValue("MINIO_USE_SSL", false);

        _client = client ?? new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(useSSL)
            .Build();

        _logger.LogMilestone(EventCodes.Housekeeping.BootstrapCompleted, "MinIO client initialized: {Endpoint}, bucket: {Bucket}, SSL: {UseSSL}",
            endpoint, _bucketName, useSSL);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _logger.LogDebug(null, "Client disposed");
    }

    /// <summary>
    ///     Uploads file data to MinIO and returns the storage path (object key).
    /// </summary>
    /// <param name="objectKey">Object key/path in bucket (e.g., "artifacts/123/456/screenshot.png")</param>
    /// <param name="fileData">File content as byte array</param>
    /// <param name="contentType">MIME type (e.g., "image/png", "video/webm")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Object key (same as input)</returns>
    public async Task<string> UploadArtifactAsync(
        string objectKey,
        byte[] fileData,
        string contentType,
        CancellationToken ct = default)
    {
        try
        {
            using var stream = new MemoryStream(fileData);

            var args = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(fileData.Length)
                .WithContentType(contentType);

            await _client.PutObjectAsync(args, ct);


            return objectKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Storage.UploadFailed, "Failed to upload artifact: {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    ///     Downloads file data from MinIO.
    /// </summary>
    /// <param name="objectKey">Object key/path in bucket</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>File content as byte array</returns>
    public async Task<byte[]> DownloadArtifactAsync(
        string objectKey,
        CancellationToken ct = default)
    {
        try
        {
            using var memoryStream = new MemoryStream();

            var args = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectKey)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _client.GetObjectAsync(args, ct);

            _logger.LogDebug(null, "Downloaded artifact: {ObjectKey} ({Size} bytes)",
                objectKey, memoryStream.Length);

            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Storage.DownloadFailed, "Failed to download artifact: {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    ///     Generates a pre-signed URL for secure, time-limited artifact access.
    ///     Useful for allowing clients to download artifacts directly from MinIO without proxying through the API.
    /// </summary>
    /// <param name="objectKey">Object key/path in bucket</param>
    /// <param name="expirySeconds">URL expiry time in seconds (default: 1 hour)</param>
    /// <param name="inline">If true, sets Content-Disposition to inline for browser viewing</param>
    /// <param name="fileName">Optional filename for Content-Disposition header</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Pre-signed URL (valid for expirySeconds)</returns>
    public async Task<string> GetPresignedUrlAsync(
        string objectKey,
        int expirySeconds = 3600,
        bool inline = false,
        string? fileName = null,
        CancellationToken ct = default)
    {
        try
        {
            var args = new PresignedGetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectKey)
                .WithExpiry(expirySeconds);

            // Set the Content-Disposition response header for inline viewing or attachment download
            if (inline || !string.IsNullOrEmpty(fileName))
            {
                var disposition = inline ? "inline" : "attachment";
                if (!string.IsNullOrEmpty(fileName))
                {
                    disposition += $"; filename=\"{fileName}\"";
                }

                args.WithHeaders(new Dictionary<string, string> { ["response-content-disposition"] = disposition });
            }

            var url = await _client.PresignedGetObjectAsync(args);

            _logger.LogDebug(null, "Generated pre-signed URL for {ObjectKey}, expires in {Expiry}s, inline={Inline}",
                objectKey, expirySeconds, inline);

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Storage.UrlGenerationFailed, "Failed to generate pre-signed URL for {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    ///     Deletes artifact from MinIO.
    /// </summary>
    /// <param name="objectKey">Object key/path in bucket</param>
    /// <param name="ct">Cancellation token</param>
    public async Task DeleteArtifactAsync(
        string objectKey,
        CancellationToken ct = default)
    {
        try
        {
            var args = new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectKey);

            await _client.RemoveObjectAsync(args, ct);

            _logger.LogMilestone(EventCodes.Storage.DeleteCompleted, "Deleted artifact: {ObjectKey}", objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Storage.DeleteFailed, "Failed to delete artifact: {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    ///     Checks if a bucket exists and creates it if not (idempotent).
    ///     Should be called on service startup to ensure the bucket is available.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    public async Task EnsureBucketExistsAsync(CancellationToken ct = default)
    {
        try
        {
            var existsArgs = new BucketExistsArgs().WithBucket(_bucketName);
            var exists = await _client.BucketExistsAsync(existsArgs, ct);

            if (!exists)
            {
                var makeArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _client.MakeBucketAsync(makeArgs, ct);
                _logger.LogMilestone(EventCodes.Housekeeping.BootstrapCompleted, "Created bucket: {Bucket}", _bucketName);
            }
            else
            {
                _logger.LogDebug(null, "Bucket already exists: {Bucket}", _bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, EventCodes.Housekeeping.BootstrapCompleted, "Failed to ensure bucket exists: {Bucket}", _bucketName);
            throw;
        }
    }
}
