using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentPackageArtifactStore : IAgentPackageArtifactStore
{
    private const string PackageCollision = "pk_agent_package_artifacts";
    private const string EvidenceCollision = "pk_agent_evidence_artifacts";
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly IAgentPackageArtifactValidator _validator;

    public PostgreSqlAgentPackageArtifactStore(
        PostgreSqlRuntimePersistenceOptions options,
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        IAgentPackageArtifactValidator validator)
    {
        _options = options;
        _dataSource = dataSource;
        _coordinator = coordinator;
        _validator = validator;
    }

    public async Task InsertPackageAsync(AgentPackageArtifactEnvelope package, CancellationToken ct = default)
    {
        var snapshot = Snapshot(package);
        _validator.ValidatePackage(snapshot);
        try
        {
            await _coordinator.ExecuteTopLevelAsync(async innerCt =>
                await InsertPackageCoreAsync(snapshot, innerCt).ConfigureAwait(false), ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, PackageCollision))
        {
            throw new InvalidOperationException($"Package artifact '{snapshot.PackagePreviewId}' already exists for tenant '{snapshot.TenantId}'.", exception);
        }
        catch (NpgsqlException exception)
        {
            throw new RuntimePersistenceUnavailableException("PostgreSQL Runtime persistence is unavailable.", exception);
        }
    }

    public async Task InsertPackageAndEvidenceAsync(
        AgentPackageArtifactEnvelope package,
        AgentEvidenceArtifactEnvelope evidence,
        CancellationToken ct = default)
    {
        var packageSnapshot = Snapshot(package);
        var evidenceSnapshot = Snapshot(evidence);
        _validator.ValidatePackage(packageSnapshot);
        _validator.ValidateEvidence(evidenceSnapshot, packageSnapshot);
        try
        {
            await _coordinator.ExecuteTopLevelAsync(async innerCt =>
            {
                await InsertPackageCoreAsync(packageSnapshot, innerCt).ConfigureAwait(false);
                await InsertEvidenceCoreAsync(evidenceSnapshot, innerCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, PackageCollision)
            || PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, EvidenceCollision))
        {
            throw new InvalidOperationException("Package or evidence artifact already exists for the tenant key.", exception);
        }
        catch (NpgsqlException exception)
        {
            throw new RuntimePersistenceUnavailableException("PostgreSQL Runtime persistence is unavailable.", exception);
        }
    }

    public async Task InsertEvidenceAsync(AgentEvidenceArtifactEnvelope evidence, CancellationToken ct = default)
    {
        var snapshot = Snapshot(evidence);
        // Parent lookup and insert share the same database transaction. The FK provides
        // the final concurrency-safe existence check.
        try
        {
            await _coordinator.ExecuteTopLevelAsync(async innerCt =>
            {
                var parent = await GetPackageInSessionAsync(snapshot.TenantId, snapshot.PackagePreviewId, innerCt).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Evidence artifact parent package does not exist for this tenant.");
                _validator.ValidateEvidence(snapshot, parent);
                await InsertEvidenceCoreAsync(snapshot, innerCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, EvidenceCollision))
        {
            throw new InvalidOperationException($"Evidence artifact '{snapshot.EvidencePreviewId}' already exists for tenant '{snapshot.TenantId}'.", exception);
        }
        catch (NpgsqlException exception)
        {
            throw new RuntimePersistenceUnavailableException("PostgreSQL Runtime persistence is unavailable.", exception);
        }
    }

    public Task<AgentPackageArtifactEnvelope?> GetPackageAsync(AgentPackageArtifactKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKey(key.TenantId, key.PackagePreviewId, nameof(key));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_package_artifacts");
        var sql = $"select tenant_id, package_preview_id, captured_at_utc_ticks, captured_at, artifact_version, draft_id, scope_fingerprint, captured_draft_version, visible_catalog_fingerprint, state_json::text from {table} where tenant_id=@tenant and package_preview_id=@id";
        return ReadPackageAsync(sql, key.TenantId, key.PackagePreviewId, ct);
    }

    public async Task<AgentEvidenceArtifactEnvelope?> GetEvidenceAsync(AgentEvidenceArtifactKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateKey(key.TenantId, key.EvidencePreviewId, nameof(key));
        var evidenceTable = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_evidence_artifacts");
        var sql = $"select tenant_id, evidence_preview_id, captured_at_utc_ticks, captured_at, artifact_version, package_preview_id, draft_id, scope_fingerprint, captured_draft_version, state_json::text from {evidenceTable} where tenant_id=@tenant and evidence_preview_id=@id";
        var evidence = await ReadEvidenceAsync(sql, key.TenantId, key.EvidencePreviewId, ct).ConfigureAwait(false);
        if (evidence is null) return null;
        var parent = await GetPackageAsync(new AgentPackageArtifactKey(evidence.TenantId, evidence.PackagePreviewId), ct).ConfigureAwait(false)
            ?? throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant("Evidence artifact refers to a missing package artifact.");
        ValidatePersistedEvidence(evidence, parent);
        return evidence;
    }

    public async Task<AgentPackageArtifactEnvelope?> GetLatestReusablePackageAsync(
        string tenantId, string draftId, string scopeFingerprint, string? capturedDraftVersion,
        string visibleCatalogFingerprint, CancellationToken ct = default)
    {
        ValidateKey(tenantId, draftId, nameof(draftId));
        ValidateKey(scopeFingerprint, visibleCatalogFingerprint, nameof(scopeFingerprint));
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_package_artifacts");
        var sql = $"""
            select tenant_id, package_preview_id, captured_at_utc_ticks, captured_at, artifact_version, draft_id, scope_fingerprint, captured_draft_version, visible_catalog_fingerprint, state_json::text
            from {table}
            where tenant_id=@tenant and draft_id=@id and scope_fingerprint=@scope
              and captured_draft_version is not distinct from @draftVersion
              and visible_catalog_fingerprint=@catalog
            order by captured_at_utc_ticks desc, package_preview_id collate "C" desc
            limit 1
            """;
        return await ReadPackageAsync(sql, tenantId, draftId, ct, command =>
        {
            command.Parameters.AddWithValue("scope", scopeFingerprint);
            command.Parameters.Add("draftVersion", NpgsqlDbType.Text).Value = (object?)capturedDraftVersion ?? DBNull.Value;
            command.Parameters.AddWithValue("catalog", visibleCatalogFingerprint);
        }).ConfigureAwait(false);
    }

    private async Task InsertPackageCoreAsync(AgentPackageArtifactEnvelope artifact, CancellationToken ct)
    {
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_package_artifacts");
        var sql = $"""
            insert into {table}
              (tenant_id, package_preview_id, captured_at_utc_ticks, captured_at, artifact_version, draft_id, scope_fingerprint, captured_draft_version, visible_catalog_fingerprint, state_json)
            values (@tenant,@id,@ticks,@captured,@version,@draft,@scope,@draftVersion,@catalog,@state::jsonb)
            """;
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
        command.Parameters.AddWithValue("tenant", artifact.TenantId);
        command.Parameters.AddWithValue("id", artifact.PackagePreviewId);
        command.Parameters.AddWithValue("ticks", artifact.CapturedAt.UtcTicks);
        command.Parameters.AddWithValue("captured", PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(artifact.CapturedAt));
        command.Parameters.AddWithValue("version", artifact.Version);
        command.Parameters.AddWithValue("draft", artifact.Owner.DraftId);
        command.Parameters.AddWithValue("scope", artifact.ScopeFingerprint);
        command.Parameters.Add("draftVersion", NpgsqlDbType.Text).Value = (object?)artifact.DraftVersion ?? DBNull.Value;
        command.Parameters.AddWithValue("catalog", artifact.VisibleCatalogFingerprint);
        command.Parameters.Add("state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(artifact, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertEvidenceCoreAsync(AgentEvidenceArtifactEnvelope artifact, CancellationToken ct)
    {
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_evidence_artifacts");
        var sql = $"""
            insert into {table}
              (tenant_id, evidence_preview_id, captured_at_utc_ticks, captured_at, artifact_version, package_preview_id, draft_id, scope_fingerprint, captured_draft_version, state_json)
            values (@tenant,@id,@ticks,@captured,@version,@package,@draft,@scope,@draftVersion,@state::jsonb)
            """;
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
        command.Parameters.AddWithValue("tenant", artifact.TenantId);
        command.Parameters.AddWithValue("id", artifact.EvidencePreviewId);
        command.Parameters.AddWithValue("ticks", artifact.CapturedAt.UtcTicks);
        command.Parameters.AddWithValue("captured", PostgreSqlControlPlaneReferenceDataStoreSupport.ReadableTimestamp(artifact.CapturedAt));
        command.Parameters.AddWithValue("version", artifact.Version);
        command.Parameters.AddWithValue("package", artifact.PackagePreviewId);
        command.Parameters.AddWithValue("draft", artifact.Owner.DraftId);
        command.Parameters.AddWithValue("scope", artifact.ScopeFingerprint);
        command.Parameters.Add("draftVersion", NpgsqlDbType.Text).Value = (object?)artifact.DraftVersion ?? DBNull.Value;
        command.Parameters.Add("state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(artifact, AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<AgentPackageArtifactEnvelope?> GetPackageInSessionAsync(string tenant, string id, CancellationToken ct)
    {
        var table = PostgreSqlControlPlaneReferenceDataStoreSupport.Table(_options, "agent_package_artifacts");
        var sql = $"select tenant_id, package_preview_id, captured_at_utc_ticks, captured_at, artifact_version, draft_id, scope_fingerprint, captured_draft_version, visible_catalog_fingerprint, state_json::text from {table} where tenant_id=@tenant and package_preview_id=@id";
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateWriteCommand(session, _options, sql);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? DecodePackage(reader) : null;
    }

    private async Task<AgentPackageArtifactEnvelope?> ReadPackageAsync(string sql, string tenant, string id, CancellationToken ct, Action<NpgsqlCommand>? configure = null)
        => await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("id", id);
            configure?.Invoke(command);
            await using var reader = await command.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            return await reader.ReadAsync(innerCt).ConfigureAwait(false) ? DecodePackage(reader) : null;
        }, ct).ConfigureAwait(false);

    private async Task<AgentEvidenceArtifactEnvelope?> ReadEvidenceAsync(string sql, string tenant, string id, CancellationToken ct)
        => await PostgreSqlControlPlaneReferenceDataStoreSupport.ExecuteReadAsync(_dataSource, async (connection, innerCt) =>
        {
            await using var command = PostgreSqlControlPlaneReferenceDataStoreSupport.CreateReadCommand(connection, _options, sql);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(innerCt).ConfigureAwait(false);
            return await reader.ReadAsync(innerCt).ConfigureAwait(false) ? DecodeEvidence(reader) : null;
        }, ct).ConfigureAwait(false);

    private AgentPackageArtifactEnvelope DecodePackage(NpgsqlDataReader reader)
    {
        AgentPackageArtifactEnvelope artifact;
        try
        {
            artifact = JsonSerializer.Deserialize(reader.GetString(9), AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope)
                ?? throw new JsonException("Package artifact JSON decoded to null.");
            _validator.ValidatePackage(artifact);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant("PostgreSQL returned an invalid package artifact payload.");
        }
        var expectedTicks = artifact.CapturedAt.UtcTicks - artifact.CapturedAt.UtcTicks % TimeSpan.TicksPerMicrosecond;
        if (reader.GetString(0) != artifact.TenantId || reader.GetString(1) != artifact.PackagePreviewId || reader.GetInt64(2) != artifact.CapturedAt.UtcTicks
            || reader.GetDateTime(3).Ticks != expectedTicks || reader.GetInt32(4) != artifact.Version || reader.GetString(5) != artifact.Owner.DraftId
            || reader.GetString(6) != artifact.ScopeFingerprint || (reader.IsDBNull(7) ? null : reader.GetString(7)) != artifact.DraftVersion
            || reader.GetString(8) != artifact.VisibleCatalogFingerprint)
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant("Package artifact structured columns disagree with the JSON envelope.");
        return artifact;
    }

    private AgentEvidenceArtifactEnvelope DecodeEvidence(NpgsqlDataReader reader)
    {
        AgentEvidenceArtifactEnvelope artifact;
        try
        {
            artifact = JsonSerializer.Deserialize(reader.GetString(9), AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope)
                ?? throw new JsonException("Evidence artifact JSON decoded to null.");
            artifact.ValidateBinding();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant("PostgreSQL returned an invalid evidence artifact payload.");
        }
        var expectedTicks = artifact.CapturedAt.UtcTicks - artifact.CapturedAt.UtcTicks % TimeSpan.TicksPerMicrosecond;
        if (reader.GetString(0) != artifact.TenantId || reader.GetString(1) != artifact.EvidencePreviewId || reader.GetInt64(2) != artifact.CapturedAt.UtcTicks
            || reader.GetDateTime(3).Ticks != expectedTicks || reader.GetInt32(4) != artifact.Version || reader.GetString(5) != artifact.PackagePreviewId
            || reader.GetString(6) != artifact.Owner.DraftId || reader.GetString(7) != artifact.ScopeFingerprint
            || (reader.IsDBNull(8) ? null : reader.GetString(8)) != artifact.DraftVersion)
            throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant("Evidence artifact structured columns disagree with the JSON envelope.");
        return artifact;
    }

    private void ValidatePersistedEvidence(AgentEvidenceArtifactEnvelope evidence, AgentPackageArtifactEnvelope package)
    {
        try { _validator.ValidateEvidence(evidence, package); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        { throw PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant("Evidence artifact does not match its persisted package."); }
    }

    private static AgentPackageArtifactEnvelope Snapshot(AgentPackageArtifactEnvelope artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var json = JsonSerializer.Serialize(artifact, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
        return JsonSerializer.Deserialize(json, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope)
            ?? throw new ArgumentException("Package artifact could not be detached.", nameof(artifact));
    }

    private static AgentEvidenceArtifactEnvelope Snapshot(AgentEvidenceArtifactEnvelope artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var json = JsonSerializer.Serialize(artifact, AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope);
        return JsonSerializer.Deserialize(json, AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope)
            ?? throw new ArgumentException("Evidence artifact could not be detached.", nameof(artifact));
    }

    private static void ValidateKey(string tenant, string id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant ID is required.", nameof(tenant));
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Artifact key is required.", parameterName);
    }
}
