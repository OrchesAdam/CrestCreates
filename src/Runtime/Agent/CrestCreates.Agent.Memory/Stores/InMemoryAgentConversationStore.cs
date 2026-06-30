using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;

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
        var sanitizedTurns = new List<AgentConversationTurn>();
        var diagnostics = new List<AgentMemoryDiagnostic>();
        var rejectedCount = 0;

        foreach (var turn in conversation.Turns)
        {
            var sanitized = _sanitizer.Sanitize(conversation.TenantId, turn.Content, turn.SourceRefs);

            if (sanitized.Rejected)
            {
                rejectedCount++;
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Turn '{turn.TurnId}' was rejected after sanitization and will not be stored.",
                    Severity = SeverityLevel.Warning,
                    SourceRefs = turn.SourceRefs
                });
                continue;
            }

            sanitizedTurns.Add(turn with
            {
                Content = sanitized.SanitizedContent,
                DescriptorRefs = turn.DescriptorRefs.ToArray(),
                SourceRefs = turn.SourceRefs.ToArray(),
                Diagnostics = sanitized.Diagnostics.ToArray()
            });
        }

        var record = conversation with
        {
            Turns = sanitizedTurns.ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
        _conversations[(conversation.TenantId, conversation.ConversationId)] = record.Snapshot();
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentConversationRecord?> GetConversationAsync(string tenantId, string conversationId, CancellationToken cancellationToken = default)
    {
        _conversations.TryGetValue((tenantId, conversationId), out var conversation);
        if (conversation is null) return new ValueTask<AgentConversationRecord?>((AgentConversationRecord?)null);

        var snapshot = conversation.Snapshot();
        return new ValueTask<AgentConversationRecord?>(snapshot);
    }
}
