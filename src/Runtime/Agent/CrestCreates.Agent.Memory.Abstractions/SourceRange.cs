namespace CrestCreates.Agent.Memory.Abstractions;

/// <summary>
/// Shared range validation for source ref ranges.
/// Both ClosureProviders and Expander must use this to ensure
/// Credential Contract matches the authorized operation's actual Contract.
/// 
/// Formal contract:
/// - RangeStart / RangeEnd must both be absent or both be present
/// - Start >= 0
/// - End >= Start  
/// - End < count
/// </summary>
public static class SourceRange
{
    /// <summary>
    /// Validates and resolves the range from a source ref.
    /// Returns false if the range is invalid (partial, negative, or out-of-bounds).
    /// When no range is specified, start and end are null (meaning "all items").
    /// </summary>
    public static bool TryResolve(
        AgentContextSourceRef sourceRef,
        int count,
        out int? start,
        out int? end)
    {
        start = sourceRef.RangeStart;
        end = sourceRef.RangeEnd;

        // No range specified — all items
        if (!start.HasValue && !end.HasValue)
            return true;

        // Partial range: only one bound specified
        if (!start.HasValue || !end.HasValue)
            return false;

        // Negative indices
        if (start.Value < 0 || end.Value < 0)
            return false;

        // End before Start
        if (end.Value < start.Value)
            return false;

        // Out of bounds
        if (start.Value >= count || end.Value >= count)
            return false;

        return true;
    }
}
