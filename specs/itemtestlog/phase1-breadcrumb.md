# Phase 1: Breadcrumb Navigation Component

## Overview
Implement elegant breadcrumb navigation for TestItemDetails.razor page, showing the hierarchical path from All Tests → Suite → Current Test Item.

## Goals
- ✅ Display hierarchical navigation path
- ✅ Enable quick navigation to parent levels
- ✅ Show current test item prominently
- ✅ Add collapse button for minimizing breadcrumb (optional)

---

## Component Structure

### HTML Structure
```razor
<div class="breadcrumbs-section">
    <!-- Collapse Button (Left) -->
    <button class="breadcrumb-btn" title="Collapse" @onclick="ToggleBreadcrumb">
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 17 17">
            <path fill-rule="evenodd" d="M1.889 0C.85 0 0 .85 0 1.889V15.11C0 16.15.85 17 1.889 17H15.11C16.15 17 17 16.15 17 15.111V1.89C17 .85 16.15 0 15.111 0H1.89zM1 1h15v15H1V1zm13 8H3V8h11v1z"></path>
        </svg>
    </button>

    <!-- Breadcrumb Links -->
    <a href="/@ProjectKey/launches" class="breadcrumb-link">All</a>

    <span class="breadcrumb-separator">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">
            <path d="M14 29.17L22.657 20 14 10.83 16.672 8 28 20 16.672 32z"></path>
        </svg>
    </span>

    @if (_parentSuite != null)
    {
        <a href="/@ProjectKey/suites/@_parentSuite.Id" class="breadcrumb-link">
            @_parentSuite.Name
        </a>

        <span class="breadcrumb-separator">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">
                <path d="M14 29.17L22.657 20 14 10.83 16.672 8 28 20 16.672 32z"></path>
            </svg>
        </span>
    }

    <span class="breadcrumb-current">@_testItem?.Name</span>
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines 21-84

### Key Classes

**`.breadcrumbs-section`**
```css
.breadcrumbs-section {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 20px;
}
```

**`.breadcrumb-btn`**
```css
.breadcrumb-btn {
    display: inline-flex;
    align-items: center;
    padding: 0.375rem;
    background: transparent;
    border: none;
    cursor: pointer;
    transition: opacity 0.15s ease;
}

.breadcrumb-btn:hover {
    opacity: 0.7;
}

.breadcrumb-btn svg {
    width: 20px;
    height: 20px;
    fill: #6b7280;
}
```

**`.breadcrumb-link`**
```css
.breadcrumb-link {
    font-size: 0.875rem;
    font-weight: 500;
    color: #3b82f6;
    text-decoration: none;
}

.breadcrumb-link:hover {
    color: #2563eb;
    text-decoration: underline;
}
```

**`.breadcrumb-separator`**
```css
.breadcrumb-separator {
    display: flex;
    align-items: center;
    color: #d1d5db;
}

.breadcrumb-separator svg {
    width: 16px;
    height: 16px;
    fill: currentColor;
}
```

**`.breadcrumb-current`**
```css
.breadcrumb-current {
    font-size: 0.875rem;
    font-weight: 600;
    color: #1f2937;
}
```

---

## C# Logic

### State Variables
```csharp
[Parameter] public string ProjectKey { get; set; } = default!;
[Parameter] public Guid ItemId { get; set; }

private TestItemDto? _testItem;
private TestItemDto? _parentSuite;
private bool _breadcrumbCollapsed = false;
```

### Load Parent Suite
```csharp
private async Task LoadParentSuiteAsync()
{
    if (_testItem?.ParentItemId == null) return;

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");
        var response = await http.GetAsync($"/api/test-items/{_testItem.ParentItemId}");

        if (response.IsSuccessStatusCode)
        {
            _parentSuite = await response.Content.ReadFromJsonAsync<TestItemDto>();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load parent suite: {ex.Message}");
    }
}
```

### Toggle Breadcrumb
```csharp
private void ToggleBreadcrumb()
{
    _breadcrumbCollapsed = !_breadcrumbCollapsed;
}
```

---

## Integration Steps

### Step 1: Add CSS
Create new file `dashboard/wwwroot/css/test-item-details.css` and add breadcrumb styles.

### Step 2: Import CSS
Add to TestItemDetails.razor:
```razor
<link href="/css/test-item-details.css" rel="stylesheet" />
```

### Step 3: Add HTML
Replace existing header with breadcrumb section.

### Step 4: Load Parent Data
Call `LoadParentSuiteAsync()` in `OnParametersSetAsync()` after loading test item.

### Step 5: Test Navigation
- Click "All" → Should navigate to launches list
- Click parent suite name → Should navigate to suite details
- Current item should not be clickable

---

## Example Flow

**Test Item Hierarchy:**
```
All Tests
  └─ Login Tests (Suite)
      └─ Login with valid credentials (Test) ← Current Item
```

**Breadcrumb Display:**
```
[≡] All > Login Tests > Login with valid credentials
```

**Click "All":**
- Navigates to: `/{projectKey}/launches`

**Click "Login Tests":**
- Navigates to: `/{projectKey}/suites/{suiteId}`

**Current Item:**
- Not clickable
- Bold styling
- Stays on current page

---

## Optional Features

### Collapse Functionality
When breadcrumb is collapsed:
- Show only: `[≡] ... > Current Item Name`
- Save space for small screens
- Toggle with collapse button

### Mobile Responsive
```css
@media (max-width: 768px) {
    .breadcrumbs-section {
        flex-wrap: wrap;
        gap: 0.25rem;
    }

    .breadcrumb-link,
    .breadcrumb-current {
        font-size: 0.75rem;
    }

    .breadcrumb-btn svg {
        width: 16px;
        height: 16px;
    }
}
```

---

## Testing Checklist

- [ ] Breadcrumb displays correctly
- [ ] "All" link navigates to launches
- [ ] Parent suite link navigates to suite details
- [ ] Current item is bold and non-clickable
- [ ] Separators display correctly
- [ ] Collapse button works (if implemented)
- [ ] Mobile responsive layout
- [ ] Hover effects work on links

---

## Next Phase
**Phase 2:** Test Navigation Controls (Previous/Next/Refresh buttons)
