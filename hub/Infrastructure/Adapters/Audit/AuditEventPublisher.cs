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

using Agenix.PlaywrightGrid.Domain.Events;
using Agenix.PlaywrightGrid.Shared.Logging;
using PlaywrightHub.Application.DTOs;
using PlaywrightHub.Application.Ports;

namespace PlaywrightHub.Infrastructure.Adapters.Audit;

/// <summary>
///     Event-driven audit store that publishes audit entries to RabbitMQ for async batch processing.
///     Replaces direct database writes with fire-and-forget event publishing.
///     Ingestion service consumes events and persists to PostgresSQL in batches.
/// </summary>
public sealed class AuditEventPublisher(
    IEventPublisher eventPublisher,
    ILogger<AuditEventPublisher> logger)
    : IAuditStore
{
    /// <summary>
    ///     Publishes audit entry as an event to RabbitMQ for async processing.
    ///     Returns immediately without awaiting persistence - fire and forget!
    /// </summary>
    public async Task AppendAsync(AuditEntryDto entry, CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            CorrelationId = entry.CorrelationId ?? Guid.NewGuid().ToString(),
            Timestamp = entry.Timestamp,
            Category = entry.Category,
            Action = entry.Action,
            Actor = entry.Actor,
            RemoteIp = entry.RemoteIp,
            Severity = entry.Severity,
            Details = entry.Details
        };

        try
        {
            await eventPublisher.PublishAuditEventAsync(evt, OperationContext.Current?.OperationId, ct);
            logger.LogDebug("Published audit event {CorrelationId}: {Category}.{Action}",
                evt.CorrelationId, evt.Category, evt.Action);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish audit event {CorrelationId}: {Category}.{Action}",
                evt.CorrelationId, evt.Category, evt.Action);
            // Swallow exception - audit logging should never crash the main execution path
        }
    }

    /// <summary>
    ///     Queries audit entries directly from a database.
    ///     Note: This operation is not supported by AuditEventPublisher - use PostgresAuditStore for queries.
    /// </summary>
    public Task<IReadOnlyList<AuditEntryDto>> QueryAsync(
        int skip = 0, int take = 100, string? category = null,
        string? action = null, DateTime? sinceUtc = null, CancellationToken ct = default)
    {
        // Queries should use PostgresAuditStore directly for immediate reads
        // AuditEventPublisher is only for async writes
        throw new NotSupportedException(
            "QueryAsync not supported on AuditEventPublisher. Use PostgresAuditStore for queries or inject it separately.");
    }
}
