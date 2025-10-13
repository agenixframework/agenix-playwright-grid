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

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Configuration options for ChunkedLogger behavior.
///     Controls chunked logging features like operation tracking, event codes, and auto-flush behavior.
/// </summary>
public sealed class ChunkedLoggerOptions
{
    /// <summary>
    ///     Enable or disable chunked logging. When false, ChunkedLogger becomes a no-op wrapper.
    ///     Default: true
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    ///     Maximum number of events per operation chunk before auto-flush.
    ///     Default: 1000
    /// </summary>
    public int MaxEventsPerChunk { get; init; } = 1000;

    /// <summary>
    ///     Maximum age of operation chunk buffer (in seconds) before auto-flush.
    ///     Default: 60 seconds
    /// </summary>
    public int MaxAgeSeconds { get; init; } = 60;

    /// <summary>
    ///     Include event code prefix in log messages (e.g., [ITEM01], [POOL10]).
    ///     Default: true
    /// </summary>
    public bool EventCodePrefix { get; init; } = true;

    /// <summary>
    ///     Include source file/line information in logs. Adds overhead - only enable for debugging.
    ///     Default: false
    /// </summary>
    public bool IncludeSourceLocation { get; init; } = false;

    /// <summary>
    ///     Creates default options with all settings at their default values.
    /// </summary>
    public static ChunkedLoggerOptions Default => new();
}
