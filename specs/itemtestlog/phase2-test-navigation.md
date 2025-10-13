# Phase 2: Test Navigation Controls

## Overview
Add Previous/Next test navigation buttons and Refresh button to the breadcrumb section, enabling users to browse through tests within the same suite without returning to the suite list.

## Goals
- ✅ Navigate to previous test in suite
- ✅ Navigate to next test in suite
- ✅ Refresh current test item data
- ✅ Handle boundary cases (first/last test)
- ✅ Disable buttons appropriately

---

## Component Structure

### HTML Structure
```razor
<div class="breadcrumbs-section">
    <!-- Collapse Button + Breadcrumb (from Phase 1) -->
    <button class="breadcrumb-btn">...</button>
    <a href="..." class="breadcrumb-link">All</a>
    ...
    <span class="breadcrumb-current">@_testItem?.Name</span>

    <!-- Navigation Actions (Right Side) -->
    <div class="breadcrumb-actions">
        <button class="nav-btn"
                title="Previous Test"
                disabled="@(_previousTestId == null)"
                @onclick="NavigateToPreviousTest">
            <i class="bi bi-chevron-left"></i>
        </button>

        <button class="nav-btn"
                title="Next Test"
                disabled="@(_nextTestId == null)"
                @onclick="NavigateToNextTest">
            <i class="bi bi-chevron-right"></i>
        </button>

        <button class="refresh-btn"
                title="Refresh"
                @onclick="RefreshTestItem">
            <i class="bi bi-arrow-clockwise"></i>
        </button>
    </div>
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines 79-110

### Key Classes

**`.breadcrumb-actions`**
```css
.breadcrumb-actions {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-left: auto;
}
```

**`.nav-btn`, `.refresh-btn`**
```css
.nav-btn,
.refresh-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    padding: 0.375rem 0.5rem;
    background: #fff;
    border: 1px solid #dee2e6;
    border-radius: 4px;
    cursor: pointer;
    transition: all 0.15s ease;
    color: #495057;
}

.nav-btn:hover,
.refresh-btn:hover {
    background: #f8f9fa;
    border-color: #adb5bd;
}

.nav-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
    background: #fff;
}

.nav-btn i,
.refresh-btn i {
    font-size: 14px;
}
```

---

## C# Logic

### State Variables
```csharp
private List<TestItemDto>? _siblingTests;
private Guid? _previousTestId;
private Guid? _nextTestId;
private bool _refreshing = false;
```

### Load Sibling Tests
```csharp
private async Task LoadSiblingTestsAsync()
{
    if (_testItem?.ParentItemId == null)
    {
        // Root level test, no siblings
        _siblingTests = null;
        _previousTestId = null;
        _nextTestId = null;
        return;
    }

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");
        var response = await http.GetAsync(
            $"/api/test-items/{_testItem.ParentItemId}/children?itemType=Test"
        );

        if (response.IsSuccessStatusCode)
        {
            _siblingTests = await response.Content
                .ReadFromJsonAsync<List<TestItemDto>>();

            CalculatePreviousNextIds();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load sibling tests: {ex.Message}");
        _siblingTests = null;
    }
}

private void CalculatePreviousNextIds()
{
    if (_siblingTests == null || _testItem == null)
    {
        _previousTestId = null;
        _nextTestId = null;
        return;
    }

    // Sort by start time (chronological order)
    var sortedTests = _siblingTests
        .OrderBy(t => t.StartTime)
        .ToList();

    var currentIndex = sortedTests.FindIndex(t => t.Id == _testItem.Id);

    if (currentIndex < 0)
    {
        _previousTestId = null;
        _nextTestId = null;
        return;
    }

    // Previous test
    _previousTestId = currentIndex > 0
        ? sortedTests[currentIndex - 1].Id
        : null;

    // Next test
    _nextTestId = currentIndex < sortedTests.Count - 1
        ? sortedTests[currentIndex + 1].Id
        : null;
}
```

### Navigation Methods
```csharp
private void NavigateToPreviousTest()
{
    if (_previousTestId.HasValue)
    {
        Navigation.NavigateTo($"/{ProjectKey}/test-items/{_previousTestId.Value}");
    }
}

private void NavigateToNextTest()
{
    if (_nextTestId.HasValue)
    {
        Navigation.NavigateTo($"/{ProjectKey}/test-items/{_nextTestId.Value}");
    }
}

private async Task RefreshTestItem()
{
    if (_refreshing) return;

    _refreshing = true;
    StateHasChanged();

    try
    {
        await LoadTestItemAsync();
        await LoadSiblingTestsAsync();
    }
    finally
    {
        _refreshing = false;
        StateHasChanged();
    }
}
```

### OnParametersSetAsync Integration
```csharp
protected override async Task OnParametersSetAsync()
{
    _loading = true;
    _error = null;

    try
    {
        await LoadTestItemAsync();       // Load current test
        await LoadParentSuiteAsync();    // Phase 1
        await LoadSiblingTestsAsync();   // Phase 2 - NEW
    }
    catch (Exception ex)
    {
        _error = ex.Message;
    }
    finally
    {
        _loading = false;
    }
}
```

---

## Backend API Requirements

### Endpoint: Get Child Items
Already exists from Phase 4 (ReportPortal model):
```
GET /api/test-items/{parentId}/children?itemType={type}
```

**Query Parameters:**
- `itemType` (optional): Filter by item type (Test, Step, Suite, etc.)

**Response:**
```json
[
  {
    "id": "guid-1",
    "name": "Test 1",
    "itemType": "Test",
    "startTime": "2025-01-16T10:00:00Z",
    "computedStatus": "Passed"
  },
  {
    "id": "guid-2",
    "name": "Test 2",
    "itemType": "Test",
    "startTime": "2025-01-16T10:05:00Z",
    "computedStatus": "Failed"
  }
]
```

**Implementation:**
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs

app.MapGet("/api/test-items/{parentId:guid}/children", async (
    Guid parentId,
    [FromQuery] string? itemType,
    [FromServices] IResultsStore store) =>
{
    var children = await store.GetChildItemsAsync(parentId);

    if (!string.IsNullOrWhiteSpace(itemType))
    {
        children = children.Where(c => c.ItemType == itemType).ToList();
    }

    return Results.Ok(children);
})
.WithName("GetTestItemChildren")
.WithTags("TestItems");
```

---

## User Experience Flow

### Scenario 1: Navigate Through Tests
**Suite has 5 tests:**
1. Login Test
2. Dashboard Test ← **Current**
3. Profile Test
4. Settings Test
5. Logout Test

**UI State:**
- Previous button: **Enabled** (goes to "Login Test")
- Next button: **Enabled** (goes to "Profile Test")
- Position: Test 2 of 5

**Click Next:**
- Navigates to "Profile Test"
- Previous: Enabled (Dashboard Test)
- Next: Enabled (Settings Test)

### Scenario 2: First Test
**Current:** Login Test (first)

**UI State:**
- Previous button: **Disabled** (grayed out)
- Next button: **Enabled**

### Scenario 3: Last Test
**Current:** Logout Test (last)

**UI State:**
- Previous button: **Enabled**
- Next button: **Disabled** (grayed out)

### Scenario 4: Refresh
**Click Refresh button:**
1. Reload current test item data
2. Reload sibling tests (in case suite changed)
3. Recalculate previous/next IDs
4. Show spinner on refresh button (optional)

---

## Keyboard Shortcuts (Optional Enhancement)

```razor
@code {
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            JS.InvokeVoidAsync("setupTestNavigationKeyboard", DotNetObjectReference.Create(this));
        }
    }

    [JSInvokable]
    public void HandleKeyPress(string key)
    {
        if (key == "ArrowLeft" && _previousTestId.HasValue)
        {
            NavigateToPreviousTest();
        }
        else if (key == "ArrowRight" && _nextTestId.HasValue)
        {
            NavigateToNextTest();
        }
        else if (key == "F5" || (key == "r" && /* Ctrl pressed */))
        {
            RefreshTestItem();
        }
    }
}
```

**JavaScript:**
```javascript
window.setupTestNavigationKeyboard = (dotNetRef) => {
    document.addEventListener('keydown', (e) => {
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') {
            return; // Don't interfere with text input
        }

        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleKeyPress', e.key);
        }
    });
};
```

---

## Testing Checklist

- [ ] Load sibling tests successfully
- [ ] Previous button disabled on first test
- [ ] Next button disabled on last test
- [ ] Previous button navigates correctly
- [ ] Next button navigates correctly
- [ ] Refresh button reloads data
- [ ] Buttons show hover effects
- [ ] Disabled buttons not clickable
- [ ] Works with root-level tests (no parent)
- [ ] Handles empty sibling list gracefully

---

## Error Handling

### No Siblings Found
```csharp
if (_siblingTests == null || _siblingTests.Count == 0)
{
    // Disable both buttons
    _previousTestId = null;
    _nextTestId = null;
}
```

### Current Test Not in Sibling List
```csharp
var currentIndex = sortedTests.FindIndex(t => t.Id == _testItem.Id);

if (currentIndex < 0)
{
    // Test not found in siblings (data inconsistency)
    Console.WriteLine("Current test not found in sibling list");
    _previousTestId = null;
    _nextTestId = null;
    return;
}
```

### Network Errors
```csharp
catch (HttpRequestException ex)
{
    Console.WriteLine($"Network error loading siblings: {ex.Message}");
    _siblingTests = null;
}
catch (Exception ex)
{
    Console.WriteLine($"Unexpected error: {ex.Message}");
    _siblingTests = null;
}
```

---

## Mobile Responsive

```css
@media (max-width: 768px) {
    .breadcrumb-actions {
        gap: 0.25rem;
    }

    .nav-btn,
    .refresh-btn {
        padding: 0.25rem 0.375rem;
    }

    .nav-btn i,
    .refresh-btn i {
        font-size: 12px;
    }
}
```

---

## Next Phase
**Phase 3:** Test History Line Component (show execution history across launches)
