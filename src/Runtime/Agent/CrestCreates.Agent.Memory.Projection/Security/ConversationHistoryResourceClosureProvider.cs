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
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _store.GetConversationAsync(tenantId, resourceId, cancellationToken);
        if (conversation is null) return null;

        // If the source ref specifies a turn range, only include descriptors from that range.
        // Otherwise include all turns.
        var turns = conversation.Turns.AsEnumerable();
        if (sourceRef is { RangeStart: not null } || sourceRef is { RangeEnd: not null })
        {
            var start = sourceRef.RangeStart ?? 0;
            var end = sourceRef.RangeEnd ?? turns.Count();
            turns = turns.Skip(start).Take(end - start + 1);
        }

        var descriptorRefs = turns
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
