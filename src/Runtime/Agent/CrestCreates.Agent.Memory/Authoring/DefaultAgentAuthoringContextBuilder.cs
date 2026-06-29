using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.Memory.Authoring;

public sealed class DefaultAgentAuthoringContextBuilder : IAgentAuthoringContextBuilder
{
    public ValueTask<AgentAuthoringContext> BuildAsync(
        AgentAuthoringRequest request,
        MetadataContextPack metadataContextPack,
        AgentMemoryPack memoryPack,
        CancellationToken cancellationToken = default)
    {
        var context = new AgentAuthoringContext
        {
            Request = request,
            MetadataContextPack = metadataContextPack,
            MemoryPack = memoryPack
        };
        return new ValueTask<AgentAuthoringContext>(context);
    }
}
