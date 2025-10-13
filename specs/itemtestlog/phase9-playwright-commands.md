# Phase 9: Playwright Server Commands Integration

## Overview
Integrate the API Commands tab content from ResultsRun.razor to display Playwright protocol commands, browser communication logs, and system events. This tab provides debugging capabilities by showing all commands sent/received during test execution.

## Goals
- ✅ Stats header with duration, failed count, commands count, kinds count
- ✅ Filter inputs for Kind and Direction
- ✅ Quick filter buttons with counts
- ✅ Commands list with pagination
- ✅ Command details display (kind, message, test ID, properties)
- ✅ Playwright protocol rendering
- ✅ Copy command functionality
- ✅ Elegant bordered container design
- ✅ Responsive mobile layout

---

## Component Structure

### Source
**Copy from:** `dashboard/Pages/ResultsRun.razor` lines **324-600**

This content should be adapted for TestItemDetails.razor by:
1. Changing variable names (`_run` → `_testItem`, `MainTab` → `_activeTab`)
2. Updating API endpoint from `/api/runs/{runId}/commands` to `/api/test-items/{itemId}/commands`
3. Keeping all styling and component logic intact

---

## HTML Structure (Adapted)
```razor
@* API Commands Tab Content *@
@if (_activeTab == "commands")
{
    <div class="test-cases-section">
        @* Enhanced Stats Header - Compact & Elegant *@
        <div class="d-flex flex-wrap align-items-center gap-2 mb-3 p-2 rounded-3" style="background: #f8f9fa; border: 1px solid #e9ecef;">
            <div class="d-flex align-items-center gap-1 px-2 py-1 rounded-pill" style="background: white; border: 1px solid #e3e8ef;">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="#667eea" viewBox="0 0 16 16">
                    <path d="M8.515 1.019A7 7 0 0 0 8 1V0a8 8 0 0 1 .589.022l-.074.997zm2.004.45a7.003 7.003 0 0 0-.985-.299l.219-.976c.383.086.76.2 1.126.342l-.36.933zm1.37.71a7.01 7.01 0 0 0-.439-.27l.493-.87a8.025 8.025 0 0 1 .979.654l-.615.789a6.996 6.996 0 0 0-.418-.302zm1.834 1.79a6.99 6.99 0 0 0-.653-.796l.724-.69c.27.285.52.59.747.91l-.818.576zm.744 1.352a7.08 7.08 0 0 0-.214-.468l.893-.45a7.976 7.976 0 0 1 .45 1.088l-.95.313a7.023 7.023 0 0 0-.179-.483zm.53 2.507a6.991 6.991 0 0 0-.1-1.025l.985-.17c.067.386.106.778.116 1.17l-1 .025zm-.131 1.538c.033-.17.06-.339.081-.51l.993.123a7.957 7.957 0 0 1-.23 1.155l-.964-.267c.046-.165.086-.332.12-.501zm-.952 2.379c.184-.29.346-.594.486-.908l.914.405c-.16.36-.345.706-.555 1.038l-.845-.535zm-.964 1.205c.122-.122.239-.248.35-.378l.758.653a8.073 8.073 0 0 1-.401.432l-.707-.707z"/>
                    <path d="M8 1a7 7 0 1 0 4.95 11.95l.707.707A8.001 8.001 0 1 1 8 0v1z"/>
                    <path d="M7.5 3a.5.5 0 0 1 .5.5v5.21l3.248 1.856a.5.5 0 0 1-.496.868l-3.5-2A.5.5 0 0 1 7 9V3.5a.5.5 0 0 1 .5-.5z"/>
                </svg>
                <span class="small" style="color: #667eea; font-weight: 500;">Duration</span>
                <span class="badge rounded-pill" style="background: #667eea; color: white; font-size: 0.7rem; padding: 0.25rem 0.5rem;">@DurationText()</span>
            </div>
            @if ((_testItem?.FailedTests ?? 0) > 0)
            {
                <div class="d-flex align-items-center gap-1 px-2 py-1 rounded-pill" style="background: white; border: 1px solid #f8d7da;">
                    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="#dc3545" viewBox="0 0 16 16">
                        <path d="M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z"/>
                        <path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
                    </svg>
                    <span class="small" style="color: #dc3545; font-weight: 500;">Failed</span>
                    <span class="badge rounded-pill bg-danger" style="font-size: 0.7rem; padding: 0.25rem 0.5rem;">@_testItem.FailedTests</span>
                </div>
            }
            <div class="d-flex align-items-center gap-1 px-2 py-1 rounded-pill" style="background: white; border: 1px solid #e3e8ef;">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="#6c757d" viewBox="0 0 16 16">
                    <path d="M6 10.5a.5.5 0 0 1 .5-.5h3a.5.5 0 0 1 0 1h-3a.5.5 0 0 1-.5-.5zm-2-3a.5.5 0 0 1 .5-.5h7a.5.5 0 0 1 0 1h-7a.5.5 0 0 1-.5-.5zm-2-3a.5.5 0 0 1 .5-.5h11a.5.5 0 0 1 0 1h-11a.5.5 0 0 1-.5-.5z"/>
                </svg>
                <span class="small text-secondary" style="font-weight: 500;">Commands</span>
                <span class="badge rounded-pill" style="background: #6c757d; color: white; font-size: 0.7rem; padding: 0.25rem 0.5rem;">@CommandCount</span>
            </div>
            <div class="d-flex align-items-center gap-1 px-2 py-1 rounded-pill" style="background: white; border: 1px solid #e3e8ef;">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="#0d6efd" viewBox="0 0 16 16">
                    <path d="M1 2.5A1.5 1.5 0 0 1 2.5 1h3A1.5 1.5 0 0 1 7 2.5v3A1.5 1.5 0 0 1 5.5 7h-3A1.5 1.5 0 0 1 1 5.5v-3zM2.5 2a.5.5 0 0 0-.5.5v3a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-3a.5.5 0 0 0-.5-.5h-3zm6.5.5A1.5 1.5 0 0 1 10.5 1h3A1.5 1.5 0 0 1 15 2.5v3A1.5 1.5 0 0 1 13.5 7h-3A1.5 1.5 0 0 1 9 5.5v-3zm1.5-.5a.5.5 0 0 0-.5.5v3a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-3a.5.5 0 0 0-.5-.5h-3zM1 10.5A1.5 1.5 0 0 1 2.5 9h3A1.5 1.5 0 0 1 7 10.5v3A1.5 1.5 0 0 1 5.5 15h-3A1.5 1.5 0 0 1 1 13.5v-3zm1.5-.5a.5.5 0 0 0-.5.5v3a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-3a.5.5 0 0 0-.5-.5h-3zm6.5.5A1.5 1.5 0 0 1 10.5 9h3a1.5 1.5 0 0 1 1.5 1.5v3a1.5 1.5 0 0 1-1.5 1.5h-3A1.5 1.5 0 0 1 9 13.5v-3zm1.5-.5a.5.5 0 0 0-.5.5v3a.5.5 0 0 0 .5.5h3a.5.5 0 0 0 .5-.5v-3a.5.5 0 0 0-.5-.5h-3z"/>
                </svg>
                <span class="small text-primary" style="font-weight: 500;">Kinds</span>
                <span class="badge rounded-pill" style="background: #0d6efd; color: white; font-size: 0.7rem; padding: 0.25rem 0.5rem;">@KindCounts.Count</span>
            </div>
        </div>

        @* Filters and Commands Section with elegant borders *@
        <div class="rounded-3 mb-3" style="border: 1px solid #e3e8ef; box-shadow: 0 1px 3px rgba(0,0,0,0.05);">
            <div class="p-3" style="background: linear-gradient(to bottom, #fafbfc 0%, #ffffff 100%); border-bottom: 1px solid #e3e8ef; border-radius: 0.5rem 0.5rem 0 0;">
            <div class="row g-2 mb-2">
                <div class="col-sm-3">
                    <input class="form-control form-control-sm" placeholder="Kind (e.g. Borrow)"
                           value="@cmdKindFilter"
                           @oninput="OnKindInput"/>
                </div>
                <div class="col-sm-3">
                    <input class="form-control form-control-sm" placeholder="Direction (c2s/s2c/runner)"
                           value="@cmdDirectionFilter"
                           @oninput="OnDirectionInput"/>
                </div>
            </div>
            @if (KindCounts.Count > 0)
            {
                <div class="mb-2">
                    <span class="small text-muted me-2">Quick filters:</span>
                    @foreach (var kv in KindCounts.OrderBy(kv => kv.Key))
                    {
                        <button
                            class="btn btn-sm @(string.Equals(cmdKindFilter, kv.Key, StringComparison.OrdinalIgnoreCase) ? "btn-primary" : "btn-outline-primary") me-1 mb-1"
                            @onclick="() => SetKindFilter(kv.Key)">
                            @kv.Key <span class="badge bg-light text-dark ms-1">@kv.Value</span>
                        </button>
                    }
                    @if (!string.IsNullOrWhiteSpace(cmdKindFilter))
                    {
                        <button class="btn btn-sm btn-outline-secondary mb-1" @onclick="ClearKindFilter">Clear</button>
                    }
                </div>
            }
            </div>

            @if (_commands == null)
            {
                <div class="text-muted p-3">Loading commands...</div>
            }
            else if (FilteredCommands.Count == 0)
            {
                <div class="text-muted p-3">No commands.</div>
            }
            else
            {
                <div class="d-flex justify-content-between align-items-center mb-0 p-3 border-bottom" style="background: #fafbfc;">
                    <div class="d-flex align-items-center gap-2">
                        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="#667eea" viewBox="0 0 16 16">
                            <path d="M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z"/>
                            <path d="m8.93 6.588-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533L8.93 6.588zM9 4.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z"/>
                        </svg>
                        <span class="small text-secondary" style="font-weight: 500;">Showing <strong>@PageStart-@PageEnd</strong> of <strong>@CmdTotal</strong></span>
                    </div>
                    <nav aria-label="Commands pagination">
                        <ul class="pagination pagination-sm mb-0">
                            <li class="page-item @(CmdPage <= 1 ? "disabled" : "")">
                                <button class="page-link" @onclick="PrevCmdPage" disabled="@(CmdPage <= 1)" style="border-radius: 6px 0 0 6px;">
                                    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                                        <path fill-rule="evenodd" d="M11.354 1.646a.5.5 0 0 1 0 .708L5.707 8l5.647 5.646a.5.5 0 0 1-.708.708l-6-6a.5.5 0 0 1 0-.708l6-6a.5.5 0 0 1 .708 0z"/>
                                    </svg>
                                    <span class="ms-1">Previous</span>
                                </button>
                            </li>
                            <li class="page-item disabled">
                                <span class="page-link bg-white border-start-0 border-end-0">
                                    Page <strong>@CmdPage</strong> of <strong>@CmdTotalPages</strong>
                                </span>
                            </li>
                            <li class="page-item @(CmdPage >= CmdTotalPages ? "disabled" : "")">
                                <button class="page-link" @onclick="NextCmdPage" disabled="@(CmdPage >= CmdTotalPages)" style="border-radius: 0 6px 6px 0;">
                                    <span class="me-1">Next</span>
                                    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                                        <path fill-rule="evenodd" d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
                                    </svg>
                                </button>
                            </li>
                        </ul>
                    </nav>
                </div>

                @* Commands List *@
                <div class="p-3">
                    @foreach (var ev in PagedCommands)
                    {
                        <div class="mb-3 pb-3 small" style="border-bottom: 1px solid #e9ecef;">
                            <div class="d-flex justify-content-between align-items-start">
                                <div>
                                    <span class="badge bg-secondary me-2" style="font-size: 0.7rem;">@ev.Kind</span>
                                    <strong style="font-size: 0.875rem;">@ev.Message</strong>
                                    @if (!string.IsNullOrWhiteSpace(ev.TestId))
                                    {
                                        <span class="badge bg-light text-dark ms-2" style="font-size: 0.7rem;">test=@ev.TestId</span>
                                    }
                                    @if (ev.Props is not null && ev.Props.Count > 0)
                                    {
                                        <div class="mt-1">
                                            @foreach (var p in ev.Props)
                                            {
                                                <span class="badge bg-light text-dark me-1" style="font-size: 0.7rem;">@p.Key=@p.Value</span>
                                            }
                                        </div>
                                    }
                                </div>
                                <div class="text-end">
                                    <div class="text-muted small">@ev.TimestampUtc.ToLocalTime().ToString("HH:mm:ss")</div>
                                    <button class="btn btn-sm btn-outline-secondary mt-1" style="font-size: 0.75rem; padding: 0.25rem 0.5rem;"
                                            @onclick="async () => await Copy(ev)">Copy
                                    </button>
                                </div>
                            </div>

                            @if (string.Equals(ev.Kind, "PwProtocol", StringComparison.OrdinalIgnoreCase))
                            {
                                <div class="mt-2">
                                    @RenderPlaywrightProtocol(ev)
                                </div>
                            }
                        </div>
                    }
                </div>
                @if (CmdTotalPages > 1)
                {
                    <div class="d-flex justify-content-end align-items-center p-3 border-top" style="background: #fafbfc; border-radius: 0 0 0.5rem 0.5rem;">
                        <nav aria-label="Commands pagination bottom">
                            <ul class="pagination pagination-sm mb-0">
                                <li class="page-item @(CmdPage <= 1 ? "disabled" : "")">
                                    <button class="page-link" @onclick="PrevCmdPage" disabled="@(CmdPage <= 1)" style="border-radius: 6px 0 0 6px;">
                                        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                                            <path fill-rule="evenodd" d="M11.354 1.646a.5.5 0 0 1 0 .708L5.707 8l5.647 5.646a.5.5 0 0 1-.708.708l-6-6a.5.5 0 0 1 0-.708l6-6a.5.5 0 0 1 .708 0z"/>
                                        </svg>
                                        <span class="ms-1">Previous</span>
                                    </button>
                                </li>
                                <li class="page-item disabled">
                                    <span class="page-link bg-white border-start-0 border-end-0">
                                        Page <strong>@CmdPage</strong> of <strong>@CmdTotalPages</strong>
                                    </span>
                                </li>
                                <li class="page-item @(CmdPage >= CmdTotalPages ? "disabled" : "")">
                                    <button class="page-link" @onclick="NextCmdPage" disabled="@(CmdPage >= CmdTotalPages)" style="border-radius: 0 6px 6px 0;">
                                        <span class="me-1">Next</span>
                                        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                                            <path fill-rule="evenodd" d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
                                        </svg>
                                    </button>
                                </li>
                            </ul>
                        </nav>
                    </div>
                }
            }
        </div>
    </div>
}
```

---

## C# Data Model

### Command Event DTO
```csharp
public class CommandEventDto
{
    public Guid Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string Kind { get; set; } = ""; // e.g., "Borrow", "Return", "PwProtocol"
    public string Message { get; set; } = "";
    public string? TestId { get; set; }
    public Dictionary<string, string>? Props { get; set; }
    public string? Direction { get; set; } // "c2s" (client-to-server), "s2c" (server-to-client), "runner"
}
```

### State Variables (Copy from ResultsRun.razor)
```csharp
private List<CommandEventDto>? _commands;
private string cmdKindFilter = "";
private string cmdDirectionFilter = "";
private int CmdPage = 1;
private int CmdPageSize = 20;

private Dictionary<string, int> KindCounts => _commands?
    .GroupBy(c => c.Kind)
    .ToDictionary(g => g.Key, g => g.Count()) ?? new();

private List<CommandEventDto> FilteredCommands => _commands?
    .Where(c => string.IsNullOrWhiteSpace(cmdKindFilter) || string.Equals(c.Kind, cmdKindFilter, StringComparison.OrdinalIgnoreCase))
    .Where(c => string.IsNullOrWhiteSpace(cmdDirectionFilter) || (c.Direction?.Contains(cmdDirectionFilter, StringComparison.OrdinalIgnoreCase) ?? false))
    .ToList() ?? new();

private int CommandCount => FilteredCommands.Count;
private int CmdTotal => FilteredCommands.Count;
private int CmdTotalPages => (int)Math.Ceiling(CmdTotal / (double)CmdPageSize);
private int PageStart => (CmdPage - 1) * CmdPageSize + 1;
private int PageEnd => Math.Min(CmdPage * CmdPageSize, CmdTotal);

private List<CommandEventDto> PagedCommands => FilteredCommands
    .Skip((CmdPage - 1) * CmdPageSize)
    .Take(CmdPageSize)
    .ToList();
```

---

## C# Logic (Copy from ResultsRun.razor)

### Load Commands
```csharp
private async Task LoadCommandsAsync()
{
    if (_testItem == null) return;

    try
    {
        var http = HttpFactory.CreateClient("WebAPI");
        var response = await http.GetAsync($"/api/test-items/{_testItem.Id}/commands");

        if (response.IsSuccessStatusCode)
        {
            _commands = await response.Content
                .ReadFromJsonAsync<List<CommandEventDto>>() ?? new();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load commands: {ex.Message}");
    }
}
```

### Filter Methods
```csharp
private void OnKindInput(ChangeEventArgs e)
{
    cmdKindFilter = e.Value?.ToString() ?? "";
    CmdPage = 1; // Reset to first page
}

private void OnDirectionInput(ChangeEventArgs e)
{
    cmdDirectionFilter = e.Value?.ToString() ?? "";
    CmdPage = 1; // Reset to first page
}

private void SetKindFilter(string kind)
{
    cmdKindFilter = kind;
    CmdPage = 1; // Reset to first page
}

private void ClearKindFilter()
{
    cmdKindFilter = "";
    CmdPage = 1; // Reset to first page
}
```

### Pagination
```csharp
private void PrevCmdPage()
{
    if (CmdPage > 1)
        CmdPage--;
}

private void NextCmdPage()
{
    if (CmdPage < CmdTotalPages)
        CmdPage++;
}
```

### Copy Command
```csharp
private async Task Copy(CommandEventDto ev)
{
    var json = System.Text.Json.JsonSerializer.Serialize(ev, new JsonSerializerOptions { WriteIndented = true });
    await JS.InvokeVoidAsync("navigator.clipboard.writeText", json);
}
```

### Duration Text Helper
```csharp
private string DurationText()
{
    if (_testItem?.FinishTime == null || _testItem.StartTime == null)
        return "N/A";

    var duration = _testItem.FinishTime.Value - _testItem.StartTime.Value;
    return FormatDuration(duration);
}
```

### Render Playwright Protocol (Optional Enhancement)
```csharp
private RenderFragment RenderPlaywrightProtocol(CommandEventDto ev)
{
    return builder =>
    {
        // If Props contains JSON protocol data, render it with syntax highlighting
        if (ev.Props?.ContainsKey("data") == true)
        {
            builder.OpenElement(0, "pre");
            builder.AddAttribute(1, "style", "background: #f8f9fa; padding: 8px; border-radius: 4px; font-size: 0.75rem; overflow-x: auto;");
            builder.AddContent(2, ev.Props["data"]);
            builder.CloseElement();
        }
    };
}
```

---

## Backend API

### Endpoint: Get Test Item Commands
```csharp
// hub/Infrastructure/Web/TestItemsEndpoints.cs

app.MapGet("/api/test-items/{itemId:guid}/commands", async (
    Guid itemId,
    [FromServices] IResultsStore store) =>
{
    var commands = await store.GetCommandsForTestItemAsync(itemId);
    return Results.Ok(commands);
})
.WithName("GetTestItemCommands")
.WithTags("TestItems");
```

### IResultsStore Method
```csharp
Task<List<CommandEventDto>> GetCommandsForTestItemAsync(Guid itemId);
```

### PostgreSQL Implementation
```csharp
public async Task<List<CommandEventDto>> GetCommandsForTestItemAsync(Guid itemId)
{
    await using var conn = new NpgsqlConnection(_connString);
    await conn.OpenAsync();

    var sql = @"
        SELECT
            id,
            timestamp_utc,
            kind,
            message,
            test_id,
            props,
            direction
        FROM command_events
        WHERE test_item_id = @itemId
        ORDER BY timestamp_utc ASC";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("itemId", itemId);

    var commands = new List<CommandEventDto>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var propsJson = reader.IsDBNull(5) ? null : reader.GetString(5);
        var props = string.IsNullOrEmpty(propsJson)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(propsJson);

        commands.Add(new CommandEventDto
        {
            Id = reader.GetGuid(0),
            TimestampUtc = reader.GetDateTime(1),
            Kind = reader.GetString(2),
            Message = reader.GetString(3),
            TestId = reader.IsDBNull(4) ? null : reader.GetString(4),
            Props = props,
            Direction = reader.IsDBNull(6) ? null : reader.GetString(6)
        });
    }

    return commands;
}
```

---

## Testing Checklist

### Visual
- [ ] Stats header displays correctly with all badges
- [ ] Filter inputs styled correctly
- [ ] Quick filter buttons highlight when active
- [ ] Commands list displays with proper spacing
- [ ] Command badges styled correctly
- [ ] Pagination controls styled correctly
- [ ] Bottom pagination displays when needed
- [ ] Copy button hover effect works

### Functional
- [ ] Load commands from API
- [ ] Kind filter works (text input)
- [ ] Direction filter works (text input)
- [ ] Quick filter buttons toggle kind filter
- [ ] Clear button clears kind filter
- [ ] Pagination previous/next buttons work
- [ ] Pagination page numbers display correctly
- [ ] Copy button copies command JSON to clipboard
- [ ] Playwright protocol renders correctly
- [ ] Empty state shows "No commands"
- [ ] Loading state shows "Loading commands..."

### Edge Cases
- [ ] No commands (empty state)
- [ ] Single command (pagination disabled)
- [ ] Many commands (100+) with pagination
- [ ] Filter with no matches
- [ ] Very long command messages
- [ ] Missing props
- [ ] Missing test ID
- [ ] Playwright protocol with large JSON

---

## CSS (Already Exists in ResultsRun.razor)
All styling is inline in the HTML, no separate CSS file needed. The component uses Bootstrap 5 utility classes with inline styles for borders, shadows, and gradients.

---

## Integration Notes

1. **Copy entire section** from ResultsRun.razor (lines 324-600)
2. **Replace variable names**:
   - `MainTab` → `_activeTab`
   - `_run.Failed` → `_testItem.FailedTests`
   - `_run` references → `_testItem` references
3. **Update API endpoint**: `/api/runs/{runId}/commands` → `/api/test-items/{itemId}/commands`
4. **Keep all state variables and methods** from ResultsRun.razor's @code block related to commands
5. **Test thoroughly** to ensure all functionality works

---

## Final Phase Complete
**All 9 phases documented!** Next step: Create implementation summary document.
