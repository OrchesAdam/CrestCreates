using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Unified helper for computing effective descriptor closure.
/// EffectiveClosure = Resource.DescriptorRefs ∪ SourceRefs.SelectMany(DescriptorRefs)
/// → Distinct → Canonical Order (by Namespace, Id, Version)
/// </summary>
internal static class EffectiveClosureHelper
{
    public static IReadOnlyList<DescriptorRef> ComputeEffectiveClosure(
        IReadOnlyList<DescriptorRef>? resourceRefs,
        IReadOnlyList<AgentContextSourceRef>? sourceRefs)
    {
        var result = new List<DescriptorRef>();

        if (resourceRefs is { Count: > 0 })
            result.AddRange(resourceRefs);

        if (sourceRefs is { Count: > 0 })
        {
            foreach (var sourceRef in sourceRefs)
            {
                if (sourceRef.DescriptorRefs is { Count: > 0 })
                    result.AddRange(sourceRef.DescriptorRefs);
            }
        }

        return result
            .Distinct()
            .OrderBy(r => r.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version)
            .ToList();
    }

    /// <summary>
    /// Computes the effective descriptor closure from all blocks' source refs.
    /// EffectiveClosure = ⋃(Blocks.SelectMany(b => b.SourceRefs).SelectMany(sr => sr.DescriptorRefs))
    /// </summary>
    public static IReadOnlyList<DescriptorRef> ComputeEffectiveClosureFromBlocks(
        IReadOnlyList<AgentCompressedContextBlock>? blocks)
    {
        var result = new List<DescriptorRef>();

        if (blocks is { Count: > 0 })
        {
            foreach (var block in blocks)
            {
                if (block.SourceRefs is { Count: > 0 })
                {
                    foreach (var sourceRef in block.SourceRefs)
                    {
                        if (sourceRef.DescriptorRefs is { Count: > 0 })
                            result.AddRange(sourceRef.DescriptorRefs);
                    }
                }
            }
        }

        return result
            .Distinct()
            .OrderBy(r => r.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version)
            .ToList();
    }
}
