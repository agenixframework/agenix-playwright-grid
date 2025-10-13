#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

namespace Agenix.PlaywrightGrid.Shared.Configuration;

/// <summary>
///     Well-known configuration keys for PlaywrightGrid client.
/// </summary>
public static class ConfigurationKeys
{
    // ========== Legacy Keys (Backward Compatibility) ==========

    /// <summary>
    ///     PlaywrightGrid hub base URL (e.g., "https://grid.example.com").
    ///     Legacy key. Prefer using Server:Url for new configurations.
    /// </summary>
    public const string HubUrl = "PlaywrightGrid:HubUrl";

    /// <summary>
    ///     Project key for organizing test results.
    ///     Legacy key. Prefer using Server:Project for new configurations.
    /// </summary>
    public const string ProjectKey = "PlaywrightGrid:ProjectKey";

    /// <summary>
    ///     API key for authentication.
    ///     Legacy key. Prefer using Server:ApiKey for new configurations.
    /// </summary>
    public const string ApiKey = "PlaywrightGrid:ApiKey";

    /// <summary>
    ///     Runner secret for worker operations.
    /// </summary>
    public const string RunnerSecret = "PlaywrightGrid:RunnerSecret";

    /// <summary>
    ///     Default application name for label keys.
    /// </summary>
    public const string DefaultApp = "PlaywrightGrid:DefaultApp";

    /// <summary>
    ///     Default label key if not specified per test.
    ///     Legacy key. Prefer using TestItem:DefaultLabelKey for new configurations.
    /// </summary>
    public const string DefaultLabelKey = "PlaywrightGrid:DefaultLabelKey";

    /// <summary>
    ///     HTTP request timeout in seconds (default: 30).
    /// </summary>
    public const string TimeoutSeconds = "PlaywrightGrid:TimeoutSeconds";

    /// <summary>
    ///     Number of retry attempts for failed requests (default: 3).
    /// </summary>
    public const string RetryCount = "PlaywrightGrid:RetryCount";

    /// <summary>
    ///     Maximum number of concurrent requests (default: 10).
    /// </summary>
    public const string MaxConcurrency = "PlaywrightGrid:MaxConcurrency";

    // ========== New Structured Keys (Recommended) ==========

    /// <summary>
    ///     Enable or disable PlaywrightGrid client (default: true).
    ///     Maps to JSON: { "enabled": true }
    /// </summary>
    public const string Enabled = "PlaywrightGrid:Enabled";

    // Server Configuration

    /// <summary>
    ///     Hub server base URL.
    ///     Maps to JSON: { "server": { "url": "https://grid.example.com" } }
    /// </summary>
    public const string ServerUrl = "PlaywrightGrid:Server:Url";

    /// <summary>
    ///     Project key for organizing test results.
    ///     Maps to JSON: { "server": { "project": "my-project" } }
    /// </summary>
    public const string ServerProject = "PlaywrightGrid:Server:Project";

    /// <summary>
    ///     API key for authentication.
    ///     Maps to JSON: { "server": { "apiKey": "xxx" } }
    /// </summary>
    public const string ServerApiKey = "PlaywrightGrid:Server:ApiKey";

    // Launch Configuration

    /// <summary>
    ///     Default launch name.
    ///     Maps to JSON: { "launch": { "name": "Demo Launch" } }
    /// </summary>
    public const string LaunchName = "PlaywrightGrid:Launch:Name";

    /// <summary>
    ///     Default launch description.
    ///     Maps to JSON: { "launch": { "description": "..." } }
    /// </summary>
    public const string LaunchDescription = "PlaywrightGrid:Launch:Description";

    /// <summary>
    ///     Launch attributes array prefix.
    ///     Maps to JSON: { "launch": { "attributes": ["t1", "t2"] } }
    ///     Access as: PlaywrightGrid:Launch:Attributes:0, PlaywrightGrid:Launch:Attributes:1, etc.
    /// </summary>
    public const string LaunchAttributes = "PlaywrightGrid:Launch:Attributes";

    // Suite Configuration

    /// <summary>
    ///     Default suite name.
    ///     Maps to JSON: { "suite": { "name": "..." } }
    /// </summary>
    public const string SuiteName = "PlaywrightGrid:Suite:Name";

    /// <summary>
    ///     Default suite description.
    ///     Maps to JSON: { "suite": { "description": "..." } }
    /// </summary>
    public const string SuiteDescription = "PlaywrightGrid:Suite:Description";

    /// <summary>
    ///     Suite attributes array prefix.
    ///     Maps to JSON: { "suite": { "attributes": ["tag1", "tag2"] } }
    /// </summary>
    public const string SuiteAttributes = "PlaywrightGrid:Suite:Attributes";

    // TestItem Configuration

    /// <summary>
    ///     Default label key for browser selection (e.g., "MyApp:Chromium:UAT:US-East").
    ///     REQUIRED for test items unless specified per-test.
    ///     Maps to JSON: { "testItem": { "defaultLabelKey": "..." } }
    /// </summary>
    public const string TestItemDefaultLabelKey = "PlaywrightGrid:TestItem:DefaultLabelKey";

    /// <summary>
    ///     Default test item attributes array prefix.
    ///     Maps to JSON: { "testItem": { "attributes": ["smoke", "regression"] } }
    /// </summary>
    public const string TestItemAttributes = "PlaywrightGrid:TestItem:Attributes";

    // Timeout Configuration

    /// <summary>
    ///     HTTP request timeout in seconds.
    ///     Maps to JSON: { "timeout": { "seconds": 30 } }
    /// </summary>
    public const string TimeoutConfigSeconds = "PlaywrightGrid:Timeout:Seconds";

    // Retry Configuration

    /// <summary>
    ///     Number of retry attempts for failed requests.
    ///     Maps to JSON: { "retry": { "count": 3 } }
    /// </summary>
    public const string RetryConfigCount = "PlaywrightGrid:Retry:Count";

    /// <summary>
    ///     Base delay between retries in seconds (uses exponential backoff).
    ///     Maps to JSON: { "retry": { "delaySeconds": 2 } }
    /// </summary>
    public const string RetryDelaySeconds = "PlaywrightGrid:Retry:DelaySeconds";

    // Concurrency Configuration

    /// <summary>
    ///     Maximum number of concurrent HTTP requests.
    ///     Maps to JSON: { "concurrency": { "maxConcurrentRequests": 10 } }
    /// </summary>
    public const string ConcurrencyMaxRequests = "PlaywrightGrid:Concurrency:MaxConcurrentRequests";
}
