# TestItemDetails.razor Implementation Guide

## Overview
Comprehensive phased implementation guide for creating a ReportPortal-style test item details page with hierarchical logs, rich metadata display, and debugging capabilities.

## Source Materials
- **HTML Prototype**: `dashboard/wwwroot/prototypes/test-logs-prototype.html`
- **Reference Component**: `dashboard/Pages/ResultsRun.razor` (for Playwright commands section)
- **Target Component**: `dashboard/Pages/TestItemDetails.razor` (new file)
- **Route**: `/{projectKey}/test-items/{itemId:guid}`

---

## Implementation Phases

### ✅ Phase 1: Breadcrumb Navigation
**File**: `phase1-breadcrumb.md`

**Components**:
- Project → Launch → Suite → Test hierarchy
- Clickable navigation links
- Current item indicator (no link)

**Complexity**: Low
**Estimated Time**: 30 minutes

---

### ✅ Phase 2: Test Navigation Controls
**File**: `phase2-test-navigation.md`

**Components**:
- Previous/Next test buttons
- Refresh button
- Test counter (e.g., "Test 3 of 15")

**Complexity**: Low
**Estimated Time**: 45 minutes

---

### ✅ Phase 3: Test History Line
**File**: `phase3-history-line.md`

**Components**:
- Horizontal history badges showing test execution across launches
- Navigation buttons (Previous/Next launch)
- Position indicator
- Elegant tooltips with launch details
- Defect type indicators
- Arrow connectors between history items
- Active item indicator (triangle)
- Keyboard navigation support

**Complexity**: High
**Estimated Time**: 3-4 hours

---

### ✅ Phase 4: Main Tabs Structure
**File**: `phase4-main-tabs.md`

**Components**:
- 6 main tabs with icons:
  - Execution Logs (🖥️ bi-terminal)
  - Stack Trace (⚠️ bi-exclamation-triangle)
  - Item Details (ℹ️ bi-info-circle)
  - Artifacts (📎 bi-paperclip)
  - Browser Session (🌐 bi-browser-chrome)
  - Playwright Commands (⚙️ bi-code-slash)
- Active tab highlighting with bottom border
- Tab content panels with conditional rendering

**Complexity**: Low
**Estimated Time**: 1 hour

---

### ✅ Phase 5: Execution Logs Tab
**File**: `phase5-execution-logs.md`

**Components**:
- Compact filter bar with search and dropdown
- Advanced filters panel (collapsible)
- Test summary bar with expand/collapse all
- Hierarchical log entries (Launch → Suite → Test → Steps)
- Markdown view (structured) vs Console view (flat text)
- Log level badges with proper styling
- Attachment badges
- Pagination with per-page selector
- Show more/less for long messages
- Light/Dark theme for console view

**Complexity**: Very High
**Estimated Time**: 6-8 hours

---

### ✅ Phase 6: Stack Trace Tab
**File**: `phase6-stack-trace.md`

**Components**:
- Error entry cards with hover effects
- Timestamp, context, and error level badges
- Error message with class name
- "Jump to log" button linking to Execution Logs tab
- Expandable stack trace details section
- HTTP method badges and status codes
- Code line highlighting
- Empty state when no errors

**Complexity**: Medium
**Estimated Time**: 2-3 hours

---

### ✅ Phase 7: Item Details Tab
**File**: `phase7-item-details.md`

**Components**:
- Header section with item name, status badge, and duration
- Grid layout with label-value rows
- Tags display with icon badges
- Attributes display with icon badges
- Code reference with copy button
- Test case ID display
- Description box with gradient background
- Parameters display with badges
- Browser and worker node information

**Complexity**: Medium
**Estimated Time**: 2-3 hours

---

### ✅ Phase 8: Artifacts Tab
**File**: `phase8-artifacts.md`

**Components**:
- File preview area with centered display
- Navigation arrows (previous/next artifact)
- File icon display for non-previewable files
- Download and Open in new tab actions
- Thumbnail strip at bottom with horizontal scroll
- Active thumbnail highlighting
- Thumbnail labels with file names
- Empty state when no artifacts

**Complexity**: Medium-High
**Estimated Time**: 3-4 hours

---

### ✅ Phase 9: Playwright Commands Integration
**File**: `phase9-playwright-commands.md`

**Components**:
- Stats header with duration, failed count, commands count, kinds count
- Filter inputs for Kind and Direction
- Quick filter buttons with counts
- Commands list with pagination
- Command details display (kind, message, test ID, properties)
- Playwright protocol rendering
- Copy command functionality
- Elegant bordered container design

**Source**: Copy from `dashboard/Pages/ResultsRun.razor` lines 324-600

**Complexity**: Low (copy-paste with adaptations)
**Estimated Time**: 1-2 hours

---

## Total Estimated Time
**20-30 hours** for complete implementation with testing

---

## Implementation Order Recommendations

### Option 1: Progressive Enhancement (Recommended)
Build foundation first, then add complex features:
1. Phase 4 (Main Tabs) - Get structure in place
2. Phase 1 (Breadcrumb) - Basic navigation
3. Phase 2 (Test Navigation) - Basic controls
4. Phase 7 (Item Details) - Simple metadata display
5. Phase 6 (Stack Trace) - Medium complexity
6. Phase 8 (Artifacts) - Medium complexity
7. Phase 9 (Playwright Commands) - Copy existing code
8. Phase 3 (History Line) - Complex feature
9. Phase 5 (Execution Logs) - Most complex feature

### Option 2: Top-to-Bottom (Linear)
Follow phases 1-9 in order for logical progression.

### Option 3: Tab-by-Tab (Parallel)
Complete one tab fully before moving to next:
1. Phases 1-4 (Foundation)
2. Phase 7 (Item Details - simplest tab)
3. Phase 6 (Stack Trace)
4. Phase 8 (Artifacts)
5. Phase 9 (Commands)
6. Phase 5 (Execution Logs - most complex)
7. Phase 3 (History Line)

---

## Technical Dependencies

### Backend APIs Required
1. `/api/test-items/{id}` - Get test item details
2. `/api/test-items/{id}/history` - Get test history across launches
3. `/api/test-items/{id}/logs` - Get log entries
4. `/api/test-items/{id}/stack-traces` - Get stack trace entries
5. `/api/test-items/{id}/artifacts` - Get artifacts
6. `/api/test-items/{id}/commands` - Get command events

### Frontend Dependencies
- Bootstrap 5 (already in project)
- Bootstrap Icons (already in project)
- JavaScript interop for:
  - Smooth scrolling
  - Copy to clipboard
  - Keyboard navigation
  - History line centering

### CSS Files to Create
- `wwwroot/css/test-item-details.css` - All custom styles from prototype

---

## Testing Strategy

### Unit Testing
- Test state management methods
- Test filtering logic
- Test pagination calculations
- Test navigation methods

### Integration Testing
- Test API calls and data loading
- Test tab switching
- Test filter interactions
- Test navigation flows

### Visual Testing
- Compare with prototype HTML
- Test responsive design (mobile/tablet/desktop)
- Test all hover states
- Test all animations and transitions

### Edge Cases to Test
- Empty states (no data)
- Large datasets (1000+ log entries)
- Very long text content
- Missing optional fields
- Network errors
- Invalid test item IDs

---

## Migration Notes

### From Prototype to Blazor
- All inline styles → Extract to CSS file
- Static HTML → Dynamic Blazor components
- Hardcoded data → API-driven data
- JavaScript functions → C# methods + JS interop

### Variable Name Conventions
- Prototype: JavaScript camelCase → Blazor: C# PascalCase for public, _camelCase for private
- Example: `currentTab` → `_activeTab`

---

## Performance Considerations

1. **Lazy Loading**: Load tab content only when tab is activated (Phase 4)
2. **Pagination**: Always paginate large lists (logs, commands)
3. **Virtual Scrolling**: Consider for 1000+ log entries (Phase 5)
4. **Caching**: Cache loaded data when switching tabs
5. **Debouncing**: Debounce filter inputs (Phase 5)

---

## Browser Compatibility
- Modern browsers (Chrome, Firefox, Safari, Edge)
- Mobile browsers (iOS Safari, Chrome Mobile)
- No IE11 support required

---

## Accessibility Features
- ARIA labels on all interactive elements
- Keyboard navigation support
- Focus indicators
- Screen reader friendly
- High contrast mode support

---

## Success Criteria

### Functional
- [ ] All 9 phases implemented
- [ ] All backend APIs working
- [ ] All tabs display correctly
- [ ] All navigation works
- [ ] All filters work
- [ ] All pagination works
- [ ] All copy/download actions work

### Visual
- [ ] Matches prototype design
- [ ] Responsive on mobile/tablet/desktop
- [ ] All animations smooth
- [ ] All hover effects work
- [ ] No visual glitches

### Performance
- [ ] Page loads in < 2 seconds
- [ ] Tab switching is instant
- [ ] Filtering is responsive (< 100ms)
- [ ] No memory leaks
- [ ] Handles 1000+ log entries

---

## Deployment Checklist

- [ ] All CSS files created and linked
- [ ] All JavaScript helpers implemented
- [ ] All backend endpoints deployed
- [ ] All database migrations applied
- [ ] Integration tests passing
- [ ] Visual regression tests passing
- [ ] Mobile testing completed
- [ ] Accessibility audit passed
- [ ] Performance audit passed
- [ ] Documentation updated

---

## Support Resources

- **Prototype HTML**: `dashboard/wwwroot/prototypes/test-logs-prototype.html`
- **Phase Documentation**: `docs/itemtestlog/phase[1-9]-*.md`
- **Example Component**: `dashboard/Pages/ResultsRun.razor`
- **Bootstrap 5 Docs**: https://getbootstrap.com/docs/5.0/
- **Bootstrap Icons**: https://icons.getbootstrap.com/
- **Blazor Docs**: https://docs.microsoft.com/en-us/aspnet/core/blazor/

---

## Questions? Issues?

- Review phase documentation thoroughly before starting implementation
- Test each phase individually before moving to next
- Use browser DevTools to debug CSS/JavaScript issues
- Use Blazor debugging tools for C# issues
- Compare rendered output with prototype HTML frequently

---

**Last Updated**: 2025-01-25
**Status**: Ready for Implementation
**Phases Documented**: 9/9 ✅
