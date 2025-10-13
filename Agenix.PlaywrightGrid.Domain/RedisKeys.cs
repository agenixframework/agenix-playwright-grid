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
    public const string NodeUpgradePrefix = "node_upgrade:";
    public const string BorrowTtlPrefix = "borrow_ttl:";
    public const string RecyclePrefix = "recycle:";

    public const string NodeQuarantinePrefix = "node_quarantine:";
    // General guidance:
    // - Prefixes are short and stable (no trailing ':').
    // - Label keys are allowed to contain ':' by design (App:Browser:Env...). We never escape ':' inside labels.
    // - For identifiers (runId, browserId, nodeId, testId), we restrict to a safe subset to keep keys predictable.
    // - If an identifier contains forbidden characters, we replace them with '-' to avoid collisions and throw optionally in debug.

    private static readonly char[] ForbiddenIdChars =
    [
        ' ', '\t', '\n', '\r', '\0', '\f', '\v', ':', '*', '?', '[', ']', '{', '}', '(', ')', '"', '\'', '\\'
    ];

    private static string SanitizeId(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException($"{paramName} cannot be null or whitespace", paramName);
        }

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
        if (s.Length > 200)
        {
            s = s[..200]; // prevent extremely long keys
        }

        return s;
    }

    private static string RequireLabel(string label)
    {
        if (!LabelKey.TryParse(label, out var lk))
        {
            throw new ArgumentException("Invalid labelKey", nameof(label));
        }

        return lk!.Normalized; // Normalized already validated
    }

    // Pools
    public static string Available(string label)
    {
        return $"available:{RequireLabel(label)}";
    }

    public static string InUse(string label)
    {
        return $"inuse:{RequireLabel(label)}";
    }

    // Maintenance flags/snapshots
    public static string MaintenanceFlag(string label)
    {
        return $"maintenance:{RequireLabel(label)}";
    }

    public static string MaintenanceTarget(string label)
    {
        return $"maintenance:target:{RequireLabel(label)}";
    }

    public static string MaintenanceSince(string label)
    {
        return $"maintenance:since:{RequireLabel(label)}";
    }

    public static string MaintenanceSnapInuse(string label)
    {
        return $"maintenance:snap_inuse:{RequireLabel(label)}";
    }

    public static string MaintenanceSnapAvail(string label)
    {
        return $"maintenance:snap_avail:{RequireLabel(label)}";
    }

    // Node registry and liveness
    public static string Node(string nodeId)
    {
        return $"node:{SanitizeId(nodeId, nameof(nodeId))}";
    }

    public static string NodeAlive(string nodeId)
    {
        return $"node_alive:{SanitizeId(nodeId, nameof(nodeId))}";
    }

    public static string NodeUpgrade(string nodeId)
    {
        return $"node_upgrade:{SanitizeId(nodeId, nameof(nodeId))}";
    }

    public static string NodeQuarantine(string nodeId)
    {
        return $"node_quarantine:{SanitizeId(nodeId, nameof(nodeId))}";
    }

    // Borrow/session TTL marker
    public static string BorrowTtl(string browserId)
    {
        return $"borrow_ttl:{SanitizeId(browserId, nameof(browserId))}";
    }

    // Request worker-side recycle of a browser/sidecar
    public static string Recycle(string browserId)
    {
        return $"recycle:{SanitizeId(browserId, nameof(browserId))}";
    }

    // Lightweight mappings (if used)
    public static string BrowserRun(string browserId)
    {
        return $"browser_run:{SanitizeId(browserId, nameof(browserId))}";
    }

    public static string BrowserTest(string browserId)
    {
        return $"browser_test:{SanitizeId(browserId, nameof(browserId))}";
    }

    // Results store
    public static string ResultsRunsByStart()
    {
        return "results:runs:byStart";
    }

    public static string ResultsRun(string runId)
    {
        return $"results:run:{SanitizeId(runId, nameof(runId))}";
    }

    public static string ResultsRunName(string runId)
    {
        return $"results:runname:{SanitizeId(runId, nameof(runId))}";
    }

    public static string ResultsTests(string runId)
    {
        return $"results:tests:{SanitizeId(runId, nameof(runId))}";
    }

    public static string ResultsCmd(string runId)
    {
        return $"results:cmd:{SanitizeId(runId, nameof(runId))}";
    }

    public static string ResultsCmdCount(string runId)
    {
        return $"results:cmdcount:{SanitizeId(runId, nameof(runId))}";
    }

    // Audit
    public static string AuditEntries()
    {
        return "audit:entries";
    }

    public static string AuditSecretsRunnerFingerprint()
    {
        return "audit:secrets:runner:fp";
    }

    public static string AuditSecretsNodeFingerprint()
    {
        return "audit:secrets:node:fp";
    }

    // Borrow queue via Redis Streams and idempotency/dedup keys
    public static string BorrowStream(string label)
    {
        return $"borrow:stream:{RequireLabel(label)}";
    }

    public static string BorrowNotify(string requestId)
    {
        return $"borrow_notify:{SanitizeId(requestId, nameof(requestId))}";
    }

    public static string BorrowIdempotency(string idempotencyKey)
    {
        return $"idem:borrow:{SanitizeId(idempotencyKey, nameof(idempotencyKey))}";
    }

    public static string BorrowIdempotencyPending(string idempotencyKey)
    {
        return $"idem:borrow:pending:{SanitizeId(idempotencyKey, nameof(idempotencyKey))}";
    }

    // Return idempotency/dedup keys
    public static string ReturnIdempotency(string idempotencyKey)
    {
        return $"idem:return:{SanitizeId(idempotencyKey, nameof(idempotencyKey))}";
    }

    public static string ReturnIdempotencyPending(string idempotencyKey)
    {
        return $"idem:return:pending:{SanitizeId(idempotencyKey, nameof(idempotencyKey))}";
    }

    // Distributed sweeper leadership locks
    public static string SweeperLeader(string jobName)
    {
        // Job name kept simple (letters/digits/-_.) and sanitized for safety
        return $"sweeper:leader:{SanitizeId(jobName, nameof(jobName))}";
    }

    // Admin: Projects & Users (minimal storage)
    public static string AdminProjectsSet()
    {
        return "admin:projects:all";
        // Set of project keys
    }

    public static string AdminProject(string key)
    {
        return $"admin:project:{SanitizeId(key, nameof(key))}";
        // JSON value
    }

    public static string AdminProjectByName(string nameLower)
    {
        return $"admin:project:byName:{SanitizeId(nameLower, nameof(nameLower))}";
        // secondary index (name -> key)
    }

    public static string AdminUsersSet()
    {
        return "admin:users:all";
        // Set of user ids
    }

    public static string AdminUser(string id)
    {
        return $"admin:user:{SanitizeId(id, nameof(id))}";
        // JSON value
    }

    public static string AdminUserByEmail(string emailLower)
    {
        return $"admin:user:byEmail:{SanitizeId(emailLower, nameof(emailLower))}";
    }

    public static string AdminUserByUsername(string usernameLower)
    {
        return $"admin:user:byUsername:{SanitizeId(usernameLower, nameof(usernameLower))}";
    }

    // Admin: Invite tokens (optional flow)
    public static string AdminInviteToken(string token)
    {
        return $"admin:invite:{SanitizeId(token, nameof(token))}";
        // JSON value with email/username and expiry
    }

    public static string AdminInviteByEmail(string emailLower)
    {
        return $"admin:invite:byEmail:{SanitizeId(emailLower, nameof(emailLower))}";
        // maps email_lower -> token
    }

    // Admin: Password reset tokens
    public static string AdminPasswordResetToken(string token)
    {
        return $"admin:password-reset:{SanitizeId(token, nameof(token))}";
        // JSON value with email/userId and expiry
    }

    public static string AdminPasswordResetByEmail(string emailLower)
    {
        return $"admin:password-reset:byEmail:{SanitizeId(emailLower, nameof(emailLower))}";
        // maps email_lower -> token
    }

    public static string AdminPasswordResetRateLimit(string emailLower)
    {
        return $"admin:password-reset:ratelimit:{SanitizeId(emailLower, nameof(emailLower))}";
        // counter for rate limiting
    }

    // Admin: Memberships (by project and by user)
    public static string AdminMembersByProject(string projectKey)
    {
        return $"admin:members:byProject:{SanitizeId(projectKey, nameof(projectKey))}";
        // Set of user ids
    }

    public static string AdminProjectsByUser(string userId)
    {
        return $"admin:members:byUser:{SanitizeId(userId, nameof(userId))}";
        // Set of project keys
    }

    public static string AdminMembership(string projectKey, string userId)
    {
        return $"admin:membership:{SanitizeId(projectKey, nameof(projectKey))}:{SanitizeId(userId, nameof(userId))}";
        // JSON value
    }

    // Admin: User credentials (hashed password stored separately)
    public static string AdminUserPassword(string userId)
    {
        return $"admin:user:password:{SanitizeId(userId, nameof(userId))}";
        // JSON value {alg,salt,hash,iter,createdUtc}
    }

    // Admin: Dashboard settings
    public static string AdminSettings()
    {
        return "admin:settings";
        // JSON value {sessionTimeoutMinutes}
    }

    // Admin: User profile photo (binary and metadata)
    public static string AdminUserPhoto(string userId)
    {
        return $"admin:user:photo:{SanitizeId(userId, nameof(userId))}";
        // binary value
    }

    public static string AdminUserPhotoContentType(string userId)
    {
        return $"admin:user:photo:ct:{SanitizeId(userId, nameof(userId))}";
        // string value
    }

    public static string AdminUserPhotoUpdated(string userId)
    {
        return $"admin:user:photo:updated:{SanitizeId(userId, nameof(userId))}";
        // ticks or ISO timestamp
    }

    // Admin: User API keys (per-user collection and individual entries)
    public static string AdminUserApiKeys(string userId)
    {
        return $"admin:user:apikeys:{SanitizeId(userId, nameof(userId))}";
        // Set of key slugs
    }

    public static string AdminUserApiKey(string userId, string nameSlug)
    {
        return $"admin:user:apikey:{SanitizeId(userId, nameof(userId))}:{SanitizeId(nameSlug, nameof(nameSlug))}";
        // JSON value
    }

    // Admin: API key reverse index (hash -> userId for O(1) lookup)
    public static string AdminApiKeyHashIndex(string hash)
    {
        return $"admin:apikey:hash:{SanitizeId(hash, nameof(hash))}";
        // String value: userId (reverse index for fast API key validation)
    }

    // Log token deduplication keys
    public static string LogToken(string tokenHash)
    {
        return $"log_token:{SanitizeId(tokenHash, nameof(tokenHash))}";
        // JSON value {tokenHash, message, loggerName, firstSeenAt, lastSeenAt, occurrenceCount}
    }

    public static string LogTokensByTestItem(string testItemId)
    {
        return $"log_tokens:test:{SanitizeId(testItemId, nameof(testItemId))}";
        // Set of token hashes for a test item
    }

    public static string LogTokensByLaunch(string launchId)
    {
        return $"log_tokens:launch:{SanitizeId(launchId, nameof(launchId))}";
        // Set of token hashes for a launch
    }

    // Artifact caching keys (distributed cache for frequently accessed artifacts)
    public static string ArtifactContent(string artifactId)
    {
        return $"artifact:content:{SanitizeId(artifactId, nameof(artifactId))}";
        // Binary value (artifact bytes for small files < 5MB)
    }

    public static string ArtifactMetadata(string artifactId)
    {
        return $"artifact:meta:{SanitizeId(artifactId, nameof(artifactId))}";
        // JSON value {guid, fileName, contentType, fileSize, uploadedAt, storagePath}
    }

    public static string ArtifactPresignedUrl(string artifactId)
    {
        return $"artifact:presigned:{SanitizeId(artifactId, nameof(artifactId))}";
        // JSON value {url, expiresAt} (MinIO pre-signed URLs cached to reduce bandwidth)
    }

    public static string ArtifactCacheStats()
    {
        return "artifact:cache:stats";
        // Hash value {hits, misses, bytesServed} (telemetry counters)
    }
}
