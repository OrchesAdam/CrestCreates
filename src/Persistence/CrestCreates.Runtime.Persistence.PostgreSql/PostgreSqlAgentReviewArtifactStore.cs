using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentReviewArtifactStore : IAgentReviewArtifactStore
{
    private const string CollisionConstraint = "pk_agent_review_artifacts";
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlAgentReviewArtifactStore(
        PostgreSqlRuntimePersistenceOptions options,
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _dataSource = dataSource;
        _coordinator = coordinator;
    }

    public async Task InsertAsync(AgentReviewArtifactEnvelope artifact, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        artifact.Validate();
        var json = JsonSerializer.Serialize(
            artifact,
            AgentReviewArtifactEnvelopeJsonSerializerContext.Default.AgentReviewArtifactEnvelope);
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_review_artifacts");

        try
        {
            await _coordinator.ExecuteTopLevelAsync(async innerCt =>
            {
                var session = _coordinator.RequireSession();
                var sql = $"""
                    insert into {table}
                        (tenant_id, review_result_id, draft_id, created_at_utc_ticks, created_at, artifact_version, state_json)
                    values (@tenant, @review, @draft, @createdTicks, @createdAt, @version, @state::jsonb)
                    """;
                await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
                command.Parameters.AddWithValue("tenant", artifact.TenantId);
                command.Parameters.AddWithValue("review", artifact.ReviewResultId);
                command.Parameters.AddWithValue("draft", artifact.DraftId);
                command.Parameters.AddWithValue("createdTicks", artifact.CreatedAt.UtcTicks);
                command.Parameters.AddWithValue("createdAt", PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(artifact.CreatedAt));
                command.Parameters.AddWithValue("version", artifact.Version);
                command.Parameters.Add("state", NpgsqlDbType.Jsonb).Value = json;
                await command.ExecuteNonQueryAsync(innerCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, CollisionConstraint))
        {
            throw new InvalidOperationException(
                $"Review artifact '{artifact.ReviewResultId}' already exists for tenant '{artifact.TenantId}'.", exception);
        }
        catch (NpgsqlException exception)
        {
            throw new RuntimePersistenceUnavailableException("PostgreSQL Runtime persistence is unavailable.", exception);
        }
    }

    public async Task<AgentReviewArtifactEnvelope?> GetAsync(string tenantId, string reviewResultId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateKey(tenantId, reviewResultId, nameof(reviewResultId));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_review_artifacts");
        var sql = $"""
            select tenant_id, review_result_id, draft_id, created_at_utc_ticks, created_at, artifact_version, state_json::text
            from {table}
            where tenant_id=@tenant and review_result_id=@review
            """;
        return await ReadOneAsync(sql, tenantId, reviewResultId, null, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentReviewArtifactEnvelope>> ListAsync(string tenantId, string? draftId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateTenant(tenantId);
        if (draftId is not null && string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("Draft ID is required when supplied.", nameof(draftId));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_review_artifacts");
        var sql = $"""
            select tenant_id, review_result_id, draft_id, created_at_utc_ticks, created_at, artifact_version, state_json::text
            from {table}
            where tenant_id=@tenant and (@draft is null or draft_id=@draft)
            order by draft_id collate "C", created_at_utc_ticks, review_result_id collate "C"
            """;
        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.Add("draft", NpgsqlDbType.Text).Value = (object?)draftId ?? DBNull.Value;
            await using var reader = await command.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            var records = new List<AgentReviewArtifactEnvelope>();
            while (await reader.ReadAsync(innerCt).ConfigureAwait(false))
                records.Add(Decode(reader));
            return (IReadOnlyList<AgentReviewArtifactEnvelope>)Array.AsReadOnly(records.ToArray());
        }, ct).ConfigureAwait(false);
    }

    public async Task<AgentReviewArtifactEnvelope?> GetLatestAsync(string tenantId, string draftId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateTenant(tenantId);
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("Draft ID is required.", nameof(draftId));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_review_artifacts");
        var sql = $"""
            select tenant_id, review_result_id, draft_id, created_at_utc_ticks, created_at, artifact_version, state_json::text
            from {table}
            where tenant_id=@tenant and draft_id=@draft
            order by created_at_utc_ticks desc, review_result_id collate "C" desc
            limit 1
            """;
        return await ReadOneAsync(sql, tenantId, draftId, draftId, ct).ConfigureAwait(false);
    }

    private async Task<AgentReviewArtifactEnvelope?> ReadOneAsync(
        string sql, string tenantId, string key, string? draftId, CancellationToken ct)
    {
        return await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue(draftId is null ? "review" : "draft", key);
            await using var reader = await command.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            return await reader.ReadAsync(innerCt).ConfigureAwait(false) ? Decode(reader) : null;
        }, ct).ConfigureAwait(false);
    }

    private static AgentReviewArtifactEnvelope Decode(NpgsqlDataReader reader)
    {
        AgentReviewArtifactEnvelope artifact;
        try
        {
            artifact = JsonSerializer.Deserialize(
                reader.GetString(6),
                AgentReviewArtifactEnvelopeJsonSerializerContext.Default.AgentReviewArtifactEnvelope)
                ?? throw new JsonException("Review artifact JSON decoded to null.");
            artifact.Validate();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "PostgreSQL returned an invalid review artifact payload.");
        }

        var expectedTicks = artifact.CreatedAt.UtcTicks
            - artifact.CreatedAt.UtcTicks % TimeSpan.TicksPerMicrosecond;
        var storedTimestamp = reader.GetDateTime(4);
        if (!StringComparer.Ordinal.Equals(reader.GetString(0), artifact.TenantId)
            || !StringComparer.Ordinal.Equals(reader.GetString(1), artifact.ReviewResultId)
            || !StringComparer.Ordinal.Equals(reader.GetString(2), artifact.DraftId)
            || reader.GetInt64(3) != artifact.CreatedAt.UtcTicks
            || storedTimestamp.Ticks != expectedTicks
            || reader.GetInt32(5) != artifact.Version)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(
                "Review artifact structured columns disagree with the JSON envelope.");
        }
        return artifact;
    }

    private static void ValidateKey(string tenantId, string key, string parameterName)
    {
        ValidateTenant(tenantId);
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Review artifact key is required.", parameterName);
    }

    private static void ValidateTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
    }
}
