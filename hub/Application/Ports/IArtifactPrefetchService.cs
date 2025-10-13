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

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Service for proactively prefetching artifact bytes into Redis cache.
///     Warms the cache when test items are loaded to eliminate first-access latency.
/// </summary>
public interface IArtifactPrefetchService
{
    /// <summary>
    ///     Prefetch artifacts for a single test item in background.
    ///     Loads artifact bytes from storage and warms Redis cache.
    ///     Non-blocking operation - runs as fire-and-forget.
    /// </summary>
    /// <param name="testItemId">Test item GUID</param>
    /// <param name="ct">Cancellation token</param>
    Task PrefetchArtifactsAsync(Guid testItemId, CancellationToken ct = default);

    /// <summary>
    ///     Prefetch artifacts for multiple test items (batch operation).
    ///     Used when loading test item trees with children.
    /// </summary>
    /// <param name="testItemIds">List of test item GUIDs</param>
    /// <param name="ct">Cancellation token</param>
    Task PrefetchArtifactsForItemsAsync(List<Guid> testItemIds, CancellationToken ct = default);

    /// <summary>
    ///     Get prefetch statistics (success rate, timing, cache hits).
    /// </summary>
    Task<PrefetchStatistics> GetStatisticsAsync();
}

/// <summary>
///     Statistics for artifact prefetching operations.
/// </summary>
public record PrefetchStatistics(
    long TotalPrefetchRequests,
    long SuccessfulPrefetches,
    long FailedPrefetches,
    long SkippedAlreadyCached,
    long TotalBytesPrefetched,
    double AveragePrefetchTimeMs,
    double SuccessRate
);
