using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;

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
        var diagnostics = new List<AgentMemoryDiagnostic>();

        string? sanitizedSummary = null;
        if (task.Summary is not null)
        {
            var summaryResult = _sanitizer.Sanitize(task.TenantId, task.Summary, Array.Empty<AgentContextSourceRef>());
            if (summaryResult.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Task '{task.TaskId}' summary was rejected after sanitization and will be set to null.",
                    Severity = SeverityLevel.Warning
                });
                sanitizedSummary = null;
            }
            else
            {
                sanitizedSummary = summaryResult.SanitizedContent;
                diagnostics.AddRange(summaryResult.Diagnostics);
            }
        }

        var sanitizedEvents = new List<AgentTaskEvent>();
        foreach (var e in task.Events)
        {
            var sanitized = _sanitizer.Sanitize(task.TenantId, e.Content, e.SourceRefs);
            if (sanitized.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Event '{e.EventId}' was rejected after sanitization and will not be stored.",
                    Severity = SeverityLevel.Warning,
                    SourceRefs = e.SourceRefs
                });
                continue;
            }
            sanitizedEvents.Add(e with
            {
                Content = sanitized.SanitizedContent,
                SourceRefs = e.SourceRefs.ToArray(),
                Diagnostics = sanitized.Diagnostics.ToArray()
            });
        }

        var record = task with
        {
            Summary = sanitizedSummary,
            Events = sanitizedEvents.ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
        _tasks[(task.TenantId, task.TaskId)] = record.Snapshot();
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentTaskRecord?> GetTaskAsync(string tenantId, string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue((tenantId, taskId), out var task);
        if (task is null) return new ValueTask<AgentTaskRecord?>((AgentTaskRecord?)null);

        var snapshot = task.Snapshot();
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
        if (sanitized.Rejected)
        {
            // Skip the event — content was entirely rejected after sanitization
            return ValueTask.CompletedTask;
        }

        var sanitizedEvent = taskEvent with
        {
            Content = sanitized.SanitizedContent,
            SourceRefs = taskEvent.SourceRefs.ToArray(),
            Diagnostics = sanitized.Diagnostics.ToArray()
        };

        var updated = existing with
        {
            Events = [..existing.Events, sanitizedEvent]
        };
        _tasks[key] = updated.Snapshot();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AgentTaskRecord>> ListTasksAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var tasks = _tasks.Values
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.TaskId, StringComparer.Ordinal)
            .Select(t => t.Snapshot())
            .ToArray();
        return new ValueTask<IReadOnlyList<AgentTaskRecord>>(tasks);
    }
}
