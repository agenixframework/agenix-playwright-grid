#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Centralized Redis key naming with guardrails to avoid collisions and accidental overlap between namespaces.
///     All keys should be constructed through this class to ensure consistent prefixes and safe segment encoding.
/// </summary>
public static class RedisKeys
{
    // Public prefixes to enable safe pattern scanning and slicing
    public const string AvailablePrefix = "available:";
    public const string InUsePrefix = "inuse:";
    public const string MaintenancePrefix = "maintenance:";
    public const string NodePrefix = "node:";
    public const string NodeAlivePrefix = "node_alive:";
    public const string BorrowTtlPrefix = "borrow_ttl:";
    public const string RecyclePrefix = "recycle:";
    // General guidance:
    // - Prefixes are short and stable (no trailing ':').
    // - Label keys are allowed to contain ':' by design (App:Browser:Env...). We never escape ':' inside labels.
    // - For identifiers (runId, browserId, nodeId, testId), we restrict to a safe subset to keep keys predictable.
    // - If an identifier contains forbidden characters, we replace them with '-' to avoid collisions and throw optionally in debug.

    private static readonly char[] ForbiddenIdChars =
    {
        ' ', '\t', '\n', '\r', '\0', '\f', '\v', ':', '*', '?', '[', ']', '{', '}', '(', ')', '"', '\'', '\\'
    };

    private static string SanitizeId(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException($"{paramName} cannot be null or whitespace", paramName);

        // Keep typical GUID/ULID and safe URL-ish chars
        var sb = new StringBuilder(id.Length);
        foreach (var ch in id!)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                sb.Append(ch);
            }
            else if (Array.IndexOf(ForbiddenIdChars, ch) >= 0)
            {
                sb.Append('-');
            }
            else
            {
                // Replace any other unicode/control with '-'
                sb.Append('-');
            }
        }

        var s = sb.ToString();
        if (s.Length > 200) s = s[..200]; // prevent extremely long keys
        return s;
    }

    private static string RequireLabel(string label)
    {
        if (!LabelKey.TryParse(label, out var lk))
            throw new ArgumentException("Invalid labelKey", nameof(label));
        return lk!.Normalized; // Normalized already validated
    }

    // Pools
    public static string Available(string label) => $"available:{RequireLabel(label)}";
    public static string InUse(string label) => $"inuse:{RequireLabel(label)}";

    // Maintenance flags/snapshots
    public static string MaintenanceFlag(string label) => $"maintenance:{RequireLabel(label)}";
    public static string MaintenanceTarget(string label) => $"maintenance:target:{RequireLabel(label)}";
    public static string MaintenanceSince(string label) => $"maintenance:since:{RequireLabel(label)}";
    public static string MaintenanceSnapInuse(string label) => $"maintenance:snap_inuse:{RequireLabel(label)}";
    public static string MaintenanceSnapAvail(string label) => $"maintenance:snap_avail:{RequireLabel(label)}";

    // Node registry and liveness
    public static string Node(string nodeId) => $"node:{SanitizeId(nodeId, nameof(nodeId))}";
    public static string NodeAlive(string nodeId) => $"node_alive:{SanitizeId(nodeId, nameof(nodeId))}";

    // Borrow/session TTL marker
    public static string BorrowTtl(string browserId) => $"borrow_ttl:{SanitizeId(browserId, nameof(browserId))}";

    // Request worker-side recycle of a browser/sidecar
    public static string Recycle(string browserId) => $"recycle:{SanitizeId(browserId, nameof(browserId))}";

    // Lightweight mappings (if used)
    public static string BrowserRun(string browserId) => $"browser_run:{SanitizeId(browserId, nameof(browserId))}";
    public static string BrowserTest(string browserId) => $"browser_test:{SanitizeId(browserId, nameof(browserId))}";

    // Results store
    public static string ResultsRunsByStart() => "results:runs:byStart";
    public static string ResultsRun(string runId) => $"results:run:{SanitizeId(runId, nameof(runId))}";
    public static string ResultsRunName(string runId) => $"results:runname:{SanitizeId(runId, nameof(runId))}";
    public static string ResultsTests(string runId) => $"results:tests:{SanitizeId(runId, nameof(runId))}";
    public static string ResultsCmd(string runId) => $"results:cmd:{SanitizeId(runId, nameof(runId))}";
    public static string ResultsCmdCount(string runId) => $"results:cmdcount:{SanitizeId(runId, nameof(runId))}";

    // Audit
    public static string AuditEntries() => "audit:entries";
    public static string AuditSecretsRunnerFingerprint() => "audit:secrets:runner:fp";
    public static string AuditSecretsNodeFingerprint() => "audit:secrets:node:fp";
}
