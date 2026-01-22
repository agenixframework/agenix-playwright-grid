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

using System.IO.Hashing;
using System.Text;
using Npgsql;

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Helper for computing canonical test case identifiers and hashes for test history tracking.
/// </summary>
public static class TestItemIdentityHelper
{
    /// <summary>
    ///     Computes canonical test case ID using 3-tier priority:
    ///     1. Explicit testCaseId (if provided)
    ///     2. Code reference (if provided)
    ///     3. Hierarchical path built from SUITE parents + test name
    /// </summary>
    public static async Task<string> ComputeCanonicalIdAsync(
        NpgsqlDataSource db,
        Guid launchId,
        string? parentItemId,
        string? testCaseId,
        string? codeReference,
        string name)
    {
        // Priority 1: Explicit test case ID
        if (!string.IsNullOrWhiteSpace(testCaseId))
        {
            return NormalizeId(testCaseId);
        }

        // Priority 2: Code reference (e.g., "tests/auth/login.spec.ts:42")
        if (!string.IsNullOrWhiteSpace(codeReference))
        {
            return NormalizeId(codeReference);
        }

        // Priority 3: Build a hierarchical path from SUITE parents + name
        return await BuildTestItemPathAsync(db, launchId, parentItemId, name);
    }

    /// <summary>
    ///     Computes 32-bit signed hash of test case ID using xxHash32 algorithm.
    /// </summary>
    public static int ComputeHash(string testCaseId)
    {
        var bytes = Encoding.UTF8.GetBytes(testCaseId);
        var hashBytes = XxHash32.Hash(bytes);
        return BitConverter.ToInt32(hashBytes, 0);
    }

    /// <summary>
    ///     Builds a hierarchical path from SUITE parents (root to leaf) + test name.
    ///     Example: "Suite1 / Suite2 / Test Name"
    /// </summary>
    private static async Task<string> BuildTestItemPathAsync(
        NpgsqlDataSource db,
        Guid launchId,
        string? parentItemId,
        string name)
    {
        var parts = new List<string>();

        // Walk up the parent chain, collecting SUITE names only
        var currentParentId = string.IsNullOrWhiteSpace(parentItemId)
            ? (Guid?)null
            : Guid.Parse(parentItemId);

        while (currentParentId.HasValue)
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT name, parent_item_id, item_type
                FROM test_items
                WHERE run_id = $1 AND launch_id = $2";
            cmd.Parameters.AddWithValue(currentParentId.Value);
            cmd.Parameters.AddWithValue(launchId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                break;
            }

            var itemType = reader.GetString(2);

            // Only include Suite items in a path (exclude steps, hooks, scenarios)
            if (itemType.Equals("Suite", StringComparison.OrdinalIgnoreCase))
            {
                var parentName = reader.GetString(0).Trim();
                parts.Insert(0, parentName); // Insert at the beginning for root→leaf order
            }

            // Move to next parent
            currentParentId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }

        // Add a test name at the end
        parts.Add(name.Trim());

        // Join with the "/" separator and normalize
        var path = string.Join(" / ", parts);
        return NormalizeId(path);
    }

    /// <summary>
    ///     Normalizes identifier: trim whitespace and apply NFC Unicode normalization.
    /// </summary>
    private static string NormalizeId(string id)
    {
        return id.Trim().Normalize(NormalizationForm.FormC);
    }
}
