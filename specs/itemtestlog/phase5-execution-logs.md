# Phase 5: Execution Logs Tab

## Overview
Implement the Execution Logs tab showing structured log entries with filtering, sorting, and multiple view modes (Markdown vs Console). This is the most complex tab with nested test steps, expand/collapse functionality, and real-time log filtering.

## Goals
- ✅ Compact filter bar with dropdown and search
- ✅ Advanced filters panel (collapsible)
- ✅ Test summary bar with expand/collapse all and density toggle
- ✅ Hierarchical log entries (Launch → Suite → Test → Steps)
- ✅ Markdown view (structured) and Console view (flat text)
- ✅ Log level badges with proper styling
- ✅ Attachment badges on log entries
- ✅ Pagination with per-page selector
- ✅ Show more/less for long log messages
- ✅ Light/Dark theme for console view

---

## Component Structure

### HTML Structure (Markdown View)

```razor
<!-- Info Panel with Filters -->
<div class="info-panel">
    <!-- Compact Filter Bar -->
    <div class="compact-filter-bar">
        <div class="filter-bar-left">
            <input type="text" class="filter-text-input compact"
                   placeholder="Filter log messages..."
                   @bind="_logFilterText"
                   @bind:event="oninput">

            <button class="filter-chip @(_showAdvancedFilters ? "active" : "")"
                    @onclick="ToggleAdvancedFilters">
                <i class="bi bi-funnel"></i>
                <span>Filters</span>
                @if (_activeFiltersCount > 0)
                {
                    <span class="filter-count">@_activeFiltersCount</span>
                }
                <i class="bi bi-chevron-down filter-chevron"></i>
            </button>
        </div>

        <div class="filter-bar-right">
            <div class="view-mode-toggle compact">
                <button class="view-mode-btn @(_viewMode == "markdown" ? "active" : "")"
                        @onclick='() => _viewMode = "markdown"'
                        title="Markdown View">
                    <i class="bi bi-list-ul"></i>
                </button>
                <button class="view-mode-btn @(_viewMode == "console" ? "active" : "")"
                        @onclick='() => _viewMode = "console"'
                        title="Console View">
                    <i class="bi bi-terminal"></i>
                </button>
            </div>

            @if (_viewMode == "console")
            {
                <div class="theme-toggle compact">
                    <button class="theme-btn @(_consoleTheme == "light" ? "active" : "")"
                            @onclick='() => _consoleTheme = "light"'
                            title="Light Theme">
                        <i class="bi bi-sun"></i>
                    </button>
                    <button class="theme-btn @(_consoleTheme == "dark" ? "active" : "")"
                            @onclick='() => _consoleTheme = "dark"'
                            title="Dark Theme">
                        <i class="bi bi-moon"></i>
                    </button>
                </div>
            }
        </div>
    </div>

    <!-- Advanced Filters Panel (Collapsible) -->
    @if (_showAdvancedFilters)
    {
        <div class="advanced-filters">
            <div class="filter-row">
                <div class="filter-col">
                    <label class="filter-label">LOG LEVEL</label>
                    <select class="filter-dropdown compact" @bind="_logLevelFilter">
                        <option value="">All Logs</option>
                        <option value="fatal">Fatal Only</option>
                        <option value="error">Error Only</option>
                        <option value="warn">Warn and Above</option>
                        <option value="info">Info and Above</option>
                    </select>
                </div>

                <div class="filter-col">
                    <label class="filter-label">STATUS</label>
                    <select class="filter-dropdown compact" @bind="_statusFilter">
                        <option value="">All Statuses</option>
                        <option value="passed">Passed</option>
                        <option value="failed">Failed</option>
                    </select>
                </div>

                <div class="filter-col">
                    <label class="filter-label">OPTIONS</label>
                    <div class="checkbox-filter compact">
                        <input type="checkbox" id="with-attachment" @bind="_showOnlyWithAttachments">
                        <label for="with-attachment">
                            <i class="bi bi-paperclip"></i>
                            With Attachments
                        </label>
                    </div>
                </div>
            </div>
        </div>
    }
</div>

<!-- Test Summary Bar -->
<div class="test-summary-bar">
    <div class="summary-left">
        <div class="summary-action-btns">
            <button class="summary-btn" @onclick="ToggleExpandAll">
                <i class="bi @(_allExpanded ? "bi-dash-square" : "bi-plus-square")"></i>
                <span>@(_allExpanded ? "Collapse all" : "Expand all")</span>
            </button>
            <span class="summary-divider"></span>
            <button class="summary-btn" @onclick="CycleDensity">
                <i class="bi bi-speedometer2"></i>
                <span>@_densityMode</span>
            </button>
        </div>
    </div>
    <div class="summary-stats">
        <div class="summary-stat passed">
            <span class="stat-icon">✓</span>
            <span>@_testItem.PassedTests passed</span>
        </div>
        <div class="summary-stat failed">
            <span class="stat-icon">✗</span>
            <span>@_testItem.FailedTests failed</span>
        </div>
        <div class="summary-stat skipped">
            <span class="stat-icon">⊝</span>
            <span>@_testItem.SkippedTests skipped</span>
        </div>
    </div>
</div>

<!-- Log Entries (Markdown Mode) -->
<div class="log-entries @GetDensityClass()" id="markdown-view">
    @foreach (var entry in FilteredLogEntries)
    {
        @if (entry.IsStepHeader)
        {
            <!-- Step Header Row -->
            <div class="log-entry step-header @(entry.IsNested ? "nested-step" : "")"
                 data-parent="@entry.Id">
                <div class="step-name-cell">
                    <button class="expand-collapse-btn @(IsExpanded(entry.Id) ? "expanded" : "")"
                            @onclick="() => ToggleExpand(entry.Id)">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">
                            <path d="M14 29.17L22.657 20 14 10.83 16.672 8 28 20 16.672 32z"></path>
                        </svg>
                    </button>
                    <div class="step-name-text">
                        @entry.Name
                        @if (!string.IsNullOrEmpty(entry.Description))
                        {
                            <small>@entry.Description</small>
                        }
                    </div>
                </div>
                <div class="step-status-cell">
                    <div class="status-indicator @entry.Status.ToLower()">@entry.Status.ToUpper()</div>
                </div>
                <div class="step-stats-cell">
                    @if (entry.AttachmentCount > 0)
                    {
                        <div class="attachment-count">
                            <i class="bi bi-paperclip"></i>
                            <span>@entry.AttachmentCount</span>
                        </div>
                    }
                    <div class="duration-block">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 14 14">
                            <path fill-rule="evenodd" d="M6.997 0A6.995 6.995 0 0 0 0 7c0 3.867 3.129 7 6.997 7a7.001 7.001 0 1 0 0-14zM7 12.6A5.598 5.598 0 0 1 1.4 7c0-3.094 2.506-5.6 5.6-5.6s5.6 2.506 5.6 5.6-2.506 5.6-5.6 5.6zm.35-9.1H6.3v4.2l3.672 2.205.528-.861-3.15-1.869V3.5z"></path>
                        </svg>
                        <span>@FormatDuration(entry.DurationMs)</span>
                    </div>
                </div>
            </div>
        }
        else
        {
            <!-- Regular Log Entry -->
            <div class="log-entry @(entry.IsNested ? "nested-step" : "")"
                 style="@(entry.IsNested ? $"padding-left: {entry.NestLevel * 40 + 8}px" : "")"
                 data-child-of="@entry.ParentId">
                <span class="log-timestamp">@entry.Timestamp.ToString("HH:mm:ss.fff")</span>
                <span class="log-level @entry.Level.ToLower()">@entry.Level.ToUpper()</span>
                <span class="log-source">@entry.Source</span>
                <span class="log-message @(entry.IsTruncated ? "truncated" : "")"
                      data-message-id="@entry.Id">@entry.Message</span>

                @if (entry.IsTruncated)
                {
                    <a href="javascript:void(0)" class="show-more-link"
                       @onclick="() => ToggleMessageExpansion(entry.Id)">
                        <span>@(IsMessageExpanded(entry.Id) ? "show less" : "show more")</span>
                        <i class="bi bi-chevron-down"></i>
                    </a>
                }

                @if (entry.HasAttachment)
                {
                    <span class="log-attachment-badge @GetAttachmentClass(entry.AttachmentType)">
                        <i class="bi @GetAttachmentIcon(entry.AttachmentType)"></i>
                        @entry.AttachmentName
                    </span>
                }
            </div>
        }
    }
</div>

<!-- Console View (Hidden by default) -->
<div class="console-view @(_consoleTheme == "dark" ? "dark-theme" : "")"
     id="console-view"
     style="display: @(_viewMode == "console" ? "block" : "none")">
    @foreach (var entry in FilteredLogEntries.Where(e => !e.IsStepHeader))
    {
        <div class="console-entry">
            <span class="console-timestamp">@entry.Timestamp.ToString("HH:mm:ss.fff")</span>
            <span class="console-level @entry.Level.ToLower()">[@entry.Level.ToUpper()]</span>
            <span class="console-source">[@entry.Source]</span>
            <span class="console-message">@entry.Message</span>
        </div>
    }
</div>

<!-- Pagination -->
<div class="compact-pagination">
    <div class="pagination-info-left">
        <strong>@_currentPageStart - @_currentPageEnd</strong> of <strong>@_totalEntries</strong> entries
    </div>
    <div class="pagination-controls">
        <button class="pagination-btn" @onclick="PreviousPage" disabled="@(_currentPage == 1)">
            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                <path fill-rule="evenodd" d="M12 8a.5.5 0 0 1-.5.5H5.707l2.147 2.146a.5.5 0 0 1-.708.708l-3-3a.5.5 0 0 1 0-.708l3-3a.5.5 0 1 1 .708.708L5.707 7.5H11.5a.5.5 0 0 1 .5.5z"></path>
            </svg>
            Previous
        </button>
        <button class="pagination-btn" @onclick="NextPage" disabled="@(_currentPage >= _totalPages)">
            Next
            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                <path fill-rule="evenodd" d="M4 8a.5.5 0 0 1 .5-.5h5.793L8.146 5.354a.5.5 0 1 1 .708-.708l3 3a.5.5 0 0 1 0 .708l-3 3a.5.5 0 0 1-.708-.708L10.293 8.5H4.5A.5.5 0 0 1 4 8z"></path>
            </svg>
        </button>
    </div>
    <div class="pagination-info-right">
        <select class="per-page-select" @bind="_itemsPerPage" @bind:event="onchange">
            <option value="10">10</option>
            <option value="25">25</option>
            <option value="50">50</option>
            <option value="100">100</option>
            <option value="200">200</option>
        </select>
        <span>per page</span>
    </div>
</div>
```

---

## CSS Styling

### Source Files
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html`:
- **Info Panel**: lines 209-393 (filters)
- **Test Summary Bar**: lines 381-472 (summary)
- **Log Entries**: lines 743-1028 (entries, step headers, log levels, badges)
- **Console View**: lines 1166-1207 (console theme)
- **Pagination**: lines 1094-1164 (pagination)

---

## C# Data Model

### Log Entry DTO
```csharp
public class LogEntryDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsStepHeader { get; set; }
    public bool IsNested { get; set; }
    public int NestLevel { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "INFO";
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "InProgress";
    public long? DurationMs { get; set; }
    public int AttachmentCount { get; set; }
    public bool HasAttachment { get; set; }
    public string AttachmentType { get; set; } = "";
    public string AttachmentName { get; set; } = "";
    public bool IsTruncated => Message.Length > 300;
}
```

### State Variables
```csharp
private List<LogEntryDto> _logEntries = new();
private string _logFilterText = "";
private string _logLevelFilter = "";
private string _statusFilter = "";
private bool _showOnlyWithAttachments = false;
private bool _showAdvancedFilters = false;
private bool _allExpanded = true;
private string _densityMode = "Default";
private string _viewMode = "markdown";
private string _consoleTheme = "light";
private HashSet<Guid> _expandedSteps = new();
private HashSet<Guid> _expandedMessages = new();
private int _currentPage = 1;
private int _itemsPerPage = 50;
private int _totalEntries => FilteredLogEntries.Count();
private int _totalPages => (int)Math.Ceiling(_totalEntries / (double)_itemsPerPage);
private int _currentPageStart => (_currentPage - 1) * _itemsPerPage + 1;
private int _currentPageEnd => Math.Min(_currentPage * _itemsPerPage, _totalEntries);
```

---

## C# Logic

### Load Log Entries
```csharp
private async Task LoadLogEntriesAsync()
{
    if (_testItem == null) return;

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");
        var response = await http.GetAsync($"/api/test-items/{_testItem.Id}/logs");

        if (response.IsSuccessStatusCode)
        {
            _logEntries = await response.Content
                .ReadFromJsonAsync<List<LogEntryDto>>() ?? new();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load log entries: {ex.Message}");
    }
}
```

### Filtering Logic
```csharp
private IEnumerable<LogEntryDto> FilteredLogEntries
{
    get
    {
        var query = _logEntries.AsEnumerable();

        // Text filter
        if (!string.IsNullOrWhiteSpace(_logFilterText))
        {
            var filter = _logFilterText.ToLower();
            query = query.Where(e =>
                e.Message.ToLower().Contains(filter) ||
                e.Source.ToLower().Contains(filter) ||
                e.Name.ToLower().Contains(filter));
        }

        // Log level filter
        if (!string.IsNullOrWhiteSpace(_logLevelFilter))
        {
            query = _logLevelFilter.ToLower() switch
            {
                "fatal" => query.Where(e => e.Level.ToLower() == "fatal"),
                "error" => query.Where(e => e.Level.ToLower() == "error"),
                "warn" => query.Where(e => e.Level.ToLower() is "warn" or "error" or "fatal"),
                "info" => query.Where(e => e.Level.ToLower() is not "debug" and not "trace"),
                _ => query
            };
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(_statusFilter))
        {
            query = query.Where(e => e.Status.ToLower() == _statusFilter.ToLower());
        }

        // Attachment filter
        if (_showOnlyWithAttachments)
        {
            query = query.Where(e => e.HasAttachment || e.AttachmentCount > 0);
        }

        // Pagination
        return query
            .Skip((_currentPage - 1) * _itemsPerPage)
            .Take(_itemsPerPage);
    }
}
```

### Expand/Collapse Logic
```csharp
private void ToggleExpand(Guid stepId)
{
    if (_expandedSteps.Contains(stepId))
        _expandedSteps.Remove(stepId);
    else
        _expandedSteps.Add(stepId);
}

private bool IsExpanded(Guid stepId) => _expandedSteps.Contains(stepId);

private void ToggleExpandAll()
{
    _allExpanded = !_allExpanded;

    if (_allExpanded)
    {
        _expandedSteps = _logEntries
            .Where(e => e.IsStepHeader)
            .Select(e => e.Id)
            .ToHashSet();
    }
    else
    {
        _expandedSteps.Clear();
    }
}
```

### Density Mode
```csharp
private void CycleDensity()
{
    _densityMode = _densityMode switch
    {
        "Default" => "Comfortable",
        "Comfortable" => "Compact",
        "Compact" => "Default",
        _ => "Default"
    };
}

private string GetDensityClass()
{
    return _densityMode.ToLower() switch
    {
        "comfortable" => "density-comfortable",
        "compact" => "density-compact",
        _ => ""
    };
}
```

### Message Expansion
```csharp
private void ToggleMessageExpansion(Guid messageId)
{
    if (_expandedMessages.Contains(messageId))
        _expandedMessages.Remove(messageId);
    else
        _expandedMessages.Add(messageId);
}

private bool IsMessageExpanded(Guid messageId) => _expandedMessages.Contains(messageId);
```

### Attachment Helpers
```csharp
private string GetAttachmentClass(string type)
{
    return type.ToLower() switch
    {
        "image" => "attachment-image",
        "warning" => "attachment-warning",
        _ => ""
    };
}

private string GetAttachmentIcon(string type)
{
    return type.ToLower() switch
    {
        "image" => "bi-file-image",
        "warning" => "bi-exclamation-triangle-fill",
        _ => "bi-paperclip"
    };
}
```

### Pagination
```csharp
private void NextPage()
{
    if (_currentPage < _totalPages)
        _currentPage++;
}

private void PreviousPage()
{
    if (_currentPage > 1)
        _currentPage--;
}
```

### Duration Formatting
```csharp
private string FormatDuration(long? durationMs)
{
    if (!durationMs.HasValue) return "N/A";

    var ms = durationMs.Value;
    if (ms < 1000)
        return $"{ms}ms";
    else if (ms < 60000)
        return $"{ms / 1000.0:F1}s";
    else
        return $"{ms / 60000.0:F1}m";
}
```

---

## Backend API

### Endpoint: Get Test Item Logs
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs

app.MapGet("/api/test-items/{itemId:guid}/logs", async (
    Guid itemId,
    [FromServices] IResultsStore store) =>
{
    var logs = await store.GetLogEntriesForTestItemAsync(itemId);
    return Results.Ok(logs);
})
.WithName("GetTestItemLogs")
.WithTags("TestItems");
```

### IResultsStore Method
```csharp
Task<List<LogEntryDto>> GetLogEntriesForTestItemAsync(Guid itemId);
```

---

## Testing Checklist

### Visual
- [ ] Filter bar displays with search and filter chip
- [ ] Advanced filters slide down smoothly when opened
- [ ] Summary bar shows correct test stats
- [ ] Step headers styled with gradients
- [ ] Log level badges maintain fixed size
- [ ] Attachment badges display correctly
- [ ] Console view light/dark themes work
- [ ] Pagination controls styled properly

### Functional
- [ ] Text filter works on message/source/name
- [ ] Log level filter works correctly
- [ ] Status filter works correctly
- [ ] Attachment filter works correctly
- [ ] Expand/collapse individual steps
- [ ] Expand all / Collapse all works
- [ ] Density toggle cycles through 3 modes
- [ ] View mode toggle (markdown/console)
- [ ] Theme toggle (light/dark) for console
- [ ] Show more/less for long messages
- [ ] Pagination next/previous buttons work
- [ ] Per-page selector updates pagination

### Edge Cases
- [ ] Empty log entries list
- [ ] No matching filter results
- [ ] Very long log messages (truncation)
- [ ] Deeply nested steps (3+ levels)
- [ ] Mixed log levels
- [ ] Multiple attachments per log
- [ ] Console view with no step headers

---

## Next Phase
**Phase 6:** Stack Trace Tab (error entries, expand/collapse, jump to log)
