# Agenix.PlaywrightGrid.Client

Official .NET client library for Playwright Grid Hub, providing a strongly-typed API for reporting test results
following the ReportPortal model.

## Overview

This client library enables seamless integration with Playwright Grid Hub for:

- **Test Result Reporting**: Report test launches, suites, test items (tests, scenarios, steps, hooks)
- **Browser Lifecycle Management**: Automatic browser borrowing and returning from the browser pool
- **ReportPortal-Aligned Model**: Industry-standard hierarchy (Launch → Suite → Test Item → Step)
- **Rich Metadata**: Attributes, tags, parameters, code references
- **Artifact Upload**: Screenshots, videos, traces, logs

## Installation

```bash
dotnet add package Agenix.PlaywrightGrid.Client
```

## Quick Start

### Basic Test Reporting

```csharp
using Agenix.PlaywrightGrid.Client;
using Agenix.PlaywrightGrid.Shared.Models;
using Agenix.PlaywrightGrid.Shared.Requests;

// Initialize client
var hubUri = new Uri("https://your-playwright-grid-hub.com");
var projectKey = "myapp";
var apiKey = "your-api-key";

using var client = new Service(hubUri, projectKey, apiKey);

// Start Launch
var launchResponse = await client.Launch.StartAsync(new StartLaunchRequest
{
    Name = "CI Build #123",
    Description = "Regression tests for release 2.0",
    Attributes = new[]
    {
        "build:2.0.123",
        "environment:staging"
    }
});
var launchId = launchResponse.Id;

// Start Suite
var suiteResponse = await client.Suite.StartAsync(launchId, new StartSuiteRequest
{
    LaunchId = launchId,
    Name = "Login Feature",
    StartTime = DateTimeOffset.UtcNow
});
var suiteId = suiteResponse.Id;

// Start Test Item (automatically borrows browser)
var testItemResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = suiteId,
    Name = "Login with valid credentials",
    Type = TestItemType.Test,
    LabelKey = "myapp:chromium:staging", // Browser pool label
    StartTime = DateTimeOffset.UtcNow
});
var testItemId = testItemResponse.Id;

// Browser connection details
Console.WriteLine($"Browser ID: {testItemResponse.BrowserId}");
Console.WriteLine($"WebSocket: {testItemResponse.WebSocketEndpoint}");

// ... Execute test using the borrowed browser ...

// Finish Test Item (automatically returns browser)
await client.TestItem.FinishAsync(testItemId, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed,
    TestDetails = new TestItemDetails
    {
        TestTitle = "Login with valid credentials",
        TestFile = "tests/auth/login.spec.ts",
        LineNumber = 42
    }
});

// Finish Suite
await client.Suite.FinishAsync(suiteId, new FinishSuiteRequest
{
    EndTime = DateTimeOffset.UtcNow
});

// Finish Launch
await client.Launch.FinishAsync(launchId, new FinishLaunchRequest
{
    EndTime = DateTimeOffset.UtcNow
});
```

## ReportPortal Model

### Hierarchy

```
Launch (CI Build #123)
  └─ Suite (Login Feature)
      └─ Test Item (Login with valid credentials) [Browser Session]
          └─ Step (Navigate to login page)
              └─ Nested Step (Fill username field) [hasStats=false]
              └─ Nested Step (Fill password field) [hasStats=false]
          └─ Step (Click submit button)
          └─ Step (Verify dashboard loaded)
```

### Test Item Types

The library supports all ReportPortal test item types:

- **Suite**: Test suite or feature container
- **Story**: User story container (BDD-style)
- **Test**: Individual test case or test method
- **Scenario**: BDD scenario (Gherkin-style)
- **Step**: Test step within a test or scenario
- **BeforeSuite**, **BeforeClass**, **BeforeMethod**, **BeforeTest**: Setup hooks
- **AfterSuite**, **AfterClass**, **AfterMethod**, **AfterTest**: Teardown hooks

### Browser Lifecycle Integration

**Key Insight**: Browser session lifecycle is orthogonal to test execution.

When you start a test item with `Type = Test` or `Type = Scenario`:

1. A browser is **automatically borrowed** from the pool based on `LabelKey`
2. Response includes browser connection details (`BrowserId`, `WebSocketEndpoint`)
3. Test executes using the borrowed browser
4. When you finish the test item, the browser is **automatically returned** to the pool

For other item types (`Step`, hooks), no browser is borrowed unless explicitly requested.

### Has Stats

Use `HasStats = false` for nested steps that provide detailed execution logs without affecting pass/fail statistics:

```csharp
var nestedStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = stepId,
    Name = "Fill username field",
    Type = TestItemType.Step,
    HasStats = false, // Doesn't affect pass/fail counts
    StartTime = DateTimeOffset.UtcNow
});
```

## BDD/Gherkin-Style Reporting

```csharp
// Start Scenario
var scenarioResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = storyId,
    Name = "Scenario: User logs in with valid credentials",
    Type = TestItemType.Scenario,
    LabelKey = "myapp:chromium:staging",
    StartTime = DateTimeOffset.UtcNow
});
var scenarioId = scenarioResponse.Id;

// Given step
var givenStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = scenarioId,
    Name = "Given the user is on the login page",
    Type = TestItemType.Step,
    HasStats = false,
    StartTime = DateTimeOffset.UtcNow
});
await client.TestItem.FinishAsync(givenStepResponse.Id, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed
});

// When step
var whenStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = scenarioId,
    Name = "When the user enters valid credentials",
    Type = TestItemType.Step,
    HasStats = false,
    StartTime = DateTimeOffset.UtcNow
});
await client.TestItem.FinishAsync(whenStepResponse.Id, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed
});

// Then step
var thenStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = scenarioId,
    Name = "Then the user should be redirected to the dashboard",
    Type = TestItemType.Step,
    HasStats = false,
    StartTime = DateTimeOffset.UtcNow
});
await client.TestItem.FinishAsync(thenStepResponse.Id, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed
});

// Finish Scenario
await client.TestItem.FinishAsync(scenarioId, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed
});
```

## Test Hooks

```csharp
// BeforeClass hook
var beforeClassResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = suiteId,
    Name = "Setup test database",
    Type = TestItemType.BeforeClass,
    HasStats = false, // Hooks typically don't affect statistics
    StartTime = DateTimeOffset.UtcNow
});
await client.TestItem.FinishAsync(beforeClassResponse.Id, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed
});

// ... tests ...

// AfterClass hook
var afterClassResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = suiteId,
    Name = "Cleanup test database",
    Type = TestItemType.AfterClass,
    HasStats = false,
    StartTime = DateTimeOffset.UtcNow
});
await client.TestItem.FinishAsync(afterClassResponse.Id, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Passed
});
```

## Attributes and Metadata

### Attributes

Attributes are key-value pairs or tags for filtering and grouping.

**For Launch/Suite requests** (using `string[]`):

```csharp
Attributes = new[]
{
    "priority:high",      // Key-value pair
    "author:john.doe",    // Key-value pair
    "smoke"               // Tag without key
}
```

**For TestItem requests** (using `ItemAttribute[]`):

```csharp
Attributes = new[]
{
    new ItemAttribute { Key = "priority", Value = "high" },
    new ItemAttribute { Key = "author", Value = "john.doe" },
    new ItemAttribute { Value = "smoke" } // Tag without key
}
```

### Parameters (Parameterized Tests)

```csharp
Parameters = new[]
{
    new ItemAttribute { Key = "username", Value = "admin@example.com" },
    new ItemAttribute { Key = "password", Value = "***" }
}
```

### Code References

```csharp
CodeRef = "tests/auth/login.spec.ts:42"
```

### Tags

```csharp
Tags = new[] { "smoke", "critical", "regression" }
```

## Test Status Values

### Test Result Status (Test Execution)

- **Passed**: Test passed successfully
- **Failed**: Test failed (assertion failure)
- **Skipped**: Test was skipped
- **Timedout**: Test exceeded timeout
- **Cancelled**: Test cancelled by user
- **Errored**: Test encountered an error (exception, crash, etc.)

### Session Status (Browser Pool)

- **Queued**: Browser requested but not yet allocated
- **Running**: Browser allocated and in use by test
- **Completed**: Test finished, browser returned to pool
- **Stopped**: Browser manually stopped by user
- **AutoStopped**: Browser auto-stopped due to inactivity timeout
- **Aborted**: Browser session aborted due to error

**Important**: These are separate concerns. A test can fail (`Status = Failed`) while the browser session completes
normally (`SessionStatus = Completed`).

## Artifact Upload

Upload screenshots, videos, traces, and logs:

```csharp
// Upload from byte array
await client.TestRun.UploadArtifactAsync(
    runId: testItemId.ToString(),
    testId: testItemId.ToString(),
    fileName: "screenshot.png",
    content: screenshotBytes,
    contentType: "image/png"
);

// Upload from file path
await client.TestRun.UploadArtifactFromFileAsync(
    runId: testItemId.ToString(),
    testId: testItemId.ToString(),
    filePath: "/path/to/screenshot.png"
);
```

## Error Handling

The client throws `ServiceException` for HTTP errors:

```csharp
try
{
    var testItemResponse = await client.TestItem.StartAsync(request);
}
catch (ServiceException ex)
{
    Console.WriteLine($"HTTP {ex.StatusCode}: {ex.Message}");
    Console.WriteLine($"Response: {ex.ResponseContent}");
}
```

Common error codes:

- **400 Bad Request**: Invalid request body
- **401 Unauthorized**: Invalid or expired API token
- **404 Not Found**: Launch, suite, or test item not found
- **409 Conflict**: Test item already finished
- **503 Service Unavailable**: No available browsers in pool

## Migration from TestRun API

The `TestRun` resource is deprecated in favor of the `TestItem` resource for ReportPortal-aligned reporting.

### Old API (Deprecated)

```csharp
var testRunResponse = await client.TestRun.StartAsync(suiteId, new StartTestRunRequest
{
    ParentItemId = suiteId,
    LabelKey = "myapp:chromium:staging",
    RunName = "Login test"
});
```

### New API (Recommended)

```csharp
var testItemResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    LaunchId = launchId,
    ParentItemId = suiteId,
    Name = "Login test",
    Type = TestItemType.Test,
    LabelKey = "myapp:chromium:staging"
});
```

### Key Differences

1. **Unified API**: `TestItem` replaces separate `TestRun` and `TestCase` concepts
2. **Item Types**: Explicit `Type` field supports tests, scenarios, steps, hooks
3. **Parent References**: Support for nested items via `ParentItemId`
4. **Has Stats**: Control whether items affect statistics
5. **ReportPortal Alignment**: Follows industry-standard model

### Migration Steps

1. Replace `client.TestRun.StartAsync()` with `client.TestItem.StartAsync()`
2. Add `Type = TestItemType.Test` to the request
3. Replace `RunName` with `Name`
4. Update finish calls to use `client.TestItem.FinishAsync()`
5. Test artifact upload remains the same (uses `TestRun` resource for now)

## Configuration

### Using appsettings.json

```json
{
  "PlaywrightGrid": {
    "HubUri": "https://your-playwright-grid-hub.com",
    "ProjectKey": "myapp",
    "ApiKey": "your-api-key"
  }
}
```

```csharp
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var hubUri = new Uri(config["PlaywrightGrid:HubUri"]);
var projectKey = config["PlaywrightGrid:ProjectKey"];
var apiKey = config["PlaywrightGrid:ApiKey"];

using var client = new Service(hubUri, projectKey, apiKey);
```

### Using Environment Variables

```bash
export PLAYWRIGHT_GRID_HUB_URI="https://your-playwright-grid-hub.com"
export PLAYWRIGHT_GRID_PROJECT_KEY="myapp"
export PLAYWRIGHT_GRID_API_KEY="your-api-key"
```

```csharp
var hubUri = new Uri(Environment.GetEnvironmentVariable("PLAYWRIGHT_GRID_HUB_URI"));
var projectKey = Environment.GetEnvironmentVariable("PLAYWRIGHT_GRID_PROJECT_KEY");
var apiKey = Environment.GetEnvironmentVariable("PLAYWRIGHT_GRID_API_KEY");

using var client = new Service(hubUri, projectKey, apiKey);
```

## Best Practices

### 1. Always Return Browsers

Ensure every test item that borrows a browser calls `FinishAsync()` to return it to the pool:

```csharp
try
{
    var testItemResponse = await client.TestItem.StartAsync(request);
    // ... execute test ...
    await client.TestItem.FinishAsync(testItemId, new FinishTestItemRequest
    {
        EndTime = DateTimeOffset.UtcNow,
        Status = TestResultStatus.Passed
    });
}
catch (Exception ex)
{
    // Always finish even on error
    await client.TestItem.FinishAsync(testItemId, new FinishTestItemRequest
    {
        EndTime = DateTimeOffset.UtcNow,
        Status = TestResultStatus.Errored,
        TestDetails = new TestItemDetails
        {
            ErrorMessage = ex.Message,
            ErrorStack = ex.StackTrace
        }
    });
}
```

### 2. Use Nested Steps for Detailed Reporting

Use `HasStats = false` for nested steps:

```csharp
var detailedStepResponse = await client.TestItem.StartAsync(new StartTestItemRequest
{
    ParentItemId = stepId,
    Name = "Fill input field",
    Type = TestItemType.Step,
    HasStats = false // Doesn't affect pass/fail counts
});
```

### 3. Include Test Details on Failure

```csharp
await client.TestItem.FinishAsync(testItemId, new FinishTestItemRequest
{
    EndTime = DateTimeOffset.UtcNow,
    Status = TestResultStatus.Failed,
    TestDetails = new TestItemDetails
    {
        ErrorMessage = "Expected 'Dashboard' but got 'Login'",
        ErrorStack = ex.StackTrace,
        TestFile = "tests/auth/login.spec.ts",
        LineNumber = 42
    }
});
```

### 4. Use Attributes for Filtering

```csharp
Attributes = new[]
{
    new ItemAttribute { Key = "environment", Value = "staging" },
    new ItemAttribute { Key = "browser", Value = "chromium" },
    new ItemAttribute { Key = "priority", Value = "high" },
    new ItemAttribute { Value = "smoke" } // Simple tag
}
```

### 5. Set Descriptive Names

Use clear, descriptive names for launches, suites, and test items:

```csharp
Name = "Login with valid credentials and redirect to dashboard"
```

### 6. Include Timestamps

Always include accurate `StartTime` and `EndTime`:

```csharp
StartTime = DateTimeOffset.UtcNow
```

## Examples

See the [Examples](./Examples) folder for complete working examples:

- **ReportPortalStyleExample.cs**: Basic ReportPortal-style reporting
- **BddStyleExample.cs**: BDD/Gherkin-style scenarios and steps
- **TestHooksExample.cs**: Setup and teardown hooks

## API Reference

### Resources

- **`ILaunchResource`**: Launch operations (start, finish, get, update)
- **`ISuiteResource`**: Suite operations (start, finish, get, nested suites)
- **`ITestItemResource`**: Test item operations (start, finish, get, children) ← **Recommended**
- **`ITestRunResource`**: [Deprecated] Test run operations (use TestItem instead)
- **`ILogItemResource`**: Log operations (create, get)

### Request DTOs

- **`StartLaunchRequest`**: Start a launch
- **`FinishLaunchRequest`**: Finish a launch
- **`UpdateLaunchRequest`**: Update launch details
- **`StartSuiteRequest`**: Start a suite
- **`FinishSuiteRequest`**: Finish a suite
- **`StartTestItemRequest`**: Start a test item ← **Recommended**
- **`FinishTestItemRequest`**: Finish a test item ← **Recommended**
- **`StartTestRunRequest`**: [Deprecated] Start a test run
- **`FinishTestRunRequest`**: [Deprecated] Finish a test run

### Response DTOs

- **`LaunchCreatedResponse`**: Launch creation response
- **`LaunchResponse`**: Launch details
- **`SuiteCreatedResponse`**: Suite creation response
- **`SuiteResponse`**: Suite details
- **`TestItemCreatedResponse`**: Test item creation response (includes browser session) ← **Recommended**
- **`TestItemResponse`**: Test item details ← **Recommended**
- **`TestRunCreatedResponse`**: [Deprecated] Test run creation response
- **`TestRunResponse`**: [Deprecated] Test run details
- **`MessageResponse`**: Generic message response

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

## License

Apache-2.0 License. See [LICENSE](../LICENSE) for details.

## Support

- **Documentation**: [Playwright Grid Docs](https://docs.your-playwright-grid.com)
- **Issues**: [GitHub Issues](https://github.com/agenixframework/agenix-playwright-grid/issues)
- **Discussions**: [GitHub Discussions](https://github.com/agenixframework/agenix-playwright-grid/discussions)

---

*Last updated: 2025-01-24*
