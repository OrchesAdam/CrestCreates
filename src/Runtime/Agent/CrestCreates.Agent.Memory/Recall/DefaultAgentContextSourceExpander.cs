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

        if (!SourceRange.TryResolve(sourceRef, conversation.Turns.Count, out var start, out var end))
            return NotFound(sourceRef);

        var turns = start.HasValue
            ? conversation.Turns.Skip(start.Value).Take(end!.Value - start.Value + 1)
            : conversation.Turns;
        var content = string.Join("\n", turns.Select(t => $"[{t.Role}] {t.Content}"));

        return Expanded(sourceRef, content);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandTaskAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        var task = await _taskStore.GetTaskAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (task is null)
            return NotFound(sourceRef);

        if (sourceRef.SourceKind == AgentSourceKind.TaskEvent)
        {
            if (!SourceRange.TryResolve(sourceRef, task.Events.Count, out var start, out var end))
                return NotFound(sourceRef);

            var events = start.HasValue
                ? task.Events.Skip(start.Value).Take(end!.Value - start.Value + 1)
                : task.Events;

            var content = string.Join("\n", events.Select(e => $"[{e.EventKind}] {e.Content}"));
            return Expanded(sourceRef, content);
        }

        if (sourceRef.RangeStart.HasValue || sourceRef.RangeEnd.HasValue)
            return NotFound(sourceRef);

        // TaskRecord — return summary
        var taskContent = $"Task: {task.Title}\nSummary: {task.Summary ?? "N/A"}\nEvents: {task.Events.Count}";
        return Expanded(sourceRef, taskContent);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandCompressedContextAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        if (sourceRef.RangeStart.HasValue || sourceRef.RangeEnd.HasValue)
            return NotFound(sourceRef);

        var block = await _contextStore.GetCompressedContextBlockAsync(
            sourceRef.TenantId,
            sourceRef.SourceId,
            cancellationToken);
        return block is null
            ? NotFound(sourceRef)
            : Expanded(sourceRef, block.Content);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandMemoryItemAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        if (sourceRef.RangeStart.HasValue || sourceRef.RangeEnd.HasValue)
            return NotFound(sourceRef);
        var memory = await _memoryStore.GetMemoryAsync(sourceRef.TenantId, sourceRef.SourceId, cancellationToken);
        if (memory is null)
            return NotFound(sourceRef);

        return Expanded(sourceRef, memory.Content);
    }

    private async ValueTask<AgentSourceExpansionResult> ExpandMemoryCandidateAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken)
    {
        if (sourceRef.RangeStart.HasValue || sourceRef.RangeEnd.HasValue)
            return NotFound(sourceRef);
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
