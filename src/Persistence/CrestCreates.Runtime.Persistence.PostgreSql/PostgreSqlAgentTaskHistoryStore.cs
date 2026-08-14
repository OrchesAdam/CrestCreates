using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Task history store. Save/Append sanitize Summary and Event content
/// before JSON materialization; Task identity advisory locks serialize
/// missing-row creation against concurrent appends; row locks serialize
/// read-modify-write appends.
/// </summary>
internal sealed class PostgreSqlAgentTaskHistoryStore : IAgentTaskHistoryStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly IAgentMemoryContentSanitizer _sanitizer;
    private readonly PostgreSqlAgentMemoryLockManager _lockManager;

    public PostgreSqlAgentTaskHistoryStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        IAgentMemoryContentSanitizer sanitizer,
        PostgreSqlAgentMemoryLockManager lockManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
    }

    public ValueTask SaveTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveCoreAsync(task, ct), cancellationToken);

    public ValueTask<AgentTaskRecord?> GetTaskAsync(string tenantId, string taskId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetCoreAsync(tenantId, taskId, ct), cancellationToken);

    public ValueTask AppendEventAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => AppendCoreAsync(tenantId, taskId, taskEvent, ct), cancellationToken);

    public ValueTask<IReadOnlyList<AgentTaskRecord>> ListTasksAsync(string tenantId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ListCoreAsync(tenantId, ct), cancellationToken);

    private async ValueTask SaveCoreAsync(AgentTaskRecord task, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        ct.ThrowIfCancellationRequested();

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
                    Severity = Core.Abstractions.Identity.SeverityLevel.Warning
                });
            }
            else
            {
                sanitizedSummary = summaryResult.SanitizedContent;
                diagnostics.AddRange(summaryResult.Diagnostics);
            }
        }

        var sanitizedEvents = new List<AgentTaskEvent>();
        foreach (var taskEvent in task.Events)
        {
            var sanitized = _sanitizer.Sanitize(task.TenantId, taskEvent.Content, taskEvent.SourceRefs);
            if (sanitized.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Event '{taskEvent.EventId}' was rejected after sanitization and will not be stored.",
                    Severity = Core.Abstractions.Identity.SeverityLevel.Warning,
                    SourceRefs = taskEvent.SourceRefs
                });
                continue;
            }
            sanitizedEvents.Add(taskEvent with
            {
                Content = sanitized.SanitizedContent,
                SourceRefs = taskEvent.SourceRefs.ToArray(),
                Diagnostics = sanitized.Diagnostics.ToArray()
            });
        }

        var record = (task with
        {
            Summary = sanitizedSummary,
            Events = sanitizedEvents.ToArray(),
            Diagnostics = diagnostics.ToArray()
        }).Snapshot();

        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            record, PostgreSqlRuntimeJsonSerializerContext.Default.AgentTaskRecord);
        var session = _coordinator.RequireSession();
        await _lockManager.AcquireAsync(session, record.TenantId, "task", [record.TaskId], ct).ConfigureAwait(false);

        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_tasks")}
                (tenant_id, task_id, revision, state_contract_version, state_json, created_at, updated_at)
            values (@tenant, @task, 1, 1, @state, clock_timestamp(), clock_timestamp())
            on conflict (tenant_id, task_id) do update
                set state_json = excluded.state_json,
                    revision = {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_tasks")}.revision + 1,
                    updated_at = clock_timestamp()
            returning revision;
            """);
        command.Parameters.AddWithValue("tenant", record.TenantId);
        command.Parameters.AddWithValue("task", record.TaskId);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(command, "state", serialized);
        using var lease = session.EnterCommand();
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentTaskRecord?> GetCoreAsync(string tenantId, string taskId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, task_id, revision, state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_tasks")}
            where tenant_id = @tenant and task_id = @task;
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("task", taskId);
        using var lease = session.EnterCommand();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var revision = reader.GetInt64(2);
        var contractVersion = reader.GetInt32(3);
        var stateJson = reader.GetString(4);
        var snapshot = PostgreSqlAgentMemoryRowMapper.MapTask(
            tenantId, taskId, revision, contractVersion, stateJson,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentTaskRecord);
        return snapshot;
    }

    private async ValueTask AppendCoreAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taskEvent);
        ct.ThrowIfCancellationRequested();

        // Sanitize/copy the Event before any JSON materialization.
        var sanitized = _sanitizer.Sanitize(tenantId, taskEvent.Content, taskEvent.SourceRefs);
        var sanitizedEvent = sanitized.Rejected
            ? null
            : taskEvent with
            {
                Content = sanitized.SanitizedContent,
                SourceRefs = taskEvent.SourceRefs.ToArray(),
                Diagnostics = sanitized.Diagnostics.ToArray()
            };

        var session = _coordinator.RequireSession();
        await _lockManager.AcquireAsync(session, tenantId, "task", [taskId], ct).ConfigureAwait(false);

        AgentMemoryOperationException? unavailable = null;
        long revision = 0;
        int contractVersion = 0;
        string stateJson = string.Empty;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select revision, state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_tasks")}
            where tenant_id = @tenant and task_id = @task
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("task", taskId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                unavailable = new AgentMemoryOperationException(
                    AgentMemoryOperationFailureCode.ResourceUnavailable,
                    $"Task '{taskId}' is unavailable for tenant '{tenantId}'. Use SaveTaskAsync to create a task first.");
            }
            else
            {
                revision = reader.GetInt64(0);
                contractVersion = reader.GetInt32(1);
                stateJson = reader.GetString(2);
            }
        }
        if (unavailable is not null)
            throw unavailable;

        var current = PostgreSqlAgentMemoryRowMapper.MapTask(
            tenantId, taskId, revision, contractVersion, stateJson,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentTaskRecord);

        if (sanitizedEvent is null)
            return; // Rejected event: no-op, but the existence contract above still holds.

        var updated = (current with
        {
            Events = [.. current.Events, sanitizedEvent]
        }).Snapshot();
        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            updated, PostgreSqlRuntimeJsonSerializerContext.Default.AgentTaskRecord);

        await using (var update = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            update {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_tasks")}
            set state_json = @state,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant and task_id = @task;
            """))
        {
            update.Parameters.AddWithValue("tenant", tenantId);
            update.Parameters.AddWithValue("task", taskId);
            PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(update, "state", serialized);
            using var lease = session.EnterCommand();
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<AgentTaskRecord>> ListCoreAsync(string tenantId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, task_id, revision, state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_tasks")}
            where tenant_id = @tenant
            order by task_id collate "C";
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        var records = new List<AgentTaskRecord>();
        using (var lease = session.EnterCommand())
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                records.Add(PostgreSqlAgentMemoryRowMapper.MapTask(
                    tenantId,
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentTaskRecord));
            }
        }

        return records
            .OrderBy(record => record.TaskId, StringComparer.Ordinal)
            .Select(record => record.Snapshot())
            .ToArray();
    }
}
