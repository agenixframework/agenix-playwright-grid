using System.Collections.Generic;

namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
/// Central label matching strategy service.
/// Implements ordered resolution: exact → trailing fallback → prefix expansion → optional wildcards.
/// </summary>
public interface ILabelMatcher
{
    /// <summary>
    /// Attempts to find the best matching available label for the requested <see cref="LabelKey"/>.
    /// Returns null when no match is found according to the configured <see cref="LabelMatchingOptions"/>.
    /// </summary>
    LabelKey? TryMatch(LabelKey requested, IEnumerable<LabelKey> available);
}
