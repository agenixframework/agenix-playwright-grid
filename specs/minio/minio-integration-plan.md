# MinIO Integration Plan for Artifact Storage

## Overview
This document outlines the plan to integrate MinIO (S3-compatible object storage) for storing test artifacts (screenshots, videos, logs, etc.) in the Agenix Playwright Grid system. The integration will replace local filesystem storage with scalable, distributed object storage.

---

## Current State

### Existing Implementation
- **Storage Location**: Local filesystem (`./data/artifacts/`)
- **Path Structure**: `artifacts/{testItemId}/{artifactId}/{fileName}`
- **Database**: `test_artifacts` table stores metadata with `storage_path` column
- **Ingestion Service**: `PostgresBatchWriter.InsertArtifactAsync()` writes files to disk

### Current Flow
1. Client uploads log item with attachment (base64 encoded)
2. Hub publishes `LogItemEvent` with attachment data in `MetadataJson`
3. Ingestion service:
   - Decodes base64 attachment data
   - Writes file to local filesystem
   - Inserts metadata into `test_artifacts` table
   - Links artifact ID to `log_items.attachment_id`

---

## Goals

1. **Scalability**: Support distributed artifact storage across multiple nodes
2. **High Availability**: Replicate artifacts across multiple MinIO instances
3. **Performance**: Faster uploads/downloads with object storage optimizations
4. **Cost Efficiency**: Lifecycle policies for automatic cleanup of old artifacts
5. **S3 Compatibility**: Enable cloud migration to AWS S3, Azure Blob, or GCS
6. **Security**: Pre-signed URLs for secure, time-limited artifact access

---

## Architecture

### Components

```
┌─────────────────┐
│  Hub Service    │──┐
└─────────────────┘  │
                     │ Publishes LogItemEvent
                     │ (with attachment metadata)
                     ▼
              ┌──────────────┐
              │  RabbitMQ    │
              └──────────────┘
                     │
                     │ Consumes events
                     ▼
         ┌────────────────────────┐
         │ Ingestion Service      │
         │  - Decode base64       │
         │  - Upload to MinIO     │
         │  - Save metadata to DB │
         └────────────────────────┘
                     │
                     │ Upload via SDK
                     ▼
              ┌──────────────┐
              │    MinIO     │
              │  (S3 API)    │
              └──────────────┘
```

### MinIO Deployment Options

**Option 1: Docker Compose (Development)**
```yaml
services:
  minio:
    image: minio/minio:latest
    ports:
      - "9000:9000"   # S3 API
      - "9001:9001"   # Web Console
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    command: server /data --console-address ":9001"
    volumes:
      - minio-data:/data
```

**Option 2: Kubernetes (Production)**
- Use MinIO Operator for multi-tenant, distributed deployment
- Automatic pod scaling and failover
- Erasure coding for data protection

---

## Implementation Plan

### Phase 1: MinIO Setup & Configuration (1-2 hours)

#### 1.1 Add MinIO to Docker Compose
**File**: `docker-compose.yml`

```yaml
services:
  minio:
    image: minio/minio:RELEASE.2025-01-01T00-00-00Z
    container_name: playwright-grid-minio
    ports:
      - "9000:9000"   # S3 API
      - "9001:9001"   # Web Console
    environment:
      MINIO_ROOT_USER: ${MINIO_ROOT_USER:-minioadmin}
      MINIO_ROOT_PASSWORD: ${MINIO_ROOT_PASSWORD:-minioadmin}
      # Public URLs (optional but recommended)
      MINIO_SERVER_URL: http://localhost:9000
      MINIO_BROWSER_REDIRECT_URL: http://localhost:9001
      # Region (optional)
      MINIO_REGION_NAME: eu-central-1
    command: server /data --console-address ":9001"
    volumes:
      - minio-data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 30s
      timeout: 10s
      retries: 3
    networks:
      - playwright-grid

volumes:
  minio-data:
```

#### 1.2 Add Environment Variables
**File**: `.env`

```bash
# MinIO Configuration (S3-compatible object storage)
MINIO_ENDPOINT=localhost:9000
MINIO_ACCESS_KEY=minioadmin
MINIO_SECRET_KEY=minioadmin
MINIO_USE_SSL=false
MINIO_REGION=eu-central-1
MINIO_BUCKET_NAME=playwright-artifacts
MINIO_PUBLIC_URL=http://localhost:9000

# Artifact Storage Configuration
ARTIFACTS_STORAGE_BACKEND=minio  # Options: local, minio, s3, azure, gcs
ARTIFACTS_STORAGE_PATH=./data/artifacts  # Fallback for local storage
ARTIFACTS_RETENTION_DAYS=30  # Auto-delete artifacts older than 30 days
```

#### 1.3 Initialize MinIO Bucket
**Script**: `scripts/init-minio.sh`

```bash
#!/bin/bash
# Wait for MinIO to be ready
until curl -f http://localhost:9000/minio/health/live; do
  echo "Waiting for MinIO..."
  sleep 2
done

# Create bucket using mc (MinIO Client)
docker run --rm --network playwright-grid \
  -e MC_HOST_local=http://minioadmin:minioadmin@minio:9000 \
  minio/mc \
  mb local/playwright-artifacts --ignore-existing

# Set bucket policy (public read for artifacts)
docker run --rm --network playwright-grid \
  -e MC_HOST_local=http://minioadmin:minioadmin@minio:9000 \
  minio/mc \
  policy set download local/playwright-artifacts

echo "MinIO bucket initialized: playwright-artifacts"
```

---

### Phase 2: SDK Integration (2-3 hours)

#### 2.1 Add Minio NuGet Package
**Files**:
- `ingestion/IngestionService.csproj`
- `hub/PlaywrightHub.csproj`

```xml
<PackageReference Include="Minio" Version="6.0.3" />
```

#### 2.2 Create MinIO Client Service
**File**: `ingestion/Infrastructure/MinioStorageService.cs`

```csharp
using Minio;
using Minio.DataModel.Args;

namespace IngestionService.Infrastructure;

/// <summary>
/// MinIO-based artifact storage service with S3-compatible API.
/// Handles file uploads, downloads, and pre-signed URL generation.
/// </summary>
public sealed class MinioStorageService : IDisposable
{
    private readonly IMinioClient _client;
    private readonly string _bucketName;
    private readonly ILogger<MinioStorageService> _logger;

    public MinioStorageService(IConfiguration config, ILogger<MinioStorageService> logger)
    {
        _logger = logger;
        _bucketName = config.GetValue("MINIO_BUCKET_NAME", "playwright-artifacts");

        var endpoint = config["MINIO_ENDPOINT"] ?? "localhost:9000";
        var accessKey = config["MINIO_ACCESS_KEY"] ?? "minioadmin";
        var secretKey = config["MINIO_SECRET_KEY"] ?? "minioadmin";
        var useSSL = config.GetValue("MINIO_USE_SSL", false);

        _client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(useSSL)
            .Build();

        _logger.LogInformation("MinIO client initialized: {Endpoint}, bucket: {Bucket}",
            endpoint, _bucketName);
    }

    /// <summary>
    /// Uploads file data to MinIO and returns the storage path (object key).
    /// </summary>
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

            _logger.LogInformation("Uploaded artifact to MinIO: {ObjectKey} ({Size} bytes)",
                objectKey, fileData.Length);

            return objectKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload artifact to MinIO: {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    /// Downloads file data from MinIO.
    /// </summary>
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

            _logger.LogDebug("Downloaded artifact from MinIO: {ObjectKey} ({Size} bytes)",
                objectKey, memoryStream.Length);

            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download artifact from MinIO: {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    /// Generates a pre-signed URL for secure, time-limited artifact access.
    /// </summary>
    public async Task<string> GetPresignedUrlAsync(
        string objectKey,
        int expirySeconds = 3600,
        CancellationToken ct = default)
    {
        try
        {
            var args = new PresignedGetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectKey)
                .WithExpiry(expirySeconds);

            var url = await _client.PresignedGetObjectAsync(args);

            _logger.LogDebug("Generated pre-signed URL for {ObjectKey}, expires in {Expiry}s",
                objectKey, expirySeconds);

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate pre-signed URL for {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    /// Deletes artifact from MinIO.
    /// </summary>
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

            _logger.LogInformation("Deleted artifact from MinIO: {ObjectKey}", objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete artifact from MinIO: {ObjectKey}", objectKey);
            throw;
        }
    }

    /// <summary>
    /// Checks if bucket exists and creates it if not.
    /// </summary>
    public async Task EnsureBucketExistsAsync(CancellationToken ct = default)
    {
        try
        {
            var args = new BucketExistsArgs().WithBucket(_bucketName);
            var exists = await _client.BucketExistsAsync(args, ct);

            if (!exists)
            {
                var makeArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _client.MakeBucketAsync(makeArgs, ct);
                _logger.LogInformation("Created MinIO bucket: {Bucket}", _bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure bucket exists: {Bucket}", _bucketName);
            throw;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
```

#### 2.3 Register MinIO Service
**File**: `ingestion/Services/IngestionServiceRunner.cs`

```csharp
// Add after line 50 (after other service registrations)
builder.Services.AddSingleton<MinioStorageService>();

// Ensure bucket exists on startup
var minioService = builder.Services.BuildServiceProvider().GetRequiredService<MinioStorageService>();
await minioService.EnsureBucketExistsAsync();
```

---

### Phase 3: Update PostgresBatchWriter (1-2 hours)

#### 3.1 Modify InsertArtifactAsync to Use MinIO
**File**: `ingestion/Infrastructure/PostgresBatchWriter.cs`

```csharp
// Add field
private readonly MinioStorageService? _minioStorage;

// Update constructor
public PostgresBatchWriter(
    string connectionString,
    RedisLogTokenCache tokenCache,
    IConfiguration config,
    ILogger<PostgresBatchWriter> logger,
    MinioStorageService? minioStorage = null)  // Optional for backward compatibility
{
    _connectionString = connectionString;
    _tokenCache = tokenCache;
    _useTokenOptimization = config.GetValue("USE_LOG_TOKEN_OPTIMIZATION", true);
    _config = config;
    _logger = logger;
    _minioStorage = minioStorage;
}

// Update InsertArtifactAsync method
private async Task<Guid> InsertArtifactAsync(
    NpgsqlConnection conn,
    Guid testItemId,
    string fileName,
    string contentType,
    byte[] fileData,
    CancellationToken ct)
{
    var artifactId = Guid.NewGuid();
    var storagePath = $"artifacts/{testItemId}/{artifactId}/{fileName}";

    // Determine storage backend
    var storageBackend = _config.GetValue("ARTIFACTS_STORAGE_BACKEND", "local");

    if (storageBackend == "minio" && _minioStorage != null)
    {
        // Upload to MinIO
        await _minioStorage.UploadArtifactAsync(storagePath, fileData, contentType, ct);
        _logger.LogInformation("Uploaded artifact to MinIO: {Path} ({Size} bytes)",
            storagePath, fileData.Length);
    }
    else
    {
        // Fallback to local filesystem
        var baseStoragePath = _config.GetValue("ARTIFACTS_STORAGE_PATH", "./data/artifacts");
        var fullPath = Path.Combine(baseStoragePath, storagePath);
        var directoryPath = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllBytesAsync(fullPath, fileData, ct);
        _logger.LogInformation("Wrote artifact to local filesystem: {Path} ({Size} bytes)",
            fullPath, fileData.Length);
    }

    // Insert artifact metadata into database (storage_path is relative/object key)
    var sql = @"
        INSERT INTO test_artifacts (id, test_item_id, file_name, content_type, file_size, storage_path, uploaded_at)
        VALUES ($1, $2, $3, $4, $5, $6, $7)";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue(artifactId);
    cmd.Parameters.AddWithValue(testItemId);
    cmd.Parameters.AddWithValue(fileName);
    cmd.Parameters.AddWithValue(contentType);
    cmd.Parameters.AddWithValue((long)fileData.Length);
    cmd.Parameters.AddWithValue(storagePath);  // Same path for both local and MinIO
    cmd.Parameters.AddWithValue(DateTime.UtcNow);

    await cmd.ExecuteNonQueryAsync(ct);

    return artifactId;
}
```

---

### Phase 4: Artifact Download Endpoint (1-2 hours)

#### 4.1 Create Artifact Download Endpoint
**File**: `hub/Infrastructure/Web/ArtifactsEndpoints.cs` (new file)

```csharp
using Microsoft.AspNetCore.Mvc;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Web;

public static class ArtifactsEndpoints
{
    public static void MapArtifactsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/artifacts")
            .WithTags("Artifacts")
            .WithOpenApi();

        // GET /api/artifacts/{id} - Download artifact
        group.MapGet("/{id:guid}", DownloadArtifact)
            .WithName("DownloadArtifact")
            .WithSummary("Download artifact file or get pre-signed URL")
            .Produces<FileStreamResult>(200)
            .Produces(404);

        // GET /api/artifacts/{id}/url - Get pre-signed download URL
        group.MapGet("/{id:guid}/url", GetArtifactUrl)
            .WithName("GetArtifactUrl")
            .WithSummary("Get pre-signed URL for artifact download")
            .Produces<ArtifactUrlResponse>(200)
            .Produces(404);
    }

    private static async Task<IResult> DownloadArtifact(
        Guid id,
        [FromServices] IResultsStore store,
        [FromServices] IConfiguration config,
        [FromServices] MinioStorageService? minioStorage)
    {
        // Get artifact metadata from database
        var artifact = await store.GetArtifactAsync(id);
        if (artifact == null)
            return Results.NotFound();

        var storageBackend = config.GetValue("ARTIFACTS_STORAGE_BACKEND", "local");

        if (storageBackend == "minio" && minioStorage != null)
        {
            // Download from MinIO
            var fileData = await minioStorage.DownloadArtifactAsync(artifact.StoragePath);
            return Results.File(fileData, artifact.ContentType, artifact.FileName);
        }
        else
        {
            // Read from local filesystem
            var baseStoragePath = config.GetValue("ARTIFACTS_STORAGE_PATH", "./data/artifacts");
            var fullPath = Path.Combine(baseStoragePath, artifact.StoragePath);

            if (!File.Exists(fullPath))
                return Results.NotFound();

            var fileStream = File.OpenRead(fullPath);
            return Results.File(fileStream, artifact.ContentType, artifact.FileName);
        }
    }

    private static async Task<IResult> GetArtifactUrl(
        Guid id,
        [FromServices] IResultsStore store,
        [FromServices] MinioStorageService? minioStorage,
        [FromQuery] int expirySeconds = 3600)
    {
        var artifact = await store.GetArtifactAsync(id);
        if (artifact == null)
            return Results.NotFound();

        if (minioStorage == null)
            return Results.BadRequest(new { error = "MinIO storage not configured" });

        var url = await minioStorage.GetPresignedUrlAsync(artifact.StoragePath, expirySeconds);

        return Results.Ok(new ArtifactUrlResponse
        {
            ArtifactId = id,
            Url = url,
            ExpiresInSeconds = expirySeconds,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expirySeconds)
        });
    }
}

public record ArtifactUrlResponse
{
    public required Guid ArtifactId { get; init; }
    public required string Url { get; init; }
    public required int ExpiresInSeconds { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
```

#### 4.2 Register Artifact Endpoints
**File**: `hub/Infrastructure/Web/EndpointMappingExtensions.cs`

```csharp
// Add after line 567
app.MapArtifactsEndpoints();
```

---

### Phase 5: Lifecycle Policies & Cleanup (1 hour)

#### 5.1 MinIO Lifecycle Policy
**Script**: `scripts/configure-minio-lifecycle.sh`

```bash
#!/bin/bash
# Configure MinIO lifecycle policy to auto-delete old artifacts

docker run --rm --network playwright-grid \
  -e MC_HOST_local=http://minioadmin:minioadmin@minio:9000 \
  minio/mc \
  ilm add local/playwright-artifacts \
  --expiry-days 30 \
  --prefix "artifacts/"

echo "MinIO lifecycle policy configured: 30-day retention"
```

#### 5.2 Database Cleanup Job
**File**: `hub/Infrastructure/Adapters/Background/ArtifactCleanupService.cs` (new file)

```csharp
public class ArtifactCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ArtifactCleanupService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IResultsStore>();
                var minioStorage = scope.ServiceProvider.GetService<MinioStorageService>();

                // Delete expired artifacts (older than 30 days)
                var expiredArtifacts = await store.GetExpiredArtifactsAsync(30);

                foreach (var artifact in expiredArtifacts)
                {
                    if (minioStorage != null)
                    {
                        await minioStorage.DeleteArtifactAsync(artifact.StoragePath);
                    }
                    else
                    {
                        // Delete from local filesystem
                        var path = Path.Combine("./data/artifacts", artifact.StoragePath);
                        if (File.Exists(path))
                            File.Delete(path);
                    }

                    await store.DeleteArtifactAsync(artifact.Id);
                }

                _logger.LogInformation("Cleaned up {Count} expired artifacts", expiredArtifacts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during artifact cleanup");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

---

## Testing Plan

### Unit Tests
1. **MinioStorageService Tests**
   - Test upload/download with mock MinIO client
   - Test pre-signed URL generation
   - Test error handling (network failures, invalid credentials)

2. **PostgresBatchWriter Tests**
   - Test attachment processing with MinIO enabled/disabled
   - Test fallback to local filesystem
   - Test storage path consistency

### Integration Tests
1. **End-to-End Artifact Upload**
   ```csharp
   [Test]
   public async Task LogItem_WithAttachment_ShouldPersistToMinIO()
   {
       // Arrange
       var client = new PlaywrightGridClient();
       var testItem = await client.TestItem.StartAsync(...);
       var logData = new CreateLogItemRequest
       {
           TestItemUuid = testItem.Id,
           Message = "Screenshot attached",
           Level = "INFO",
           Attach = new LogItemAttach
           {
               Name = "screenshot.png",
               Data = File.ReadAllBytes("test.png"),
               MimeType = "image/png"
           }
       };

       // Act
       await client.Log.CreateAsync(logData);

       // Assert
       var artifact = await GetArtifactFromDatabase(testItem.Id);
       var exists = await MinIOClient.ObjectExists(artifact.StoragePath);
       Assert.IsTrue(exists);
   }
   ```

2. **Pre-signed URL Download Test**
   ```csharp
   [Test]
   public async Task GetPresignedUrl_ShouldReturnValidUrl()
   {
       // Arrange
       var artifactId = await UploadTestArtifact();

       // Act
       var response = await HttpClient.GetAsync($"/api/artifacts/{artifactId}/url");
       var urlResponse = await response.Content.ReadFromJsonAsync<ArtifactUrlResponse>();

       // Download using pre-signed URL
       var downloadResponse = await HttpClient.GetAsync(urlResponse.Url);

       // Assert
       Assert.IsTrue(downloadResponse.IsSuccessStatusCode);
       var content = await downloadResponse.Content.ReadAsByteArrayAsync();
       Assert.IsNotEmpty(content);
   }
   ```

### Performance Tests
- Upload 1000 artifacts in parallel
- Measure MinIO vs local filesystem performance
- Test with different file sizes (1KB, 1MB, 10MB)

---

## Migration Strategy

### Step 1: Dual-Write Phase (Week 1)
- Deploy MinIO alongside existing local storage
- Configure `ARTIFACTS_STORAGE_BACKEND=local` (default)
- New artifacts written to both local and MinIO
- Verify MinIO uploads successful before committing

### Step 2: Validation Phase (Week 2)
- Enable MinIO for 10% of traffic
- Monitor error rates, latency, storage usage
- Compare artifact retrieval times (local vs MinIO)
- Fix any issues discovered

### Step 3: Full Migration (Week 3)
- Set `ARTIFACTS_STORAGE_BACKEND=minio` for all services
- Stop writing to local filesystem
- Keep local files for 30 days as backup
- Monitor for issues

### Step 4: Cleanup (Week 4)
- Delete local artifact files older than 30 days
- Remove local filesystem code paths
- Update documentation

---

## Rollback Plan

If issues occur during migration:

1. **Immediate Rollback**: Set `ARTIFACTS_STORAGE_BACKEND=local`
2. **Database Integrity**: All `storage_path` values remain valid for both backends
3. **Data Recovery**: MinIO backup can be restored to local filesystem if needed
4. **Zero Downtime**: Configuration change doesn't require service restart

---

## Monitoring & Alerting

### Metrics to Track
- **Upload Success Rate**: % of successful uploads to MinIO
- **Upload Latency**: P50, P95, P99 upload times
- **Download Latency**: P50, P95, P99 download times
- **Storage Usage**: Total bytes stored in MinIO
- **Error Rate**: Failed uploads/downloads per minute
- **Pre-signed URL Generation**: Success rate and latency

### Alerts
- Upload failure rate > 5% for 5 minutes
- Download latency P95 > 2 seconds
- MinIO service unavailable
- Storage usage > 80% of allocated capacity

---

## Security Considerations

### Access Control
- Use IAM policies for MinIO bucket access
- Rotate access keys regularly (30-day rotation)
- Use least-privilege principle for service accounts

### Data Protection
- Enable MinIO encryption at rest
- Use TLS/SSL for data in transit
- Implement pre-signed URLs with short expiry (default: 1 hour)
- Add request authentication for artifact download endpoints

### Audit Logging
- Log all artifact uploads/downloads with user ID
- Track pre-signed URL generation
- Monitor unusual access patterns

---

## Cost Estimation

### Storage Costs
- **MinIO Self-Hosted**: ~$0.02/GB/month (hardware + electricity)
- **AWS S3 (if migrating)**: ~$0.023/GB/month
- **Expected Usage**: 10GB/day × 30 days = 300GB = ~$6-7/month

### Bandwidth Costs
- **MinIO Self-Hosted**: No egress charges
- **AWS S3**: $0.09/GB egress after first 100GB
- **Expected Egress**: 50GB/month = ~$4.50/month

### Total Cost
- **Self-Hosted MinIO**: ~$7/month (storage only)
- **AWS S3**: ~$12/month (storage + bandwidth)

---

## Success Criteria

- [ ] MinIO deployed and accessible
- [ ] Artifacts successfully uploaded to MinIO
- [ ] Pre-signed URLs generated and working
- [ ] Download endpoints return correct files
- [ ] Local filesystem fallback works
- [ ] Lifecycle policies deleting old artifacts
- [ ] Performance metrics meet SLAs:
  - Upload P95 < 500ms
  - Download P95 < 1000ms
  - 99.9% uptime
- [ ] Zero data loss during migration
- [ ] Documentation updated

---

## Timeline

| Phase | Duration | Tasks |
|-------|----------|-------|
| Phase 1: Setup | 1-2 hours | Docker Compose, environment vars, bucket init |
| Phase 2: SDK | 2-3 hours | Minio NuGet, service creation, DI registration |
| Phase 3: Integration | 1-2 hours | Update PostgresBatchWriter, storage backend switching |
| Phase 4: Endpoints | 1-2 hours | Download API, pre-signed URLs |
| Phase 5: Lifecycle | 1 hour | Cleanup policies, background jobs |
| Testing | 2-3 hours | Unit tests, integration tests, performance tests |
| Migration | 4 weeks | Dual-write, validation, full migration, cleanup |

**Total Development Time**: ~8-12 hours
**Total Migration Time**: 4 weeks (phased rollout)

---

## References

- [MinIO Documentation](https://min.io/docs/)
- [MinIO .NET SDK](https://github.com/minio/minio-dotnet)
- [S3 API Compatibility](https://docs.min.io/docs/aws-sdk-for-dotnet-with-minio.html)
- [MinIO Lifecycle Management](https://min.io/docs/minio/linux/administration/object-management/lifecycle-management.html)
- [Pre-signed URLs](https://min.io/docs/minio/linux/developers/dotnet/API.html#presignedgetobject)

---

## Next Steps (Future Enhancements)
Runtime Testing: Test both endpoints with real artifacts
Dashboard Integration: Update dashboard to use artifact download endpoints
Cleanup Jobs: Implement artifact cleanup based on retention policy
Bulk Downloads: Add endpoint to download multiple artifacts as ZIP
Redis Cluster Support: Shard cache across multiple Redis nodes
CDN Integration: Serve artifacts via CloudFlare/CloudFront
Smart Eviction: LFU (Least Frequently Used) instead of LRU

## Questions & Decisions

### Open Questions
1. Should we use multi-part uploads for large files (>100MB)?
2. Do we need CDN integration (CloudFlare, Fastly) for faster downloads?
3. Should we implement artifact compression before upload?
4. Do we need versioning for artifact files?

### Decisions Made
- ✅ Use MinIO for self-hosted deployments (not AWS S3)
- ✅ Store storage_path as relative path (works for both local and MinIO)
- ✅ Default retention: 30 days
- ✅ Pre-signed URLs expire in 1 hour
- ✅ Fallback to local filesystem if MinIO unavailable

---

*Last Updated: 2025-01-10*
*Author: Claude AI*
*Status: Draft*
