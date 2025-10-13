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

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Persistent remember-me token for long-lived authentication.
/// </summary>
public sealed record RememberMeToken
{
    /// <summary>Auto-generated token ID.</summary>
    public int Id { get; init; }

    /// <summary>User ID this token belongs to.</summary>
    public required string UserId { get; init; }

    /// <summary>Hashed token value (SHA256).</summary>
    public required string TokenHash { get; init; } = string.Empty;

    /// <summary>Token creation time (UTC).</summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>Token expiration time (UTC).</summary>
    public DateTime ExpiresUtc { get; init; }

    /// <summary>Last time this token was used (UTC).</summary>
    public DateTime? LastUsedUtc { get; init; }
}
