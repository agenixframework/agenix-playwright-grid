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

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Specifies how the case of label key segments should be treated during parsing/normalization.
/// </summary>
public enum LabelKeyCasePolicy
{
    /// <summary>
    ///     Preserve the original case of each segment.
    /// </summary>
    Keep,

    /// <summary>
    ///     Convert each segment to lower-case using invariant rules.
    /// </summary>
    Lower,

    /// <summary>
    ///     Convert each segment to upper-case using invariant rules.
    /// </summary>
    Upper
}

/// <summary>
///     Options that control how label keys are parsed and validated.
///     See <c>LabelKey.TryParse</c> for usage; a default instance is available via <see cref="Default" />.
/// </summary>
public sealed class LabelKeyParsingOptions
{
    /// <summary>
    ///     A reusable default set of parsing options.
    /// </summary>
    public static readonly LabelKeyParsingOptions Default = new();

    /// <summary>
    ///     Minimum number of segments required (default 2: App:Browser).
    /// </summary>
    public int MinSegments { get; init; } = 2; // App:Browser at minimum

    /// <summary>
    ///     Maximum number of segments allowed (safety cap, default 8).
    /// </summary>
    public int MaxSegments { get; init; } = 8; // arbitrary safety cap

    /// <summary>
    ///     Characters not allowed in any individual segment. If null or empty, no extra restriction applies.
    /// </summary>
    public char[]? ForbiddenChars { get; init; } = [' ', '\t', '\n', '\r'];

    /// <summary>
    ///     Normalization rule for the case of segments.
    /// </summary>
    public LabelKeyCasePolicy CasePolicy { get; init; } = LabelKeyCasePolicy.Keep;

    /// <summary>
    ///     When true, the second segment must be a known browser token (Chromium/Firefox/WebKit). Default: true.
    /// </summary>
    public bool EnforceBrowserSecond { get; init; } = true;
}
