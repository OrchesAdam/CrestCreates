using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentConversationStore : IAgentConversationStore
{
    private readonly ConcurrentDictionary<(string TenantId, string ConversationId), AgentConversationRecord> _conversations = new();
    private readonly IAgentMemoryContentSanitizer _sanitizer;

    public InMemoryAgentConversationStore(IAgentMemoryContentSanitizer sanitizer)
    {
        _sanitizer = sanitizer;
    }

    public ValueTask SaveConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default)
    {
        var sanitizedTurns = conversation.Turns.Select(t =>
        {
            var sanitized = _sanitizer.Sanitize(conversation.TenantId, t.Content, t.SourceRefs);
            return t with
            {
                Content = sanitized.SanitizedContent,
                DescriptorRefs = t.DescriptorRefs.ToArray(),
                SourceRefs = t.SourceRefs.ToArray()
            };
        }).ToArray();

        _conversations[(conversation.TenantId, conversation.ConversationId)] = conversation with
        {
            Turns = sanitizedTurns
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentConversationRecord?> GetConversationAsync(string tenantId, string conversationId, CancellationToken cancellationToken = default)
    {
        _conversations.TryGetValue((tenantId, conversationId), out var conversation);
        if (conversation is null) return new ValueTask<AgentConversationRecord?>((AgentConversationRecord?)null);

        var snapshot = conversation with
        {
            Turns = conversation.Turns
                .Select(t => t with { DescriptorRefs = t.DescriptorRefs.ToArray(), SourceRefs = t.SourceRefs.ToArray() })
                .ToArray()
        };
        return new ValueTask<AgentConversationRecord?>(snapshot);
    }
}
