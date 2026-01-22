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

namespace Agenix.PlaywrightGrid.Domain.Events;

/// <summary>
///     Event representing an audit log entry for async processing via RabbitMQ.
///     Published by Hub, consumed by Ingestion service for batch persistence.
/// </summary>
public sealed record AuditEvent
{
    /// <summary>
    ///     Unique correlation ID for tracking this event through the system.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    ///     UTC timestamp when the audited action occurred.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    ///     Audit category (e.g., "admin", "browser", "launch", "test").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    ///     Specific action within the category (e.g., "user.login.success", "browser.autoStop").
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    ///     Actor who performed the action (user ID, API key, or "system").
    /// </summary>
    public string? Actor { get; init; }

    /// <summary>
    ///     Remote IP address of the actor (if applicable).
    /// </summary>
    public string? RemoteIp { get; init; }

    /// <summary>
    ///     Severity level (Info, Warning, Error). Defaults to "Info" if not specified.
    /// </summary>
    public string? Severity { get; init; }

    /// <summary>
    ///     Additional structured details as key-value pairs (serialized to JSONB in database).
    /// </summary>
    public Dictionary<string, string>? Details { get; init; }

    /// <summary>
    ///     Event schema version for backward compatibility. Current version: 1.
    /// </summary>
    public int Version { get; init; } = 1;
}
