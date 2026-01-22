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

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Service for managing browser pool operations including borrowing and returning browser sessions.
/// </summary>
public interface IBrowserPoolService
{
    /// <summary>
    ///     Attempts to borrow a browser from the pool for the specified label key.
    /// </summary>
    /// <param name="labelKey">The label key identifying the browser pool (e.g., "AppA:Chromium:UAT").</param>
    /// <param name="runId">The test run identifier.</param>
    /// <param name="runName">Optional human-readable name for the test run.</param>
    /// <param name="timeout">Optional timeout for waiting for an available browser. Defaults to 120 seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing browser session details if successful, or error information if failed.</returns>
    Task<BrowserBorrowResult> TryBorrowBrowserAsync(
        string labelKey,
        string runId,
        string? runName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a browser session back to the pool.
    /// </summary>
    /// <param name="browserId">The browser session identifier.</param>
    /// <param name="nodeId">The worker node ID where the browser is running.</param>
    /// <param name="finalStatus">Optional final status of the test run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReturnBrowserAsync(
        string browserId,
        string? nodeId,
        string? finalStatus = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Result of a browser borrow operation.
/// </summary>
public sealed class BrowserBorrowResult
{
    /// <summary>
    ///     Indicates whether the borrow operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    ///     Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    ///     HTTP status code to return to the client (503 for no capacity, 408 for timeout, etc.).
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    ///     Indicates if the failure was due to maintenance mode.
    /// </summary>
    public bool IsMaintenance { get; init; }

    // Browser session details (populated on success)
    public string? BrowserId { get; init; }
    public string? WebSocketEndpoint { get; init; }
    public string? BrowserType { get; init; }
    public string? NodeId { get; init; }
    public string? WorkerNodeId { get; init; }
    public string? PlaywrightVersion { get; init; }
    public string? BrowserVersion { get; init; }
    public string? RegionOs { get; init; }
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    ///     Creates a successful borrow result.
    /// </summary>
    public static BrowserBorrowResult SuccessResult(
        string browserId,
        string webSocketEndpoint,
        string? browserType,
        string? nodeId,
        string? workerNodeId,
        string? playwrightVersion,
        string? browserVersion,
        string? regionOs,
        DateTime? expiresAt)
    {
        return new BrowserBorrowResult
        {
            Success = true,
            BrowserId = browserId,
            WebSocketEndpoint = webSocketEndpoint,
            BrowserType = browserType,
            NodeId = nodeId,
            WorkerNodeId = workerNodeId,
            PlaywrightVersion = playwrightVersion,
            BrowserVersion = browserVersion,
            RegionOs = regionOs,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    ///     Creates a failed borrow result.
    /// </summary>
    public static BrowserBorrowResult FailureResult(string errorMessage, int statusCode)
    {
        return new BrowserBorrowResult { Success = false, ErrorMessage = errorMessage, StatusCode = statusCode };
    }

    /// <summary>
    ///     Creates a failed borrow result due to maintenance mode.
    /// </summary>
    public static BrowserBorrowResult MaintenanceResult(string labelKey)
    {
        return new BrowserBorrowResult
        {
            Success = false,
            ErrorMessage = $"Pool {labelKey} is currently under maintenance. Please try again later.",
            StatusCode = 503,
            IsMaintenance = true
        };
    }
}
