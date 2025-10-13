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

namespace PlaywrightHub.Application.DTOs;

/// <summary>
///     DTO for creating a new log item.
/// </summary>
public record CreateLogItemDto
{
    /// <summary>
    ///     The UUID of the test item this log belongs to.
    /// </summary>
    public required Guid TestItemUuid { get; init; }

    /// <summary>
    ///     Optional launch UUID for faster launch-level queries.
    /// </summary>
    public Guid? LaunchUuid { get; init; }

    /// <summary>
    ///     The timestamp of the log entry.
    /// </summary>
    public required DateTime Time { get; init; }

    /// <summary>
    ///     The log level (TRACE, DEBUG, INFO, WARN, ERROR, FATAL).
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    ///     The log message text.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    ///     Optional binary data for file attachment.
    /// </summary>
    public byte[]? AttachmentData { get; init; }

    /// <summary>
    ///     Optional name for the file attachment.
    /// </summary>
    public string? AttachmentName { get; init; }

    /// <summary>
    ///     Optional MIME type for the file attachment.
    /// </summary>
    public string? AttachmentMimeType { get; init; }
}

/// <summary>
///     DTO representing a created log item response.
/// </summary>
public record LogItemCreatedDto
{
    /// <summary>
    ///     The UUID of the created log item.
    /// </summary>
    public required Guid Id { get; init; }
}

/// <summary>
///     DTO representing a log item with all details.
/// </summary>
public record LogItemDto
{
    /// <summary>
    ///     The unique identifier of the log item.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     The UUID of the test item this log belongs to.
    /// </summary>
    public required Guid TestItemUuid { get; init; }

    /// <summary>
    ///     Optional launch UUID for faster launch-level queries.
    /// </summary>
    public Guid? LaunchUuid { get; init; }

    /// <summary>
    ///     The timestamp of the log entry.
    /// </summary>
    public required DateTime Time { get; init; }

    /// <summary>
    ///     The log level (TRACE, DEBUG, INFO, WARN, ERROR, FATAL).
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    ///     The log message text.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    ///     Optional ID of the attached artifact.
    /// </summary>
    public Guid? AttachmentId { get; init; }

    /// <summary>
    ///     The timestamp when the log item was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
///     DTO for hierarchical log entries with step headers.
/// </summary>
public record HierarchicalLogEntryDto
{
    public required Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public bool IsStepHeader { get; init; }
    public bool IsNested { get; init; }
    public int NestLevel { get; init; }
    public required DateTime Timestamp { get; init; }
    public string Level { get; init; } = "INFO";
    public string Source { get; init; } = "";
    public string Message { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status { get; init; } = "InProgress";
    public long? DurationMs { get; init; }
    public int AttachmentCount { get; init; }
    public bool HasAttachment { get; init; }
    public string AttachmentType { get; init; } = "";
    public string AttachmentName { get; init; } = "";
    public Guid? ArtifactId { get; init; }
}
