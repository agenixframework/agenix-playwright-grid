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

using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Abstraction for persisting and querying audit log entries of security-relevant events.
/// </summary>
public interface IAuditStore
{
    /// <summary>
    ///     Appends a new audit entry.
    /// </summary>
    /// <param name="entry">The audit entry to append.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task AppendAsync(AuditEntryDto entry, CancellationToken ct = default);

    /// <summary>
    ///     Queries audit entries with optional paging and filters.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="action">Optional action filter.</param>
    /// <param name="sinceUtc">Optional start timestamp (UTC).</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<IReadOnlyList<AuditEntryDto>> QueryAsync(
        int skip = 0,
        int take = 100,
        string? category = null,
        string? action = null,
        DateTime? sinceUtc = null,
        CancellationToken ct = default);
}
