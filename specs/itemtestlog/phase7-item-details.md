# Phase 7: Item Details Tab

## Overview
Implement the Item Details tab showing comprehensive test item metadata including tags, attributes, code reference, test case ID, description, and parameters. This tab provides all static information about the test item.

## Goals
- ✅ Header section with item name, status badge, and duration
- ✅ Grid layout with label-value rows
- ✅ Tags display with icon badges
- ✅ Attributes display with icon badges
- ✅ Code reference with copy button
- ✅ Test case ID display
- ✅ Description box with gradient background
- ✅ Parameters display with badges
- ✅ Empty state styling for missing fields
- ✅ Hover effects on rows
- ✅ Copy to clipboard functionality

---

## Component Structure

### HTML Structure
```razor
<!-- Item Details View -->
<div class="item-details-view" id="details-view">
    <div class="item-details-container">
        <!-- Header Section -->
        <div class="item-details-header">
            <div class="item-details-title-section">
                <h2 class="item-details-name">@_testItem?.Name</h2>
                <span class="item-status-badge @(_testItem?.Status?.ToLower())">@_testItem?.Status?.ToUpper()</span>
            </div>
            @if (_testItem?.Duration != null)
            {
                <div class="item-details-duration">
                    <i class="bi bi-clock"></i>
                    <span>@FormatDuration(_testItem.Duration.Value)</span>
                </div>
            }
        </div>

        <!-- Content Section -->
        <div class="item-details-content">
            <!-- Test Title -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.TestTitle))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Test Title</div>
                    <div class="item-details-value">@_testItem.TestTitle</div>
                </div>
            }

            <!-- Type -->
            <div class="item-details-row">
                <div class="item-details-label">Type</div>
                <div class="item-details-value">@(_testItem?.ItemType ?? "Test")</div>
            </div>

            <!-- Start Time -->
            <div class="item-details-row">
                <div class="item-details-label">Start Time</div>
                <div class="item-details-value">@_testItem?.StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff")</div>
            </div>

            <!-- Finish Time -->
            @if (_testItem?.FinishTime != null)
            {
                <div class="item-details-row">
                    <div class="item-details-label">Finish Time</div>
                    <div class="item-details-value">@_testItem.FinishTime?.ToString("yyyy-MM-dd HH:mm:ss.fff")</div>
                </div>
            }

            <!-- Tags -->
            @if (_testItem?.Tags?.Any() == true)
            {
                <div class="item-details-row">
                    <div class="item-details-label">Tags</div>
                    <div class="item-details-value">
                        @foreach (var tag in _testItem.Tags)
                        {
                            <span class="item-tag">
                                <i class="bi bi-tag"></i>
                                @tag
                            </span>
                        }
                    </div>
                </div>
            }

            <!-- Attributes -->
            @if (_testItem?.Attributes?.Any() == true)
            {
                <div class="item-details-row">
                    <div class="item-details-label">Attributes</div>
                    <div class="item-details-value">
                        @foreach (var attr in _testItem.Attributes)
                        {
                            <span class="item-tag">
                                <i class="bi bi-info-circle"></i>
                                @attr
                            </span>
                        }
                    </div>
                </div>
            }

            <!-- Code Reference -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.CodeRef))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Code Reference</div>
                    <div class="item-details-value">
                        <code>@_testItem.CodeRef</code>
                        <button class="item-copy-button"
                                @onclick="() => CopyToClipboard(_testItem.CodeRef)"
                                title="Copy to clipboard">
                            <i class="bi bi-clipboard"></i>
                            Copy
                        </button>
                    </div>
                </div>
            }

            <!-- Test File -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.TestFile))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Test File</div>
                    <div class="item-details-value">
                        <code>@_testItem.TestFile</code>
                        @if (_testItem.LineNumber.HasValue)
                        {
                            <text>:@_testItem.LineNumber</text>
                        }
                    </div>
                </div>
            }

            <!-- Test Case ID (UniqueId) -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.UniqueId))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Test Case ID</div>
                    <div class="item-details-value">
                        <code>@_testItem.UniqueId</code>
                        <button class="item-copy-button"
                                @onclick="() => CopyToClipboard(_testItem.UniqueId)"
                                title="Copy to clipboard">
                            <i class="bi bi-clipboard"></i>
                            Copy
                        </button>
                    </div>
                </div>
            }

            <!-- Parameters -->
            @if (_testItem?.Parameters != null && _testItem.Parameters.Count > 0)
            {
                <div class="item-details-row">
                    <div class="item-details-label">Parameters</div>
                    <div class="item-details-value">
                        @foreach (var param in _testItem.Parameters)
                        {
                            <span class="item-parameters-badge">
                                <strong>@param.Key:</strong> @param.Value
                            </span>
                        }
                    </div>
                </div>
            }

            <!-- Description -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.Description))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Description</div>
                    <div class="item-details-value">
                        <div class="item-description-box">
                            @((MarkupString)_testItem.Description)
                        </div>
                    </div>
                </div>
            }

            <!-- Browser Info -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.BrowserType))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Browser</div>
                    <div class="item-details-value">@_testItem.BrowserType</div>
                </div>
            }

            <!-- Worker Node -->
            @if (!string.IsNullOrWhiteSpace(_testItem?.WorkerNodeId))
            {
                <div class="item-details-row">
                    <div class="item-details-label">Worker Node</div>
                    <div class="item-details-value">@_testItem.WorkerNodeId</div>
                </div>
            }

            <!-- Retry Attempt -->
            @if (_testItem?.RetryAttempt.HasValue == true && _testItem.RetryAttempt > 0)
            {
                <div class="item-details-row">
                    <div class="item-details-label">Retry Attempt</div>
                    <div class="item-details-value">@_testItem.RetryAttempt</div>
                </div>
            }
        </div>
    </div>
</div>
```

---

## CSS Styling

### Source
Copy from `dashboard/wwwroot/prototypes/test-logs-prototype.html` lines **1722-1913**

### Key Sections

**1. Container and Header** (lines 1723-1755)
```css
.item-details-view {
    padding: 0;
    background-color: #fff;
}

.item-details-container {
    background: #fff;
    max-width: 1400px;
    margin: 0 auto;
}

.item-details-header {
    padding: 32px 40px;
    background: linear-gradient(135deg, #f8f9fa 0%, #e9ecef 100%);
    border-bottom: 1px solid #dee2e6;
    display: flex;
    align-items: center;
    justify-content: space-between;
}

.item-details-name {
    font-size: 20px;
    font-weight: 600;
    color: #212529;
    letter-spacing: -0.01em;
}
```

**2. Status Badge** (lines 1756-1776)
```css
.item-status-badge {
    display: inline-flex;
    align-items: center;
    padding: 6px 14px;
    border-radius: 6px;
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.item-status-badge.failed {
    background: linear-gradient(135deg, #dc3545 0%, #c82333 100%);
    color: #fff;
}

.item-status-badge.passed {
    background: linear-gradient(135deg, #28a745 0%, #218838 100%);
    color: #fff;
}
```

**3. Grid Layout Rows** (lines 1795-1826)
```css
.item-details-row {
    display: grid;
    grid-template-columns: 200px 1fr;
    gap: 24px;
    padding: 20px 40px;
    border-bottom: 1px solid #f1f3f5;
    transition: background-color 0.2s ease;
}

.item-details-row:hover {
    background-color: #f8f9fa;
}

.item-details-row:last-child {
    border-bottom: none;
}

.item-details-label {
    font-size: 13px;
    font-weight: 600;
    color: #495057;
    text-transform: uppercase;
    letter-spacing: 0.3px;
}

.item-details-value {
    font-size: 14px;
    color: #212529;
    word-break: break-word;
    line-height: 1.6;
}
```

**4. Code Styling** (lines 1827-1840)
```css
.item-details-value code {
    font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
    font-size: 13px;
    background-color: #f8f9fa;
    padding: 4px 8px;
    border-radius: 4px;
    color: #495057;
    border: 1px solid #e9ecef;
}

.item-details-value.empty {
    color: #adb5bd;
    font-style: italic;
}
```

**5. Tag Badges** (lines 1842-1867)
```css
.item-tag {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    background: linear-gradient(135deg, #f8f9fa 0%, #e9ecef 100%);
    border: 1px solid #dee2e6;
    border-radius: 6px;
    font-size: 12px;
    font-weight: 500;
    color: #495057;
    margin-right: 8px;
    margin-bottom: 8px;
    transition: all 0.2s ease;
}

.item-tag:hover {
    background: linear-gradient(135deg, #e9ecef 0%, #dee2e6 100%);
    transform: translateY(-1px);
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.08);
}

.item-tag i {
    font-size: 11px;
    opacity: 0.7;
}
```

**6. Description Box** (lines 1869-1878)
```css
.item-description-box {
    padding: 20px;
    background: linear-gradient(135deg, #f8f9fa 0%, #ffffff 100%);
    border-left: 4px solid #667eea;
    border-radius: 6px;
    font-size: 14px;
    line-height: 1.7;
    color: #495057;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}
```

**7. Copy Button** (lines 1880-1901)
```css
.item-copy-button {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 4px 10px;
    background-color: #fff;
    border: 1px solid #dee2e6;
    border-radius: 4px;
    font-size: 12px;
    color: #495057;
    cursor: pointer;
    transition: all 0.2s ease;
}

.item-copy-button:hover {
    background-color: #f8f9fa;
    border-color: #adb5bd;
}

.item-copy-button i {
    font-size: 13px;
}
```

**8. Parameter Badges** (lines 1903-1912)
```css
.item-parameters-badge {
    display: inline-block;
    padding: 3px 8px;
    background-color: #fff;
    border: 1px solid #dee2e6;
    border-radius: 3px;
    font-size: 12px;
    font-weight: 500;
    color: #495057;
}
```

---

## C# Logic

### Copy to Clipboard
```csharp
private async Task CopyToClipboard(string text)
{
    try
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);

        // Optional: Show toast notification
        // ToastService.ShowSuccess("Copied to clipboard!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to copy to clipboard: {ex.Message}");
    }
}
```

### Duration Formatting
```csharp
private string FormatDuration(TimeSpan duration)
{
    if (duration.TotalSeconds < 1)
        return $"{duration.Milliseconds}ms";
    else if (duration.TotalMinutes < 1)
        return $"{duration.TotalSeconds:F1}s";
    else if (duration.TotalHours < 1)
        return $"{duration.TotalMinutes:F1}m";
    else
        return $"{duration.TotalHours:F1}h";
}
```

### Parameter Display Helper
```csharp
// Assuming Parameters is Dictionary<string, object> or JsonElement
private IEnumerable<KeyValuePair<string, string>> GetDisplayParameters()
{
    if (_testItem?.Parameters == null) yield break;

    // If Parameters is Dictionary<string, object>
    foreach (var param in _testItem.Parameters)
    {
        yield return new KeyValuePair<string, string>(
            param.Key,
            param.Value?.ToString() ?? "null"
        );
    }
}
```

---

## Data Requirements

### Test Item DTO Fields Used
All fields already exist in `TestItemDto` from Phase 4:
- `Name` - Test item name
- `Status` - Test status (Passed, Failed, etc.)
- `Duration` - Test duration
- `TestTitle` - Title of the test
- `ItemType` - Type (Test, Step, Suite, etc.)
- `StartTime` - Start timestamp
- `FinishTime` - Finish timestamp
- `Tags` - String array of tags
- `Attributes` - String array of attributes
- `CodeRef` - Code reference (e.g., "tests/login.spec.ts:42")
- `TestFile` - Test file path
- `LineNumber` - Line number in test file
- `UniqueId` - Test case unique identifier
- `Parameters` - JSONB parameters (Dictionary<string, object>)
- `Description` - Test description (can contain HTML)
- `BrowserType` - Browser used (chromium, firefox, webkit)
- `WorkerNodeId` - Worker that executed the test
- `RetryAttempt` - Retry attempt number (0 for first run)

---

## Testing Checklist

### Visual
- [ ] Header gradient background displays correctly
- [ ] Item name and status badge aligned
- [ ] Duration displayed with clock icon
- [ ] Grid layout with 200px label column
- [ ] Row hover effect (background changes)
- [ ] Tags display with icons and gradient
- [ ] Attributes display with icons and gradient
- [ ] Code blocks styled with monospace font
- [ ] Description box with purple left border
- [ ] Copy button hover effect works
- [ ] Parameter badges styled correctly
- [ ] Empty fields hidden (conditional rendering works)

### Functional
- [ ] All test item fields display correctly
- [ ] Tags render as separate badges
- [ ] Attributes render as separate badges
- [ ] Code reference copy button works
- [ ] Unique ID copy button works
- [ ] Copy to clipboard success (browser console)
- [ ] Duration formatted correctly (ms/s/m/h)
- [ ] Parameters parse and display correctly
- [ ] Description HTML renders safely (MarkupString)

### Edge Cases
- [ ] No tags (row hidden)
- [ ] No attributes (row hidden)
- [ ] No code reference (row hidden)
- [ ] No description (row hidden)
- [ ] Very long test name (wraps correctly)
- [ ] Very long code reference (breaks correctly)
- [ ] HTML in description (renders safely)
- [ ] Many tags (wrap to multiple lines)
- [ ] Many parameters (wrap to multiple lines)

---

## Mobile Responsive

```css
@media (max-width: 768px) {
    .item-details-header {
        flex-direction: column;
        align-items: flex-start;
        gap: 16px;
        padding: 20px;
    }

    .item-details-row {
        grid-template-columns: 1fr;
        gap: 8px;
        padding: 16px 20px;
    }

    .item-details-label {
        margin-bottom: 4px;
    }

    .item-details-name {
        font-size: 18px;
    }

    .item-tag {
        font-size: 11px;
        padding: 5px 10px;
    }

    .item-description-box {
        padding: 16px;
        font-size: 13px;
    }
}
```

---

## Security Considerations

**Description Field:**
- Use `@((MarkupString)_testItem.Description)` to render HTML
- Ensure backend sanitizes HTML input to prevent XSS attacks
- Consider using a markdown parser instead of raw HTML

**Alternative (Markdown):**
```razor
@if (!string.IsNullOrWhiteSpace(_testItem?.Description))
{
    <div class="item-details-row">
        <div class="item-details-label">Description</div>
        <div class="item-details-value">
            <div class="item-description-box">
                @Markdig.Markdown.ToHtml(_testItem.Description)
            </div>
        </div>
    </div>
}
```

---

## Next Phase
**Phase 8:** Artifacts Tab (file preview, thumbnails, download/open actions)
