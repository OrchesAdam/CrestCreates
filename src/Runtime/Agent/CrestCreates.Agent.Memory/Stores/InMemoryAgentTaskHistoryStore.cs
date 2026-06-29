using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentTaskHistoryStore : IAgentTaskHistoryStore
{
    private readonly ConcurrentDictionary<(string TenantId, string TaskId), AgentTaskRecord> _tasks = new();
    private readonly IAgentMemoryContentSanitizer _sanitizer;

    public InMemoryAgentTaskHistoryStore(IAgentMemoryContentSanitizer sanitizer)
    {
        _sanitizer = sanitizer;
    }

    public ValueTask SaveTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default)
    {
        var sanitizedSummary = task.Summary is not null
            ? _sanitizer.Sanitize(task.TenantId, task.Summary, Array.Empty<AgentContextSourceRef>()).SanitizedContent
            : null;

        var sanitizedEvents = task.Events.Select(e =>
        {
            var sanitized = _sanitizer.Sanitize(task.TenantId, e.Content, e.SourceRefs);
            return e with
            {
                Content = sanitized.SanitizedContent,
                SourceRefs = e.SourceRefs.ToArray()
            };
        }).ToArray();

        _tasks[(task.TenantId, task.TaskId)] = task with
        {
            Summary = sanitizedSummary,
            Events = sanitizedEvents
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentTaskRecord?> GetTaskAsync(string tenantId, string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue((tenantId, taskId), out var task);
        if (task is null) return new ValueTask<AgentTaskRecord?>((AgentTaskRecord?)null);

        var snapshot = task with
        {
            Events = task.Events
                .Select(e => e with { SourceRefs = e.SourceRefs.ToArray() })
                .ToArray()
        };
        return new ValueTask<AgentTaskRecord?>(snapshot);
    }

    public ValueTask AppendEventAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken cancellationToken = default)
    {
        var key = (tenantId, taskId);
        if (!_tasks.TryGetValue(key, out var existing))
        {
            throw new InvalidOperationException($"Task '{taskId}' not found for tenant '{tenantId}'. Use SaveTaskAsync to create a task first.");
        }

        var sanitized = _sanitizer.Sanitize(tenantId, taskEvent.Content, taskEvent.SourceRefs);
        var sanitizedEvent = taskEvent with
        {
            Content = sanitized.SanitizedContent,
            SourceRefs = taskEvent.SourceRefs.ToArray()
        };

        _tasks[key] = existing with
        {
            Events = [..existing.Events, sanitizedEvent]
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AgentTaskRecord>> ListTasksAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var tasks = _tasks.Values
            .Where(t => t.TenantId == tenantId)
            .Select(t => t with
            {
                Events = t.Events
                    .Select(e => e with { SourceRefs = e.SourceRefs.ToArray() })
                    .ToArray()
            })
            .ToArray();
        return new ValueTask<IReadOnlyList<AgentTaskRecord>>(tasks);
    }
}
