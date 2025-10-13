# Reporting Developer Guide (under Review)

## Overview

This guide demonstrates how to report test results to Playwright Grid using HTTP API requests, following ReportPortal's proven reporting model with integrated browser lifecycle management.

## Sample Test Structure

```
(Launch) CI Build #123
  (Suite) Login Feature
    (Test) Login with valid credentials [Browser Session: chrome-abc123]
      (Step) Navigate to login page
      (Step) Enter credentials
        (Nested Step) Fill username field
        (Nested Step) Fill password field
      (Step) Click submit button
      (Step) Verify dashboard loaded
```

## Preconditions

### Base URL
```
https://your-playwright-grid-hub.com
```

### API Version
```
v1
```

### Project Key
Your project identifier (e.g., `myapp`, `ecommerce-frontend`)

## Retrieving API Token

### Option 1: UI Profile
1. Navigate to your Dashboard user profile
2. Find your API key in the user settings
3. Use this key in the `Authorization` header

### Option 2: Admin-Generated API Key (Not available yet)
Contact your Playwright Grid administrator to generate a project-specific API key.

### Using the API Token

Include the token in all requests:

```http
Authorization: Bearer <your-api-token>
Content-Type: application/json
```

## Reporting Workflow

### 1. Start Launch

**Endpoint:**
```
POST /api/v1/{projectKey}/launches
```

**Request Body:**
```json
{
  "name": "CI Build #123",
  "description": "Regression tests for release 2.0",
  "startTime": "2025-01-24T10:00:00.000Z",
  "attributes": [
    {
      "key": "build",
      "value": "2.0.123"
    },
    {
      "key": "environment",
      "value": "staging"
    },
    {
      "value": "smoke"
    }
  ]
}
```

**Response:**
```json
{
  "id": "96d1bc02-6a3f-451e-b706-719149d51ce4"
}
```

**Field Descriptions:**
- `name` (required): Launch name displayed in the dashboard
- `description` (optional): Detailed description supporting Markdown
- `startTime` (required): ISO 8601 timestamp
- `attributes` (optional): Key-value pairs for filtering and grouping
  - Keys are optional (use for structured metadata)
  - Values are required (use for tags)

---

### 2. Start Root (Suite) Item

**Endpoint:**
```
POST /api/v1/{projectKey}/suites
```

**Request Body:**
```json
{
  "launchId": "96d1bc02-6a3f-451e-b706-719149d51ce4",
  "name": "Login Feature",
  "description": "Tests for user authentication",
  "startTime": "2025-01-24T10:01:00.000Z",
  "attributes": [
    {
      "key": "feature",
      "value": "authentication"
    }
  ]
}
```

**Response:**
```json
{
  "id": "a7e2cd03-7b4g-562f-c817-820250e62df5"
}
```

**Field Descriptions:**
- `launchId` (required): UUID from launch start response
- `name` (required): Suite name (e.g., Feature file, Test class)
- `description` (optional): Suite description
- `startTime` (required): ISO 8601 timestamp
- `attributes` (optional): Suite-specific metadata

---

### 3. Start Child (Test) Item with Browser Borrowing

**Endpoint:**
```
POST /api/v1/{projectKey}/test-items
```

**Request Body:**
```json
{
  "launchId": "96d1bc02-6a3f-451e-b706-719149d51ce4",
  "suiteId": "a7e2cd03-7b4g-562f-c817-820250e62df5",
  "name": "Login with valid credentials",
  "type": "test",
  "description": "Verify user can log in with correct username and password",
  "startTime": "2025-01-24T10:02:00.000Z",
  "labelKey": "myapp:chromium:staging",
  "attributes": [
    {
      "key": "priority",
      "value": "high"
    },
    {
      "key": "author",
      "value": "john.doe"
    }
  ]
}
```

**Response:**
```json
{
  "id": "b8f3de04-8c5h-673g-d928-931361f73eg6",
  "browserId": "chrome-abc123",
  "websocketEndpoint": "wss://worker-node-1:3001/browser/chrome-abc123",
  "browserType": "chromium",
  "workerNodeId": "worker-node-1",
  "sessionStatus": "Running"
}
```

**Field Descriptions:**
- `launchId` (required): UUID from launch start response
- `suiteId` (required): UUID from suite start response
- `name` (required): Test name
- `type` (required): Item type - `test`, `scenario`, `before_class`, `after_method`, etc.
- `description` (optional): Test description
- `startTime` (required): ISO 8601 timestamp
- `labelKey` (required): Browser pool label to borrow from (format: `{project}:{browser}:{environment}`)
- `attributes` (optional): Test-specific metadata

**Browser Session Integration:**
- The backend automatically borrows a browser from the pool based on `labelKey`
- The response includes browser connection details for the test runner
- `sessionStatus` transitions: `Queued` → `Running`

---

### 4. Start Child (Step) Item

**Endpoint:**
```
POST /api/v1/{projectKey}/test-items
```

**Request Body:**
```json
{
  "launchId": "96d1bc02-6a3f-451e-b706-719149d51ce4",
  "parentItemId": "b8f3de04-8c5h-673g-d928-931361f73eg6",
  "name": "Navigate to login page",
  "type": "step",
  "description": "Open https://myapp.com/login",
  "startTime": "2025-01-24T10:02:01.000Z"
}
```

**Response:**
```json
{
  "id": "c9g4ef05-9d6i-784h-e039-042472g84fh7"
}
```

**Field Descriptions:**
- `launchId` (required): UUID from launch start response
- `parentItemId` (required): UUID of parent test item
- `name` (required): Step name
- `type` (required): `step`
- `description` (optional): Step details
- `startTime` (required): ISO 8601 timestamp
- `hasStats` (optional, default: `true`): Set to `false` for nested steps that shouldn't affect pass/fail counts

---

### 5. Start Child (Nested Step) Item

**Endpoint:**
```
POST /api/v1/{projectKey}/test-items
```

**Request Body:**
```json
{
  "launchId": "96d1bc02-6a3f-451e-b706-719149d51ce4",
  "parentItemId": "c9g4ef05-9d6i-784h-e039-042472g84fh7",
  "name": "Fill username field",
  "type": "step",
  "hasStats": false,
  "description": "Enter 'admin@example.com'",
  "startTime": "2025-01-24T10:02:02.000Z"
}
```

**Response:**
```json
{
  "id": "d0h5fg06-0e7j-895i-f140-153583h95gi8"
}
```

**Field Descriptions:**
- `hasStats`: Set to `false` for nested steps that are purely informational
- Nested steps don't contribute to statistics but provide detailed execution logs

---

### 6. Finish Child (Nested Step) Item

**Endpoint:**
```
PUT /api/v1/{projectKey}/test-items/{itemId}/finish
```

**Request Body:**
```json
{
  "endTime": "2025-01-24T10:02:03.000Z",
  "status": "Passed"
}
```

**Response:**
```json
{
  "message": "Test item finished successfully"
}
```

**Status Values:**
- `Passed`: Test/step passed
- `Failed`: Test/step failed (will be marked "To Investigate")
- `Skipped`: Test/step was skipped
- `Timedout`: Test/step exceeded timeout
- `Cancelled`: Test/step was cancelled by user
- `Errored`: Test/step encountered an error

---

### 7. Finish Child (Step) Item

**Endpoint:**
```
PUT /api/v1/{projectKey}/test-items/{itemId}/finish
```

**Request Body:**
```json
{
  "endTime": "2025-01-24T10:02:10.000Z",
  "status": "Passed"
}
```

---

### 8. Finish Parent (Test) Item with Browser Return

**Endpoint:**
```
PUT /api/v1/{projectKey}/test-items/{itemId}/finish
```

**Request Body:**
```json
{
  "endTime": "2025-01-24T10:02:15.000Z",
  "status": "Passed",
  "testDetails": {
    "testTitle": "Login with valid credentials",
    "testFile": "tests/auth/login.spec.ts",
    "lineNumber": 42,
    "retryAttempt": 0,
    "tags": ["smoke", "critical"]
  }
}
```

**Response:**
```json
{
  "message": "Test item finished successfully",
  "sessionStatus": "Completed"
}
```

**Browser Session Integration:**
- The backend automatically returns the browser to the pool
- `sessionStatus` transitions: `Running` → `Completed`
- Browser becomes available for other tests

**Optional Test Details:**
- `testTitle`: Full test case title
- `testFile`: Source file path
- `lineNumber`: Line number in source file
- `errorMessage`: Error message (if status is Failed/Errored)
- `errorStack`: Full stack trace (if status is Failed/Errored)
- `retryAttempt`: Retry number (0-based)
- `tags`: Test tags for filtering

---

### 9. Save Single Log Without Attachment

**Endpoint:**
```
POST /api/v1/{projectKey}/test-items/{itemId}/logs
```

**Request Body:**
```json
{
  "time": "2025-01-24T10:02:05.000Z",
  "level": "INFO",
  "message": "Navigating to https://myapp.com/login"
}
```

**Response:**
```json
{
  "id": "e1i6gh07-1f8k-906j-g251-264694i06hj9"
}
```

**Log Levels:**
- `TRACE`: Detailed diagnostic logs
- `DEBUG`: Debug information
- `INFO`: Informational messages
- `WARN`: Warning messages
- `ERROR`: Error messages
- `FATAL`: Fatal error messages

---

### 10. Save Log With Attachment

**Endpoint:**
```
POST /api/v1/{projectKey}/test-items/{itemId}/logs
```

**Request (multipart/form-data):**
```http
Content-Type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW

------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="log"

{
  "time": "2025-01-24T10:02:08.000Z",
  "level": "INFO",
  "message": "Screenshot after login failure"
}
------WebKitFormBoundary7MA4YWxkTrZu0gW
Content-Disposition: form-data; name="file"; filename="screenshot.png"
Content-Type: image/png

<binary data>
------WebKitFormBoundary7MA4YWxkTrZu0gW--
```

**Response:**
```json
{
  "id": "f2j7hi08-2g9l-017k-h362-375705j17ik0"
}
```

---

### 11. Batch Save Logs

**Endpoint:**
```
POST /api/v1/{projectKey}/test-items/logs/batch
```

**Request Body:**
```json
[
  {
    "itemId": "b8f3de04-8c5h-673g-d928-931361f73eg6",
    "time": "2025-01-24T10:02:05.000Z",
    "level": "INFO",
    "message": "Test started"
  },
  {
    "itemId": "c9g4ef05-9d6i-784h-e039-042472g84fh7",
    "time": "2025-01-24T10:02:06.000Z",
    "level": "DEBUG",
    "message": "Waiting for page load"
  },
  {
    "itemId": "c9g4ef05-9d6i-784h-e039-042472g84fh7",
    "time": "2025-01-24T10:02:09.000Z",
    "level": "INFO",
    "message": "Page loaded successfully"
  }
]
```

**Response:**
```json
{
  "message": "Logs saved successfully",
  "count": 3
}
```

---

### 12. Save Launch Log

**Endpoint:**
```
POST /api/v1/{projectKey}/launches/{launchId}/logs
```

**Request Body:**
```json
{
  "time": "2025-01-24T10:00:05.000Z",
  "level": "INFO",
  "message": "Launch started with 120 tests"
}
```

**Response:**
```json
{
  "id": "g3k8ij09-3h0m-128l-i473-486816k28jl1"
}
```

---

### 13. Finish Root (Suite) Item

**Endpoint:**
```
PUT /api/v1/{projectKey}/suites/{suiteId}/finish
```

**Request Body:**
```json
{
  "endTime": "2025-01-24T10:15:00.000Z"
}
```

**Response:**
```json
{
  "message": "Suite finished successfully"
}
```

**Note:** Suite status is calculated automatically based on child test items.

---

### 14. Finish Launch

**Endpoint:**
```
PUT /api/v1/{projectKey}/launches/{launchId}/finish
```

**Request Body:**
```json
{
  "endTime": "2025-01-24T11:00:00.000Z"
}
```

**Response:**
```json
{
  "message": "Launch finished successfully",
  "link": "https://your-playwright-grid-hub.com/myapp/launches/96d1bc02-6a3f-451e-b706-719149d51ce4"
}
```

---

## Complete Example: Node.js/TypeScript

```typescript
import axios from 'axios';

const API_BASE = 'https://your-playwright-grid-hub.com/api/v1';
const PROJECT_KEY = 'myapp';
const API_TOKEN = 'your-api-token-here';

const client = axios.create({
  baseURL: `${API_BASE}/${PROJECT_KEY}`,
  headers: {
    'Authorization': `Bearer ${API_TOKEN}`,
    'Content-Type': 'application/json'
  }
});

async function reportTest() {
  // 1. Start Launch
  const launchRes = await client.post('/launches', {
    name: 'CI Build #123',
    description: 'Regression tests',
    startTime: new Date().toISOString(),
    attributes: [
      { key: 'build', value: '2.0.123' },
      { key: 'environment', value: 'staging' }
    ]
  });
  const launchId = launchRes.data.id;

  // 2. Start Suite
  const suiteRes = await client.post('/suites', {
    launchId,
    name: 'Login Feature',
    startTime: new Date().toISOString()
  });
  const suiteId = suiteRes.data.id;

  // 3. Start Test (borrow browser)
  const testRes = await client.post('/test-items', {
    launchId,
    suiteId,
    name: 'Login with valid credentials',
    type: 'test',
    labelKey: 'myapp:chromium:staging',
    startTime: new Date().toISOString()
  });
  const testId = testRes.data.id;
  const { websocketEndpoint, browserId } = testRes.data;

  console.log(`Browser borrowed: ${browserId}`);
  console.log(`Connect to: ${websocketEndpoint}`);

  // 4. Start Step
  const stepRes = await client.post('/test-items', {
    launchId,
    parentItemId: testId,
    name: 'Navigate to login page',
    type: 'step',
    startTime: new Date().toISOString()
  });
  const stepId = stepRes.data.id;

  // 5. Save Log
  await client.post(`/test-items/${stepId}/logs`, {
    time: new Date().toISOString(),
    level: 'INFO',
    message: 'Opening https://myapp.com/login'
  });

  // Simulate test execution...
  await new Promise(resolve => setTimeout(resolve, 2000));

  // 6. Finish Step
  await client.put(`/test-items/${stepId}/finish`, {
    endTime: new Date().toISOString(),
    status: 'Passed'
  });

  // 7. Finish Test (return browser)
  await client.put(`/test-items/${testId}/finish`, {
    endTime: new Date().toISOString(),
    status: 'Passed',
    testDetails: {
      testTitle: 'Login with valid credentials',
      testFile: 'tests/auth/login.spec.ts',
      lineNumber: 42
    }
  });

  console.log('Browser returned to pool');

  // 8. Finish Suite
  await client.put(`/suites/${suiteId}/finish`, {
    endTime: new Date().toISOString()
  });

  // 9. Finish Launch
  const finishRes = await client.put(`/launches/${launchId}/finish`, {
    endTime: new Date().toISOString()
  });

  console.log(`Launch finished: ${finishRes.data.link}`);
}

reportTest().catch(console.error);
```

---

## Complete Example: C# (.NET)

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class ReportingExample
{
    private static readonly HttpClient client = new HttpClient
    {
        BaseAddress = new Uri("https://your-playwright-grid-hub.com/api/v1/myapp/"),
        DefaultRequestHeaders = {
            { "Authorization", "Bearer your-api-token-here" }
        }
    };

    static async Task Main()
    {
        // 1. Start Launch
        var launchData = new {
            name = "CI Build #123",
            description = "Regression tests",
            startTime = DateTime.UtcNow.ToString("o"),
            attributes = new[] {
                new { key = "build", value = "2.0.123" },
                new { key = "environment", value = "staging" }
            }
        };
        var launchRes = await PostJson("launches", launchData);
        var launchId = launchRes.GetProperty("id").GetString();

        // 2. Start Suite
        var suiteData = new {
            launchId,
            name = "Login Feature",
            startTime = DateTime.UtcNow.ToString("o")
        };
        var suiteRes = await PostJson("suites", suiteData);
        var suiteId = suiteRes.GetProperty("id").GetString();

        // 3. Start Test (borrow browser)
        var testData = new {
            launchId,
            suiteId,
            name = "Login with valid credentials",
            type = "test",
            labelKey = "myapp:chromium:staging",
            startTime = DateTime.UtcNow.ToString("o")
        };
        var testRes = await PostJson("test-items", testData);
        var testId = testRes.GetProperty("id").GetString();
        var browserId = testRes.GetProperty("browserId").GetString();
        var websocketEndpoint = testRes.GetProperty("websocketEndpoint").GetString();

        Console.WriteLine($"Browser borrowed: {browserId}");
        Console.WriteLine($"Connect to: {websocketEndpoint}");

        // 4. Start Step
        var stepData = new {
            launchId,
            parentItemId = testId,
            name = "Navigate to login page",
            type = "step",
            startTime = DateTime.UtcNow.ToString("o")
        };
        var stepRes = await PostJson("test-items", stepData);
        var stepId = stepRes.GetProperty("id").GetString();

        // 5. Save Log
        var logData = new {
            time = DateTime.UtcNow.ToString("o"),
            level = "INFO",
            message = "Opening https://myapp.com/login"
        };
        await PostJson($"test-items/{stepId}/logs", logData);

        // Simulate test execution...
        await Task.Delay(2000);

        // 6. Finish Step
        await PutJson($"test-items/{stepId}/finish", new {
            endTime = DateTime.UtcNow.ToString("o"),
            status = "Passed"
        });

        // 7. Finish Test (return browser)
        await PutJson($"test-items/{testId}/finish", new {
            endTime = DateTime.UtcNow.ToString("o"),
            status = "Passed",
            testDetails = new {
                testTitle = "Login with valid credentials",
                testFile = "tests/auth/login.spec.ts",
                lineNumber = 42
            }
        });

        Console.WriteLine("Browser returned to pool");

        // 8. Finish Suite
        await PutJson($"suites/{suiteId}/finish", new {
            endTime = DateTime.UtcNow.ToString("o")
        });

        // 9. Finish Launch
        var finishRes = await PutJson($"launches/{launchId}/finish", new {
            endTime = DateTime.UtcNow.ToString("o")
        });
        var link = finishRes.GetProperty("link").GetString();

        Console.WriteLine($"Launch finished: {link}");
    }

    static async Task<JsonElement> PostJson(string path, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(responseBody).RootElement;
    }

    static async Task<JsonElement> PutJson(string path, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PutAsync(path, content);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(responseBody).RootElement;
    }
}
```

---

## Browser Lifecycle States

### Session Status (Browser Pool)
- `Queued`: Browser requested but not yet allocated
- `Running`: Browser allocated and in use by test
- `Completed`: Test finished, browser returned to pool
- `Stopped`: Browser manually stopped by user
- `AutoStopped`: Browser auto-stopped due to inactivity timeout
- `Aborted`: Browser session aborted due to error

### Test Status (Test Execution)
- `InProgress`: Test currently running
- `Passed`: Test passed successfully
- `Failed`: Test failed (assertion failure)
- `Skipped`: Test was skipped
- `Timedout`: Test exceeded timeout
- `Cancelled`: Test cancelled by user
- `Errored`: Test encountered an error (exception, crash, etc.)

**Key Insight:** Browser lifecycle (`sessionStatus`) is independent of test outcome (`status`). A test can fail while the browser session completes normally.

---

## Best Practices

### 1. Always Return Browsers
Ensure every test item that borrows a browser calls the finish endpoint to return it to the pool.

### 2. Use Nested Steps for Detailed Reporting
Use `hasStats: false` for nested steps to provide detailed execution logs without affecting pass/fail statistics.

### 3. Batch Logs for Performance
When reporting multiple logs for the same test item, use the batch endpoint to reduce HTTP overhead.

### 4. Include Test Details on Failure
When finishing a failed test, include `errorMessage` and `errorStack` in the `testDetails` object.

### 5. Use Attributes for Filtering
Add relevant attributes (environment, browser, priority, etc.) to enable powerful filtering in the dashboard.

### 6. Set Descriptive Names
Use clear, descriptive names for launches, suites, and test items to make the dashboard easy to navigate.

### 7. Include Timestamps
Always include accurate `startTime` and `endTime` to enable duration tracking and timeline visualization.

### 8. Handle Errors Gracefully
If a test crashes before finishing, ensure you still call the finish endpoint (with `Errored` status) to release the browser.

---

## Error Handling

### Common Error Responses

**400 Bad Request:**
```json
{
  "error": "Invalid request body",
  "details": {
    "field": "startTime",
    "message": "startTime is required"
  }
}
```

**401 Unauthorized:**
```json
{
  "error": "Invalid or expired API token"
}
```

**404 Not Found:**
```json
{
  "error": "Launch not found",
  "launchId": "96d1bc02-6a3f-451e-b706-719149d51ce4"
}
```

**409 Conflict:**
```json
{
  "error": "Test item already finished",
  "testItemId": "b8f3de04-8c5h-673g-d928-931361f73eg6"
}
```

**503 Service Unavailable:**
```json
{
  "error": "No available browsers in pool",
  "labelKey": "myapp:chromium:staging"
}
```

---

## Advanced Features

### Parameterized Tests

Report parameterized tests with the `parameters` field:

```json
{
  "launchId": "...",
  "suiteId": "...",
  "name": "Login test",
  "type": "test",
  "labelKey": "myapp:chromium:staging",
  "startTime": "...",
  "parameters": [
    { "key": "username", "value": "admin@example.com" },
    { "key": "password", "value": "***" }
  ]
}
```

### Retry Tracking

Track test retries using the `retryAttempt` field:

```json
{
  "endTime": "...",
  "status": "Passed",
  "testDetails": {
    "retryAttempt": 2
  }
}
```

### Code References

Link test items to source code:

```json
{
  "launchId": "...",
  "suiteId": "...",
  "name": "Login test",
  "type": "test",
  "labelKey": "myapp:chromium:staging",
  "startTime": "...",
  "codeRef": "tests/auth/login.spec.ts:42"
}
```

### Test Item Types

Supported item types:
- `suite`: Test suite/feature container
- `story`: User story container (BDD)
- `test`: Test case/method
- `scenario`: BDD scenario
- `step`: Test step
- `before_suite`, `before_class`, `before_method`, `before_test`: Setup hooks
- `after_suite`, `after_class`, `after_method`, `after_test`: Teardown hooks

---

## Migration from Legacy Endpoints

If you're migrating from the old `/api/runs` endpoints to the new ReportPortal-style endpoints:

### Old (Deprecated)
```javascript
POST /api/v1/{projectKey}/runs
```

### New (Recommended)
```javascript
POST /api/v1/{projectKey}/test-items
```

The old endpoints will continue to work but are deprecated and will be removed in a future version.

---

*Document version: 1.0*
*Last updated: 2025-01-24*
*Based on ReportPortal Reporting Developers Guide*
