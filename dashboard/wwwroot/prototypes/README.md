# Playwright Grid Dashboard - HTML Prototypes

This directory contains static HTML prototypes for the Blazor components and pages in the Playwright Grid Dashboard.

## Purpose

These prototypes serve as:

- **Design References**: Visual examples of how components should look and behave
- **Development Guides**: HTML/CSS structure that can be translated to Blazor
- **Testing**: Quick way to test layouts and styles without running the full Blazor app
- **Documentation**: Living examples of the UI patterns used in the application

## Access the Prototypes

Open `index.html` in your browser to see all available prototypes:

```
http://localhost:3001/prototypes/index.html
```

Or access individual prototypes directly:

- http://localhost:3001/prototypes/test-item-tree.html
- http://localhost:3001/prototypes/test-item-card.html
- http://localhost:3001/prototypes/test-step-tree.html
- http://localhost:3001/prototypes/results-run.html
- http://localhost:3001/prototypes/test-case-details.html
- http://localhost:3001/prototypes/test-item-details.html

## Components

### 1. Test Item Tree (`test-item-tree.html`)

**Source**: `dashboard/Components/TestItemTree.razor`

Hierarchical test item display with ReportPortal-style structure.

**Features**:

- Recursive tree rendering with unlimited nesting
- Multiple item types: Test, Step, Suite, Scenario, Story, Hooks
- Collapsible/expandable nodes
- Dual status indicators (ComputedStatus + SessionStatus)
- Inline metadata display (attributes, tags, parameters, code refs)
- Error message and stack trace display
- Test aggregation statistics
- Item type badges with icons

**Used In**: ResultsRun.razor, TestItemDetails.razor

---

### 2. Test Item Card (`test-item-card.html`)

**Source**: `dashboard/Components/TestItemCard.razor`

Card-based layout for individual test item display with rich details.

**Features**:

- Gradient card headers with status colors
- Comprehensive timing information
- Attributes and tags display
- Browser session information
- Visual progress bars for test aggregations
- Error alerts with expandable stack traces
- Code reference and parameters
- Child count indicator
- Clickable cards with custom callbacks

**Used In**: TestRunDetails.razor (Grid view - future)

---

### 3. Test Step Tree (`test-step-tree.html`)

**Source**: `dashboard/Components/TestStepTree.razor`

Hierarchical test step display with execution details.

**Features**:

- Nested step structure with indentation
- Step categories (hook, action, assertion, etc.)
- Duration display for each step
- Code snippets (expandable)
- Error messages for failed steps
- Collapsible/expandable step groups

**Used In**: ResultsRun.razor (Legacy view)

---

## Pages

### 4. Results Run (`results-run.html`)

**Source**: `dashboard/Pages/ResultsRun.razor`

Detailed test execution page showing test overview and execution details.

**Features**:

- Gradient header with test name and status
- Test overview metadata grid (ID, browser, worker, duration)
- Test file information with code reference
- View mode toggle (Legacy vs Hierarchical)
- Test step execution timeline
- Artifacts and screenshots gallery
- Action buttons (retry, export, delete)

**Route**: `/{projectKey}_default/results/{runId}`

---

### 5. Test Case Details (`test-case-details.html`)

**Source**: `dashboard/Pages/TestCaseDetails.razor`

Comprehensive test case view with execution details and diagnostics.

**Features**:

- Test information grid
- Error details with full stack traces
- Test steps execution list with statuses
- Console output (STDOUT/STDERR tabs)
- Screenshots gallery
- Test actions (retry, download trace, copy ID)

**Route**: `/{projectKey}/test-cases/{testCaseId}`

---

### 6. Test Item Details (`test-item-details.html`)

**Source**: `dashboard/Pages/TestItemDetails.razor`

Full-page ReportPortal-style test item view with hierarchy.

**Features**:

- Test overview with detailed metadata
- Test aggregation statistics with progress bars
- Child test items tree (hierarchical display)
- Attachments and artifacts list
- Hierarchy information (Launch/Suite/Parent)
- Comprehensive actions toolbar

**Route**: `/{projectKey}/test-items/{itemId:guid}`

---

## Design System

### Colors

- **Primary**: `#667eea` (Purple)
- **Success**: `#28a745` (Green)
- **Danger**: `#dc3545` (Red)
- **Warning**: `#ffc107` (Yellow)
- **Info**: `#17a2b8` (Cyan)
- **Secondary**: `#6c757d` (Gray)

### Item Type Colors

- **Test**: Primary (Blue)
- **Step**: Secondary (Gray)
- **Suite**: Dark
- **Scenario**: Info (Cyan)
- **Story**: Purple
- **Hooks**: Warning (Yellow)

### Status Colors

- **Passed**: Success (Green)
- **Failed**: Danger (Red)
- **Skipped**: Warning (Yellow)
- **Timedout**: Secondary (Gray)
- **InProgress**: Info (Blue)
- **Queued**: Secondary (Gray)

### Typography

- **Headers**: System font stack
- **Code**: 'Courier New', monospace
- **Body**: Default Bootstrap font stack

### Spacing

- **Tree Indentation**: 24px per level
- **Card Padding**: 1.5rem
- **Section Margins**: 1.5rem

---

## Technologies Used

- **Bootstrap 5.3.0**: Layout, components, utilities
- **Bootstrap Icons 1.10.0**: Icon set
- **Custom CSS**: Component-specific styling
    - `test-item-components.css`
    - `test-step-tree.css`
    - `test-case-details.css`
    - `test-run-details.css`
    - `site.css`

---

## Browser Compatibility

✅ Modern browsers (Chrome, Firefox, Safari, Edge)
✅ Mobile browsers (iOS Safari, Chrome Mobile)
✅ Responsive design (320px - 1920px)
✅ Print-friendly styles
✅ High contrast mode support
✅ Reduced motion support

---

## Development Workflow

### Using Prototypes for Blazor Development

1. **Design First**: Create/update HTML prototype
2. **Review**: Test layout, interactions, responsive behavior
3. **Implement**: Translate HTML to Blazor Razor syntax
4. **Integrate**: Add C# logic, data binding, event handling
5. **Test**: Verify functionality matches prototype

### Updating Prototypes

When updating Blazor components:

1. Update the corresponding HTML prototype
2. Test changes in isolation
3. Update Blazor component
4. Keep prototype in sync for future reference

---

## File Structure

```
dashboard/wwwroot/prototypes/
├── index.html                  # Prototype gallery (this file)
├── README.md                   # This documentation
├── test-item-tree.html         # Component prototype
├── test-item-card.html         # Component prototype
├── test-step-tree.html         # Component prototype
├── results-run.html            # Page prototype
├── test-case-details.html      # Page prototype
└── test-item-details.html      # Page prototype
```

---

## Related Files

### Blazor Components

- `dashboard/Components/TestItemTree.razor`
- `dashboard/Components/TestItemCard.razor`
- `dashboard/Components/TestStepTree.razor`

### Blazor Pages

- `dashboard/Pages/ResultsRun.razor`
- `dashboard/Pages/TestCaseDetails.razor`
- `dashboard/Pages/TestItemDetails.razor`

### Stylesheets

- `dashboard/wwwroot/css/test-item-components.css`
- `dashboard/wwwroot/css/test-step-tree.css`
- `dashboard/wwwroot/css/test-case-details.css`
- `dashboard/wwwroot/css/test-run-details.css`
- `dashboard/wwwroot/css/site.css`

---

## Notes

- **Static Data**: All prototypes use static/dummy data
- **Interactivity**: Basic JavaScript for toggles and tabs
- **No Backend**: These are pure frontend prototypes
- **CDN Resources**: Uses Bootstrap CDN (no local files needed)
- **Accessibility**: Basic ARIA attributes included

---

## Future Enhancements

- [ ] Add dark mode variants
- [ ] Add loading states and skeletons
- [ ] Add empty states for all components
- [ ] Add mobile-specific prototypes
- [ ] Add animation examples
- [ ] Add print-specific layouts

---

Generated: 2025-01-28
Dashboard Version: 1.0.1-preview.6
