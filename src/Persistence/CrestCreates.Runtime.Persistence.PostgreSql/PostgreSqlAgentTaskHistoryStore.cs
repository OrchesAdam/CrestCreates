using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Task history store participant. Save/Append sanitize Summary and
/// Event content before JSON materialization; Task identity advisory locks
/// serialize missing-row creation against concurrent appends.
/// </summary>
internal sealed class PostgreSqlAgentTaskHistoryStore : IAgentTaskHistoryStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly IAgentMemoryContentSanitizer _sanitizer;

    public PostgreSqlAgentTaskHistoryStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        IAgentMemoryContentSanitizer sanitizer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
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
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentTaskRecord?> GetCoreAsync(string tenantId, string taskId, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask AppendCoreAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<IReadOnlyList<AgentTaskRecord>> ListCoreAsync(string tenantId, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");
}
