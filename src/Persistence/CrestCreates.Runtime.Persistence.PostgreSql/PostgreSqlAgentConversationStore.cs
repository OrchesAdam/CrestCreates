using CrestCreates.Agent.Memory.Abstractions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Conversation store. Sanitization and snapshot construction complete
/// before any JSON parameter or transaction is created; the aggregate snapshot
/// is stored beside structured identity/version columns.
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
        ArgumentNullException.ThrowIfNull(conversation);
        ct.ThrowIfCancellationRequested();

        var sanitizedTurns = new List<AgentConversationTurn>();
        var diagnostics = new List<AgentMemoryDiagnostic>();
        foreach (var turn in conversation.Turns)
        {
            var sanitized = _sanitizer.Sanitize(conversation.TenantId, turn.Content, turn.SourceRefs);
            if (sanitized.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Turn '{turn.TurnId}' was rejected after sanitization and will not be stored.",
                    Severity = Core.Abstractions.Identity.SeverityLevel.Warning,
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

        var record = (conversation with
        {
            Turns = sanitizedTurns.ToArray(),
            Diagnostics = diagnostics.ToArray()
        }).Snapshot();

        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            record, PostgreSqlRuntimeJsonSerializerContext.Default.AgentConversationRecord);
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_conversations")}
                (tenant_id, conversation_id, revision, state_contract_version, state_json, created_at, updated_at)
            values (@tenant, @conversation, 1, 1, @state, clock_timestamp(), clock_timestamp())
            on conflict (tenant_id, conversation_id) do update
                set state_json = excluded.state_json,
                    revision = {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_conversations")}.revision + 1,
                    updated_at = clock_timestamp()
            returning revision;
            """);
        command.Parameters.AddWithValue("tenant", record.TenantId);
        command.Parameters.AddWithValue("conversation", record.ConversationId);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(command, "state", serialized);
        using var lease = session.EnterCommand();
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentConversationRecord?> GetCoreAsync(string tenantId, string conversationId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, conversation_id, revision, state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_conversations")}
            where tenant_id = @tenant and conversation_id = @conversation;
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("conversation", conversationId);
        using var lease = session.EnterCommand();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var revision = reader.GetInt64(2);
        var contractVersion = reader.GetInt32(3);
        var stateJson = reader.GetString(4);
        var snapshot = PostgreSqlAgentMemoryRowMapper.MapConversation(
            tenantId, conversationId, revision, contractVersion, stateJson,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentConversationRecord);
        return snapshot;
    }
}
