# Phase 6: Stack Trace Tab

## Overview
Implement the Stack Trace tab showing error entries with expandable stack trace details, jump to log functionality, and HTTP status codes. This tab provides detailed error analysis for failed tests.

## Goals
- ✅ Error entry cards with hover effects
- ✅ Timestamp and context display
- ✅ Error level badges (ERROR, FATAL, WARN)
- ✅ Error message with class name
- ✅ "Jump to log" button linking to Execution Logs tab
- ✅ Expandable stack trace details section
- ✅ HTTP method badges and status codes
- ✅ Code line highlighting
- ✅ Empty state when no errors
- ✅ Monospace font for technical content

---

## Component Structure

### HTML Structure
```razor
<!-- Stack Trace View -->
<div class="stack-trace-view" id="stack-view">
    @if (_stackTraceEntries == null || _stackTraceEntries.Count == 0)
    {
        <!-- Empty State -->
        <div class="stack-trace-empty">
            <i class="bi bi-check-circle"></i>
            <p>No errors or stack traces found for this test item.</p>
        </div>
    }
    else
    {
        <!-- Error Entries -->
        @foreach (var entry in _stackTraceEntries)
        {
            <div class="stack-trace-entry">
                <!-- Header Section -->
                <div class="stack-trace-header">
                    <div class="stack-trace-main">
                        <!-- Time + Context + Level Badge -->
                        <div class="stack-trace-time-context">
                            <span class="stack-trace-timestamp">@entry.Timestamp.ToString("HH:mm:ss.fff")</span>
                            @if (!string.IsNullOrEmpty(entry.Context))
                            {
                                <span class="stack-trace-context">@entry.Context</span>
                            }
                            <span class="stack-trace-level-badge @entry.Level.ToLower()">@entry.Level.ToUpper()</span>
                        </div>

                        <!-- Error Message -->
                        <div class="stack-trace-message">
                            <span class="stack-trace-class">@entry.ExceptionClass</span>: @entry.Message
                        </div>
                    </div>

                    <!-- Jump to Log Button -->
                    @if (entry.LogEntryId.HasValue)
                    {
                        <a class="stack-trace-jump"
                           @onclick="() => JumpToLogEntry(entry.LogEntryId.Value)"
                           href="javascript:void(0)">
                            <i class="bi bi-box-arrow-up-right"></i>
                            Jump to log
                        </a>
                    }
                </div>

                <!-- Expandable Stack Trace Details -->
                <div class="stack-trace-expand">
                    <div class="stack-trace-expand-header"
                         @onclick="() => ToggleStackTraceExpand(entry.Id)">
                        <div class="stack-trace-expand-title">
                            <i class="bi bi-code-slash"></i>
                            <span>Stack Trace Details</span>
                        </div>
                        <i class="bi bi-chevron-down stack-trace-expand-icon @(IsStackTraceExpanded(entry.Id) ? "expanded" : "")"></i>
                    </div>

                    @if (IsStackTraceExpanded(entry.Id))
                    {
                        <div class="stack-trace-expand-content show">
                            <div class="stack-trace-details">
                                @if (!string.IsNullOrEmpty(entry.HttpMethod))
                                {
                                    <p>
                                        <span class="stack-trace-http-method">@entry.HttpMethod</span>
                                        <span>@entry.Url</span>
                                    </p>
                                }

                                @if (entry.StatusCode.HasValue)
                                {
                                    <p>
                                        HTTP Status Code: <span class="status-code">@entry.StatusCode</span>
                                    </p>
                                }

                                @foreach (var line in entry.StackTraceLines)
                                {
                                    <p class="code-line">@line</p>
                                }
                            </div>
                        </div>
                    }
                </div>
            </div>
        }
    }
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines **1209-1439**

### Key Sections

**1. Stack Trace Entry Cards** (lines 1215-1227)
```css
.stack-trace-entry {
    margin-bottom: 16px;
    border: 1px solid #dee2e6;
    border-radius: 8px;
    background-color: #fff;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    transition: all 0.2s ease;
}

.stack-trace-entry:hover {
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.08);
    border-color: #ced4da;
}
```

**2. Header Layout** (lines 1229-1267)
```css
.stack-trace-header {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    padding: 14px 18px;
    gap: 16px;
}

.stack-trace-timestamp {
    font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
    font-size: 12px;
    font-weight: 600;
    color: #6c757d;
}

.stack-trace-context {
    font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
    font-size: 12px;
    color: #667eea;
    background-color: rgba(102, 126, 234, 0.08);
    padding: 3px 10px;
    border-radius: 4px;
    font-weight: 500;
}
```

**3. Error Level Badges** (lines 1268-1292)
```css
.stack-trace-level-badge {
    display: inline-flex;
    align-items: center;
    padding: 3px 10px;
    border-radius: 4px;
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

.stack-trace-level-badge.error {
    background-color: #dc3545;
    color: #fff;
}

.stack-trace-level-badge.fatal {
    background-color: #721c24;
    color: #fff;
}

.stack-trace-level-badge.warn {
    background-color: #ffc107;
    color: #000;
}
```

**4. Jump to Log Button** (lines 1307-1333)
```css
.stack-trace-jump {
    flex-shrink: 0;
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 14px;
    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    color: #fff;
    border: none;
    border-radius: 6px;
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.2s ease;
    box-shadow: 0 2px 4px rgba(102, 126, 234, 0.15);
    text-decoration: none;
}

.stack-trace-jump:hover {
    background: linear-gradient(135deg, #5a6dd8 0%, #6a4190 100%);
    box-shadow: 0 3px 6px rgba(102, 126, 234, 0.25);
    transform: translateY(-1px);
}
```

**5. Expandable Section** (lines 1335-1384)
```css
.stack-trace-expand {
    border-top: 1px solid #f1f3f5;
    background-color: #fafbfc;
}

.stack-trace-expand-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 18px;
    cursor: pointer;
    transition: all 0.2s ease;
    user-select: none;
}

.stack-trace-expand-header:hover {
    background-color: #f1f3f5;
}

.stack-trace-expand-icon {
    font-size: 11px;
    transition: transform 0.2s ease;
    color: #6c757d;
}

.stack-trace-expand-icon.expanded {
    transform: rotate(180deg);
}

.stack-trace-expand-content {
    padding: 14px 18px;
    border-top: 1px solid #e9ecef;
    background-color: #fff;
    display: none;
}

.stack-trace-expand-content.show {
    display: block;
}
```

**6. Stack Trace Details** (lines 1386-1425)
```css
.stack-trace-details {
    font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
    font-size: 12px;
    line-height: 1.7;
    color: #495057;
}

.stack-trace-details .code-line {
    display: block;
    padding: 6px 12px;
    background-color: #f8f9fa;
    border-left: 3px solid #667eea;
    border-radius: 4px;
    margin: 8px 0;
    color: #212529;
}

.stack-trace-details .status-code {
    font-weight: 700;
    color: #dc3545;
}

.stack-trace-http-method {
    display: inline-block;
    padding: 2px 8px;
    background-color: #0d6efd;
    color: #fff;
    border-radius: 3px;
    font-size: 11px;
    font-weight: 700;
    margin-right: 8px;
}
```

**7. Empty State** (lines 1427-1439)
```css
.stack-trace-empty {
    text-align: center;
    padding: 40px 20px;
    color: #6c757d;
    font-size: 14px;
}

.stack-trace-empty i {
    font-size: 48px;
    color: #dee2e6;
    margin-bottom: 16px;
    display: block;
}
```

---

## C# Data Model

### Stack Trace Entry DTO
```csharp
public class StackTraceEntryDto
{
    public Guid Id { get; set; }
    public Guid? LogEntryId { get; set; } // Link to log entry for jump functionality
    public DateTimeOffset Timestamp { get; set; }
    public string Context { get; set; } = ""; // e.g., "test.spec.ts:42"
    public string Level { get; set; } = "ERROR"; // ERROR, FATAL, WARN
    public string ExceptionClass { get; set; } = ""; // e.g., "TimeoutError"
    public string Message { get; set; } = "";
    public string? HttpMethod { get; set; } // GET, POST, etc.
    public string? Url { get; set; }
    public int? StatusCode { get; set; } // HTTP status code
    public List<string> StackTraceLines { get; set; } = new();
}
```

### State Variables
```csharp
private List<StackTraceEntryDto> _stackTraceEntries = new();
private HashSet<Guid> _expandedStackTraces = new();
```

---

## C# Logic

### Load Stack Trace Entries
```csharp
private async Task LoadStackTraceEntriesAsync()
{
    if (_testItem == null) return;

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");
        var response = await http.GetAsync($"/api/test-items/{_testItem.Id}/stack-traces");

        if (response.IsSuccessStatusCode)
        {
            _stackTraceEntries = await response.Content
                .ReadFromJsonAsync<List<StackTraceEntryDto>>() ?? new();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load stack traces: {ex.Message}");
    }
}
```

### Expand/Collapse Stack Trace
```csharp
private void ToggleStackTraceExpand(Guid entryId)
{
    if (_expandedStackTraces.Contains(entryId))
        _expandedStackTraces.Remove(entryId);
    else
        _expandedStackTraces.Add(entryId);
}

private bool IsStackTraceExpanded(Guid entryId) => _expandedStackTraces.Contains(entryId);
```

### Jump to Log Entry
```csharp
private void JumpToLogEntry(Guid logEntryId)
{
    // Switch to Execution Logs tab
    _activeTab = "logs";

    // Optionally: Scroll to and highlight the log entry
    // This can be done with JavaScript interop
    JS.InvokeVoidAsync("scrollToLogEntry", logEntryId.ToString());

    StateHasChanged();
}
```

### JavaScript Helper (Optional)
```javascript
// wwwroot/js/test-item-details.js

window.scrollToLogEntry = function(logEntryId) {
    const logEntry = document.querySelector(`[data-log-id="${logEntryId}"]`);
    if (logEntry) {
        logEntry.scrollIntoView({ behavior: 'smooth', block: 'center' });
        logEntry.classList.add('highlight');
        setTimeout(() => logEntry.classList.remove('highlight'), 2000);
    }
};
```

**CSS for highlight effect:**
```css
.log-entry.highlight {
    background-color: rgba(102, 126, 234, 0.15);
    transition: background-color 0.3s ease;
}
```

---

## Backend API

### Endpoint: Get Stack Trace Entries
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs

app.MapGet("/api/test-items/{itemId:guid}/stack-traces", async (
    Guid itemId,
    [FromServices] IResultsStore store) =>
{
    var stackTraces = await store.GetStackTraceEntriesForTestItemAsync(itemId);
    return Results.Ok(stackTraces);
})
.WithName("GetStackTraceEntries")
.WithTags("TestItems");
```

### IResultsStore Method
```csharp
Task<List<StackTraceEntryDto>> GetStackTraceEntriesForTestItemAsync(Guid itemId);
```

### PostgreSQL Implementation Example
```csharp
public async Task<List<StackTraceEntryDto>> GetStackTraceEntriesForTestItemAsync(Guid itemId)
{
    await using var conn = new NpgsqlConnection(_connString);
    await conn.OpenAsync();

    // Query log_items or dedicated stack_traces table
    var sql = @"
        SELECT
            id,
            timestamp,
            level,
            message,
            error_stack,
            http_method,
            url,
            status_code
        FROM log_items
        WHERE test_item_id = @itemId
          AND level IN ('ERROR', 'FATAL', 'WARN')
          AND error_stack IS NOT NULL
        ORDER BY timestamp ASC";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("itemId", itemId);

    var entries = new List<StackTraceEntryDto>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var errorStack = reader.IsDBNull(4) ? "" : reader.GetString(4);
        var stackLines = errorStack
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        // Parse exception class from message
        var message = reader.GetString(3);
        var exceptionClass = "Exception";
        if (message.Contains(':'))
        {
            exceptionClass = message.Split(':')[0].Trim();
            message = message.Substring(message.IndexOf(':') + 1).Trim();
        }

        entries.Add(new StackTraceEntryDto
        {
            Id = reader.GetGuid(0),
            LogEntryId = reader.GetGuid(0), // Same ID for linking
            Timestamp = reader.GetDateTime(1),
            Level = reader.GetString(2),
            ExceptionClass = exceptionClass,
            Message = message,
            HttpMethod = reader.IsDBNull(5) ? null : reader.GetString(5),
            Url = reader.IsDBNull(6) ? null : reader.GetString(6),
            StatusCode = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            StackTraceLines = stackLines
        });
    }

    return entries;
}
```

---

## Testing Checklist

### Visual
- [ ] Error cards display with proper spacing and shadows
- [ ] Hover effect works on error cards
- [ ] Timestamp in monospace font
- [ ] Context badge styled with purple background
- [ ] Level badges (ERROR, FATAL, WARN) with correct colors
- [ ] Jump to log button gradient matches app theme
- [ ] Expand icon rotates 180° when expanded
- [ ] Stack trace details show in monospace font
- [ ] HTTP method badges display correctly
- [ ] Code lines have left border and background
- [ ] Status codes highlighted in red
- [ ] Empty state icon and message centered

### Functional
- [ ] Load stack trace entries from API
- [ ] Expand/collapse individual stack traces
- [ ] Jump to log switches to Logs tab
- [ ] Jump to log scrolls to correct entry (if implemented)
- [ ] Jump to log highlights entry temporarily
- [ ] Empty state shown when no errors
- [ ] HTTP method and URL display when available
- [ ] Status code displays when available
- [ ] Stack trace lines parsed correctly

### Edge Cases
- [ ] No errors (empty state)
- [ ] Single error entry
- [ ] Multiple errors
- [ ] Very long error messages (word-break works)
- [ ] Very long stack traces (scrollable)
- [ ] Missing HTTP method/URL/status code
- [ ] Missing LogEntryId (button hidden)
- [ ] Network errors handled gracefully

---

## Mobile Responsive

```css
@media (max-width: 768px) {
    .stack-trace-view {
        padding: 12px;
    }

    .stack-trace-header {
        flex-direction: column;
        align-items: flex-start;
        gap: 12px;
    }

    .stack-trace-jump {
        width: 100%;
        justify-content: center;
    }

    .stack-trace-time-context {
        flex-wrap: wrap;
    }

    .stack-trace-details {
        font-size: 11px;
    }
}
```

---

## Next Phase
**Phase 7:** Item Details Tab (metadata, tags, attributes, code reference, description)
