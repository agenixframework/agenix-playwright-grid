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

using System.Text.Json.Serialization;

namespace PlaywrightHub.Application.DTOs;

/// <summary>
///     Audit log entry for security-relevant events (node registration, secret changes, admin actions).
///     Secrets and PII must not be included in Details; use counts/fingerprints only.
/// </summary>
public sealed class AuditEntryDto
{
    /// <summary>
    ///     UTC timestamp when the event occurred.
    /// </summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary>
    ///     Logical category (e.g., node, admin, secrets).
    /// </summary>
    public required string Category { get; init; } = string.Empty; // e.g., node, admin, secrets

    /// <summary>
    ///     Action identifier (e.g., register.success, register.denied, rotation.enabled).
    /// </summary>
    public required string Action { get; init; } = string.Empty;   // e.g., register.success, register.denied, rotation.enabled

    /// <summary>
    ///     Optional actor that triggered the event (dashboard, runner, nodeId).
    /// </summary>
    public string? Actor { get; init; }                             // e.g., dashboard, runner, nodeId

    /// <summary>
    ///     Optional remote IP address as observed by the hub.
    /// </summary>
    public string? RemoteIp { get; init; }

    /// <summary>
    ///     Optional correlation id associated with the event (run id).
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Severity level (Info|Warning|Error). Default: Info.
    /// </summary>
    public string? Severity { get; init; } = "Info";               // Info|Warning|Error

    /// <summary>
    ///     Free-form details map. Do not include secrets or PII. Keep values concise.
    /// </summary>
    public Dictionary<string, string>? Details { get; init; }
}
