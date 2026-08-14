using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Conversation store participant. Sanitization completes before any
/// JSON parameter or transaction is created; the aggregate snapshot is stored
/// beside structured identity/version columns.
/// </summary>
internal sealed class PostgreSqlAgentConversationStore : IAgentConversationStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly IAgentMemoryContentSanitizer _sanitizer;

    public PostgreSqlAgentConversationStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        IAgentMemoryContentSanitizer sanitizer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
    }

    public ValueTask SaveConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveCoreAsync(conversation, ct), cancellationToken);

    public ValueTask<AgentConversationRecord?> GetConversationAsync(string tenantId, string conversationId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetCoreAsync(tenantId, conversationId, ct), cancellationToken);

    private async ValueTask SaveCoreAsync(AgentConversationRecord conversation, CancellationToken ct)
    {
        throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");
    }

    private async ValueTask<AgentConversationRecord?> GetCoreAsync(string tenantId, string conversationId, CancellationToken ct)
    {
        throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");
    }
}
