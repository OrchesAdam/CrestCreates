using CrestCreates.Accountability.Abstractions.Contracts;
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

    public PostgreSqlAuditSink(NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_envelopes");
    }

    public string Id => "postgresql-runtime-audit";

    public async ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var integrity = envelope.Integrity ?? throw new InvalidOperationException("AuditEnvelope integrity is required.");
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset? acceptedAt;
            await using (var command = new NpgsqlCommand(
                $"insert into {_table} (sink_id, audit_id, integrity_json, envelope_json) values (@sink, @audit, @integrity, @envelope) on conflict (sink_id, audit_id) do nothing returning accepted_at;",
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
                var accepted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                acceptedAt = accepted switch
                {
                    DateTimeOffset timestamp => timestamp,
                    DateTime timestamp => new DateTimeOffset(
                        timestamp.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
                            : timestamp.ToUniversalTime()),
                    _ => null
                };
            }

            if (acceptedAt is not null)
            {
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
                $"select integrity_json::text, accepted_at from {_table} where sink_id=@sink and audit_id=@audit;",
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
}
