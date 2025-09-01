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
///     Options controlling how label matching is performed.
/// </summary>
public sealed class LabelMatchingOptions
{
    /// <summary>
    ///     A reusable default set of matching options: trailing + prefix enabled, wildcards disabled.
    /// </summary>
    public static readonly LabelMatchingOptions Default = new();

    /// <summary>
    ///     When true, after trying exact matches, the matcher will progressively drop trailing segments
    ///     (down to <see cref="MinSegmentsForFallback" />) to find a less specific available label.
    /// </summary>
    public bool TrailingFallbackEnabled { get; init; } = true;

    /// <summary>
    ///     When true, if no exact or trailing match is found, the matcher will accept longer available labels that start
    ///     with the requested segments (prefix expansion).
    /// </summary>
    public bool PrefixExpansionEnabled { get; init; } = true;

    /// <summary>
    ///     When true, the requested label may include '*' wildcard segments which match any single segment.
    ///     Wildcards are considered during exact and prefix expansion phases.
    /// </summary>
    public bool WildcardsEnabled { get; init; } = false;

    /// <summary>
    ///     Do not fallback below this number of segments when dropping trailing segments. Default: 2 (App:Browser).
    /// </summary>
    public int MinSegmentsForFallback { get; init; } = 2;
}
