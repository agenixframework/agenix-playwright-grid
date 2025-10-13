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
///     Event representing a command log append operation.
///     Published to message broker for async processing.
/// </summary>
public sealed record CommandEvent
{
    /// <summary>
    ///     Type of event: CommandAppended
    /// </summary>
    public string EventType { get; init; } = "CommandAppended";

    /// <summary>
    ///     Run/Test item ID this command log belongs to
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    ///     Full command log data (serialized DTO)
    /// </summary>
    public string DataJson { get; init; } = string.Empty;

    /// <summary>
    ///     When the event was created
    /// </summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>
    ///     Correlation ID for tracing events end-to-end
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;
}
