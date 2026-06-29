using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.Memory.Authoring;

public sealed class DefaultAgentAuthoringContextBuilder : IAgentAuthoringContextBuilder
{
    private readonly IAgentMemoryRetriever _retriever;

    public DefaultAgentAuthoringContextBuilder(IAgentMemoryRetriever retriever)
    {
        _retriever = retriever;
    }

    public async ValueTask<AgentAuthoringContext> BuildAsync(AgentAuthoringRequest request, MetadataContextPack metadataContextPack, CancellationToken cancellationToken = default)
    {
        var query = request.MemoryQuery ?? new AgentMemoryQuery
        {
            TenantId = request.TenantId,
            IntentText = request.IntentText
        };

        var memoryPack = await _retriever.RecallAsync(query, cancellationToken);

        return new AgentAuthoringContext
        {
            Request = request,
            MetadataContextPack = metadataContextPack,
            MemoryPack = memoryPack
        };
    }
}
