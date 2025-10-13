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

using Agenix.PlaywrightGrid.Domain;

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Optional durable mirror for Admin entities (Projects, Users, Memberships, Settings).
///     Implementations should be best-effort and never throw on transient issues in request paths.
///     Read paths remain Redis-backed; this interface only mirrors writes to a durable store.
/// </summary>
public interface IAdminDurableStore
{
    Task UpsertProjectAsync(Project project, CancellationToken ct = default);
    Task DeleteProjectAsync(string projectKey, CancellationToken ct = default);
    Task UpsertUserAsync(User user, CancellationToken ct = default);
    Task UpsertMembershipAsync(Membership membership, CancellationToken ct = default);
    Task RemoveMembershipAsync(string projectKey, string userId, CancellationToken ct = default);
    Task DeleteUserAsync(string userId, CancellationToken ct = default);

    // Admin settings persistence
    Task SaveSettingAsync(string key, string jsonValue, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);

    // Remember Me token management
    Task CreateRememberMeTokenAsync(RememberMeToken token, CancellationToken ct = default);
    Task<RememberMeToken?> GetRememberMeTokenAsync(string tokenHash, CancellationToken ct = default);
    Task UpdateRememberMeTokenLastUsedAsync(int tokenId, DateTime lastUsedUtc, CancellationToken ct = default);
    Task DeleteRememberMeTokenAsync(int tokenId, CancellationToken ct = default);
    Task DeleteAllRememberMeTokensForUserAsync(string userId, CancellationToken ct = default);
    Task DeleteExpiredRememberMeTokensAsync(CancellationToken ct = default);
}
