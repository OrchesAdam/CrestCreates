using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class ConversationHistoryResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentConversationStore _store;
    private readonly ITenantContext _tenantContext;

    public ConversationHistoryResourceClosureProvider(
        IAgentConversationStore store,
        ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    public string ResourceKind => AgentMemoryResourceKind.ConversationHistory.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId is null) return null;

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
