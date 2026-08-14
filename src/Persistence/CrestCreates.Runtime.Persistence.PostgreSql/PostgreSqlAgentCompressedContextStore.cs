using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Compressed Context store participant. The parent aggregate and its
/// tenant-wide Block projection switch atomically; parent upsert always
/// precedes child Block INSERTs to satisfy the immediate foreign key.
/// </summary>
internal sealed class PostgreSqlAgentCompressedContextStore : IAgentCompressedContextStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly PostgreSqlAgentMemoryLockManager _lockManager;

    public PostgreSqlAgentCompressedContextStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        PostgreSqlAgentMemoryLockManager lockManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
    }

    public ValueTask SaveCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveCoreAsync(context, replace: true, ct), cancellationToken);

    public ValueTask CreateCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveCoreAsync(context, replace: false, ct), cancellationToken);

    public ValueTask<AgentCompressedContext?> GetCompressedContextAsync(string tenantId, string contextId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetCoreAsync(tenantId, contextId, ct), cancellationToken);

    public ValueTask<AgentCompressedContextBlock?> GetCompressedContextBlockAsync(string tenantId, string blockId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetBlockCoreAsync(tenantId, blockId, ct), cancellationToken);

    private async ValueTask SaveCoreAsync(AgentCompressedContext context, bool replace, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentCompressedContext?> GetCoreAsync(string tenantId, string contextId, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentCompressedContextBlock?> GetBlockCoreAsync(string tenantId, string blockId, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");
}
