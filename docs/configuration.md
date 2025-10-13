# PlaywrightGrid Configuration

This document describes how to configure the Agenix PlaywrightGrid client using the `PlaywrightGrid.json` configuration file.

## Quick Start

### 1. Create PlaywrightGrid.json

```json
{
  "$schema": "./PlaywrightGrid.schema.json",
  "enabled": true,
  "server": {
    "url": "https://grid.example.com",
    "project": "my-project",
    "apiKey": "your-api-key-here"
  },
  "launch": {
    "name": "Regression Suite",
    "attributes": ["env:production"]
  },
  "testRun": {
    "defaultLabelKey": "MyApp:Chromium:UAT:US-East"
  }
}
```

### 2. Load Configuration

```csharp
using Agenix.PlaywrightGrid.Shared.Configuration;

var config = ConfigurationHelper.FromJsonFile();
var serverUrl = ConfigurationHelper.GetServerUrl(config);
```

## Label Key Requirement

**YES, `label_key` is REQUIRED** for each test run. It determines which browser pool to borrow from.

**Format**: `App:Browser:Environment:Region[:OS]`

**Options**:
1. Set default in config: `"testRun": { "defaultLabelKey": "..." }`
2. Override per-test in code
3. Set via environment variable

See [PlaywrightGrid.json](../PlaywrightGrid.json) for examples.
