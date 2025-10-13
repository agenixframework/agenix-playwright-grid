# Phase 8: Artifacts Tab

## Overview
Implement the Artifacts tab showing test attachments (screenshots, videos, traces) with file preview, thumbnail navigation, and download/open actions. This tab provides visual access to test artifacts.

## Goals
- ✅ File preview area with centered display
- ✅ Navigation arrows (previous/next artifact)
- ✅ File icon display for non-previewable files
- ✅ Download and Open in new tab actions
- ✅ Thumbnail strip at bottom
- ✅ Active thumbnail highlighting
- ✅ Horizontal scroll for many thumbnails
- ✅ Thumbnail labels with file names
- ✅ Empty state when no artifacts
- ✅ Responsive design for mobile

---

## Component Structure

### HTML Structure
```razor
<!-- Artifacts View -->
<div class="artifacts-view" id="artifacts-view">
    @if (_artifacts == null || _artifacts.Count == 0)
    {
        <!-- Empty State -->
        <div class="artifact-preview">
            <div class="artifact-main-display">
                <div class="artifact-file-icon">
                    <i class="bi bi-folder2-open" style="font-size: 64px; color: #6c757d;"></i>
                </div>
                <p style="color: #6c757d; font-size: 14px;">No artifacts found for this test item.</p>
            </div>
        </div>
    }
    else
    {
        <!-- Artifacts Viewer -->
        <div class="artifacts-viewer">
            <!-- Preview Area -->
            <div class="artifact-preview">
                <!-- Navigation Arrows -->
                <div class="artifact-navigation">
                    <button class="artifact-nav-btn"
                            @onclick="PreviousArtifact"
                            disabled="@(_currentArtifactIndex == 0)">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                            <path d="M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z"/>
                        </svg>
                    </button>

                    <button class="artifact-nav-btn"
                            @onclick="NextArtifact"
                            disabled="@(_currentArtifactIndex >= _artifacts.Count - 1)">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                            <path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/>
                        </svg>
                    </button>
                </div>

                <!-- Current Artifact Display -->
                <div class="artifact-main-display">
                    @if (_currentArtifact != null)
                    {
                        @if (IsImageFile(_currentArtifact.FileName))
                        {
                            <!-- Image Preview -->
                            <img src="@_currentArtifact.Url"
                                 alt="@_currentArtifact.FileName"
                                 style="max-width: 100%; max-height: 600px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);" />
                        }
                        else if (IsVideoFile(_currentArtifact.FileName))
                        {
                            <!-- Video Preview -->
                            <video controls
                                   style="max-width: 100%; max-height: 600px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
                                <source src="@_currentArtifact.Url" type="@_currentArtifact.ContentType">
                                Your browser does not support the video tag.
                            </video>
                        }
                        else
                        {
                            <!-- File Icon for Non-Previewable Files -->
                            <div class="artifact-file-icon">
                                <i class="@GetFileIcon(_currentArtifact.FileName)" style="font-size: 64px; color: #6c757d;"></i>
                            </div>
                        }

                        <!-- File Info -->
                        <div style="text-align: center; margin-top: 16px;">
                            <strong style="font-size: 16px; color: #212529;">@_currentArtifact.FileName</strong>
                            <p style="font-size: 13px; color: #6c757d; margin-top: 4px;">
                                @FormatFileSize(_currentArtifact.FileSize)
                                @if (!string.IsNullOrEmpty(_currentArtifact.ContentType))
                                {
                                    <text> · @_currentArtifact.ContentType</text>
                                }
                            </p>
                        </div>

                        <!-- Actions -->
                        <div class="artifact-actions">
                            <a class="artifact-action-link"
                               href="@_currentArtifact.Url"
                               download="@_currentArtifact.FileName">
                                <i class="bi bi-download"></i>
                                Download
                            </a>
                            <a class="artifact-action-link"
                               href="@_currentArtifact.Url"
                               target="_blank"
                               rel="noopener noreferrer">
                                <i class="bi bi-box-arrow-up-right"></i>
                                Open in new tab
                            </a>
                        </div>
                    }
                </div>
            </div>

            <!-- Thumbnail Strip -->
            <div class="artifact-thumbnails">
                @foreach (var (artifact, index) in _artifacts.Select((a, i) => (a, i)))
                {
                    <div class="artifact-thumbnail @(index == _currentArtifactIndex ? "active" : "")"
                         @onclick="() => SelectArtifact(index)">
                        @if (IsImageFile(artifact.FileName))
                        {
                            <img src="@artifact.Url"
                                 alt="@artifact.FileName"
                                 style="width: 100%; height: 100%; object-fit: cover; border-radius: 6px;" />
                        }
                        else
                        {
                            <i class="@GetFileIcon(artifact.FileName) artifact-thumbnail-icon"></i>
                        }
                        <div class="artifact-thumbnail-label" title="@artifact.FileName">
                            @artifact.FileName
                        </div>
                    </div>
                }
            </div>
        </div>
    }
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines **1914-2141**

### Key Sections

**1. Artifacts Container** (lines 1915-1925)
```css
.artifacts-view {
    padding: 24px;
    background-color: #f8f9fa;
}

.artifacts-viewer {
    background: #fff;
    border-radius: 8px;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
    overflow: hidden;
}
```

**2. Preview Area** (lines 1927-1936)
```css
.artifact-preview {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    background: #f8f9fa;
    padding: 48px 24px;
    min-height: 500px;
    position: relative;
}
```

**3. Navigation Arrows** (lines 1938-1980)
```css
.artifact-navigation {
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    padding: 0 24px;
    pointer-events: none;
}

.artifact-nav-btn {
    pointer-events: all;
    width: 48px;
    height: 48px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(255, 255, 255, 0.95);
    border: 1px solid #dee2e6;
    border-radius: 50%;
    cursor: pointer;
    transition: all 0.2s ease;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.artifact-nav-btn:hover:not(:disabled) {
    background: #fff;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    transform: scale(1.05);
}

.artifact-nav-btn:disabled {
    opacity: 0.3;
    cursor: not-allowed;
}
```

**4. File Icon** (lines 1991-2006)
```css
.artifact-file-icon {
    width: 120px;
    height: 120px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #e9ecef;
    border-radius: 12px;
    border: 2px dashed #adb5bd;
}

.artifact-file-icon svg {
    width: 64px;
    height: 64px;
    fill: #6c757d;
}
```

**5. Action Links** (lines 2008-2036)
```css
.artifact-actions {
    display: flex;
    gap: 12px;
    margin-top: 8px;
}

.artifact-action-link {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 8px 16px;
    background: #fff;
    border: 1px solid #667eea;
    border-radius: 6px;
    color: #667eea;
    font-size: 13px;
    font-weight: 500;
    text-decoration: none;
    transition: all 0.2s ease;
}

.artifact-action-link:hover {
    background: #667eea;
    color: #fff;
}
```

**6. Thumbnail Strip** (lines 2038-2093)
```css
.artifact-thumbnails {
    display: flex;
    gap: 12px;
    padding: 20px 24px;
    background: #fff;
    border-top: 1px solid #dee2e6;
    overflow-x: auto;
    align-items: center;
}

.artifact-thumbnail {
    flex-shrink: 0;
    width: 100px;
    height: 100px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #f8f9fa;
    border: 2px solid transparent;
    border-radius: 8px;
    cursor: pointer;
    transition: all 0.2s ease;
    position: relative;
}

.artifact-thumbnail:hover {
    border-color: #adb5bd;
}

.artifact-thumbnail.active {
    border-color: #667eea;
    background: #fff;
    box-shadow: 0 2px 8px rgba(102, 126, 234, 0.2);
}

.artifact-thumbnail-label {
    position: absolute;
    bottom: 4px;
    left: 4px;
    right: 4px;
    background: rgba(0, 0, 0, 0.7);
    color: #fff;
    font-size: 10px;
    padding: 2px 4px;
    border-radius: 4px;
    text-align: center;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
```

**7. Mobile Responsive** (lines 2123-2141)
```css
@media (max-width: 768px) {
    .artifact-preview {
        min-height: 400px;
        padding: 32px 16px;
    }

    .artifact-navigation {
        padding: 0 16px;
    }

    .artifact-thumbnails {
        padding: 16px;
    }

    .artifact-thumbnail {
        width: 80px;
        height: 80px;
    }
}
```

---

## C# Data Model

### Artifact DTO
```csharp
public class ArtifactDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string Url { get; set; } = ""; // Presigned URL or direct URL
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; } // Bytes
    public DateTimeOffset CreatedAt { get; set; }
}
```

### State Variables
```csharp
private List<ArtifactDto> _artifacts = new();
private int _currentArtifactIndex = 0;
private ArtifactDto? _currentArtifact => _artifacts.Count > 0 && _currentArtifactIndex < _artifacts.Count
    ? _artifacts[_currentArtifactIndex]
    : null;
```

---

## C# Logic

### Load Artifacts
```csharp
private async Task LoadArtifactsAsync()
{
    if (_testItem == null) return;

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");
        var response = await http.GetAsync($"/api/test-items/{_testItem.Id}/artifacts");

        if (response.IsSuccessStatusCode)
        {
            _artifacts = await response.Content
                .ReadFromJsonAsync<List<ArtifactDto>>() ?? new();

            _currentArtifactIndex = 0; // Reset to first artifact
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load artifacts: {ex.Message}");
    }
}
```

### Navigation
```csharp
private void PreviousArtifact()
{
    if (_currentArtifactIndex > 0)
    {
        _currentArtifactIndex--;
        StateHasChanged();
    }
}

private void NextArtifact()
{
    if (_currentArtifactIndex < _artifacts.Count - 1)
    {
        _currentArtifactIndex++;
        StateHasChanged();
    }
}

private void SelectArtifact(int index)
{
    if (index >= 0 && index < _artifacts.Count)
    {
        _currentArtifactIndex = index;
        StateHasChanged();
    }
}
```

### File Type Detection
```csharp
private bool IsImageFile(string fileName)
{
    var extension = Path.GetExtension(fileName).ToLower();
    return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp";
}

private bool IsVideoFile(string fileName)
{
    var extension = Path.GetExtension(fileName).ToLower();
    return extension is ".mp4" or ".webm" or ".ogg" or ".mov";
}

private string GetFileIcon(string fileName)
{
    var extension = Path.GetExtension(fileName).ToLower();

    return extension switch
    {
        ".pdf" => "bi bi-file-pdf",
        ".zip" or ".rar" or ".7z" => "bi bi-file-zip",
        ".txt" or ".log" => "bi bi-file-text",
        ".json" or ".xml" => "bi bi-file-code",
        ".har" => "bi bi-file-earmark-binary", // HAR archive
        _ => "bi bi-file-earmark" // Generic file
    };
}
```

### File Size Formatting
```csharp
private string FormatFileSize(long bytes)
{
    string[] sizes = { "B", "KB", "MB", "GB" };
    double len = bytes;
    int order = 0;

    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len = len / 1024;
    }

    return $"{len:F2} {sizes[order]}";
}
```

### Keyboard Navigation (Optional)
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await JS.InvokeVoidAsync("setupArtifactKeyboardNav");
    }
}
```

**JavaScript:**
```javascript
// wwwroot/js/test-item-details.js

window.setupArtifactKeyboardNav = function() {
    document.addEventListener('keydown', (e) => {
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

        if (e.key === 'ArrowLeft') {
            document.querySelector('.artifact-nav-btn:first-child')?.click();
        } else if (e.key === 'ArrowRight') {
            document.querySelector('.artifact-nav-btn:last-child')?.click();
        }
    });
};
```

---

## Backend API

### Endpoint: Get Test Item Artifacts
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs

app.MapGet("/api/test-items/{itemId:guid}/artifacts", async (
    Guid itemId,
    [FromServices] IResultsStore store,
    [FromServices] IStorageService storage) =>
{
    var artifacts = await store.GetArtifactsForTestItemAsync(itemId);

    // Generate presigned URLs if using MinIO/S3
    foreach (var artifact in artifacts)
    {
        if (!string.IsNullOrEmpty(artifact.StoragePath))
        {
            artifact.Url = await storage.GetPresignedDownloadUrlAsync(artifact.StoragePath);
        }
    }

    return Results.Ok(artifacts);
})
.WithName("GetTestItemArtifacts")
.WithTags("TestItems");
```

### IResultsStore Method
```csharp
Task<List<ArtifactDto>> GetArtifactsForTestItemAsync(Guid itemId);
```

### PostgreSQL Implementation
```csharp
public async Task<List<ArtifactDto>> GetArtifactsForTestItemAsync(Guid itemId)
{
    await using var conn = new NpgsqlConnection(_connString);
    await conn.OpenAsync();

    var sql = @"
        SELECT
            id,
            file_name,
            storage_path,
            content_type,
            file_size,
            created_at
        FROM test_artifacts
        WHERE test_item_id = @itemId
        ORDER BY created_at ASC";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("itemId", itemId);

    var artifacts = new List<ArtifactDto>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        artifacts.Add(new ArtifactDto
        {
            Id = reader.GetGuid(0),
            FileName = reader.GetString(1),
            Url = reader.GetString(2), // Will be replaced with presigned URL
            ContentType = reader.GetString(3),
            FileSize = reader.GetInt64(4),
            CreatedAt = reader.GetDateTime(5)
        });
    }

    return artifacts;
}
```

---

## Testing Checklist

### Visual
- [ ] Preview area displays correctly with centered content
- [ ] Navigation arrows styled as circular buttons
- [ ] Arrow buttons disabled at first/last artifact
- [ ] Image previews render correctly
- [ ] Video previews render with controls
- [ ] File icons display for non-previewable files
- [ ] Download and Open buttons styled correctly
- [ ] Thumbnail strip scrolls horizontally
- [ ] Active thumbnail has purple border
- [ ] Thumbnail labels display file names
- [ ] Empty state icon and message centered

### Functional
- [ ] Load artifacts from API
- [ ] Previous button navigates to previous artifact
- [ ] Next button navigates to next artifact
- [ ] Click thumbnail switches to that artifact
- [ ] Disabled state works (first/last artifact)
- [ ] Download link downloads file
- [ ] Open in new tab opens file in new tab
- [ ] Image preview loads correctly
- [ ] Video preview plays correctly
- [ ] File size formatted correctly
- [ ] Content type displays correctly

### Edge Cases
- [ ] No artifacts (empty state)
- [ ] Single artifact (navigation disabled)
- [ ] Many artifacts (thumbnail scroll works)
- [ ] Very large images (max-width/height works)
- [ ] Very long file names (truncation works)
- [ ] Missing content type
- [ ] Invalid file URLs (error handling)
- [ ] Keyboard navigation (arrow keys)

---

## Next Phase
**Phase 9:** Playwright Commands Integration (copy from ResultsRun.razor lines 324-600)
