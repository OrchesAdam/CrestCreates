using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Persistence;

namespace CrestCreates.Agent.Memory.Persistence;

/// <summary>
/// Exact persisted-snapshot equality for create-or-exact-replay Memory
/// semantics. Compares every persisted property, collection sequence, and
/// nested snapshot value. Never replaced by state-hash equality.
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
            && left.SourceRefs.SequenceEqual(right.SourceRefs)
            && string.Equals(left.SupersedesMemoryId, right.SupersedesMemoryId, StringComparison.Ordinal)
            && string.Equals(left.SupersededByMemoryId, right.SupersededByMemoryId, StringComparison.Ordinal)
            && left.RedactionKinds.SequenceEqual(right.RedactionKinds, StringComparer.Ordinal)
            && left.SanitizationDiagnostics.SequenceEqual(right.SanitizationDiagnostics);
    }
}
