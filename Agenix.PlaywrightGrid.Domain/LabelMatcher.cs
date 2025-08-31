using System;
using System.Collections.Generic;
using System.Linq;

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
/// Default implementation of <see cref="ILabelMatcher"/>.
/// Performs ordered matching: exact → trailing fallback → prefix expansion with optional wildcard segments.
/// </summary>
public sealed class LabelMatcher : ILabelMatcher
{
    private readonly LabelMatchingOptions _options;

    public LabelMatcher(LabelMatchingOptions? options = null)
    {
        _options = options ?? LabelMatchingOptions.Default;
        if (_options.MinSegmentsForFallback < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinSegmentsForFallback must be >= 1");
    }

    public LabelKey? TryMatch(LabelKey requested, IEnumerable<LabelKey> available)
    {
        var list = available as IList<LabelKey> ?? available.ToList();
        if (list.Count == 0) return null;

        // 1) Exact (same segment count)
        var exact = list
            .Where(a => a.Segments.Count == requested.Segments.Count && SegmentsEqual(requested.Segments, a.Segments, _options.WildcardsEnabled))
            .OrderBy(a => a.Normalized, StringComparer.Ordinal)
            .FirstOrDefault();
        if (exact is not null) return exact;

        // 2) Trailing fallback (drop segments until MinSegmentsForFallback)
        if (_options.TrailingFallbackEnabled)
        {
            for (var len = requested.Segments.Count - 1; len >= Math.Min(_options.MinSegmentsForFallback, requested.Segments.Count); len--)
            {
                if (len < 1) break;
                var fallback = list
                    .Where(a => a.Segments.Count == len && SegmentsEqualPrefix(requested.Segments, a.Segments, len, _options.WildcardsEnabled))
                    .OrderBy(a => a.Normalized, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (fallback is not null)
                    return fallback;
            }
        }

        // 3) Prefix expansion (accept more specific available that start with requested)
        if (_options.PrefixExpansionEnabled)
        {
            var candidates = list
                .Where(a => a.Segments.Count > requested.Segments.Count
                            && SegmentsStartWith(a.Segments, requested.Segments, _options.WildcardsEnabled))
                .ToList();
            if (candidates.Count > 0)
            {
                var minLen = candidates.Min(c => c.Segments.Count);
                return candidates
                    .Where(c => c.Segments.Count == minLen)
                    .OrderBy(c => c.Normalized, StringComparer.Ordinal)
                    .First();
            }
        }

        return null;
    }

    private static bool SegmentsEqual(IReadOnlyList<string> requested, IReadOnlyList<string> candidate, bool wildcards)
    {
        if (requested.Count != candidate.Count) return false;
        for (var i = 0; i < requested.Count; i++)
        {
            var r = requested[i];
            var c = candidate[i];
            if (wildcards && r == "*") continue;
            if (!string.Equals(r, c, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool SegmentsEqualPrefix(IReadOnlyList<string> requested, IReadOnlyList<string> candidate, int length, bool wildcards)
    {
        if (candidate.Count != length) return false;
        for (var i = 0; i < length; i++)
        {
            var r = requested[i];
            var c = candidate[i];
            if (wildcards && r == "*") continue;
            if (!string.Equals(r, c, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool SegmentsStartWith(IReadOnlyList<string> candidate, IReadOnlyList<string> prefix, bool wildcards)
    {
        if (prefix.Count > candidate.Count) return false;
        for (var i = 0; i < prefix.Count; i++)
        {
            var r = prefix[i];
            var c = candidate[i];
            if (wildcards && r == "*") continue;
            if (!string.Equals(r, c, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
