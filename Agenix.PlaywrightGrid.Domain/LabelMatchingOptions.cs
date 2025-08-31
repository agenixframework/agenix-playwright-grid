namespace Agenix.PlaywrightGrid.Domain;

/// <summary>
/// Options controlling how label matching is performed.
/// </summary>
public sealed class LabelMatchingOptions
{
    /// <summary>
    /// A reusable default set of matching options: trailing + prefix enabled, wildcards disabled.
    /// </summary>
    public static readonly LabelMatchingOptions Default = new();

    /// <summary>
    /// When true, after trying exact matches, the matcher will progressively drop trailing segments
    /// (down to <see cref="MinSegmentsForFallback"/>) to find a less specific available label.
    /// </summary>
    public bool TrailingFallbackEnabled { get; init; } = true;

    /// <summary>
    /// When true, if no exact or trailing match is found, the matcher will accept longer available labels that start
    /// with the requested segments (prefix expansion).
    /// </summary>
    public bool PrefixExpansionEnabled { get; init; } = true;

    /// <summary>
    /// When true, the requested label may include '*' wildcard segments which match any single segment.
    /// Wildcards are considered during exact and prefix expansion phases.
    /// </summary>
    public bool WildcardsEnabled { get; init; } = false;

    /// <summary>
    /// Do not fallback below this number of segments when dropping trailing segments. Default: 2 (App:Browser).
    /// </summary>
    public int MinSegmentsForFallback { get; init; } = 2;
}
