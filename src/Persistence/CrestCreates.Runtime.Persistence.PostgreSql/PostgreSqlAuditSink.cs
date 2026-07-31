using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public sealed class PostgreSqlAuditSink : IAuditSink
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    public PostgreSqlAuditSink(PostgreSqlRuntimePersistenceOptions options) => _options = options;
    public string Id => "postgresql-runtime-audit";

    public async ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var integrity = envelope.Integrity ?? throw new InvalidOperationException("AuditEnvelope integrity is required.");
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var table = $"\"{_options.Schema.Replace("\"", "\"\"")}\".runtime_audit_envelopes";
        await using var read = new NpgsqlCommand($"select integrity_value, accepted_at from {table} where audit_id=@id", connection, transaction);
        read.Parameters.AddWithValue("id", envelope.AuditId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var existing = reader.GetString(0);
            var accepted = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new AuditSinkWriteResult { SinkId = Id, AuditId = envelope.AuditId, Integrity = integrity, Status = string.Equals(existing, integrity.Value, StringComparison.Ordinal) ? AuditSinkWriteStatus.Duplicate : AuditSinkWriteStatus.Conflict, ExistingIntegrity = new CanonicalHash { Value = existing, Algorithm = integrity.Algorithm, AlgorithmVersion = integrity.AlgorithmVersion, ArtifactKind = integrity.ArtifactKind, DescriptorKind = integrity.DescriptorKind, Scope = integrity.Scope, Purpose = integrity.Purpose, ContractVersion = integrity.ContractVersion, CanonicalShapeVersion = integrity.CanonicalShapeVersion }, FirstAcceptedAt = accepted };
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        await using var insert = new NpgsqlCommand($"insert into {table} (audit_id, integrity_value, accepted_at) values (@id,@integrity,clock_timestamp())", connection, transaction);
        insert.Parameters.AddWithValue("id", envelope.AuditId); insert.Parameters.AddWithValue("integrity", integrity.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AuditSinkWriteResult { SinkId = Id, AuditId = envelope.AuditId, Integrity = integrity, Status = AuditSinkWriteStatus.Accepted, FirstAcceptedAt = DateTimeOffset.UtcNow };
    }
}
