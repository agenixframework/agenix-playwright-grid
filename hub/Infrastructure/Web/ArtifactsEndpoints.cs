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

using System.IO.Compression;
using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Mvc;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;
using PlaywrightHub.Infrastructure.Caching;
using ArtifactMetadata = PlaywrightHub.Infrastructure.Caching.ArtifactMetadata;

namespace PlaywrightHub.Infrastructure.Web;

public static class ArtifactsEndpoints
{
    public static void MapArtifactsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/artifacts")
            .WithTags("Artifacts")
            .WithOpenApi();

        // POST /api/test-items/{testItemId}/artifacts - Upload artifact for test item
        routes.MapPost("/api/test-items/{testItemId:guid}/artifacts", UploadArtifact)
            .WithTags("Artifacts")
            .WithName("UploadArtifact")
            .WithSummary("Upload artifact for test item (async processing)")
            .WithOpenApi()
            .Produces<ArtifactUploadResponse>(202)
            .Produces(400)
            .Produces(404)
            .DisableAntiforgery(); // Disable for multipart form uploads

        // GET /api/artifacts/{id} - Download artifact
        group.MapGet("/{id:guid}", DownloadArtifact)
            .WithName("DownloadArtifact")
            .WithSummary("Download artifact file")
            .Produces<FileStreamResult>()
            .Produces(404);

        // GET /api/artifacts/{id}/url - Get pre-signed download URL
        group.MapGet("/{id:guid}/url", GetArtifactUrl)
            .WithName("GetArtifactUrl")
            .WithSummary("Get pre-signed URL for artifact download (MinIO only)")
            .Produces<ArtifactUrlResponse>()
            .Produces(400)
            .Produces(404);

        // GET /api/test-items/{testItemId}/artifacts - List artifacts for test item
        routes.MapGet("/api/test-items/{testItemId:guid}/artifacts", ListArtifactsForTestItem)
            .WithTags("Artifacts")
            .WithName("ListArtifactsForTestItem")
            .WithSummary("List all artifacts attached to a test item")
            .WithOpenApi()
            .Produces<List<TestAttachmentDto>>()
            .Produces(404);

        // GET /api/test-items/{testItemId}/artifacts/download-zip - Download all artifacts as ZIP
        routes.MapGet("/api/test-items/{testItemId:guid}/artifacts/download-zip", DownloadArtifactsAsZip)
            .WithTags("Artifacts")
            .WithName("DownloadArtifactsAsZip")
            .WithSummary("Download all artifacts for a test item as a ZIP file")
            .WithOpenApi()
            .Produces(200, contentType: "application/zip")
            .Produces(404);
    }

    private static async Task<IResult> UploadArtifact(
        Guid testItemId,
        HttpRequest request,
        [FromServices] IResultsStore store,
        [FromServices] IEventPublisher eventPublisher,
        [FromServices] IConfiguration config,
        [FromHeader(Name = "X-Project-Key")] string? projectKeyHeader,
        [FromServices] ILogger<IResultsStore> logger)
    {
        var chunkedLogger = ArtifactsEndpointsWrapper.GetLogger(logger);
        using var op = chunkedLogger.BeginOperation("UploadArtifact", new Dictionary<string, object>
        {
            ["TestItemId"] = testItemId,
            ["ProjectKey"] = projectKeyHeader ?? "unknown"
        });

        chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadStarted,
            "testItemId={TestItemId} projectKey={ProjectKey}",
            testItemId, projectKeyHeader ?? "unknown");

        // Verify test item exists
        var testItem = await store.GetTestItemAsync(testItemId);
        if (testItem == null)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadFailed,
                "error=TestItemNotFound testItemId={TestItemId}", testItemId);
            return ProblemDetailsHelpers.NotFound(
                $"Test item {testItemId} not found",
                eventCode: EventCodes.TestItem.TestItemNotFound,
                instance: request.Path,
                traceId: request.HttpContext.TraceIdentifier);
        }

        // Get project key from header or default to "unknown"
        var projectKey = projectKeyHeader ?? "unknown";

        // Read multipart form data
        if (!request.HasFormContentType)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadFailed,
                "error=InvalidContentType");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["Content-Type"] = ["Request must be multipart/form-data"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: request.Path,
                traceId: request.HttpContext.TraceIdentifier);
        }

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadFailed,
                "error=NoFileProvided");
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["file"] = ["No file uploaded"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: request.Path,
                traceId: request.HttpContext.TraceIdentifier);
        }

        // Validate file size
        var maxSizeMb = config.GetValue("AGENIX_ARTIFACTS_MAX_SIZE_MB", 100);
        var maxSizeBytes = maxSizeMb * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadFailed,
                "error=FileTooLarge size={Size} maxSize={MaxSize}", file.Length, maxSizeBytes);
            return ProblemDetailsHelpers.PayloadTooLarge(
                $"File too large. Maximum size is {maxSizeMb}MB",
                eventCode: EventCodes.Artifacts.ArtifactUploadFailed,
                instance: request.Path,
                traceId: request.HttpContext.TraceIdentifier);
        }

        // Read file content
        byte[] content;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            content = ms.ToArray();
        }

        // Create artifact metadata with "pending" status
        var artifactId = await store.CreateArtifactMetadataAsync(
            testItemId,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length,
            projectKey
        );

        // Publish event for async processing
        var uploadEvent = new ArtifactUploadEvent(
            artifactId,
            testItemId,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length,
            content,
            DateTime.UtcNow,
            projectKey
        );

        try
        {
            await eventPublisher.PublishArtifactUploadEventAsync(uploadEvent);

            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadCompleted,
                "artifactId={ArtifactId} size={Size} testItemId={TestItemId}",
                artifactId, file.Length, testItemId);

            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploaded,
                "artifactId={ArtifactId} filename={FileName}",
                artifactId, file.FileName);

            return Results.Accepted(
                $"/api/artifacts/{artifactId}",
                new ArtifactUploadResponse
                {
                    ArtifactId = artifactId,
                    TestItemId = testItemId,
                    FileName = file.FileName,
                    FileSize = file.Length,
                    Status = "pending",
                    Message = "Artifact upload queued for processing"
                });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUploadFailed, ex,
                "error=PublishFailed artifactId={ArtifactId}", artifactId);

            return ProblemDetailsHelpers.InternalServerError(
                "Failed to queue artifact upload",
                eventCode: EventCodes.Artifacts.ArtifactUploadFailed,
                instance: request.Path,
                traceId: request.HttpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> ListArtifactsForTestItem(
        Guid testItemId,
        [FromServices] IResultsStore store,
        [FromServices] ILogger<IResultsStore> logger)
    {
        var chunkedLogger = ArtifactsEndpointsWrapper.GetLogger(logger);
        using var op = chunkedLogger.BeginOperation("ListArtifactsForTestItem", new Dictionary<string, object>
        {
            ["TestItemId"] = testItemId
        });

        chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactListed,
            "testItemId={TestItemId}", testItemId);

        // Use the existing GetArtifactsForTestAsync method
        // The method signature uses (runId, testId) but V1 schema only uses test_item_id
        // Pass testItemId as runId parameter, testId is ignored
        var artifacts = await store.GetArtifactsForTestAsync(testItemId.ToString(), string.Empty);

        if (artifacts == null || artifacts.Count == 0)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactListedBatch,
                "count=0 testItemId={TestItemId}", testItemId);
            return Results.Ok(new List<TestAttachmentDto>());
        }

        chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactListedBatch,
            "count={Count} testItemId={TestItemId}", artifacts.Count, testItemId);

        return Results.Ok(artifacts);
    }

    private static async Task<IResult> DownloadArtifact(
        Guid id,
        [FromServices] IResultsStore store,
        [FromServices] IConfiguration config,
        [FromServices] MinioStorageService? minioStorage,
        [FromServices] RedisArtifactCache? cache,
        [FromServices] ILogger<IResultsStore> logger,
        HttpContext httpContext,
        [FromQuery] bool inline = false)
    {
        var chunkedLogger = ArtifactsEndpointsWrapper.GetLogger(logger);
        using var op = chunkedLogger.BeginOperation("DownloadArtifact", new Dictionary<string, object>
        {
            ["ArtifactId"] = id,
            ["Inline"] = inline
        });

        chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloadStarted,
            "artifactId={ArtifactId} inline={Inline}", id, inline);

        var cacheEnabled = config.GetValue("AGENIX_ARTIFACTS_CACHE_ENABLED", true);
        var storageBackend = config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");

        // 1. Try to get metadata from cache first (avoid DB query)
        ArtifactMetadata? metadata = null;
        if (cacheEnabled && cache != null)
        {
            metadata = await cache.GetMetadataAsync(id);
        }

        if (metadata == null)
        {
            // Cache miss or disabled - query database
            var artifact = await store.GetArtifactAsync(id);
            if (artifact == null)
            {
                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloadFailed,
                    "error=ArtifactNotFound artifactId={ArtifactId}", id);
                return ProblemDetailsHelpers.NotFound(
                    $"Artifact {id} not found",
                    eventCode: EventCodes.Artifacts.ArtifactDownloadFailed,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            metadata = new ArtifactMetadata(
                artifact.Id,
                artifact.FileName,
                artifact.ContentType,
                artifact.FileSize,
                artifact.UploadedAt,
                artifact.StoragePath
            );

            // Cache metadata for future requests
            if (cacheEnabled && cache != null)
            {
                await cache.SetMetadataAsync(id, metadata);
                await cache.IncrementMissAsync();
                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactCacheMiss,
                    "type=Metadata artifactId={ArtifactId}", id);
            }
        }
        else if (cacheEnabled && cache != null)
        {
            await cache.IncrementHitAsync();
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactCacheHit,
                "type=Metadata artifactId={ArtifactId}", id);
        }

        // 2. ETag support (304 Not Modified)
        var etag = $"\"{id}-{metadata.UploadedAt.Ticks}\"";
        if (httpContext.Request.Headers["If-None-Match"] == etag)
        {
            httpContext.Response.Headers["Cache-Control"] = "public, max-age=3600, immutable";
            httpContext.Response.Headers["ETag"] = etag;
            return Results.StatusCode(304); // Not Modified
        }

        // 3. Check if should cache content in Redis (small files < 5MB)
        if (cacheEnabled && cache != null && cache.ShouldCacheContent(metadata.FileSize))
        {
            // Try Redis cache first
            var cachedContent = await cache.GetContentAsync(id);
            if (cachedContent != null)
            {
                await cache.AddBytesServedAsync(cachedContent.Length);
                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactCacheHit,
                    "type=Content artifactId={ArtifactId} size={Size}", id, cachedContent.Length);

                httpContext.Response.Headers["Cache-Control"] = "public, max-age=3600, immutable";
                httpContext.Response.Headers["ETag"] = etag;

                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                    "artifactId={ArtifactId} source=RedisCache size={Size}", id, cachedContent.Length);

                if (inline)
                {
                    // For inline viewing, write response manually
                    // Override content type if it's generic octet-stream - detect from file extension
                    var contentType = metadata.ContentType;
                    if (contentType == "application/octet-stream" || string.IsNullOrEmpty(contentType))
                    {
                        contentType = GetContentTypeFromFileName(metadata.FileName) ?? "application/octet-stream";
                    }

                    httpContext.Response.ContentType = contentType;
                    httpContext.Response.Headers["Content-Disposition"] = $"inline; filename=\"{metadata.FileName}\"";
                    await httpContext.Response.Body.WriteAsync(cachedContent);
                    return Results.Empty;
                }

                return Results.File(cachedContent, metadata.ContentType, metadata.FileName, true);
            }

            // Cache miss - download and cache
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactCacheMiss,
                "type=Content artifactId={ArtifactId}", id);

            byte[] fileData;
            if (storageBackend == "minio" && minioStorage != null)
            {
                try
                {
                    fileData = await minioStorage.DownloadArtifactAsync(metadata.StoragePath);
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError, ex,
                        "error=MinioDownloadFailed artifactId={ArtifactId}", id);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to download artifact from MinIO.",
                        eventCode: EventCodes.Artifacts.ArtifactStorageError,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }
            }
            else
            {
                var baseStoragePath = config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts") ??
                                      "./data/artifacts";
                var fullPath = Path.Combine(baseStoragePath, metadata.StoragePath);

                if (!File.Exists(fullPath))
                {
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError,
                        "error=FileNotFoundOnDisk artifactId={ArtifactId} path={Path}", id, fullPath);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Artifact file not found on disk.",
                        eventCode: EventCodes.Artifacts.ArtifactStorageError,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }

                fileData = await File.ReadAllBytesAsync(fullPath);
            }

            // Store in Redis cache
            await cache.SetContentAsync(id, fileData);
            await cache.AddBytesServedAsync(fileData.Length);

            httpContext.Response.Headers["Cache-Control"] = "public, max-age=3600, immutable";
            httpContext.Response.Headers["ETag"] = etag;

            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                "artifactId={ArtifactId} source={Backend} size={Size}", id, storageBackend!, fileData.Length);

            if (inline)
            {
                // For inline viewing, write response manually
                // Override content type if it's generic octet-stream - detect from file extension
                var contentType = metadata.ContentType;
                if (contentType == "application/octet-stream" || string.IsNullOrEmpty(contentType))
                {
                    contentType = GetContentTypeFromFileName(metadata.FileName) ?? "application/octet-stream";
                }

                httpContext.Response.ContentType = contentType;
                httpContext.Response.Headers["Content-Disposition"] = $"inline; filename=\"{metadata.FileName}\"";
                await httpContext.Response.Body.WriteAsync(fileData);
                return Results.Empty;
            }

            return Results.File(fileData, metadata.ContentType, metadata.FileName, true);
        }

        // 4. Large files (> 5MB) or cache disabled - stream or redirect to MinIO
        if (storageBackend == "minio" && minioStorage != null)
        {
            // For large files with MinIO, use pre-signed URL redirect
            if (cacheEnabled && cache != null && !inline)
            {
                // Only use cached URLs for download (not inline viewing) since cache doesn't track disposition
                var presignedUrl = await cache.GetPresignedUrlAsync(id);
                if (presignedUrl != null && presignedUrl.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
                {
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactCacheHit,
                        "type=PresignedUrl artifactId={ArtifactId}", id);
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                        "artifactId={ArtifactId} source=MinioRedirectCached", id);
                    return Results.Redirect(presignedUrl.Url);
                }

                // Generate new pre-signed URL for download
                try
                {
                    var url = await minioStorage.GetPresignedUrlAsync(
                        metadata.StoragePath,
                        3600,
                        false,
                        metadata.FileName);
                    await cache.SetPresignedUrlAsync(id, url, DateTime.UtcNow.AddSeconds(3600));
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUrlGenerated,
                        "type=Download artifactId={ArtifactId}", id);
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                        "artifactId={ArtifactId} source=MinioRedirectNew", id);
                    return Results.Redirect(url);
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError, ex,
                        "error=PresignedUrlGenerationFailed artifactId={ArtifactId}", id);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to generate pre-signed URL.",
                        eventCode: EventCodes.Artifacts.ArtifactStorageError,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }
            }

            // Generate pre-signed URL for inline viewing (not cached)
            if (inline)
            {
                try
                {
                    var url = await minioStorage.GetPresignedUrlAsync(
                        metadata.StoragePath,
                        3600,
                        true,
                        metadata.FileName);
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUrlGenerated,
                        "type=Inline artifactId={ArtifactId}", id);
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                        "artifactId={ArtifactId} source=MinioInlineRedirect", id);
                    return Results.Redirect(url);
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError, ex,
                        "error=PresignedUrlGenerationFailedInline artifactId={ArtifactId}", id);
                    return ProblemDetailsHelpers.InternalServerError(
                        "Failed to generate pre-signed URL.",
                        eventCode: EventCodes.Artifacts.ArtifactStorageError,
                        instance: httpContext.Request.Path,
                        traceId: httpContext.TraceIdentifier);
                }
            }

            // Cache disabled - direct download
            try
            {
                var fileData = await minioStorage.DownloadArtifactAsync(metadata.StoragePath);
                httpContext.Response.Headers["Cache-Control"] = "public, max-age=3600, immutable";
                httpContext.Response.Headers["ETag"] = etag;

                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                    "artifactId={ArtifactId} source=MinioDirect size={Size}", id, fileData.Length);

                if (inline)
                {
                    // For inline viewing, write response manually
                    // Override content type if it's generic octet-stream - detect from file extension
                    var contentType = metadata.ContentType;
                    if (contentType == "application/octet-stream" || string.IsNullOrEmpty(contentType))
                    {
                        contentType = GetContentTypeFromFileName(metadata.FileName) ?? "application/octet-stream";
                    }

                    httpContext.Response.ContentType = contentType;
                    httpContext.Response.Headers["Content-Disposition"] = $"inline; filename=\"{metadata.FileName}\"";
                    await httpContext.Response.Body.WriteAsync(fileData);
                    return Results.Empty;
                }

                return Results.File(fileData, metadata.ContentType, metadata.FileName);
            }
            catch (Exception ex)
            {
                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError, ex,
                    "error=MinioDownloadFailed artifactId={ArtifactId}", id);
                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to download artifact from MinIO.",
                    eventCode: EventCodes.Artifacts.ArtifactStorageError,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }
        }

        {
            // Local storage - stream file (no caching for large files)
            var baseStoragePath = config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts") ??
                                  "./data/artifacts";
            var fullPath = Path.Combine(baseStoragePath, metadata.StoragePath);

            if (!File.Exists(fullPath))
            {
                chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError,
                    "error=FileNotFoundOnDisk artifactId={ArtifactId} path={Path}", id, fullPath);
                return ProblemDetailsHelpers.InternalServerError(
                    "Artifact file not found on disk.",
                    eventCode: EventCodes.Artifacts.ArtifactStorageError,
                    instance: httpContext.Request.Path,
                    traceId: httpContext.TraceIdentifier);
            }

            var fileStream = File.OpenRead(fullPath);
            httpContext.Response.Headers["Cache-Control"] = "public, max-age=3600, immutable";
            httpContext.Response.Headers["ETag"] = etag;

            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloaded,
                "artifactId={ArtifactId} source=LocalStorageStream size={Size}", id, metadata.FileSize);

            // When inline=true, don't pass filename to Results.File() - it won't set Content-Disposition
            // When inline=false, pass filename - it will set Content-Disposition: attachment
            if (inline)
            {
                // For inline viewing, we need to write to response manually
                // Override content type if it's generic octet-stream - detect from file extension
                var contentType = metadata.ContentType;
                if (contentType == "application/octet-stream" || string.IsNullOrEmpty(contentType))
                {
                    contentType = GetContentTypeFromFileName(metadata.FileName) ?? "application/octet-stream";
                }

                httpContext.Response.ContentType = contentType;
                httpContext.Response.Headers["Content-Disposition"] = $"inline; filename=\"{metadata.FileName}\"";
                await fileStream.CopyToAsync(httpContext.Response.Body);
                await fileStream.DisposeAsync();
                return Results.Empty;
            }

            return Results.File(fileStream, metadata.ContentType, metadata.FileName, enableRangeProcessing: true);
        }
    }

    /// <summary>
    ///     Gets the correct MIME type based on file extension.
    ///     Used to override generic application/octet-stream content types for inline viewing.
    /// </summary>
    private static string? GetContentTypeFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            // Images
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".tiff" or ".tif" => "image/tiff",

            // Documents
            ".pdf" => "application/pdf",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".md" or ".markdown" => "text/markdown",
            ".log" => "text/plain",
            ".rtf" => "application/rtf",

            // Videos
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".ogv" => "video/ogg",

            // Audio
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",

            // Code/Programming
            ".js" => "text/javascript",
            ".mjs" => "text/javascript",
            ".css" => "text/css",
            ".ts" => "text/typescript",
            ".tsx" => "text/typescript",
            ".jsx" => "text/javascript",
            ".py" => "text/x-python",
            ".java" => "text/x-java",
            ".c" => "text/x-c",
            ".cpp" or ".cc" or ".cxx" => "text/x-c++",
            ".h" or ".hpp" => "text/x-c++",
            ".cs" => "text/x-csharp",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".php" => "text/x-php",
            ".rb" => "text/x-ruby",
            ".swift" => "text/x-swift",
            ".kt" or ".kts" => "text/x-kotlin",
            ".sh" or ".bash" => "text/x-shellscript",
            ".yaml" or ".yml" => "text/yaml",
            ".toml" => "text/toml",
            ".ini" => "text/plain",
            ".conf" => "text/plain",
            ".sql" => "text/x-sql",

            // Markup/Data
            ".xhtml" => "application/xhtml+xml",
            ".rss" => "application/rss+xml",
            ".atom" => "application/atom+xml",

            // Archives (these will download, not inline)
            ".zip" => "application/zip",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".7z" => "application/x-7z-compressed",
            ".rar" => "application/x-rar-compressed",
            ".bz2" => "application/x-bzip2",

            // Office Documents (these will download, not inline)
            ".doc" or ".docx" => "application/msword",
            ".xls" or ".xlsx" => "application/vnd.ms-excel",
            ".ppt" or ".pptx" => "application/vnd.ms-powerpoint",

            _ => null
        };
    }

    private static async Task<IResult> GetArtifactUrl(
        Guid id,
        [FromServices] IResultsStore store,
        [FromServices] IConfiguration config,
        [FromServices] MinioStorageService? minioStorage,
        [FromServices] ILogger<IResultsStore> logger,
        HttpContext httpContext,
        [FromQuery] int expirySeconds = 3600,
        [FromQuery] bool inline = false)
    {
        var chunkedLogger = ArtifactsEndpointsWrapper.GetLogger(logger);
        using var op = chunkedLogger.BeginOperation("GetArtifactUrl", new Dictionary<string, object>
        {
            ["ArtifactId"] = id,
            ["ExpirySeconds"] = expirySeconds,
            ["Inline"] = inline
        });

        // Get artifact metadata from database
        var artifact = await store.GetArtifactAsync(id);
        if (artifact == null)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError,
                "error=ArtifactNotFound artifactId={ArtifactId}", id);
            return ProblemDetailsHelpers.NotFound(
                $"Artifact {id} not found",
                eventCode: EventCodes.Artifacts.ArtifactDownloadFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        var storageBackend = config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");

        if (storageBackend != "minio" || minioStorage == null)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError,
                "error=MinioNotConfigured artifactId={ArtifactId}", id);
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["storage"] = ["Pre-signed URLs are only available when MinIO storage backend is enabled. Hint: Set AGENIX_ARTIFACTS_STORAGE_BACKEND=minio in configuration"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        // Validate expiry range (1 minute to 7 days)
        if (expirySeconds < 60 || expirySeconds > 604800)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError,
                "error=InvalidExpiry expiry={Expiry} artifactId={ArtifactId}", expirySeconds, id);
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["expirySeconds"] = ["Invalid expiry seconds. Must be between 60 (1 minute) and 604800 (7 days)"] },
                eventCode: EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        try
        {
            var url = await minioStorage.GetPresignedUrlAsync(
                artifact.StoragePath,
                expirySeconds,
                inline,
                inline ? artifact.FileName : artifact.FileName);

            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactUrlGenerated,
                "artifactId={ArtifactId} url={Url} expiry={Expiry}", id, url, expirySeconds);

            return Results.Ok(new ArtifactUrlResponse
            {
                ArtifactId = id,
                Url = url,
                ExpiresInSeconds = expirySeconds,
                ExpiresAt = DateTime.UtcNow.AddSeconds(expirySeconds),
                FileName = artifact.FileName,
                ContentType = artifact.ContentType,
                FileSize = artifact.FileSize
            });
        }
        catch (Exception ex)
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactStorageError, ex,
                "error=PresignedUrlGenerationFailed artifactId={ArtifactId}", id);
            return ProblemDetailsHelpers.InternalServerError(
                "Failed to generate pre-signed URL.",
                eventCode: EventCodes.Artifacts.ArtifactStorageError,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> DownloadArtifactsAsZip(
        Guid testItemId,
        [FromServices] IResultsStore store,
        [FromServices] IConfiguration config,
        [FromServices] ILogger<IResultsStore> logger,
        HttpContext httpContext)
    {
        var chunkedLogger = ArtifactsEndpointsWrapper.GetLogger(logger);
        using var op = chunkedLogger.BeginOperation("DownloadArtifactsAsZip", new Dictionary<string, object>
        {
            ["TestItemId"] = testItemId
        });

        // Get all artifacts for test item
        var artifacts = await store.GetArtifactsForTestAsync(testItemId.ToString(), string.Empty);

        if (artifacts == null || !artifacts.Any())
        {
            chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactBatchZipFailed,
                "error=NoArtifactsFound testItemId={TestItemId}", testItemId);
            return ProblemDetailsHelpers.NotFound(
                $"No artifacts found for test item {testItemId}",
                eventCode: EventCodes.Artifacts.ArtifactDownloadFailed,
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        }

        chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactDownloadStarted,
            "type=ZipBatch testItemId={TestItemId} count={Count}", testItemId, artifacts.Count);

        // Get storage configuration
        var storageBackend = config.GetValue("AGENIX_ARTIFACTS_STORAGE_BACKEND", "local");
        var baseStoragePath =
            config.GetValue("AGENIX_ARTIFACTS_STORAGE_PATH", "./data/artifacts") ?? "./data/artifacts";

        // Set response headers for ZIP download
        httpContext.Response.ContentType = "application/zip";
        httpContext.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"test-{testItemId:N}-artifacts.zip\"";

        // Stream ZIP directly to response
        using var zipArchive = new ZipArchive(httpContext.Response.Body, ZipArchiveMode.Create, true);

        int addedCount = 0;
        foreach (var artifact in artifacts)
        {
            try
            {
                var fullPath = Path.Combine(baseStoragePath, artifact.Path);

                if (!File.Exists(fullPath))
                {
                    chunkedLogger.LogWarning(EventCodes.Artifacts.ArtifactStorageError,
                        "error=FileNotFound artifactId={ArtifactId} path={Path}", artifact.Id, fullPath);
                    continue; // Skip missing files
                }

                // Create ZIP entry with original filename
                var entry = zipArchive.CreateEntry(artifact.Name, CompressionLevel.Fastest);

                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(fullPath);
                await fileStream.CopyToAsync(entryStream);

                addedCount++;
            }
            catch (Exception ex)
            {
                chunkedLogger.LogWarning(EventCodes.Artifacts.ArtifactBatchZipFailed,
                    "error=FailedToAddEntry artifactId={ArtifactId} message={Message}",
                    artifact.Id, ex.Message);
            }
        }

        chunkedLogger.LogMilestone(EventCodes.Artifacts.ArtifactBatchZipCreated,
            "testItemId={TestItemId} addedCount={Count}", testItemId, addedCount);

        return Results.Empty;
    }
}

/// <summary>
///     Response model for artifact upload endpoint.
///     Contains the artifact ID and status of async processing.
/// </summary>
public record ArtifactUploadResponse
{
    public required Guid ArtifactId { get; init; }
    public required Guid TestItemId { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
}

/// <summary>
///     Response model for pre-signed artifact URL endpoint.
///     Contains the URL and metadata about the artifact and expiration.
/// </summary>
public record ArtifactUrlResponse
{
    public required Guid ArtifactId { get; init; }
    public required string Url { get; init; }
    public required int ExpiresInSeconds { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
}
