using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

public sealed class AgentContextSourceRefCanonicalComparer : IEqualityComparer<AgentContextSourceRef>
{
    public static AgentContextSourceRefCanonicalComparer Instance { get; } = new();

    public bool Equals(AgentContextSourceRef? x, AgentContextSourceRef? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.SourceKind == y.SourceKind
            && string.Equals(x.TenantId, y.TenantId, StringComparison.Ordinal)
            && string.Equals(x.SourceId, y.SourceId, StringComparison.Ordinal)
            && x.RangeStart == y.RangeStart && x.RangeEnd == y.RangeEnd
            && string.Equals(x.CorrelationId, y.CorrelationId, StringComparison.Ordinal)
            && string.Equals(x.CausationId, y.CausationId, StringComparison.Ordinal)
            && Equals(x.CanonicalContentHash, y.CanonicalContentHash)
            && DescriptorRefsEqual(x.DescriptorRefs, y.DescriptorRefs);
    }

    public int GetHashCode(AgentContextSourceRef obj)
    {
        var hash = new HashCode();
        hash.Add(obj.SourceKind);
        hash.Add(obj.TenantId, StringComparer.Ordinal);
        hash.Add(obj.SourceId, StringComparer.Ordinal);
        hash.Add(obj.RangeStart); hash.Add(obj.RangeEnd);
        hash.Add(obj.CorrelationId, StringComparer.Ordinal);
        hash.Add(obj.CausationId, StringComparer.Ordinal);
        hash.Add(obj.CanonicalContentHash);
        foreach (var descriptor in obj.DescriptorRefs.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version))
            hash.Add(descriptor);
        return hash.ToHashCode();
    }

    private static bool DescriptorRefsEqual(IReadOnlyList<CrestCreates.Metadata.Abstractions.DescriptorRef> left, IReadOnlyList<CrestCreates.Metadata.Abstractions.DescriptorRef> right)
        => left.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version)
            .SequenceEqual(right.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version));
}
