# Phase 3: Test History Line Component

## Overview
Implement the test execution history line showing test runs across multiple launches with inline navigation, elegant tooltips, and visual status indicators. This is the most complex and visually rich component.

## Goals
- ✅ Display test execution history across 20+ launches
- ✅ Show status badges with color coding (Passed/Failed/Skipped/Stopped)
- ✅ Inline navigation buttons (Previous/Next launch)
- ✅ Position indicator (e.g., "1 / 20")
- ✅ Elegant tooltips on hover with launch details
- ✅ Arrow connectors between history items
- ✅ Active item indicator (triangle pointing down)
- ✅ Keyboard navigation (Arrow keys)
- ✅ Smooth scroll to center active item
- ✅ Click any badge to jump to that launch

---

## Component Structure

### HTML Structure
```razor
<!-- Test History Line with Inline Navigation -->
<div class="history-line">
    <!-- Previous Launch Button -->
    <button class="history-nav-btn history-nav-prev"
            id="prevLaunchBtn"
            title="Previous Launch (←)"
            disabled="@(_currentHistoryIndex == 0)"
            @onclick="NavigateToPreviousLaunch">
        <i class="bi bi-chevron-left"></i>
    </button>

    <!-- Scrollable History Items -->
    <div class="history-line-scroll" @ref="_historyScrollRef">
        <div class="history-line-items">
            @foreach (var (historyItem, index) in _historyItems.Select((item, i) => (item, i)))
            {
                <div class="history-line-item @GetHistoryItemClass(index)"
                     @onclick="() => NavigateToHistoryLaunch(index)">
                    <div class="history-item-content">
                        <div class="history-status-block @GetStatusClass(historyItem.Status)">
                            #@historyItem.LaunchNumber
                        </div>

                        @if (_showTooltips && index != _currentHistoryIndex)
                        {
                            <div class="history-item-tooltip">
                                <div class="tooltip-header">
                                    <span class="tooltip-launch-number">Launch #@historyItem.LaunchNumber</span>
                                    <span class="tooltip-status-badge @GetStatusClass(historyItem.Status).ToLower()">
                                        @historyItem.Status.ToUpper()
                                    </span>
                                </div>

                                @if (historyItem.Attributes?.Any() == true)
                                {
                                    <div class="tooltip-section">
                                        <div class="tooltip-label">Launch Attributes:</div>
                                        <div class="tooltip-attributes">
                                            @foreach (var attr in historyItem.Attributes.Take(5))
                                            {
                                                <span class="tooltip-attribute">@attr</span>
                                            }
                                        </div>
                                    </div>
                                }

                                <div class="tooltip-section">
                                    <div class="tooltip-label">Duration:</div>
                                    <div class="tooltip-duration">
                                        <span class="tooltip-value">@FormatDuration(historyItem.Duration)</span>
                                    </div>
                                </div>

                                @if (historyItem.ErrorCount > 0)
                                {
                                    <div class="tooltip-section">
                                        <div class="tooltip-label">Item Details:</div>
                                        <span class="tooltip-value">@historyItem.ErrorCount Error</span>
                                    </div>
                                }
                            </div>
                        }
                    </div>
                </div>
            }
        </div>
    </div>

    <!-- Next Launch Button -->
    <button class="history-nav-btn history-nav-next"
            id="nextLaunchBtn"
            title="Next Launch (→)"
            disabled="@(_currentHistoryIndex >= _historyItems.Count - 1)"
            @onclick="NavigateToNextLaunch">
        <i class="bi bi-chevron-right"></i>
    </button>

    <!-- Position Indicator -->
    <span class="launch-position-indicator" id="launchPosition">
        @(_currentHistoryIndex + 1) / @_historyItems.Count
    </span>
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines **2153-2538**

### Key Sections

**1. History Line Container** (lines 2153-2200)
```css
.history-line {
    margin: 5px 25px 20px;
    max-width: 100%;
    position: relative;
    overflow: hidden;
    display: flex;
    align-items: center;
    gap: 12px;
}

.history-nav-btn {
    flex-shrink: 0;
    width: 36px;
    height: 36px;
    border-radius: 50%;
    border: 2px solid #667eea;
    background: linear-gradient(135deg, rgba(102, 126, 234, 0.08), rgba(118, 75, 162, 0.08));
    color: #667eea;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: all 0.3s ease;
    font-size: 16px;
    box-shadow: 0 2px 6px rgba(102, 126, 234, 0.15);
}

.history-nav-btn:hover:not(:disabled) {
    background: linear-gradient(135deg, #667eea, #764ba2);
    color: white;
    transform: scale(1.1);
    box-shadow: 0 4px 12px rgba(102, 126, 234, 0.3);
}

.history-nav-btn:disabled {
    opacity: 0.3;
    cursor: not-allowed;
    border-color: #ccc;
    background: rgba(0, 0, 0, 0.03);
    color: #999;
    box-shadow: none;
}
```

**2. History Items** (lines 2201-2280)
```css
.history-line-scroll {
    position: relative;
    overflow-x: auto;
    overflow-y: hidden;
    padding: 15px 0;
    scroll-behavior: smooth;
}

.history-line-items {
    display: flex;
    align-items: center;
    justify-content: flex-start;
    margin-bottom: 8px;
    box-sizing: border-box;
}

.history-line-item {
    position: relative;
    margin-right: 40px;
    flex-shrink: 0;
    transition: all 0.3s ease;
}

/* Arrow connector between items */
.history-line-item::after {
    content: "";
    position: absolute;
    right: -22px;
    top: 12px;
    border-top: 5px solid transparent;
    border-bottom: 5px solid transparent;
    border-left: 6px solid #ccc;
    width: 0;
    height: 0;
}

.history-line-item.history-item-last::after {
    display: none;
}

/* Active indicator triangle pointing down */
.history-line-item.history-item-active::before {
    content: "";
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    bottom: -20px;
    width: 0;
    height: 0;
    border-left: 10px solid transparent;
    border-right: 10px solid transparent;
    border-top: 12px solid #667eea;
    z-index: 10;
}

.history-status-block {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 76px;
    height: 32px;
    border-radius: 4px;
    color: #fff;
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    font-size: 13px;
    font-weight: 600;
    text-align: center;
    cursor: pointer;
    transition: all 0.2s ease;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.history-status-block:hover {
    transform: translateY(-1px);
    box-shadow: 0 4px 8px rgba(0, 0, 0, 0.15);
}

.history-status-block.status-failed {
    background-color: #f65e5e;
}

.history-status-block.status-passed {
    background-color: #56b985;
}

.history-status-block.status-skipped {
    background-color: #ffc107;
}

.history-status-block.status-stopped {
    background-color: #f65e5e;
}

.history-line-item.history-item-active .history-status-block {
    cursor: default;
    box-shadow: 0 4px 12px rgba(102, 126, 234, 0.3);
    border: 2px solid #667eea;
    animation: pulseActive 0.5s ease;
}

@keyframes pulseActive {
    0%, 100% { transform: scale(1); }
    50% { transform: scale(1.05); }
}
```

**3. Tooltips** (lines 2387-2533)
```css
.history-item-tooltip {
    position: absolute;
    bottom: calc(100% + 25px);
    left: 50%;
    transform: translateX(-50%);
    background: linear-gradient(135deg, #2c3e50 0%, #34495e 100%);
    color: white;
    padding: 12px 16px;
    border-radius: 8px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.3), 0 2px 8px rgba(0, 0, 0, 0.2);
    opacity: 0;
    pointer-events: none;
    transition: opacity 0.3s ease, transform 0.3s ease;
    z-index: 1000;
    min-width: 240px;
    white-space: nowrap;
    font-size: 12px;
}

.history-line-item:hover .history-item-tooltip {
    opacity: 1;
    transform: translateX(-50%) translateY(-5px);
}

.history-item-tooltip::after {
    content: "";
    position: absolute;
    top: 100%;
    left: 50%;
    transform: translateX(-50%);
    border-left: 8px solid transparent;
    border-right: 8px solid transparent;
    border-top: 8px solid #34495e;
}

.tooltip-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 8px;
    padding-bottom: 8px;
    border-bottom: 1px solid rgba(255, 255, 255, 0.2);
}

.tooltip-section {
    margin-bottom: 6px;
    font-size: 11px;
}

.tooltip-label {
    color: rgba(255, 255, 255, 0.6);
    text-transform: uppercase;
    font-size: 9px;
    font-weight: 600;
    letter-spacing: 0.5px;
    margin-bottom: 3px;
}

.tooltip-value {
    color: #fff;
    font-weight: 500;
}

.tooltip-attributes {
    display: flex;
    gap: 6px;
    flex-wrap: wrap;
    margin-top: 4px;
}

.tooltip-attribute {
    padding: 2px 6px;
    background: rgba(255, 255, 255, 0.15);
    border-radius: 3px;
    font-size: 10px;
    font-weight: 500;
}

```

**4. Position Indicator** (lines 2335-2354)
```css
.launch-position-indicator {
    display: inline-flex;
    align-items: center;
    flex-shrink: 0;
    padding: 6px 14px;
    margin-left: 8px;
    font-size: 12px;
    font-weight: 600;
    color: #667eea;
    background: linear-gradient(135deg, rgba(102, 126, 234, 0.1), rgba(118, 75, 162, 0.1));
    border-radius: 14px;
    border: 1px solid rgba(102, 126, 234, 0.3);
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    min-width: 60px;
    height: 32px;
    justify-content: center;
    white-space: nowrap;
    line-height: 1;
}
```

---

## C# Data Model

### Test History Item DTO
```csharp
public record TestHistoryItemDto
{
    public Guid LaunchId { get; init; }
    public int LaunchNumber { get; init; }
    public string Status { get; init; } = "Unknown"; // Passed, Failed, Skipped, Stopped
    public List<string>? Attributes { get; init; }
    public TimeSpan? Duration { get; init; }
    public int ErrorCount { get; init; }
    public Guid TestItemId { get; init; } // ID of this specific test item in that launch
}
```

### State Variables
```csharp
private List<TestHistoryItemDto> _historyItems = new();
private int _currentHistoryIndex = 0;
private bool _showTooltips = true;
private ElementReference _historyScrollRef;
```

---

## C# Logic

### Load Test History
```csharp
private async Task LoadTestHistoryAsync()
{
    if (_testItem == null) return;

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");

        // API call to get test history across launches
        // Uses test name/unique identifier to find same test in other launches
        var response = await http.GetAsync(
            $"/api/test-items/{_testItem.Id}/history?limit=20"
        );

        if (response.IsSuccessStatusCode)
        {
            _historyItems = await response.Content
                .ReadFromJsonAsync<List<TestHistoryItemDto>>() ?? new();

            // Find current test's position in history
            _currentHistoryIndex = _historyItems
                .FindIndex(h => h.TestItemId == _testItem.Id);

            if (_currentHistoryIndex < 0)
                _currentHistoryIndex = 0;

            StateHasChanged();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load test history: {ex.Message}");
        _historyItems = new();
    }
}
```

### Navigation Methods
```csharp
private string GetHistoryItemClass(int index)
{
    var classes = new List<string>();

    if (index == _currentHistoryIndex)
        classes.Add("history-item-active");

    if (index == _historyItems.Count - 1)
        classes.Add("history-item-last");

    return string.Join(" ", classes);
}

private string GetStatusClass(string status)
{
    return status.ToLower() switch
    {
        "passed" => "status-passed",
        "failed" => "status-failed",
        "skipped" => "status-skipped",
        "stopped" => "status-stopped",
        _ => "status-failed"
    };
}


private async Task NavigateToPreviousLaunch()
{
    if (_currentHistoryIndex > 0)
    {
        await NavigateToHistoryLaunch(_currentHistoryIndex - 1);
    }
}

private async Task NavigateToNextLaunch()
{
    if (_currentHistoryIndex < _historyItems.Count - 1)
    {
        await NavigateToHistoryLaunch(_currentHistoryIndex + 1);
    }
}

private async Task NavigateToHistoryLaunch(int index)
{
    if (index < 0 || index >= _historyItems.Count) return;

    var historyItem = _historyItems[index];

    // Navigate to the test item in that launch
    Navigation.NavigateTo($"/{ProjectKey}/test-items/{historyItem.TestItemId}");
}

private string FormatDuration(TimeSpan? duration)
{
    if (!duration.HasValue) return "N/A";

    var d = duration.Value;
    if (d.TotalSeconds < 1)
        return $"{d.Milliseconds}ms";
    else if (d.TotalMinutes < 1)
        return $"{d.TotalSeconds:F1}s";
    else
        return $"{d.TotalMinutes:F1}m";
}
```

### Auto-Scroll to Center Active Item
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender && _historyItems.Count > 0)
    {
        await ScrollToActiveHistoryItem();
    }
}

private async Task ScrollToActiveHistoryItem()
{
    try
    {
        await JS.InvokeVoidAsync("scrollHistoryToActive", _currentHistoryIndex);
    }
    catch
    {
        // Ignore JS errors
    }
}
```

### JavaScript for Smooth Scroll
```javascript
// wwwroot/js/test-item-details.js

window.scrollHistoryToActive = function(activeIndex) {
    const historyScroll = document.querySelector('.history-line-scroll');
    const historyItems = document.querySelectorAll('.history-line-item');

    if (!historyScroll || !historyItems || activeIndex >= historyItems.length) {
        return;
    }

    const activeItem = historyItems[activeIndex];
    const itemLeft = activeItem.offsetLeft;
    const itemWidth = activeItem.offsetWidth;
    const scrollWidth = historyScroll.offsetWidth;

    // Center the item in the scroll container
    const scrollTo = itemLeft - (scrollWidth / 2) + (itemWidth / 2);

    historyScroll.scrollTo({
        left: Math.max(0, scrollTo),
        behavior: 'smooth'
    });
};
```

---

## Backend API Implementation

### Endpoint: Get Test History
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs

app.MapGet("/api/test-items/{itemId:guid}/history", async (
    Guid itemId,
    [FromQuery] int limit,
    [FromServices] IResultsStore store) =>
{
    // Get the test item to find its unique identifier
    var testItem = await store.GetTestItemAsync(itemId);
    if (testItem == null)
        return Results.NotFound();

    // Find test history using unique ID or name+type combination
    var history = await store.GetTestItemHistoryAsync(
        testItem.UniqueId ?? testItem.Name,
        testItem.ItemType,
        limit
    );

    return Results.Ok(history);
})
.WithName("GetTestItemHistory")
.WithTags("TestItems");
```

### IResultsStore Method
```csharp
// hub/Application/Ports/IResultsStore.cs

Task<List<TestHistoryItemDto>> GetTestItemHistoryAsync(
    string uniqueIdOrName,
    string itemType,
    int limit);
```

### PostgreSQL Implementation
```csharp
// hub/Infrastructure/Adapters/Results/PostgresResultsStore.cs

public async Task<List<TestHistoryItemDto>> GetTestItemHistoryAsync(
    string uniqueIdOrName,
    string itemType,
    int limit)
{
    await using var conn = new NpgsqlConnection(_connString);
    await conn.OpenAsync();

    // Query test items across launches matching unique_id or name
    var sql = @"
        SELECT
            ti.run_id as test_item_id,
            ti.launch_id,
            l.launch_number,
            ti.computed_status as status,
            ti.attributes,
            ti.start_time,
            ti.finish_time,
            ti.duration_ms,
            ti.error_message,
            NULL as defect_type
        FROM test_items ti
        JOIN launches l ON ti.launch_id = l.id
        WHERE (ti.unique_id = @uniqueId OR ti.name = @name)
          AND ti.item_type = @itemType
          AND ti.has_stats = true
        ORDER BY l.launch_number DESC
        LIMIT @limit";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("uniqueId", uniqueIdOrName);
    cmd.Parameters.AddWithValue("name", uniqueIdOrName);
    cmd.Parameters.AddWithValue("itemType", itemType);
    cmd.Parameters.AddWithValue("limit", limit);

    var history = new List<TestHistoryItemDto>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var attributes = reader.IsDBNull(4)
            ? null
            : (string[])reader.GetValue(4);

        var startTime = reader.GetDateTime(5);
        var finishTime = reader.IsDBNull(6)
            ? (DateTime?)null
            : reader.GetDateTime(6);

        var duration = finishTime.HasValue
            ? finishTime.Value - startTime
            : null;

        var errorMessage = reader.IsDBNull(8)
            ? null
            : reader.GetString(8);

        var errorCount = string.IsNullOrEmpty(errorMessage) ? 0 : 1;

        history.Add(new TestHistoryItemDto
        {
            TestItemId = reader.GetGuid(0),
            LaunchId = reader.GetGuid(1),
            LaunchNumber = reader.GetInt32(2),
            Status = reader.GetString(3) ?? "Unknown",
            Attributes = attributes?.ToList(),
            Duration = duration,
            ErrorCount = errorCount
        });
    }

    return history;
}
```

---

## Keyboard Navigation

### JavaScript Setup
```javascript
// wwwroot/js/test-item-details.js

window.setupHistoryKeyboard = function() {
    document.addEventListener('keydown', (e) => {
        // Don't interfere with text input
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') {
            return;
        }

        const prevBtn = document.getElementById('prevLaunchBtn');
        const nextBtn = document.getElementById('nextLaunchBtn');

        if (e.key === 'ArrowLeft' && prevBtn && !prevBtn.disabled) {
            e.preventDefault();
            prevBtn.click();
        } else if (e.key === 'ArrowRight' && nextBtn && !nextBtn.disabled) {
            e.preventDefault();
            nextBtn.click();
        }
    });
};
```

### Blazor Integration
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await JS.InvokeVoidAsync("setupHistoryKeyboard");

        if (_historyItems.Count > 0)
        {
            await ScrollToActiveHistoryItem();
        }
    }
}
```

---

## Testing Checklist

### Visual
- [ ] History badges display with correct colors
- [ ] Arrow connectors appear between badges
- [ ] Active indicator triangle points down from current launch
- [ ] Previous/Next buttons styled correctly
- [ ] Position indicator shows "X / Y" format
- [ ] Tooltips appear on hover (not on active item)

### Functional
- [ ] Load test history successfully
- [ ] Previous button navigates to earlier launch
- [ ] Next button navigates to later launch
- [ ] Click any badge navigates to that launch
- [ ] Keyboard arrows navigate history
- [ ] Auto-scroll centers active item on load
- [ ] Previous button disabled at first launch
- [ ] Next button disabled at last launch
- [ ] Position indicator updates correctly

### Tooltip Content
- [ ] Launch number + status badge displayed
- [ ] Attributes shown as pills
- [ ] Duration formatted correctly
- [ ] Error count shows when > 0
- [ ] Tooltip appears above badge
- [ ] Tooltip arrow points down to badge
- [ ] Hover animation smooth

### Edge Cases
- [ ] Single launch history (no navigation)
- [ ] No history available (hide component?)
- [ ] Very long launch numbers (#999+)
- [ ] Missing attributes/defect type
- [ ] Network errors handled gracefully
- [ ] Mobile responsive layout

---

## Mobile Responsive

```css
@media (max-width: 768px) {
    .history-line {
        margin: 5px 15px 15px;
        gap: 8px;
    }

    .history-nav-btn {
        width: 32px;
        height: 32px;
        font-size: 14px;
    }

    .history-line-item {
        margin-right: 24px;
    }

    .history-status-block {
        width: 64px;
        height: 28px;
        font-size: 12px;
    }

    .launch-position-indicator {
        min-width: 50px;
        height: 28px;
        font-size: 11px;
        padding: 4px 10px;
    }

    .history-item-tooltip {
        min-width: 200px;
        font-size: 11px;
        padding: 10px 12px;
    }
}
```

---

## Performance Optimization]]

### Limit History Items
```csharp
// Default to 20 launches, allow user to expand
private const int DefaultHistoryLimit = 20;
private int _historyLimit = DefaultHistoryLimit;

private async Task LoadMoreHistory()
{ww
    _historyLimit += 20;
    await LoadTestHistoryAsync();
}
```

### Lazy Load Tooltips
```csharp
// Only render tooltip on first hover
private HashSet<int> _tooltipRendered = new();

private bool ShouldRenderTooltip(int index)
{]]]
    return _tooltipRendered.Contains(index);
}

private void OnHistoryItemMouseEnter(int index)
{
    if (!_tooltipRendered.Contains(index))
    {
        _tooltipRendered.Add(index);
        StateHasChanged();
    }
}
```

---

## Future Enhancements

Failed to load test history for item "45a10f23-525c-400f-93a6-98587c652ad2"
System.InvalidOperationException: An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set.
at System.Net.Http.HttpClient.PrepareRequestMessage(HttpRequestMessage request)
at System.Net.Http.HttpClient.SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
at Dashboard.Pages.TestItemDetails.LoadTestHistoryAsync() in /Users/asuruceanu/RiderProjects/agenix-playwright-grid/dashboard/Pages/TestItemDetails.razor:line 1140
   - Date range filter

2. **Comparison Mode**
   - Select two launches to compare
   - Highlight differences

3. **Trends**
   - Pass rate trend line above history
   - Duration trend chart

4. **Export**
   - Export history as CSV/JSON
   - Generate trend report

---

## Next Phase
**Phase 4:** Main Tabs Structure (Execution Logs, Stack Trace, Item Details, Artifacts tabs)
