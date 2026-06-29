using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Recall;

public sealed class DefaultAgentContextSourceExpander : IAgentContextSourceExpander
{
    private readonly IAgentConversationStore _conversationStore;
    private readonly IAgentTaskHistoryStore _taskStore;
    private readonly IAgentCompressedContextStore _contextStore;
    private readonly IAgentMemoryStore _memoryStore;

    public DefaultAgentContextSourceExpander(
        IAgentConversationStore conversationStore,
        IAgentTaskHistoryStore taskStore,
        IAgentCompressedContextStore contextStore,
        IAgentMemoryStore memoryStore)
    {
        _conversationStore = conversationStore;
        _taskStore = taskStore;
        _contextStore = contextStore;
        _memoryStore = memoryStore;
    }

    public async ValueTask<AgentSourceExpansionResult> ExpandAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken = default)
    {
        return sourceRef.SourceKind switch
        {
            AgentSourceKind.ConversationTurn => await ExpandConversationAsync(sourceRef, cancellationToken),
            AgentSourceKind.TaskRecord or AgentSourceKind.TaskEvent => await ExpandTaskAsync(sourceRef, cancellationToken),
            AgentSourceKind.CompressedContextBlock => await ExpandCompressedContextAsync(sourceRef, cancellationToken),
            AgentSourceKind.MemoryItem => await ExpandMemoryItemAsync(sourceRef, cancellationToken),
            AgentSourceKind.MemoryCandidate => await ExpandMemoryCandidateAsync(sourceRef, cancellationToken),
            _ => NotExpandable(sourceRef)
        };
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandConversationAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        var conversation = await _conversationStore.GetConversationAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (conversation is null)
            return NotFound(sourceRef);

        var content = string.Join("\n", conversation.Turns
            .Where((_, i) => sourceRef.RangeStart is not { } start || i >= start)
            .Where((_, i) => sourceRef.RangeEnd is not { } end || i <= end)
            .Select(t => $"[{t.Role}] {t.Content}"));

        return Expanded(sourceRef, content);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandTaskAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        var task = await _taskStore.GetTaskAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (task is null)
            return NotFound(sourceRef);

        if (sourceRef.SourceKind == AgentSourceKind.TaskEvent)
        {
            var events = task.Events
                .Where((_, i) => sourceRef.RangeStart is not { } start || i >= start)
                .Where((_, i) => sourceRef.RangeEnd is not { } end || i <= end)
                .ToList();

            var content = string.Join("\n", events.Select(e => $"[{e.EventKind}] {e.Content}"));
            return Expanded(sourceRef, content);
        }

        // TaskRecord — return summary
        var taskContent = $"Task: {task.Title}\nSummary: {task.Summary ?? "N/A"}\nEvents: {task.Events.Count}";
        return Expanded(sourceRef, taskContent);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandCompressedContextAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        var context = await _contextStore.GetCompressedContextAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (context is null)
            return NotFound(sourceRef);

        var content = string.Join("\n---\n", context.Blocks.Select(b => b.Content));
        return Expanded(sourceRef, content);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandMemoryItemAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        var memory = await _memoryStore.GetMemoryAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (memory is null)
            return NotFound(sourceRef);

        return Expanded(sourceRef, memory.Content);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandMemoryCandidateAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        var candidate = await _memoryStore.GetCandidateAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (candidate is null)
            return NotFound(sourceRef);

        return Expanded(sourceRef, candidate.Content);
    }

    private static AgentSourceExpansionResult Expanded(AgentContextSourceRef sourceRef, string content) => new()
    {
        SourceRef = sourceRef,
        Status = AgentMemorySourceExpansionStatus.Expanded,
        SanitizedContent = content
    };

    private static AgentSourceExpansionResult NotFound(AgentContextSourceRef sourceRef) => new()
    {
        SourceRef = sourceRef,
        Status = AgentMemorySourceExpansionStatus.NotFound,
        Diagnostics = [new AgentMemoryDiagnostic
        {
            Code = AgentMemoryDiagnosticCodes.SourceNotFound,
            Message = $"Source '{sourceRef.SourceId}' of kind '{sourceRef.SourceKind}' not found for tenant '{sourceRef.TenantId}'.",
            Severity = SeverityLevel.Warning,
            SourceRefs = [sourceRef]
        }]
    };

    private static AgentSourceExpansionResult NotExpandable(AgentContextSourceRef sourceRef) => new()
    {
        SourceRef = sourceRef,
        Status = AgentMemorySourceExpansionStatus.NotExpandable,
        Diagnostics = [new AgentMemoryDiagnostic
        {
            Code = AgentMemoryDiagnosticCodes.SourceNotExpandable,
            Message = $"Source kind '{sourceRef.SourceKind}' is not expandable.",
            Severity = SeverityLevel.Info,
            SourceRefs = [sourceRef]
        }]
    };
}
