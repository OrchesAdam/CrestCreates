using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Repeatable, strongly typed ledger projection for envelopes written before the retention
/// migration. Rows that cannot be projected are quarantined; they are never cleaned and never
/// converted into the host partition.
/// </summary>
internal static class PostgreSqlAuditRetentionLedgerBackfill
{
    public const string Version = "V017";
    private const int BatchSize = 500;

    public static async Task RunAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        CancellationToken cancellationToken)
    {
        var ledger = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_ledger");
        var envelopes = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_envelopes");

        while (true)
        {
            var rows = await ReadBatchAsync(connection, envelopes, ledger, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
                return;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
                await ProjectAsync(connection, transaction, ledger, envelopes, row, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<List<RawEnvelope>> ReadBatchAsync(
        NpgsqlConnection connection,
        string envelopes,
        string ledger,
        CancellationToken cancellationToken)
    {
        var rows = new List<RawEnvelope>();
        await using var command = new NpgsqlCommand(
            $"""
             select e.sink_id, e.audit_id, e.integrity_json::text, e.envelope_json::text, e.accepted_at
             from {envelopes} e
             where not exists (
                 select 1 from {ledger} l where l.sink_id = e.sink_id and l.audit_id = e.audit_id)
             order by e.sink_id collate "C", e.audit_id collate "C"
             limit {BatchSize};
             """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new RawEnvelope(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return rows;
    }

    private static async Task ProjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ledger,
        string envelopes,
        RawEnvelope row,
        CancellationToken cancellationToken)
    {
        var projection = Project(row);
        await using (var command = new NpgsqlCommand(
            $"""
             insert into {ledger} (
                 sink_id, audit_id, integrity_json, first_accepted_at, scope_kind, tenant_id,
                 occurred_at, body_state, protection, quarantine_reason)
             values (@sink, @audit, @integrity, @accepted, @scope, @tenant, @occurred, @state, @protection, @reason)
             on conflict (sink_id, audit_id) do nothing;
             """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("sink", row.SinkId);
            command.Parameters.AddWithValue("audit", row.AuditId);
            if (projection.IntegrityJson is null)
                command.Parameters.Add("integrity", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = DBNull.Value;
            else
                PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", projection.IntegrityJson);
            command.Parameters.AddWithValue("accepted", row.AcceptedAt);
            command.Parameters.AddWithValue("scope", projection.ScopeKind);
            command.Parameters.AddWithValue("tenant", projection.TenantId);
            command.Parameters.AddWithValue("occurred", projection.OccurredAt);
            command.Parameters.AddWithValue("state", projection.BodyState);
            command.Parameters.AddWithValue("protection", projection.Protection);
            if (projection.QuarantineReason is null)
                command.Parameters.Add("reason", NpgsqlTypes.NpgsqlDbType.Text).Value = DBNull.Value;
            else
                command.Parameters.AddWithValue("reason", projection.QuarantineReason);
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (inserted == 0)
                return;
        }

        if (!string.Equals(projection.BodyState, "Quarantined", StringComparison.Ordinal))
            return;

        await using var quarantine = new NpgsqlCommand(
            $"update {envelopes} set body_state = 'Quarantined' where sink_id=@sink and audit_id=@audit and body_state = 'BodyPresent';",
            connection,
            transaction);
        quarantine.Parameters.AddWithValue("sink", row.SinkId);
        quarantine.Parameters.AddWithValue("audit", row.AuditId);
        await quarantine.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LedgerProjection Project(RawEnvelope row)
    {
        CrestCreates.Accountability.Abstractions.Contracts.AuditEnvelope envelope;
        try
        {
            envelope = PostgreSqlRuntimeStoreSupport.Deserialize(
                row.EnvelopeJson,
                PostgreSqlRuntimeJsonSerializerContext.Default.AuditEnvelope);
        }
        catch (Exception)
        {
            return Quarantined(row, "invalid_envelope");
        }

        if (!string.Equals(envelope.AuditId, row.AuditId, StringComparison.Ordinal))
            return Quarantined(row, "audit_id_mismatch");

        if (envelope.OccurredAt == default)
            return Quarantined(row, "invalid_occurred_at");

        if (envelope.TenantId is not null && string.IsNullOrWhiteSpace(envelope.TenantId))
            return Quarantined(row, "invalid_tenant");

        CanonicalHash? integrity = null;
        if (!string.IsNullOrWhiteSpace(row.IntegrityJson))
        {
            try
            {
                integrity = PostgreSqlRuntimeStoreSupport.Deserialize(
                    row.IntegrityJson,
                    PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
            }
            catch (Exception)
            {
                return Quarantined(row, "invalid_integrity");
            }
        }

        if (integrity is null)
            return Quarantined(row, "invalid_integrity");

        var protection = AuditRetentionSemanticNames.IsGovernanceTarget(envelope.Target?.Kind)
            ? "governance"
            : "none";
        var isHost = envelope.TenantId is null;
        return new LedgerProjection(
            row.IntegrityJson,
            isHost ? "host" : "tenant",
            isHost ? string.Empty : envelope.TenantId!,
            envelope.OccurredAt,
            "BodyPresent",
            protection,
            null);
    }

    private static LedgerProjection Quarantined(RawEnvelope row, string reason)
        => new(
            IntegrityJson: null,
            ScopeKind: "quarantine",
            TenantId: string.Empty,
            OccurredAt: row.AcceptedAt,
            BodyState: "Quarantined",
            Protection: "none",
            QuarantineReason: reason);

    private sealed record RawEnvelope(
        string SinkId,
        string AuditId,
        string? IntegrityJson,
        string EnvelopeJson,
        DateTimeOffset AcceptedAt);

    private sealed record LedgerProjection(
        string? IntegrityJson,
        string ScopeKind,
        string TenantId,
        DateTimeOffset OccurredAt,
        string BodyState,
        string Protection,
        string? QuarantineReason);
}
