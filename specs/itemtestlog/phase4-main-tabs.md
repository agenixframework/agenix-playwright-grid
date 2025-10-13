# Phase 4: Main Tabs Structure

## Overview
Implement the main tab navigation system that organizes test item content into logical sections. This phase creates the tab container, tab buttons, and tab content panels.

## Goals
- ✅ Horizontal tab bar with 6 main tabs
- ✅ Active tab highlighting with bottom border
- ✅ Tab icons from Bootstrap Icons
- ✅ Tab content panels with conditional rendering
- ✅ Smooth transitions between tabs
- ✅ Mobile-responsive tab design

---

## Component Structure

### HTML Structure
```razor
<!-- Main Tabs -->
<div class="main-tabs">
    <button class="main-tab @(_activeTab == "logs" ? "active" : "")"
            @onclick='() => _activeTab = "logs"'>
        <i class="bi bi-terminal"></i>
        EXECUTION LOGS
    </button>
    <button class="main-tab @(_activeTab == "stack" ? "active" : "")"
            @onclick='() => _activeTab = "stack"'>
        <i class="bi bi-exclamation-triangle"></i>
        STACK TRACE
    </button>
    <button class="main-tab @(_activeTab == "details" ? "active" : "")"
            @onclick='() => _activeTab = "details"'>
        <i class="bi bi-info-circle"></i>
        ITEM DETAILS
    </button>
    <button class="main-tab @(_activeTab == "artifacts" ? "active" : "")"
            @onclick='() => _activeTab = "artifacts"'>
        <i class="bi bi-paperclip"></i>
        ARTIFACTS
    </button>
    <button class="main-tab @(_activeTab == "browser" ? "active" : "")"
            @onclick='() => _activeTab = "browser"'>
        <i class="bi bi-browser-chrome"></i>
        BROWSER SESSION
    </button>
    <button class="main-tab @(_activeTab == "commands" ? "active" : "")"
            @onclick='() => _activeTab = "commands"'>
        <i class="bi bi-code-slash"></i>
        PLAYWRIGHT SERVER COMMANDS
    </button>
</div>

<!-- Log Content Container -->
<div class="log-content">
    @if (_activeTab == "logs")
    {
        <!-- Phase 5: Execution Logs Tab -->
    }
    else if (_activeTab == "stack")
    {
        <!-- Phase 6: Stack Trace Tab -->
    }
    else if (_activeTab == "details")
    {
        <!-- Phase 7: Item Details Tab -->
    }
    else if (_activeTab == "artifacts")
    {
        <!-- Phase 8: Artifacts Tab -->
    }
    else if (_activeTab == "browser")
    {
        <!-- Browser Session Tab -->
    }
    else if (_activeTab == "commands")
    {
        <!-- Phase 9: Playwright Commands Tab -->
    }
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines **111-208**

### Key Sections

**1. Main Tabs Container** (lines 111-119)
```css
.main-tabs {
    display: flex;
    gap: 4px;
    background: #f8f9fa;
    padding: 8px 16px;
    border-bottom: 1px solid #dee2e6;
    margin-bottom: 0;
}
```

**2. Tab Buttons** (lines 121-158)
```css
.main-tab {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 12px 20px;
    background: transparent;
    border: none;
    border-radius: 8px 8px 0 0;
    color: #6c757d;
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.2s ease;
    position: relative;
    letter-spacing: 0.2px;
}

.main-tab:hover:not(.active) {
    color: #495057;
    background-color: rgba(255, 255, 255, 0.6);
}

.main-tab.active {
    background: #ffffff;
    color: #667eea;
    border-bottom: 3px solid #667eea;
    margin-bottom: -1px;
}

.main-tab i {
    width: 16px;
    height: 16px;
    opacity: 0.7;
}

.main-tab.active i {
    opacity: 1;
}
```

**3. Log Content Container** (lines 202-208)
```css
.log-content {
    background-color: #fff;
    border: 1px solid #dee2e6;
    border-top: none;
}
```

---

## C# State Management

### State Variables
```csharp
private string _activeTab = "logs"; // Default to Execution Logs tab
```

### Tab Navigation Method
```csharp
private void SwitchTab(string tabName)
{
    _activeTab = tabName;
    StateHasChanged();
}
```

---

## Tab Icons Mapping

| Tab Name | Bootstrap Icon | Purpose |
|----------|---------------|---------|
| Execution Logs | `bi-terminal` | Log entries with levels and messages |
| Stack Trace | `bi-exclamation-triangle` | Error stack traces and exceptions |
| Item Details | `bi-info-circle` | Test item metadata and properties |
| Artifacts | `bi-paperclip` | Screenshots, videos, attachments |
| Browser Session | `bi-browser-chrome` | Browser session info and diagnostics |
| Playwright Commands | `bi-code-slash` | API command history |

---

## Integration Steps

1. **Add CSS Link**
   - Copy main-tabs CSS from prototype
   - Add to `wwwroot/css/test-item-details.css`

2. **Add State Variable**
   - Add `_activeTab` string field to component
   - Initialize to "logs" for default tab

3. **Add Tab HTML**
   - Copy main-tabs div structure
   - Apply active class conditionally: `@(_activeTab == "logs" ? "active" : "")`

4. **Add Content Container**
   - Create log-content div
   - Add conditional rendering for each tab's content

5. **Verify Bootstrap Icons**
   - Ensure Bootstrap Icons CSS is loaded in `_Host.cshtml`
   - All icons should render correctly

---

## Testing Checklist

### Visual
- [ ] Tab bar displays horizontally with proper spacing
- [ ] Active tab has purple bottom border (3px solid #667eea)
- [ ] Active tab background is white
- [ ] Inactive tabs have gray text (#6c757d)
- [ ] Hover state shows light background on inactive tabs
- [ ] Icons display correctly with proper opacity
- [ ] Tab text is uppercase with proper letter spacing

### Functional
- [ ] Clicking tab switches active tab
- [ ] Content panel updates to show correct tab content
- [ ] Default tab (logs) loads on page mount
- [ ] Browser back button doesn't break tab state
- [ ] No console errors when switching tabs

### Responsive
- [ ] Tabs wrap gracefully on mobile (consider horizontal scroll)
- [ ] Tab padding reduces on smaller screens
- [ ] Icons remain visible at all breakpoints
- [ ] Tab text truncates or abbreviates on mobile

---

## Mobile Responsive

```css
@media (max-width: 768px) {
    .main-tabs {
        overflow-x: auto;
        overflow-y: hidden;
        padding: 6px 12px;
        gap: 2px;
    }

    .main-tab {
        padding: 10px 16px;
        font-size: 12px;
        flex-shrink: 0; /* Prevent compression */
        white-space: nowrap;
    }
}

@media (max-width: 480px) {
    .main-tab {
        padding: 8px 12px;
        gap: 6px;
    }

    .main-tab i {
        width: 14px;
        height: 14px;
    }
}
```

---

## Keyboard Navigation (Future Enhancement)

```csharp
private async Task HandleKeyDown(KeyboardEventArgs e)
{
    var tabs = new[] { "logs", "stack", "details", "artifacts", "browser", "commands" };
    var currentIndex = Array.IndexOf(tabs, _activeTab);

    if (e.Key == "ArrowLeft" && currentIndex > 0)
    {
        _activeTab = tabs[currentIndex - 1];
    }
    else if (e.Key == "ArrowRight" && currentIndex < tabs.Length - 1)
    {
        _activeTab = tabs[currentIndex + 1];
    }

    await InvokeAsync(StateHasChanged);
}
```

**HTML:**
```razor
<div class="main-tabs" @onkeydown="HandleKeyDown" tabindex="0">
    <!-- tabs here -->
</div>
```

---

## Accessibility

```razor
<div class="main-tabs" role="tablist" aria-label="Test item content sections">
    <button class="main-tab @(_activeTab == "logs" ? "active" : "")"
            role="tab"
            aria-selected="@(_activeTab == "logs")"
            aria-controls="logs-panel"
            @onclick='() => _activeTab = "logs"'>
        <i class="bi bi-terminal"></i>
        EXECUTION LOGS
    </button>
    <!-- other tabs -->
</div>

<div class="log-content" role="tabpanel" id="logs-panel" aria-labelledby="logs-tab">
    @if (_activeTab == "logs")
    {
        <!-- content -->
    }
</div>
```

---

## Performance Optimization

**Lazy Loading Tab Content:**
```csharp
private Dictionary<string, bool> _tabsLoaded = new()
{
    { "logs", false },
    { "stack", false },
    { "details", false },
    { "artifacts", false },
    { "browser", false },
    { "commands", false }
};

private async Task SwitchTabAsync(string tabName)
{
    _activeTab = tabName;

    if (!_tabsLoaded[tabName])
    {
        // Load tab content on first access
        await LoadTabContentAsync(tabName);
        _tabsLoaded[tabName] = true;
    }

    StateHasChanged();
}

private async Task LoadTabContentAsync(string tabName)
{
    switch (tabName)
    {
        case "stack":
            await LoadStackTraceAsync();
            break;
        case "artifacts":
            await LoadArtifactsAsync();
            break;
        case "commands":
            await LoadCommandsAsync();
            break;
    }
}
```

---

## Technical Notes

- **Tab State Persistence**: Use `NavigationManager` query parameters to persist active tab in URL
- **Deep Linking**: Support URLs like `/test-items/{id}?tab=stack` to open specific tabs
- **Animation**: Consider adding fade-in transition when switching tab content
- **Badge Counts**: Future enhancement - show counts on tabs (e.g., "STACK TRACE (3)" for 3 errors)

---

## Next Phase
**Phase 5:** Execution Logs Tab (filter bar, log entries, console view, markdown view, pagination)
