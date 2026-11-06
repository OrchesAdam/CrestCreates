using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public sealed class PostgreSqlAuditSink : IAuditSink
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly string _table;
    private readonly string _ledger;

    public PostgreSqlAuditSink(NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_envelopes");
        _ledger = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_ledger");
    }

    public string Id => "postgresql-runtime-audit";

    public async ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var integrity = envelope.Integrity ?? throw new InvalidOperationException("AuditEnvelope integrity is required.");
        if (envelope.TenantId is not null && string.IsNullOrWhiteSpace(envelope.TenantId))
            throw new InvalidOperationException("AuditEnvelope tenant identity is not a valid retention scope.");
        if (envelope.OccurredAt == default)
            throw new InvalidOperationException("AuditEnvelope OccurredAt is required for retention projection.");

        var scopeKind = envelope.TenantId is null ? "host" : "tenant";
        var tenantId = envelope.TenantId ?? string.Empty;
        var protection = AuditRetentionSemanticNames.IsGovernanceTarget(envelope.Target?.Kind)
            ? "governance"
            : "none";
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset? acceptedAt;
            await using (var command = new NpgsqlCommand(
                $"""
                 insert into {_ledger} (sink_id, audit_id, integrity_json, first_accepted_at, scope_kind, tenant_id, occurred_at, body_state, protection)
                 values (@sink, @audit, @integrity, clock_timestamp(), @scope, @tenant, @occurred, 'BodyPresent', @protection)
                 on conflict (sink_id, audit_id) do nothing
                 returning first_accepted_at;
                 """,
                connection,
                transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds
            })
            {
                command.Parameters.AddWithValue("sink", Id);
                command.Parameters.AddWithValue("audit", envelope.AuditId);
                PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash));
                command.Parameters.AddWithValue("scope", scopeKind);
                command.Parameters.AddWithValue("tenant", tenantId);
                command.Parameters.AddWithValue("occurred", envelope.OccurredAt);
                command.Parameters.AddWithValue("protection", protection);
                acceptedAt = await ReadAcceptedAtAsync(command, cancellationToken).ConfigureAwait(false);
            }

            if (acceptedAt is not null)
            {
                await using (var command = new NpgsqlCommand(
                    $"insert into {_table} (sink_id, audit_id, integrity_json, envelope_json, body_state) values (@sink, @audit, @integrity, @envelope, 'BodyPresent');",
                    connection,
                    transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds
                })
                {
                    command.Parameters.AddWithValue("sink", Id);
                    command.Parameters.AddWithValue("audit", envelope.AuditId);
                    PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash));
                    PostgreSqlRuntimeStoreSupport.AddJson(command, "envelope", PostgreSqlRuntimeStoreSupport.Serialize(envelope, PostgreSqlRuntimeJsonSerializerContext.Default.AuditEnvelope));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                return new AuditSinkWriteResult
                {
                    SinkId = Id,
                    AuditId = envelope.AuditId,
                    Integrity = integrity,
                    Status = AuditSinkWriteStatus.Accepted,
                    FirstAcceptedAt = acceptedAt
                };
            }

            CanonicalHash existing;
            DateTimeOffset firstAcceptedAt;
            await using (var command = new NpgsqlCommand(
                $"select integrity_json::text, first_accepted_at from {_ledger} where sink_id=@sink and audit_id=@audit;",
                connection,
                transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds
            })
            {
                command.Parameters.AddWithValue("sink", Id);
                command.Parameters.AddWithValue("audit", envelope.AuditId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Audit uniqueness conflict did not return an accepted envelope.");
                existing = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(0), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
                firstAcceptedAt = reader.GetFieldValue<DateTimeOffset>(1);
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            var duplicate = existing == integrity;
            return new AuditSinkWriteResult
            {
                SinkId = Id,
                AuditId = envelope.AuditId,
                Integrity = integrity,
                Status = duplicate ? AuditSinkWriteStatus.Duplicate : AuditSinkWriteStatus.Conflict,
                ExistingIntegrity = duplicate ? null : existing,
                FirstAcceptedAt = firstAcceptedAt
            };
        }
        catch (RuntimePersistenceException)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            throw new RuntimePersistenceUnavailableException("PostgreSQL Audit sink is unavailable.");
        }
    }

    private static async Task<DateTimeOffset?> ReadAcceptedAtAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var accepted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return accepted switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(
                timestamp.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
                    : timestamp.ToUniversalTime()),
            _ => null
        };
    }
}
