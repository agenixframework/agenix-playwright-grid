# Phase 4: Dashboard Real-time Updates - Plan

## Goal
Maintain real-time dashboard updates when using event-driven architecture. Ingestion service publishes SignalR events after batch writes.

## Architecture
```
Ingestion Service
  ├─ Consume events from RabbitMQ
  ├─ Batch write to PostgreSQL
  └─ Publish to SignalR hub → Dashboard updates
```

## Implementation (4 files)

### 1. Add Package: `ingestion/IngestionService.csproj`
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.0" />
```

### 2. Create: `ingestion/Infrastructure/SignalRNotifier.cs` (80 lines)
```csharp
public sealed class SignalRNotifier : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public async Task NotifyTestItemAsync(Guid itemId, Guid launchId)
    public async Task NotifyCommandLogAsync(string runId)
    public async Task NotifyLogItemAsync(Guid itemId, Guid launchId)
}
```

### 3. Modify: `ingestion/Infrastructure/PostgresBatchWriter.cs`
Add SignalR calls after each COPY:
```csharp
await writer.CompleteAsync();
await _notifier.NotifyTestItemBatchAsync(events);
```

### 4. Modify: `ingestion/Services/IngestionServiceRunner.cs`
Register SignalR notifier:
```csharp
builder.Services.AddSingleton<SignalRNotifier>();
```

## Config
```bash
SIGNALR_HUB_URL=http://hub:5001/hubs/launches  # Hub URL
ENABLE_SIGNALR_NOTIFICATIONS=true              # Toggle
```

## Benefits
- Maintains current dashboard real-time UX
- No breaking changes to hub
- Decoupled write path with async notifications
