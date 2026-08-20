using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Testing.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

internal sealed class PostgreSqlAuditSinkContractDriver : IAuditSinkContractDriver
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimePersistenceOptions _options;

    public PostgreSqlAuditSinkContractDriver(NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options)
    {
        _dataSource = dataSource;
        _options = options;
    }

    public IAuditSink CreateSink()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            $"delete from {_options.SchemaQuotedTable("runtime_audit_envelopes")} where sink_id=@sink;", connection);
        command.Parameters.AddWithValue("sink", "postgresql-runtime-audit");
        command.ExecuteNonQuery();
        return new PostgreSqlAuditSink(_dataSource, _options);
    }

    public AuditEnvelope CreateEnvelope(string auditId, string integrityValue) => new()
    {
        AuditId = auditId,
        OccurredAt = DateTimeOffset.UnixEpoch,
        CorrelationId = "contract",
        Actor = new AuditActor { Kind = "system", Id = "test" },
        Action = new AuditAction { Kind = "system", Name = "contract" },
        Target = new AuditTarget { Kind = "test", Id = auditId },
        Outcome = new AuditOutcome { Status = "succeeded" },
        Integrity = new CanonicalHash
        {
            Value = integrityValue,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AccountabilityRecord",
            Scope = "InternalFull",
            Purpose = "AuditEvidence",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "accountability-record-hash-v1"
        }
    };

    public async ValueTask<AuditEnvelope?> ReadAsync(IAuditSink sink, string auditId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"select envelope_json::text from {_options.SchemaQuotedTable("runtime_audit_envelopes")} where sink_id=@sink and audit_id=@audit;", connection);
        command.Parameters.AddWithValue("sink", sink.Id);
        command.Parameters.AddWithValue("audit", auditId);
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null ? null : PostgreSqlRuntimeStoreSupport.Deserialize(json, PostgreSqlRuntimeJsonSerializerContext.Default.AuditEnvelope);
    }

    public async ValueTask<IReadOnlyList<AuditEnvelope>> ReadAllAsync(IAuditSink sink, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"select envelope_json::text from {_options.SchemaQuotedTable("runtime_audit_envelopes")} where sink_id=@sink order by audit_id collate \"C\";", connection);
        command.Parameters.AddWithValue("sink", sink.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<AuditEnvelope>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(0), PostgreSqlRuntimeJsonSerializerContext.Default.AuditEnvelope));
        return result;
    }
}

internal static class PostgreSqlRuntimePersistenceOptionsContractExtensions
{
    public static string SchemaQuotedTable(this PostgreSqlRuntimePersistenceOptions options, string table)
        => $"\"{options.Schema.Replace("\"", "\"\"")}\".\"{table}\"";
}
