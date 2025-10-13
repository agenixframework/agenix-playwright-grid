# Architecture Plan: Suite History Matrix

## 1. High-Level Approach

We'll implement two complementary matrix views that share common infrastructure but query different levels of the test hierarchy:

### Launch-Level Matrix
- **Entry Point:** Launch details page
- **Queries:** Parent items (`item_type IN ('Suite', 'Story', 'BeforeSuite', 'AfterSuite', 'BeforeClass', 'AfterClass')`)
- **Navigation:** Click Suite → Navigate to suite details

### Suite-Level Matrix  
- **Entry Point:** Suite details page
- **Queries:** Child items (`item_type IN ('Test', 'Scenario', 'BeforeTest', 'AfterTest', 'BeforeMethod', 'AfterMethod')`)
- **Navigation:** Click Test → Navigate to test details

**Important:** Both matrices **EXCLUDE 'Step' items** - too granular, would create matrix bloat

### Shared Infrastructure
- Same API pattern & DTO structure
- Same rendering component with mode switching
- Same styling and interaction patterns
- Different database queries and filtering logic

---

## 2. Technical Stack Decisions

### Backend
- **API Framework:** ASP.NET Core Minimal APIs (existing pattern)
- **Database:** PostgreSQL with custom functions (not ORM)
- **Caching:** Optional Redis layer (Phase 5)
- **Serialization:** System.Text.Json (built-in)

**Decision Rationale:**
- Custom PostgreSQL functions give maximum query optimization
- Can leverage CTEs for complex hierarchical queries
- Avoids Entity Framework overhead for this read-heavy feature
- Functions can be fine-tuned with EXPLAIN ANALYZE

### Frontend
- **UI Framework:** Blazor Server (existing)
- **Component Model:** Reusable `ItemTestHistoryMatrix.razor` with parameters
- **State Management:** Component-scoped (no global state)
- **Styling:** Shared CSS module, Bootstrap 5 compatible
- **Interactivity:** EventCallback for navigation, no JavaScript interop

**Decision Rationale:**
- Reusable component reduces duplication between matrix types
- Parameter-driven approach makes testing easier
- Blazor Server already has SignalR for future real-time updates
- Keeping state local avoids complexity

### Database
- **Migrations:** Evolve (existing pattern)
- **Optimization:** CTE-based hierarchical queries
- **Indexes:** Composite indexes on (launch_id, item_type, parent_item_id)
- **Anti-patterns Avoided:** No N+1 queries, no in-memory processing

---

## 3. Component Architecture

### Backend Layer
```
┌─────────────────────────────────────┐
│  API Endpoints (Minimal APIs)       │
├─────────────────────────────────────┤
│  GET /api/launches/{id}/parent-items-history  │ {id} = db_id of launch
│  GET /api/suites/{id}/child-items-history     │ {id} = db_id of suite
└────────────┬────────────────────────┘
             │
             ▼
┌─────────────────────────────────────┐
│  IResultsStore Interface            │
├─────────────────────────────────────┤
│  GetLaunchParentItemsHistoryAsync   │
│  GetSuiteChildItemsHistoryAsync     │
└────────────┬────────────────────────┘
             │
             ▼
┌─────────────────────────────────────┐
│  PostgresResultsStore                │
│  (implements queries)               │
└────────────┬────────────────────────┘
             │
             ▼
┌─────────────────────────────────────┐
│  PostgreSQL Functions               │
│  • get_launch_parent_items_history  │
│  • get_suite_child_items_history    │
└─────────────────────────────────────┘
```

### Frontend Layer
```
┌─────────────────────────────────────┐
│  TestRunDetails.razor                │
│  SuiteDetails.razor                 │
└────────────┬────────────────────────┘
             │
             ▼
┌─────────────────────────────────────┐
│  ItemTestHistoryMatrix.razor           │
│  (reusable component)               │
├─────────────────────────────────────┤
│  Parameters:                      │
│  • Context (launch/suite)           │
│  • ItemTypes (array)                │
│  • Depth                            │
│  • ViewMode                         │
└────────────┬────────────────────────┘
             │
             ▼
┌─────────────────────────────────────┐
│  API Client                         │
│  HttpClient → GET endpoints         │
└─────────────────────────────────────┘
```

---

## 4. Key Implementation Patterns

### Pattern 1: Parameter-Driven Component
```csharp
// Single component handles both contexts
<HistoryMatrix 
    Context="launch|suite"
    ItemTypes="@itemTypeArray"
    Depth="@_depth" />
    
// Determines:
// - Which API endpoint to call
// - Which columns to show
// - Navigation targets
```

### Pattern 2: Database-First Queries
```sql
// Single query returns fully-structured result
// Avoids N+1 problem
// All processing in PostgreSQL
WITH RECURSIVE item_tree AS (
    // Get items for this context
    WHERE item_type IN (@itemTypes)
)
SELECT jsonb_build_object(...) FROM item_tree;
```

### Pattern 3: Lazy Loading Tree
```csharp
// Don't load entire tree at once
// First load only parent items
// Click to expand loads children on-demand
// Progressive disclosure for performance
```

---

## 5. Performance Strategy

### Database Optimization
1. **Index Coverage:** Ensure composite indexes on (launch_id, item_type, parent_item_id)
2. **CTE Optimization:** Use EXPLAIN ANALYZE to verify plans
3. **JSON Aggregation:** Build final structure in SQL, not C#
4. **Connection Pooling:** Use existing NpgsqlDataSource configuration

### Frontend Optimization
1. **Virtual Scrolling:** For matrices > 50 rows (optional Phase 5)
2. **Memoization:** Cache rendered rows when data unchanged
3. **Debounced Search:** 300ms debounce on search input
4. **CSS Containment:** `contain: strict` on cells for render performance

### Caching Strategy (Phase 5)
```csharp
// Redis cache key pattern
"history:launch:{launchId}:depth:{depth}:filter:{filter}" → JSON payload
TTL: 5 minutes (configurable)

// Invalidate on:
// - New launch completed
// - Test item status change
// - Manual cache clear
```

---

## 6. Error Handling Strategy

### Backend Errors
```csharp
try
{
    var result = await GetLaunchParentItemsHistoryAsync(...);
    return Results.Ok(result);
}
catch (PostgresException ex) when (ex.SqlState == "42P01")
{
    // Table doesn't exist - return 503 with retry-after
    return Results.Problem("Migration not applied", statusCode: 503);
}
catch (TimeoutException)
{
    return Results.Problem("Query timeout", statusCode: 504);
}
```

### Frontend Errors
```razor
@code {
    private string _errorMessage;
    
    private async Task LoadData()
    {
        try
        {
            _data = await Http.GetFromJsonAsync<...>(...);
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            // Log to Application Insights / Serilog
        }
    }
}

@if (_errorMessage != null)
{
    <div class="error-state">
        <i class="bi bi-exclamation-triangle"></i>
        <p>Failed to load history: @_errorMessage</p>
        <button @onclick="LoadData">Retry</button>
    </div>
}
```

---

## 7. Development Phases (Revised)

### Phase 1: Database Foundation (2-3 days)
**Goal:** Foundation queries that work correctly

**Tasks:**
1. Write and test PostgreSQL function for launch-level matrix
2. Write and test PostgreSQL function for suite-level matrix
3. Create required composite indexes
4. Verify no N+1 queries with EXPLAIN ANALYZE
5. Unit test both functions with known data sets

**Deliverables:**
- `V45__history_matrix_functions.sql` migration
- Passing unit tests for both functions
- Performance benchmarks (baseline)

### Phase 2: API & DTOs (1 day)
**Goal:** Expose endpoints and shared models

**Tasks:**
1. Create DTO classes in Shared library
2. Implement API endpoints with proper routing
3. Add OpenAPI documentation annotations
4. Write integration tests for endpoints
5. Manual test with curl / Swagger UI

**Deliverables:**
- Working API endpoints
- DTO serialization tests passing
- OpenAPI schema accurate

### Phase 3: Blazor Component (2-3 days)
**Goal:** Reusable component that renders correctly

**Tasks:**
1. Create `SuiteHistoryMatrix.razor` component shell
2. Implement parameter binding and validation
3. Add data loading with loading/error states
4. Convert CSS from prototype to component styles
5. Implement interactive features (hover, click, search)
6. Unit test component logic

**Deliverables:**
- Component renders with test data
- All interactive features work
- No console errors

### Phase 4: Integration (1-2 days)
**Goal:** Works in actual application with navigation

**Tasks:**
1. Add Launch-Level matrix to LaunchDetails.razor
2. Add Suite-Level matrix to SuiteDetails.razor
3. Wire up navigation between matrices
4. Add toolbar controls
5. Test end-to-end flow
6. Cross-browser testing

**Deliverables:**
- Works with real database data
- Navigation flows correctly
- No console errors

### Phase 5: Performance & Polish (1-2 days) [Optional]
**Goal:** Meet performance targets and add polish

**Tasks:**
1. Add Redis caching layer
2. Performance testing with large datasets
3. Optimize slow queries
4. Add loading skeletons
5. Add animations and transitions

**Deliverables:**
- Meets performance benchmarks
- Polished UI
- Demo recorded

---

## 8. Testing Strategy

### 8.1 Unit Tests (Database Functions)
```sql
-- Test passed status
SELECT get_launch_parent_items_history('suite1', 'project1', 5);
-- Expected: All cells status = 'passed', tooltip = "8 tests: 8 passed"

-- Test failed status
-- Insert: 1 failed, 7 passed
-- Expected: status = 'failed', tooltip includes "1 failed"

-- Test empty status
-- Insert: no test_items for this suite in this launch
-- Expected: status = 'empty'
```

### 8.2 Integration Tests (API)
```csharp
[Fact]
public async Task LaunchHistory_ReturnsValidMatrix()
{
    // Arrange: Create launch with suite executions
    
    // Act: GET /api/launches/{id}/parent-items-history?depth=5
    
    // Assert: Status 200, valid JSON, correct counts
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var data = await response.Content.ReadFromJsonAsync<LaunchLevelHistoryMatrixDto>();
    data.Columns.Should().HaveCount(5);
    data.Rows.Should().NotBeEmpty();
}
```

---

## 9. Risk Mitigation

**Risk 1: Query performance with large datasets**
- **Impact:** High
- **Probability:** Medium
- **Mitigation:** Add indexes, use CTE optimization, Phase 5 caching

**Risk 2: Complex hierarchy breaks in edge cases**
- **Impact:** Medium
- **Probability:** Low
- **Mitigation:** Comprehensive test suite with real-world data

**Risk 3: Blazor component render performance**
- **Impact:** Medium
- **Probability:** Low
- **Mitigation:** Use virtual scrolling, CSS containment, memoization

**Risk 4: Scope creep adding Steps or other types**
- **Impact:** Low
- **Probability:** Medium
- **Mitigation:** Stick to spec - Steps explicitly excluded

---

**Plan Version:** 1.0
**Status:** Ready for implementation
