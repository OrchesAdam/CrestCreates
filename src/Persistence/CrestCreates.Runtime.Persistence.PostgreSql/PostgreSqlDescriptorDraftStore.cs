using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlDescriptorDraftStore : IDescriptorDraftStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlDescriptorDraftStore(
        PostgreSqlRuntimePersistenceOptions options,
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _dataSource = dataSource;
        _coordinator = coordinator;
    }

    public async Task SaveAsync(Draft draft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateSaveInput(draft);
        DescriptorDraftPayloadSupport.EnsureSupported(draft.Payload);

        var snapshot = draft.Snapshot();
        var payloadType = DescriptorDraftPayloadSupport.GetPayloadType(snapshot.Payload);
        var json = PostgreSqlControlPlaneReferenceDataJsonCodec.Serialize(snapshot);

        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "control_plane_descriptor_drafts");

        await _coordinator.ExecuteTopLevelAsync(async innerCt =>
        {
            var session = _coordinator.RequireSession();
            var sql = $"""
                insert into {table}
                    (tenant_id, draft_id, payload_type, descriptor_kind, operation, author_kind, status,
                     created_at_utc_ticks, created_at, state_contract_version, state_json, updated_at)
                values
                    (@tenantId, @draftId, @payloadType, @descriptorKind, @operation, @authorKind, @status,
                     @createdAtUtcTicks, @createdAt, @stateContractVersion, @stateJson::jsonb, clock_timestamp())
                on conflict (tenant_id, draft_id) do update set
                    payload_type = excluded.payload_type,
                    descriptor_kind = excluded.descriptor_kind,
                    operation = excluded.operation,
                    author_kind = excluded.author_kind,
                    status = excluded.status,
                    created_at_utc_ticks = excluded.created_at_utc_ticks,
                    created_at = excluded.created_at,
                    state_contract_version = excluded.state_contract_version,
                    state_json = excluded.state_json,
                    updated_at = clock_timestamp()
                """;

            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
            cmd.Parameters.AddWithValue("tenantId", snapshot.TenantId);
            cmd.Parameters.AddWithValue("draftId", snapshot.DraftId);
            cmd.Parameters.AddWithValue("payloadType", payloadType);
            cmd.Parameters.AddWithValue("descriptorKind", (int)snapshot.DescriptorKind);
            cmd.Parameters.AddWithValue("operation", (int)snapshot.Operation);
            cmd.Parameters.AddWithValue("authorKind", (int)snapshot.AuthorKind);
            cmd.Parameters.AddWithValue("status", (int)snapshot.Status);
            cmd.Parameters.AddWithValue("createdAtUtcTicks", snapshot.CreatedAt.UtcTicks);
            cmd.Parameters.AddWithValue("createdAt", snapshot.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("stateContractVersion", PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion);
            cmd.Parameters.AddWithValue("stateJson", json);
            await cmd.ExecuteNonQueryAsync(innerCt).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<Draft?> GetAsync(string tenantId, string draftId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateGetInput(tenantId, draftId);

        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "control_plane_descriptor_drafts");
        var sql = $"""
            select tenant_id, draft_id, payload_type, descriptor_kind, operation, author_kind, status,
                   created_at_utc_ticks, created_at, state_contract_version, state_json
            from {table}
            where tenant_id=@tenant and draft_id=@draft
            """;

        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            cmd.Parameters.AddWithValue("tenant", tenantId);
            cmd.Parameters.AddWithValue("draft", draftId);
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            if (!await reader.ReadAsync(innerCt).ConfigureAwait(false))
                return null;
            var jsonStr = reader.GetString(10);
            return PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize(jsonStr);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Draft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateListInput(tenantId, query);

        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "control_plane_descriptor_drafts");
        var sql = $"""
            select tenant_id, draft_id, payload_type, descriptor_kind, operation, author_kind, status,
                   created_at_utc_ticks, created_at, state_contract_version, state_json
            from {table}
            where tenant_id=@tenant
            """;

        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            cmd.Parameters.AddWithValue("tenant", tenantId);
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            var results = new List<Draft>();
            while (await reader.ReadAsync(innerCt).ConfigureAwait(false))
            {
                var jsonStr = reader.GetString(10);
                results.Add(PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize(jsonStr));
            }
            return DescriptorDraftStoreSemantics.OrderDrafts(results).ToList().AsReadOnly();
        }, ct).ConfigureAwait(false);
    }
}
