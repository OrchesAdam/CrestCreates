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
        // Handle closure: resourceId is the ContextId, no sourceRef.
        // Grant closure: sourceRef is provided with the specific source kind and identity.
        //
        // CompressedContextBlock grants: sourceRef.SourceId is a BlockId.
        //   Query the block directly — do NOT assume resourceId == ContextId.
        // Context grants: resourceId is the ContextId.
        //   Query the context and compute closure from all blocks.

        if (sourceRef is not null
            && sourceRef.SourceKind == AgentSourceKind.CompressedContextBlock
            && !string.IsNullOrEmpty(sourceRef.SourceId))
        {
            // Grant closure for a specific CompressedContextBlock.
            // sourceRef.SourceId is the BlockId — query the block directly.
            var block = await _store.GetCompressedContextBlockAsync(
                tenantId, sourceRef.SourceId, cancellationToken);
            if (block is null) return null;

            var descriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosure(
                null, block.SourceRefs);

            return new AgentMemoryCurrentClosure
            {
                CurrentDescriptorRefs = descriptorRefs,
                TenantId = tenantId
            };
        }

        // Handle closure for Context, or grant closure without specific block.
        // resourceId is the ContextId.
        var context = await _store.GetCompressedContextAsync(tenantId, resourceId, cancellationToken);
        if (context is null) return null;

        var contextDescriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosureFromBlocks(context.Blocks);

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = contextDescriptorRefs,
            TenantId = context.TenantId
        };
    }
}
