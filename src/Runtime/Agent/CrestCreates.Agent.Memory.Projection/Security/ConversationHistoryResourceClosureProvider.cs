using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class ConversationHistoryResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentConversationStore _store;

    public ConversationHistoryResourceClosureProvider(
        IAgentConversationStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.ConversationHistory.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _store.GetConversationAsync(tenantId, resourceId, cancellationToken);
        if (conversation is null) return null;

        var descriptorRefs = conversation.Turns
            .SelectMany(t => t.DescriptorRefs)
            .Distinct()
            .ToArray();

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = conversation.TenantId
        };
    }
}
