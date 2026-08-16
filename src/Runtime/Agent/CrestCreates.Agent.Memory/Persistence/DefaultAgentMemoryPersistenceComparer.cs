using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Persistence;

/// <summary>
/// Provider-neutral persisted-snapshot equality for exact replay. Records'
/// default equality does not recurse into collection-valued fields, so nested
/// provenance (SourceRefs → DescriptorRefs, Diagnostics → SourceRefs) must be
/// compared explicitly: two semantically identical snapshots that differ only
/// by freshly allocated arrays must still compare equal.
/// </summary>
public sealed class DefaultAgentMemoryPersistenceComparer : IAgentMemoryPersistenceComparer
{
    public bool Equals(AgentMemoryItem left, AgentMemoryItem right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
            && string.Equals(left.MemoryId, right.MemoryId, StringComparison.Ordinal)
            && left.Kind == right.Kind
            && string.Equals(left.Content, right.Content, StringComparison.Ordinal)
            && left.CanonicalContentHash.Equals(right.CanonicalContentHash)
            && left.PromotedAt == right.PromotedAt
            && left.Confidence == right.Confidence
            && left.Status == right.Status
            && left.IsAuthoritative == right.IsAuthoritative
            && left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal)
            && left.DescriptorRefs.SequenceEqual(right.DescriptorRefs)
            && SourceRefsEqual(left.SourceRefs, right.SourceRefs)
            && string.Equals(left.SupersedesMemoryId, right.SupersedesMemoryId, StringComparison.Ordinal)
            && string.Equals(left.SupersededByMemoryId, right.SupersededByMemoryId, StringComparison.Ordinal)
            && left.RedactionKinds.SequenceEqual(right.RedactionKinds, StringComparer.Ordinal)
            && DiagnosticsEqual(left.SanitizationDiagnostics, right.SanitizationDiagnostics);
    }

    private static bool SourceRefsEqual(
        IReadOnlyList<AgentContextSourceRef> left,
        IReadOnlyList<AgentContextSourceRef> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (a.SourceKind != b.SourceKind
                || !string.Equals(a.TenantId, b.TenantId, StringComparison.Ordinal)
                || !string.Equals(a.SourceId, b.SourceId, StringComparison.Ordinal)
                || a.RangeStart != b.RangeStart
                || a.RangeEnd != b.RangeEnd
                || !a.DescriptorRefs.SequenceEqual(b.DescriptorRefs)
                || !string.Equals(a.CorrelationId, b.CorrelationId, StringComparison.Ordinal)
                || !string.Equals(a.CausationId, b.CausationId, StringComparison.Ordinal)
                || !CanonicalHashEqual(a.CanonicalContentHash, b.CanonicalContentHash))
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanonicalHashEqual(CanonicalHash? left, CanonicalHash? right)
        => left is null ? right is null : right is not null && left.Equals(right);

    private static bool DiagnosticsEqual(
        IReadOnlyList<AgentMemoryDiagnostic> left,
        IReadOnlyList<AgentMemoryDiagnostic> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (!a.Code.Equals(b.Code)
                || !string.Equals(a.Message, b.Message, StringComparison.Ordinal)
                || a.Severity != b.Severity
                || !SourceRefsEqual(a.SourceRefs, b.SourceRefs))
            {
                return false;
            }
        }
        return true;
    }
}
