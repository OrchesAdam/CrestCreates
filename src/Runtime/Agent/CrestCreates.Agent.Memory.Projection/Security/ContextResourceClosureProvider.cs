using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class ContextResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentCompressedContextStore _store;

    public ContextResourceClosureProvider(
        IAgentCompressedContextStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.Context.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default)
    {
        // For CompressedContextBlock grants, resourceId is the ContextId
        // and sourceRef.SourceId may contain a specific BlockId.
        // Compute closure from the specific block if sourceRef is provided,
        // otherwise from the entire context.
        var context = await _store.GetCompressedContextAsync(tenantId, resourceId, cancellationToken);
        if (context is null) return null;

        IReadOnlyList<DescriptorRef> descriptorRefs;

        if (sourceRef is not null
            && !string.IsNullOrEmpty(sourceRef.SourceId)
            && !string.Equals(sourceRef.SourceId, resourceId, StringComparison.Ordinal))
        {
            // sourceRef.SourceId is a BlockId — compute closure from that specific block only
            var block = context.Blocks?.FirstOrDefault(b =>
                string.Equals(b.BlockId, sourceRef.SourceId, StringComparison.Ordinal));
            if (block is null) return null;

            descriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosure(
                null, block.SourceRefs);
        }
        else
        {
            // No specific block — compute from entire context
            descriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosureFromBlocks(context.Blocks);
        }

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = context.TenantId
        };
    }
}
