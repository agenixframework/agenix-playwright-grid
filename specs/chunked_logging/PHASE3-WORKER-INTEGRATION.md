# Phase 3: Worker Service Integration - Chunked Logging

## Overview

Phase 3 integrates chunked logging into Worker nodes, providing operation-scoped logging for browser lifecycle management, worker registration, and health monitoring.

## Status: 📋 PLANNED

**Dependencies**: ✅ Phase 1 Complete, ⏳ Phase 2 In Progress
**Timeline**: 1-2 hours
**Impact**: Worker service only

---

## Goals

1. **Browser Lifecycle Tracking** - Startup, connection, cleanup as discrete operations
2. **Worker Registration** - Registration attempts logged with retry tracking
3. **Health Monitoring** - Health check ticks as operations
4. **Error Classification** - Playwright failures, Hub communication errors, etc.

---

## Implementation Plan

### 3.1 - PoolManager Integration

#### File: `worker/Services/PoolManager.cs` (MODIFY)

**Update browser startup operations**:

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

public async Task<BrowserInstance> StartBrowserAsync(string poolKey, CancellationToken ct)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(PoolManager));

    using var op = chunkedLogger.BeginOperation(
        "StartBrowser",
        inputs: new Dictionary<string, object>
        {
            ["poolKey"] = poolKey,
            ["browserType"] = GetBrowserType(poolKey),
            ["workerNodeId"] = _workerNodeId
        });

    try
    {
        chunkedLogger.LogMilestone(
            EventCodes.Worker.BrowserStartupRequested,
            "poolKey={PoolKey}",
            poolKey);

        // Launch Playwright browser
        var browser = await LaunchPlaywrightAsync(poolKey, ct);

        chunkedLogger.LogMilestone(
            EventCodes.Worker.PlaywrightLaunched,
            "browserType={BrowserType}",
            GetBrowserType(poolKey));

        // Get CDP endpoint
        var endpoint = browser.GetWebSocketEndpoint();

        chunkedLogger.LogMilestone(
            EventCodes.Worker.BrowserConnected,
            "endpoint={Endpoint}",
            endpoint);

        var browserId = Guid.NewGuid().ToString();
        var instance = new BrowserInstance(browserId, browser, endpoint, poolKey);

        var outputs = new Dictionary<string, object>
        {
            ["browserId"] = browserId,
            ["endpoint"] = endpoint
        };

        ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);

        return instance;
    }
    catch (PlaywrightException ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(
            ex,
            ErrorType.DependencyFailure,
            DependencyName.Playwright);
        throw;
    }
    catch (TimeoutException ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(
            ex,
            ErrorType.Timeout,
            DependencyName.Playwright);
        throw;
    }
}
```

**Update browser cleanup**:

```csharp
public async Task CleanupBrowserAsync(string browserId, CancellationToken ct)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(PoolManager));

    using var op = chunkedLogger.BeginOperation(
        "CleanupBrowser",
        inputs: new Dictionary<string, object> { ["browserId"] = browserId });

    try
    {
        chunkedLogger.LogMilestone(
            EventCodes.Worker.CleanupRequested,
            "browserId={BrowserId}",
            browserId);

        var browser = _activeBrowsers[browserId];
        await browser.CloseAsync();

        chunkedLogger.LogMilestone(
            EventCodes.Worker.BrowserClosed,
            "browserId={BrowserId}",
            browserId);

        _activeBrowsers.TryRemove(browserId, out _);
    }
    catch (Exception ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(
            ex,
            ErrorType.Unexpected);
        throw;
    }
}
```

**Expected Output**:
```
╔═ Operation: StartBrowser  OperationId=abc...
║ Start: 2025-12-23T10:30:00.123Z
║ Inputs: poolKey=AppA:Chromium:UAT browserType=Chromium workerNodeId=worker-1
║
║ [INF][WRK01] Browser startup requested - poolKey=AppA:Chromium:UAT
║ [INF][WRK02] Playwright launched - browserType=Chromium
║ [INF][WRK03] Browser connected - endpoint=ws://localhost:9222/devtools/browser/abc123
║
╚═ End: SUCCESS  Duration=2.5s  browserId=br_456 endpoint=ws://...  KeyEvents=[WRK01,WRK02,WRK03]
```

---

### 3.2 - HubHttpClient Integration

#### File: `worker/Infrastructure/Adapters/HubHttpClient.cs` (MODIFY)

**Update registration method**:

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

public async Task<bool> RegisterWithHubAsync(CancellationToken ct)
{
    var chunkedLogger = new ChunkedLogger(_logger, nameof(HubHttpClient));

    using var op = chunkedLogger.BeginOperation(
        "RegisterWorker",
        inputs: new Dictionary<string, object>
        {
            ["workerNodeId"] = _workerNodeId,
            ["hubUrl"] = _hubUrl
        });

    try
    {
        chunkedLogger.LogMilestone(
            EventCodes.Worker.RegistrationStarted,
            "workerNodeId={WorkerNodeId} hubUrl={HubUrl}",
            _workerNodeId, _hubUrl);

        var payload = BuildRegistrationPayload();

        chunkedLogger.LogDebug(
            EventCodes.Worker.RegistrationSent,
            "pools={PoolCount}",
            payload.Pools.Count);

        var response = await _httpClient.PostAsJsonAsync(
            "/api/workers/register",
            payload,
            ct);

        response.EnsureSuccessStatusCode();

        chunkedLogger.LogMilestone(
            EventCodes.Worker.RegistrationConfirmed,
            "statusCode={StatusCode}",
            (int)response.StatusCode);

        var outputs = new Dictionary<string, object>
        {
            ["statusCode"] = (int)response.StatusCode
        };

        ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);

        return true;
    }
    catch (HttpRequestException ex)
    {
        ((ChunkedLogger.OperationScope)op).Fail(
            ex,
            ErrorType.DependencyFailure,
            DependencyName.Hub);

        _logger.LogWarning("Registration failed, will retry in {Interval}s", _retryInterval);
        return false;
    }
}
```

**Expected Output**:
```
╔═ Operation: RegisterWorker  OperationId=def...
║ Start: 2025-12-23T10:35:00.000Z
║ Inputs: workerNodeId=worker-1 hubUrl=http://localhost:5100
║
║ [INF][WRK20] Worker registration started - workerNodeId=worker-1 hubUrl=http://localhost:5100
║ [DBG][WRK21] Registration sent - pools=3
║ [INF][WRK22] Worker registration confirmed - statusCode=200
║
╚═ End: SUCCESS  Duration=125ms  statusCode=200  KeyEvents=[WRK20,WRK21,WRK22]
```

---

### 3.3 - Health Check Integration

#### File: `worker/Services/HealthCheckService.cs` (NEW or MODIFY)

```csharp
using Agenix.PlaywrightGrid.Shared.Logging;

public class HealthCheckService : BackgroundService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IConfiguration _config;
    private readonly HubHttpClient _hubClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(
            _config.GetValue("AGENIX_WORKER_HEALTH_CHECK_INTERVAL_SECONDS", 30));

        var chunkedLogger = new ChunkedLogger(_logger, nameof(HealthCheckService));

        while (!stoppingToken.IsCancellationRequested)
        {
            using var op = chunkedLogger.BeginOperation("HealthCheck");

            try
            {
                chunkedLogger.LogMilestone(
                    EventCodes.Worker.HealthCheckStarted,
                    "interval={Interval}s",
                    interval.TotalSeconds);

                // Check browser pool health
                var poolStatus = await CheckPoolHealthAsync();

                // Report to hub
                var response = await _hubClient.SendHealthReportAsync(poolStatus, stoppingToken);

                chunkedLogger.LogMilestone(
                    EventCodes.Worker.HealthCheckCompleted,
                    "activeBrowsers={Active} totalCapacity={Capacity}",
                    poolStatus.ActiveBrowsers, poolStatus.TotalCapacity);

                var outputs = new Dictionary<string, object>
                {
                    ["activeBrowsers"] = poolStatus.ActiveBrowsers,
                    ["totalCapacity"] = poolStatus.TotalCapacity
                };

                ((ChunkedLogger.OperationScope)op).SetOutputs(outputs);
            }
            catch (Exception ex)
            {
                ((ChunkedLogger.OperationScope)op).Fail(
                    ex,
                    ErrorType.DependencyFailure,
                    DependencyName.Hub);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
```

**Expected Output**:
```
╔═ Operation: HealthCheck  OperationId=ghi...
║ Start: 2025-12-23T10:40:00.000Z
║
║ [INF][WRK30] Health check started - interval=30s
║ [INF][WRK31] Health check completed - activeBrowsers=5 totalCapacity=10
║
╚═ End: SUCCESS  Duration=45ms  activeBrowsers=5 totalCapacity=10  KeyEvents=[WRK30,WRK31]
```

---

### 3.4 - Serilog Configuration

#### File: `worker/appsettings.json` (MODIFY)

```json
{
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Sinks.File",
      "Agenix.PlaywrightGrid.Shared"
    ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "ChunkedConsole",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
          "maxEventsPerChunk": 1000,
          "maxAgeSeconds": 60
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "/tmp/pg-worker-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithMachineName",
      "WithThreadId",
      "WithOperationContext",
      "WithEventCode",
      "WithCodeContext"
    ]
  }
}
```

---

## Testing Phase 3

### Manual Testing

```bash
# Start worker with chunked logging
export AGENIX_LOGGING_CHUNKED_ENABLED=true
dotnet run --project worker
```

**Expected Console Output** (on startup):

```
╔═ Operation: RegisterWorker  OperationId=abc...
║ [INF][WRK20] Worker registration started - workerNodeId=worker-1
║ [DBG][WRK21] Registration sent - pools=3
║ [INF][WRK22] Worker registration confirmed - statusCode=200
╚═ End: SUCCESS  Duration=125ms  KeyEvents=[WRK20,WRK21,WRK22]

╔═ Operation: StartBrowser  OperationId=def...
║ [INF][WRK01] Browser startup requested - poolKey=AppA:Chromium:UAT
║ [INF][WRK02] Playwright launched - browserType=Chromium
║ [INF][WRK03] Browser connected - endpoint=ws://localhost:9222/...
╚═ End: SUCCESS  Duration=2.5s  KeyEvents=[WRK01,WRK02,WRK03]
```

---

## Success Criteria

- [ ] Browser startup logged as discrete operations
- [ ] Worker registration attempts tracked with event codes
- [ ] Health checks logged with status summary
- [ ] Playwright errors classified as DependencyFailure
- [ ] Hub communication failures classified correctly
- [ ] All operations have OperationId and duration
- [ ] KeyEvents summary present in chunk footer
- [ ] No performance degradation

---

**Status**: 📋 PLANNED
**Estimated Effort**: 1-2 hours
**Dependencies**: Phase 1 ✅, Phase 2 ⏳
