# Agenix.PlaywrightGrid Client Libraries

## Overview

This document describes the new client libraries for Agenix Playwright Grid, inspired by
the [ReportPortal .NET client](https://github.com/reportportal/client-dotnet) architecture.

The client is split into two packages:

- **Agenix.PlaywrightGrid.Shared** - Framework-agnostic models, interfaces, and configuration
- **Agenix.PlaywrightGrid.Client** - HTTP client implementation

## Authentication

The Playwright Grid Hub uses Bearer token authentication with API keys.

### Obtaining an API Key

API keys are managed through the Dashboard:

1. Login to Dashboard
2. Navigate to Users → Your Profile → API Keys
3. Create a new API key with a descriptive name
4. Copy the generated key (shown only once)

### Using the API Key

Pass your API key when creating the client:

```csharp
var client = new Service(
    baseUri: new Uri("https://grid.example.com"),
    projectKey: "MyProject",
    apiKey: "my-key-abc123..."  // Your API key
);
```

The client automatically adds `Authorization: Bearer <apiKey>` to all requests.

### Authentication Flow

1. Client sends API key as Bearer token
2. Hub validates token and resolves user
3. Hub verifies user is a project member
4. Request authorized if user belongs to project (any role)

### Project Roles

All project roles have equal access to client APIs:

- **ProjectLead**: Full access
- **Member**: Full access
- **Client**: Full access
- **Maintainer**: Full access

Role information is tracked for auditing but does not affect API access.

### HTTP Status Codes

- **401 Unauthorized**: Invalid, revoked, or missing API key
- **403 Forbidden**: Valid key but user not a member of project
- **404 Not Found**: Project does not exist

## Architecture

### Hierarchy

```
Launch (Test Session)
  ├─ Suite (Feature / Test Class)
  │   ├─ Test Run (Scenario / Test Method) + Browser Session
  │   ├─ Test Run (Scenario / Test Method) + Browser Session
  │   └─ Test Run (Scenario / Test Method) + Browser Session
  └─ Suite (Feature / Test Class)
      └─ Test Run (Scenario / Test Method) + Browser Session
```

### Key Concepts

1. **Merged Browser Lifecycle**: Starting a test run automatically borrows a browser; finishing returns it
2. **Reporter Pattern**: Hierarchical reporters (LaunchReporter → SuiteReporter → TestRunReporter)
3. **Context Pattern**: AsyncLocal contexts for thread-safe access to current execution state
4. **Multi-Provider Configuration**: Environment variables, JSON files, in-memory sources

## Project Structure

### Agenix.PlaywrightGrid.Shared

```
Agenix.PlaywrightGrid.Shared/
├── Configuration/
│   ├── IConfiguration.cs                    ✅ TODO
│   ├── IConfigurationBuilder.cs             ✅ TODO
│   ├── IConfigurationProvider.cs            ✅ TODO
│   ├── Configuration.cs                     ✅ TODO
│   ├── ConfigurationBuilder.cs              ✅ TODO
│   └── Providers/
│       ├── EnvironmentVariablesConfigurationProvider.cs  ✅ TODO
│       ├── JsonFileConfigurationProvider.cs              ✅ TODO
│       └── InMemoryConfigurationProvider.cs              ✅ TODO
│
├── Reporter/
│   ├── ILaunchReporter.cs                   ✅ TODO
│   ├── ILaunchReporterInfo.cs               ✅ TODO
│   ├── ISuiteReporter.cs                    ✅ TODO
│   ├── ISuiteReporterInfo.cs                ✅ TODO
│   ├── ITestRunReporter.cs                  ✅ TODO
│   ├── ITestRunReporterInfo.cs              ✅ TODO
│   ├── IBrowserSession.cs                   ✅ TODO
│   ├── ILogsReporter.cs                     ✅ TODO
│   ├── LaunchInfo.cs                        ✅ TODO
│   ├── SuiteInfo.cs                         ✅ TODO
│   ├── TestRunInfo.cs                       ✅ TODO
│   └── BrowserSessionInfo.cs                ✅ TODO
│
├── Execution/
│   ├── ILaunchContext.cs                    ✅ TODO
│   ├── ISuiteContext.cs                     ✅ TODO
│   ├── ITestRunContext.cs                   ✅ TODO
│   ├── LaunchContext.cs                     ✅ TODO
│   ├── SuiteContext.cs                      ✅ TODO
│   └── TestRunContext.cs                    ✅ TODO
│
├── Requests/
│   ├── StartLaunchRequest.cs                ✅ DONE
│   ├── FinishLaunchRequest.cs               ✅ DONE
│   ├── UpdateLaunchRequest.cs               ✅ DONE
│   ├── StartSuiteRequest.cs                 ✅ DONE
│   ├── FinishSuiteRequest.cs                ✅ DONE
│   ├── StartTestRunRequest.cs               ✅ DONE
│   ├── FinishTestRunRequest.cs              ✅ DONE
│   └── CreateLogItemRequest.cs              ✅ DONE
│
├── Responses/
│   ├── LaunchCreatedResponse.cs             ✅ TODO
│   ├── LaunchResponse.cs                    ✅ TODO
│   ├── SuiteCreatedResponse.cs              ✅ TODO
│   ├── SuiteResponse.cs                     ✅ TODO
│   ├── TestRunCreatedResponse.cs            ✅ TODO
│   ├── TestRunResponse.cs                   ✅ TODO
│   └── MessageResponse.cs                   ✅ TODO
│
├── Models/
│   ├── LaunchStatus.cs                      ✅ DONE
│   ├── SuiteStatus.cs                       ✅ DONE
│   ├── TestRunStatus.cs                     ✅ DONE
│   ├── TestItemType.cs                      ✅ DONE
│   └── LogLevel.cs                          ✅ DONE
│
└── Extensibility/
    ├── ILaunchExtension.cs                  ✅ TODO
    ├── ISuiteExtension.cs                   ✅ TODO
    ├── ITestRunExtension.cs                 ✅ TODO
    └── ExtensionManager.cs                  ✅ TODO
```

### Agenix.PlaywrightGrid.Client

```
Agenix.PlaywrightGrid.Client/
├── Abstractions/
│   ├── IClientService.cs                    ✅ TODO
│   └── Requests/
│       ├── ILaunchResource.cs               ✅ TODO
│       ├── ISuiteResource.cs                ✅ TODO
│       ├── ITestRunResource.cs              ✅ TODO
│       └── ILogItemResource.cs              ✅ TODO
│
├── Resources/
│   ├── LaunchResource.cs                    ✅ TODO
│   ├── SuiteResource.cs                     ✅ TODO
│   ├── TestRunResource.cs                   ✅ TODO
│   └── LogItemResource.cs                   ✅ TODO
│
├── Extensions/
│   └── ServiceCollectionExtensions.cs       ✅ TODO
│
├── Service.cs                                ✅ TODO
└── ServiceException.cs                       ✅ TODO
```

## Usage Examples

### Basic Usage

```csharp
using Agenix.PlaywrightGrid.Client;
using Agenix.PlaywrightGrid.Shared.Requests;

// Create service
var service = new Service(
    new Uri("https://grid.example.com"),
    projectKey: "MyProject",
    apiKey: "my-api-key"
);

// Start launch
var launch = await service.Launch.StartAsync(new StartLaunchRequest
{
    Name = "Nightly Tests",
    Attributes = new[] { "env:UAT", "build:1.2.3" }
});

// Start suite
var suite = await service.Suite.StartAsync(launch.Id, new StartSuiteRequest
{
    Name = "UserAuthentication.feature"
});

// Start test run (automatically borrows browser!)
var testRun = await service.TestRun.StartAsync(suite.Id, new StartTestRunRequest
{
    LabelKey = "MyApp:Chromium:UAT",
    RunName = "Login with valid credentials"
});

// Use browser
Console.WriteLine($"Connect to: {testRun.WebSocketEndpoint}");
var playwright = await Playwright.CreateAsync();
var browser = await playwright.Chromium.ConnectOverCDPAsync(testRun.WebSocketEndpoint);

// ... run tests ...

// Finish test run (automatically returns browser!)
await service.TestRun.FinishAsync(testRun.RunId, new FinishTestRunRequest
{
    Status = TestRunStatus.Passed
});

await service.Suite.FinishAsync(suite.Id, new FinishSuiteRequest());
await service.Launch.FinishAsync(launch.Id, new FinishLaunchRequest());
```

### Using Dependency Injection

```csharp
// Startup.cs
services.AddPlaywrightGridClient(options =>
{
    options.HubUrl = "https://grid.example.com";
    options.ProjectKey = "MyProject";
    options.ApiKey = Configuration["ApiKey"];
});

// Test class
public class MyTests
{
    private readonly IClientService _gridService;

    public MyTests(IClientService gridService)
    {
        _gridService = gridService;
    }

    [Test]
    public async Task RunTest()
    {
        var launch = await _gridService.Launch.StartAsync(...);
        // ...
    }
}
```

## Key Design Decisions

### 1. Merged Browser Lifecycle

- `TestRun.StartAsync()` automatically borrows browser from grid
- Response includes `WebSocketEndpoint` and browser session details
- `TestRun.FinishAsync()` automatically returns browser
- **Atomic operation**: If borrow fails, no test run is created

### 2. Suite Layer

- Explicit middle layer between Launch and TestRun
- Represents feature files (Reqnroll) or test classes (NUnit/XUnit)
- Supports nested suites via `ParentParentItemId`

### 3. ReportPortal-Inspired Patterns

- Reporter pattern for hierarchical reporting
- Context pattern for thread-safe access
- Multi-provider configuration system
- Shared/Client library split

## Implementation Status

### Phase 1: Foundation ✅ IN PROGRESS

- [x] Project files created
- [x] Core model enums (LaunchStatus, TestRunStatus, etc.)
- [x] Request models (Start/Finish/Update)
- [ ] Response models
- [ ] Reporter interfaces
- [ ] Configuration system

### Phase 2: HTTP Client 🔜 NEXT

- [ ] IClientService and Service class
- [ ] Resource implementations (Launch, Suite, TestRun, LogItem)
- [ ] HTTP client factory
- [ ] DI extensions
- [ ] Error handling and retries

### Phase 3: Reporter Implementations 🔜 PLANNED

- [ ] LaunchReporter, SuiteReporter, TestRunReporter
- [ ] Context implementations
- [ ] Extension points

### Phase 4: Testing & Documentation 🔜 PLANNED

- [ ] Unit tests for all components
- [ ] Integration tests with mock backend
- [ ] API documentation
- [ ] Usage examples

## Next Steps

1. **Complete Response Models** - Define all response DTOs
2. **Implement Client Service** - Create Service class and Resources
3. **Backend API Updates** - Add suite endpoints to hub
4. **Database Migration** - Create suites table
5. **Reqnroll Plugin** - Create plugin using these libraries

## Backend Requirements

The hub backend needs the following new endpoints:

```
POST   /api/launches                          - Create launch
GET    /api/launches/{id}                     - Get launch details
PUT    /api/launches/{id}/finish              - Finish launch
PUT    /api/launches/{id}                     - Update launch

POST   /api/launches/{launchId}/suites        - Create suite
GET    /api/suites/{id}                       - Get suite details
PUT    /api/suites/{id}/finish                - Finish suite
GET    /api/launches/{launchId}/suites        - List suites in launch

POST   /api/suites/{suiteId}/runs             - Create test run + borrow browser
GET    /api/runs/{runId}                      - Get test run details
PUT    /api/runs/{runId}/finish               - Finish test run + return browser
GET    /api/suites/{suiteId}/runs             - List test runs in suite

POST   /api/logs                              - Create log item
GET    /api/logs                              - Query logs
```

## Contributing

When implementing new files, follow these guidelines:

1. **Add license headers** to all source files
2. **Use XML documentation** for public APIs
3. **Follow naming conventions** from ReportPortal
4. **Write unit tests** for all components
5. **Update this README** as you progress

## Questions & Feedback

For questions or feedback, please open an issue on GitHub.

---

**Status**: 🚧 Under Active Development
**Last Updated**: 2025-01-15
**Completion**: ~20% (Foundation established, core models complete)
