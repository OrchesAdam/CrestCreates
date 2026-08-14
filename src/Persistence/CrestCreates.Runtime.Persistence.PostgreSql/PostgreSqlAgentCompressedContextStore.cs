using CrestCreates.Agent.Memory.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Compressed Context store. The parent aggregate and its tenant-wide
/// Block projection switch atomically; parent upsert always precedes child
/// Block INSERTs to satisfy the immediate foreign key.
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
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();

        var duplicate = context.Blocks
            .GroupBy(block => block.BlockId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.IdentityConflict,
                "Compressed context contains duplicate BlockId values.");
        }
        if (context.Blocks.Any(block => !string.Equals(block.TenantId, context.TenantId, StringComparison.Ordinal)))
        {
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.TenantMismatch,
                "Compressed context block tenant does not match the context tenant.");
        }

        var snapshot = context.Snapshot();
        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            snapshot, PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContext);
        var serializedBlocks = snapshot.Blocks
            .Select(block => PostgreSqlAgentMemoryStoreSupport.Serialize(
                block, PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock))
            .ToArray();

        var session = _coordinator.RequireSession();
        var existingBlockIds = new List<string>();
        var newBlockIds = snapshot.Blocks.Select(block => block.BlockId).ToArray();

        await _lockManager.AcquireAsync(session, context.TenantId, "context", [context.ContextId], ct).ConfigureAwait(false);

        await using (var existing = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select block_id
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_blocks")}
            where tenant_id = @tenant and context_id = @context;
            """))
        {
            existing.Parameters.AddWithValue("tenant", context.TenantId);
            existing.Parameters.AddWithValue("context", context.ContextId);
            using var lease = session.EnterCommand();
            await using var reader = await existing.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                existingBlockIds.Add(reader.GetString(0));
        }

        await _lockManager.AcquireAsync(
            session,
            context.TenantId,
            "block",
            existingBlockIds.Concat(newBlockIds).ToArray(),
            ct).ConfigureAwait(false);

        // Tenant-wide availability of the new Block IDs.
        if (newBlockIds.Length > 0)
        {
            await using var availability = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
                select block_id, context_id
                from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_blocks")}
                where tenant_id = @tenant and block_id = any(@blocks);
                """);
            availability.Parameters.AddWithValue("tenant", context.TenantId);
            availability.Parameters.Add("blocks", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = newBlockIds;
            using var lease = session.EnterCommand();
            await using var reader = await availability.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var owner = reader.GetString(1);
                if (!string.Equals(owner, context.ContextId, StringComparison.Ordinal))
                {
                    throw new AgentMemoryOperationException(
                        AgentMemoryOperationFailureCode.IdentityConflict,
                        "Compressed context BlockId already exists.");
                }
            }
        }

        var exists = await ContextExistsAsync(session, context.TenantId, context.ContextId, ct).ConfigureAwait(false);
        if (exists && !replace)
        {
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.IdentityConflict,
                "Context identity already exists.");
        }

        await using var parent = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_contexts")}
                (tenant_id, context_id, revision, state_contract_version, state_json, created_at, updated_at)
            values (@tenant, @context, 1, 1, @state, clock_timestamp(), clock_timestamp())
            on conflict (tenant_id, context_id) do update
                set state_json = excluded.state_json,
                    revision = {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_contexts")}.revision + 1,
                    updated_at = clock_timestamp()
            returning revision;
            """);
        parent.Parameters.AddWithValue("tenant", context.TenantId);
        parent.Parameters.AddWithValue("context", context.ContextId);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(parent, "state", serialized);
        using (var lease = session.EnterCommand())
            await parent.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Delete old Block projection, then insert the new blocks parent-first.
        await using (var delete = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            delete from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_blocks")}
            where tenant_id = @tenant and context_id = @context;
            """))
        {
            delete.Parameters.AddWithValue("tenant", context.TenantId);
            delete.Parameters.AddWithValue("context", context.ContextId);
            using var lease = session.EnterCommand();
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshot.Blocks.Count; index++)
        {
            var block = snapshot.Blocks[index];
            await using var insert = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
                insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_blocks")}
                    (tenant_id, block_id, context_id, ordinal, state_contract_version, block_json)
                values (@tenant, @block, @context, @ordinal, 1, @state);
                """);
            insert.Parameters.AddWithValue("tenant", block.TenantId);
            insert.Parameters.AddWithValue("block", block.BlockId);
            insert.Parameters.AddWithValue("context", context.ContextId);
            insert.Parameters.AddWithValue("ordinal", index);
            PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(insert, "state", serializedBlocks[index]);
            using var lease = session.EnterCommand();
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async ValueTask<AgentCompressedContext?> GetCoreAsync(string tenantId, string contextId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        // Snapshot consistency with the writer: the parent aggregate and the
        // Block projection are read in two statements, and a replacement can
        // commit between them under READ COMMITTED. Holding the same Context
        // advisory lock as the write path serializes readers against writers,
        // so a reader always observes one consistent aggregate version.
        await _lockManager.AcquireAsync(session, tenantId, "context", [contextId], ct).ConfigureAwait(false);

        AgentCompressedContext? parent = null;
        long parentRevision = 0;
        int parentVersion = 0;
        string parentJson = string.Empty;

        await using (var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, context_id, revision, state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_contexts")}
            where tenant_id = @tenant and context_id = @context;
            """))
        {
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("context", contextId);
            using var lease = session.EnterCommand();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;
            parentRevision = reader.GetInt64(2);
            parentVersion = reader.GetInt32(3);
            parentJson = reader.GetString(4);
        }

        parent = PostgreSqlAgentMemoryRowMapper.MapContext(
            tenantId, contextId, parentRevision, parentVersion, parentJson,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContext);

        var blocks = new List<AgentCompressedContextBlock>();
        await using (var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, block_id, context_id, ordinal, state_contract_version, block_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_blocks")}
            where tenant_id = @tenant and context_id = @context
            order by ordinal;
            """))
        {
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("context", contextId);
            using var lease = session.EnterCommand();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                blocks.Add(PostgreSqlAgentMemoryRowMapper.MapContextBlock(
                    tenantId,
                    reader.GetString(1),
                    contextId,
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock));
            }
        }

        if (blocks.Count != parent.Blocks.Count)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Context Block projection count disagrees with the aggregate.");
        for (var index = 0; index < blocks.Count; index++)
        {
            if (index >= parent.Blocks.Count
                || !string.Equals(blocks[index].BlockId, parent.Blocks[index].BlockId, StringComparison.Ordinal)
                || !string.Equals(blocks[index].TenantId, parent.Blocks[index].TenantId, StringComparison.Ordinal)
                || !PostgreSqlRuntimeStoreSupport.JsonEquals(
                    PostgreSqlAgentMemoryStoreSupport.Serialize(
                        blocks[index], PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock),
                    PostgreSqlAgentMemoryStoreSupport.Serialize(
                        parent.Blocks[index], PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock)))
            {
                throw PostgreSqlAgentMemoryStoreSupport.Invariant("Context Block projection disagrees with the parent aggregate.");
            }
        }

        return parent.Snapshot();
    }

    private async ValueTask<AgentCompressedContextBlock?> GetBlockCoreAsync(string tenantId, string blockId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        string? contextId = null;
        int ordinal = 0;
        int version = 0;
        string blockJson = string.Empty;

        await using (var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, block_id, context_id, ordinal, state_contract_version, block_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_blocks")}
            where tenant_id = @tenant and block_id = @block;
            """))
        {
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("block", blockId);
            using var lease = session.EnterCommand();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;
            contextId = reader.GetString(2);
            ordinal = reader.GetInt32(3);
            version = reader.GetInt32(4);
            blockJson = reader.GetString(5);
        }

        var parent = await GetCoreAsync(tenantId, contextId!, ct).ConfigureAwait(false);
        if (parent is null)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Block references a missing parent Context.");

        var block = PostgreSqlAgentMemoryRowMapper.MapContextBlock(
            tenantId, blockId, contextId!, ordinal, version, blockJson,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock);

        if (ordinal < 0 || ordinal >= parent.Blocks.Count)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Block ordinal is outside the parent Context range.");
        var slot = parent.Blocks[ordinal];
        if (!string.Equals(slot.BlockId, block.BlockId, StringComparison.Ordinal)
            || !string.Equals(slot.TenantId, block.TenantId, StringComparison.Ordinal)
            || !PostgreSqlRuntimeStoreSupport.JsonEquals(
                PostgreSqlAgentMemoryStoreSupport.Serialize(
                    block, PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock),
                PostgreSqlAgentMemoryStoreSupport.Serialize(
                    slot, PostgreSqlRuntimeJsonSerializerContext.Default.AgentCompressedContextBlock)))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Block row does not match the parent Context ordinal slot.");
        }

        return block.Snapshot();
    }

    private async ValueTask<bool> ContextExistsAsync(
        PostgreSqlRuntimeSession session,
        string tenantId,
        string contextId,
        CancellationToken ct)
    {
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select exists (
                select 1 from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_compressed_contexts")}
                where tenant_id = @tenant and context_id = @context);
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("context", contextId);
        using var lease = session.EnterCommand();
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
