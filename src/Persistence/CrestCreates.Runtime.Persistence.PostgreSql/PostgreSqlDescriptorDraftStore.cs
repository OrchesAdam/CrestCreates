using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
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
        await PostgreSqlRuntimeTestHooks.NotifyAfterReferenceSnapshotCapturedAsync(ct).ConfigureAwait(false);
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
            cmd.Parameters.AddWithValue(
                "createdAt",
                PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(snapshot.CreatedAt));
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
            return ReadPersistedDraft(reader, 10);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Draft>> ListAsync(string tenantId, DraftQuery? query = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DescriptorDraftStoreSemantics.ValidateListInput(tenantId, query);

        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "control_plane_descriptor_drafts");
        var predicates = new List<string> { "tenant_id=@tenant" };
        if (query?.DescriptorKind is { } descriptorKind)
            predicates.Add("descriptor_kind=@descriptorKind");
        if (query?.Operation is { } operation)
            predicates.Add("operation=@operation");
        if (query?.AuthorKind is { } authorKind)
            predicates.Add("author_kind=@authorKind");
        if (query?.Status is { } status)
            predicates.Add("status=@status");
        if (query?.CreatedFrom is { } createdFrom)
            predicates.Add("created_at_utc_ticks>=@createdFromTicks");
        if (query?.CreatedTo is { } createdTo)
            predicates.Add("created_at_utc_ticks<=@createdToTicks");

        var sql = $"""
            select tenant_id, draft_id, payload_type, descriptor_kind, operation, author_kind, status,
                   created_at_utc_ticks, created_at, state_contract_version, state_json
            from {table}
            where {string.Join(" and ", predicates)}
            """;

        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var cmd = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            cmd.Parameters.AddWithValue("tenant", tenantId);
            if (query?.DescriptorKind is { } descriptorKind)
                cmd.Parameters.AddWithValue("descriptorKind", (int)descriptorKind);
            if (query?.Operation is { } operation)
                cmd.Parameters.AddWithValue("operation", (int)operation);
            if (query?.AuthorKind is { } authorKind)
                cmd.Parameters.AddWithValue("authorKind", (int)authorKind);
            if (query?.Status is { } status)
                cmd.Parameters.AddWithValue("status", (int)status);
            if (query?.CreatedFrom is { } createdFrom)
                cmd.Parameters.AddWithValue("createdFromTicks", createdFrom.UtcTicks);
            if (query?.CreatedTo is { } createdTo)
                cmd.Parameters.AddWithValue("createdToTicks", createdTo.UtcTicks);
            await using var reader = await cmd.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            var results = new List<Draft>();
            while (await reader.ReadAsync(innerCt).ConfigureAwait(false))
                results.Add(ReadPersistedDraft(reader, 10));
            return DescriptorDraftStoreSemantics.OrderDrafts(results).ToList().AsReadOnly();
        }, ct).ConfigureAwait(false);
    }

    private static Draft ReadPersistedDraft(NpgsqlDataReader reader, int jsonOrdinal)
    {
        var draft = PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize(reader.GetString(jsonOrdinal));
        var descriptorKind = (DescriptorKind)reader.GetInt32(3);
        var operation = (DescriptorDraftOperation)reader.GetInt32(4);
        var authorKind = (DescriptorDraftAuthorKind)reader.GetInt32(5);
        var status = (DescriptorDraftStatus)reader.GetInt32(6);
        if (!IsDefined(descriptorKind)
            || !IsDefined(operation)
            || !IsDefined(authorKind)
            || !IsDefined(status)
            || !string.Equals(reader.GetString(0), draft.TenantId, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), draft.DraftId, StringComparison.Ordinal)
            || reader.GetInt32(2) != DescriptorDraftPayloadSupport.GetPayloadType(draft.Payload)
            || descriptorKind != draft.DescriptorKind
            || operation != draft.Operation
            || authorKind != draft.AuthorKind
            || status != draft.Status
            || reader.GetInt64(7) != draft.CreatedAt.UtcTicks
            || reader.GetInt32(9) != PostgreSqlControlPlaneReferenceDataStoreSupport.StateContractVersion)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "Descriptor Draft structured columns disagree with the JSON snapshot.");
        }

        var createdAt = reader.GetDateTime(8);
        var expectedCreatedAtTicks = draft.CreatedAt.UtcTicks
            - draft.CreatedAt.UtcTicks % TimeSpan.TicksPerMicrosecond;
        if (createdAt.Ticks != expectedCreatedAtTicks)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "Descriptor Draft readable timestamp disagrees with the JSON snapshot.");
        }

        return draft;
    }

    private static bool IsDefined(DescriptorKind value)
        => value is DescriptorKind.Unknown
            or DescriptorKind.Schema
            or DescriptorKind.Capability
            or DescriptorKind.Event
            or DescriptorKind.Workflow
            or DescriptorKind.Form
            or DescriptorKind.HumanTask
            or DescriptorKind.DynamicApiEndpoint
            or DescriptorKind.McpTool
            or DescriptorKind.AgentTool;

    private static bool IsDefined(DescriptorDraftOperation value)
        => value is DescriptorDraftOperation.Create
            or DescriptorDraftOperation.Update
            or DescriptorDraftOperation.Deprecate
            or DescriptorDraftOperation.Remove;

    private static bool IsDefined(DescriptorDraftAuthorKind value)
        => value is DescriptorDraftAuthorKind.Human
            or DescriptorDraftAuthorKind.Agent
            or DescriptorDraftAuthorKind.System
            or DescriptorDraftAuthorKind.Import
            or DescriptorDraftAuthorKind.Generator;

    private static bool IsDefined(DescriptorDraftStatus value)
        => value is DescriptorDraftStatus.Created
            or DescriptorDraftStatus.Invalid
            or DescriptorDraftStatus.Materialized
            or DescriptorDraftStatus.Reviewed
            or DescriptorDraftStatus.Cancelled;
}
