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

namespace Agenix.PlaywrightGrid.Client;

/// <summary>
///     Configuration options for the Playwright Grid Client.
/// </summary>
public sealed class PlaywrightGridClientOptions
{
    /// <summary>
    ///     The base URI of the Playwright Grid Hub (e.g., "https://grid.example.com").
    /// </summary>
    public required Uri BaseUri { get; set; }

    /// <summary>
    ///     The project key for authentication and routing.
    /// </summary>
    public required string ProjectKey { get; set; }

    /// <summary>
    ///     The API key for authentication. Optional if using other auth methods.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    ///     Maximum number of retry attempts for transient errors (default: 3).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    ///     Base delay between retries in seconds. Uses exponential backoff (default: 2).
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>
    ///     HTTP client timeout in seconds (default: 30).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Whether to enable retry policies for transient errors (default: true).
    /// </summary>
    public bool EnableRetryPolicy { get; set; } = true;

    /// <summary>
    ///     Maximum number of concurrent HTTP connections per server (default: 10).
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 10;

    /// <summary>
    ///     Validates the options and throws if invalid.
    /// </summary>
    public void Validate()
    {
        if (BaseUri == null)
        {
            throw new ArgumentNullException(nameof(BaseUri), "BaseUri must be specified.");
        }

        if (string.IsNullOrWhiteSpace(ProjectKey))
        {
            throw new ArgumentException("ProjectKey cannot be null or whitespace.", nameof(ProjectKey));
        }

        if (MaxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetryAttempts), "MaxRetryAttempts must be non-negative.");
        }

        if (RetryDelaySeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelaySeconds), "RetryDelaySeconds must be non-negative.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "TimeoutSeconds must be positive.");
        }

        if (MaxConcurrentRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentRequests),
                "MaxConcurrentRequests must be positive.");
        }
    }

    // NOTE: FromConfiguration method removed to eliminate circular dependency with Shared project.
    // Users should construct PlaywrightGridClientOptions directly or use their own configuration helpers.
}
