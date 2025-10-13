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
///     Event representing a log item append operation.
///     Published to message broker for async processing.
/// </summary>
public sealed record LogItemEvent
{
    /// <summary>
    ///     Type of event: LogItemAppended
    /// </summary>
    public string EventType { get; init; } = "LogItemAppended";

    /// <summary>
    ///     Test item ID this log belongs to
    /// </summary>
    public Guid ItemId { get; init; }

    /// <summary>
    ///     Launch ID for partitioning/cleanup
    /// </summary>
    public Guid LaunchId { get; init; }

    /// <summary>
    ///     Log level: Trace, Debug, Info, Warn, Error, Fatal
    /// </summary>
    public string Level { get; init; } = "Info";

    /// <summary>
    ///     Full log message
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    ///     When the log occurred
    /// </summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>
    ///     Logger name (e.g., "Playwright.Browser")
    /// </summary>
    public string? LoggerName { get; init; }

    /// <summary>
    ///     Additional key-value metadata (serialized JSON)
    /// </summary>
    public string? MetadataJson { get; init; }

    /// <summary>
    ///     Correlation ID for tracing events end-to-end
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;
}
