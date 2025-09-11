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

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
///     Validation and normalization rules for <c>RunName</c>, a human-friendly label for a test run.
///     Rules:
///     - Trim surrounding whitespace.
///     - If the result is empty, treat it as <c>null</c> (RunName is optional).
///     - Maximum length: 128 characters after trimming.
///     - Allowed characters: ASCII letters and digits, space, dot (.), underscore (_), and hyphen (-).
///     - Control characters are rejected.
///
///     Case policy: input casing is preserved; comparisons and searches should use case-insensitive
///     semantics where applicable (e.g., Dashboard search). This avoids surprising mutations
///     while keeping UX friendly.
/// </summary>
public static class RunNameRules
{
    /// <summary>
    ///     The maximum allowed length for a normalized <c>RunName</c>.
    /// </summary>
    public const int MaxLength = 128;

    // Regex: one or more of [A-Za-z0-9 ._-]
    private static readonly Regex AllowedPattern = new(
        pattern: "^[A-Za-z0-9 ._-]+$",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Attempts to normalize and validate a provided <paramref name="input"/> according to the rules.
    ///     On success, returns <c>true</c> and sets <paramref name="normalized"/> to the trimmed value.
    ///     If the input is <c>null</c> or trims to empty, returns <c>true</c> with <paramref name="normalized"/> = <c>null</c>.
    ///     On failure, returns <c>false</c> and provides a human-readable <paramref name="error"/>.
    /// </summary>
    public static bool TryNormalize(string? input, out string? normalized, [NotNullWhen(false)] out string? error)
    {
        normalized = null;
        error = null;

        if (input is null)
        {
            // Optional field: absent is acceptable
            return true;
        }

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            // Treat empty as not supplied
            return true;
        }

        if (trimmed.Length > MaxLength)
        {
            error = $"RunName exceeds maximum length of {MaxLength} characters.";
            return false;
        }

        // Quick control-char / non-printable guard
        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch))
            {
                error = "RunName contains control characters which are not allowed.";
                return false;
            }
        }

        if (!AllowedPattern.IsMatch(trimmed))
        {
            error = "RunName contains invalid characters. Allowed: letters, numbers, space, dot (.), underscore (_), hyphen (-).";
            return false;
        }

        normalized = trimmed; // Preserve case as provided
        return true;
    }
}
