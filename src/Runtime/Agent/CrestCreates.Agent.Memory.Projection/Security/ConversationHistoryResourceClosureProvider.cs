using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using SourceRange = CrestCreates.Agent.Memory.Abstractions.SourceRange;

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

        // If the source ref specifies a turn range, validate it against the same
        // contract the Expander uses. Invalid range = resource not found (fail-closed).
        var turns = (IReadOnlyList<AgentConversationTurn>)conversation.Turns;
        if (sourceRef is not null)
        {
            if (!SourceRange.TryResolve(sourceRef, turns.Count, out var start, out var end))
                return null;

            if (start.HasValue)
            {
                turns = turns
                    .Skip(start.Value)
                    .Take(end!.Value - start.Value + 1)
                    .ToArray();
            }
        }

        var descriptorRefs = turns
            .SelectMany(t => EffectiveClosureHelper.ComputeEffectiveClosure(t.DescriptorRefs, t.SourceRefs))
            .Distinct()
            .OrderBy(r => r.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version)
            .ToArray();

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = conversation.TenantId
        };
    }
}
