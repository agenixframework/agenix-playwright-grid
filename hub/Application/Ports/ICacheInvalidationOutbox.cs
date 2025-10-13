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

using Npgsql;

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Interface for the transactional cache invalidation outbox.
///     Ensures that cache invalidation is atomic with database transactions.
/// </summary>
public interface ICacheInvalidationOutbox
{
    /// <summary>
    ///     Adds a cache key to the outbox for invalidation.
    ///     MUST be called within an active transaction if atomicity is required.
    /// </summary>
    Task AddAsync(string key, NpgsqlConnection connection, NpgsqlTransaction? transaction = null, CancellationToken ct = default);

    /// <summary>
    ///     Gets pending invalidation tasks from the outbox.
    /// </summary>
    Task<IReadOnlyList<(long Id, string Key)>> GetPendingAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    ///     Removes processed invalidation tasks from the outbox.
    /// </summary>
    Task DeleteAsync(IEnumerable<long> ids, CancellationToken ct = default);
}
