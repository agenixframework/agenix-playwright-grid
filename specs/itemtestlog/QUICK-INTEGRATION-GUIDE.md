# Quick Integration Guide - Phase 5 Enhancements

**Estimated Time:** 15 minutes

---

## Step 1: Add NuGet Packages

```bash
cd hub
dotnet add package Polly --version 8.2.0
dotnet add package Microsoft.Extensions.Caching.Memory --version 8.0.0
```

---

## Step 2: Register Services

**File:** `hub/Services/HubServiceRunner.cs`

Find the service registration section (around line 100-200) and add:

```csharp
// Add memory cache for test item caching
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100; // Max 100 cached items
    options.CompactionPercentage = 0.25; // Compact 25% when limit reached
});

// Add test item cache singleton
builder.Services.AddSingleton<TestItemCache>();
```

---

## Step 3: Register Endpoints

**File:** `hub/Infrastructure/Web/EndpointMappingExtensions.cs`

Find the `MapEndpoints` method and add:

```csharp
public static void MapEndpoints(this IEndpointRouteBuilder app)
{
    app.MapBrowserEndpoints();
    app.MapLaunchesEndpoints();
    app.MapSuitesEndpoints();
    app.MapTestRunsEndpoints();
    app.MapTestCasesEndpoints();
    app.MapTestItemsEndpoints();
    app.MapArtifactsEndpoints();
    app.MapLogItemsEndpoints();

    // Add this line
    app.MapEnhancedLogItemsEndpoints();

    app.MapLaunchFiltersEndpoints();
    app.MapAdminEndpoints();
    app.MapProjectSettingsEndpoints();
}
```

---

## Step 4: Add Using Statements

**File:** `hub/Services/HubServiceRunner.cs` (top of file)

```csharp
using PlaywrightHub.Infrastructure.Caching;
```

**File:** `hub/Infrastructure/Web/EndpointMappingExtensions.cs` (top of file)

```csharp
using PlaywrightHub.Infrastructure.Caching;
using PlaywrightHub.Infrastructure.Helpers;
```

---

## Step 5: Build and Test

```bash
# Build the solution
dotnet build

# Expected output: Build succeeded (0 errors, X warnings)
```

---

## Step 6: Verify Endpoints

Run the hub and check Swagger:

```bash
dotnet run --project hub
```

Navigate to: `https://localhost:5100/swagger/index.html`

**Expected New Endpoints:**
- GET `/api/test-items/{itemId}/logs/hierarchical`
- GET `/api/test-items/{itemId}/logs/flat`
- GET `/api/test-items/{itemId}/logs/search`
- GET `/api/test-items/{itemId}/logs/export`
- GET `/api/test-items/{itemId}/logs/stats`

---

## Step 7: Test with cURL

```bash
# Test hierarchical logs (with caching)
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/hierarchical?take=50&maxDepth=3"

# Test flat logs with search
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/flat?search=error&level=ERROR"

# Test search
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/search?query=failed"

# Test export (JSON)
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/export?format=json" > logs.json

# Test export (CSV)
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/export?format=csv" > logs.csv

# Test cache stats
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/stats"
```

---

## Step 8: Monitor Cache Performance

After running a few requests, check cache statistics:

```bash
curl "https://localhost:5100/api/test-items/{your-item-id}/logs/stats"
```

**Expected Response:**
```json
{
  "itemId": "uuid",
  "cacheHits": 5,
  "cacheMisses": 2,
  "cacheHitRate": 0.7142,
  "timestamp": "2025-01-18T10:30:00Z"
}
```

**Goal:** Cache hit rate > 80% in production

---

## Troubleshooting

### Build Errors

**Error:** `The type or namespace name 'Polly' could not be found`
**Solution:** Run `dotnet restore` and ensure Polly package is installed

**Error:** `The type or namespace name 'TestItemCache' could not be found`
**Solution:** Add `using PlaywrightHub.Infrastructure.Caching;` to HubServiceRunner.cs

### Runtime Errors

**Error:** `Unable to resolve service for type 'TestItemCache'`
**Solution:** Ensure `builder.Services.AddSingleton<TestItemCache>();` is added

**Error:** `Unable to resolve service for type 'IMemoryCache'`
**Solution:** Ensure `builder.Services.AddMemoryCache()` is added

### API Errors

**404 Not Found:** Verify endpoint is registered in `MapEndpoints()`
**500 Internal Server Error:** Check logs for exception details
**Cache always misses:** Verify `useCache=true` parameter is set

---

## Performance Validation

Run this test to validate cache performance:

```bash
# First request (cache miss)
time curl "https://localhost:5100/api/test-items/{id}/logs/hierarchical"

# Second request (cache hit)
time curl "https://localhost:5100/api/test-items/{id}/logs/hierarchical"

# Expected result:
# First request: ~80-100ms
# Second request: ~1-5ms (from cache)
```

---

## Next Steps

1. ✅ Integrate endpoints (Steps 1-3)
2. ✅ Test locally (Steps 4-8)
3. 🔄 Update dashboard UI to use new endpoints
4. 🔄 Write integration tests
5. 🔄 Deploy to staging environment
6. 🔄 Monitor cache hit rates in production
7. 🔄 Tune cache TTL based on usage patterns

---

## Dashboard Integration Example

**File:** `dashboard/Pages/TestItemDetails.razor`

```csharp
private async Task LoadLogsAsync()
{
    var http = HttpFactory.CreateClient("WebAPI");

    // Use enhanced hierarchical endpoint
    var response = await http.GetAsync(
        $"/api/test-items/{ItemId}/logs/hierarchical?take=1000&maxDepth=5&useCache=true");

    if (response.IsSuccessStatusCode)
    {
        var result = await response.Content
            .ReadFromJsonAsync<HierarchicalLogsResponse>();

        _logs = result.Logs;
        _cacheHit = result.CacheHit;
        _fallbackMode = result.FallbackMode;

        // Show warning if fallback was used
        if (_fallbackMode)
        {
            Logger.LogWarning("Hierarchical logs unavailable, using flat view");
        }
    }
}
```

---

**Estimated Total Time:** 15 minutes
**Difficulty:** Easy (copy-paste + dotnet restore)
**Breaking Changes:** None (all new endpoints)
