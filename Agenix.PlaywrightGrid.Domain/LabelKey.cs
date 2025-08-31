using System;
using System.Collections.Generic;
using System.Linq;

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
/// Value object representing a label key like "App:Chromium:UAT[:Region[:OS…]]".
/// Provides parsing, validation and normalization to ensure consistent rules across components.
/// </summary>
public sealed class LabelKey : IEquatable<LabelKey>
{
    /// <summary>
    /// The original label key as provided by the caller (trimmed of surrounding whitespace).
    /// </summary>
    public string Original { get; }

    /// <summary>
    /// The normalized representation according to the supplied <see cref="LabelKeyParsingOptions"/>.
    /// </summary>
    public string Normalized { get; }

    /// <summary>
    /// The read-only list of label segments in order.
    /// </summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>
    /// Convenience accessor for the first segment (App). Empty string if missing.
    /// </summary>
    public string App => Segments.Count > 0 ? Segments[0] : string.Empty;

    /// <summary>
    /// Convenience accessor for the second segment (Browser). Empty string if missing.
    /// </summary>
    public string Browser => Segments.Count > 1 ? Segments[1] : string.Empty;

    /// <summary>
    /// Convenience accessor for the third segment (Environment). Empty string if missing.
    /// </summary>
    public string Env => Segments.Count > 2 ? Segments[2] : string.Empty;

    private LabelKey(string original, string normalized, IReadOnlyList<string> segments)
    {
        Original = original;
        Normalized = normalized;
        Segments = segments;
    }

    /// <summary>
    /// Returns the normalized label key string.
    /// </summary>
    public override string ToString() => Normalized;

    /// <summary>
    /// Value-based equality comparing the normalized representation.
    /// </summary>
    public bool Equals(LabelKey? other)
        => other is not null && string.Equals(Normalized, other.Normalized, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LabelKey lk && Equals(lk);

    /// <inheritdoc />
    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);

    /// <summary>
    /// Attempts to parse an input string into a <see cref="LabelKey"/> using the provided options.
    /// Returns true when parsing and validation succeed; otherwise false.
    /// </summary>
    public static bool TryParse(string? input, out LabelKey? value, LabelKeyParsingOptions? options = null)
    {
        return TryParseDetailed(input, out value, out _, options);
    }

    /// <summary>
    /// Attempts to parse an input string into a <see cref="LabelKey"/> using the provided options.
    /// Provides a human-friendly error message describing the first validation failure.
    /// </summary>
    public static bool TryParseDetailed(string? input, out LabelKey? value, out string? error, LabelKeyParsingOptions? options = null)
    {
        value = null;
        error = null;
        if (input is null)
        {
            error = "labelKey is null";
            return false;
        }
        options ??= LabelKeyParsingOptions.Default;

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            error = "labelKey is empty";
            return false;
        }
        if (trimmed.StartsWith(':'))
        {
            error = "labelKey cannot start with ':'";
            return false;
        }
        if (trimmed.EndsWith(':'))
        {
            error = "labelKey cannot end with ':'";
            return false;
        }
        if (trimmed.Contains("::"))
        {
            error = "labelKey cannot contain empty segments ('::')";
            return false;
        }

        // Normalization policy: trim each segment, keep case as-is by default, collapse multiple ':' already handled
        var rawSegments = trimmed.Split(':', StringSplitOptions.TrimEntries);
        if (rawSegments.Length < options.MinSegments)
        {
            error = $"labelKey must have at least {options.MinSegments} segments";
            return false;
        }
        if (rawSegments.Length > options.MaxSegments)
        {
            error = $"labelKey must have at most {options.MaxSegments} segments";
            return false;
        }

        // Validate forbidden characters if any
        if (options.ForbiddenChars is { Length: > 0 } fc)
        {
            foreach (var s in rawSegments)
            {
                if (s.Length == 0)
                {
                    error = "labelKey contains an empty segment";
                    return false;
                }
                if (s.IndexOfAny(fc) >= 0)
                {
                    error = "labelKey contains forbidden characters (whitespace not allowed within segments)";
                    return false;
                }
            }
        }

        // Enforce Browser as second segment if set
        if (rawSegments.Length >= 2 && options.EnforceBrowserSecond && !IsKnownBrowser(rawSegments[1]))
        {
            error = "second segment must be a known browser: Chromium | Firefox | WebKit";
            return false;
        }

        var normalizedSegments = options.CasePolicy switch
        {
            LabelKeyCasePolicy.Keep => rawSegments,
            LabelKeyCasePolicy.Lower => rawSegments.Select(x => x.ToLowerInvariant()).ToArray(),
            LabelKeyCasePolicy.Upper => rawSegments.Select(x => x.ToUpperInvariant()).ToArray(),
            _ => rawSegments
        };

        var normalized = string.Join(':', normalizedSegments);
        value = new LabelKey(trimmed, normalized, Array.AsReadOnly(normalizedSegments));
        return true;
    }

    private static bool IsKnownBrowser(string s)
        => string.Equals(s, "Chromium", StringComparison.OrdinalIgnoreCase)
           || string.Equals(s, "Firefox", StringComparison.OrdinalIgnoreCase)
           || string.Equals(s, "WebKit", StringComparison.OrdinalIgnoreCase);
}
