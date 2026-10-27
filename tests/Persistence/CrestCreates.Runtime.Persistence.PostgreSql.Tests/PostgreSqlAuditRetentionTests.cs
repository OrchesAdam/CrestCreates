using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAuditRetentionTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    private const string Sink = PostgreSqlAuditRetentionStore.SinkIdValue;
    private static readonly DateTimeOffset Snapshot = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly AuditActor Actor = new() { Kind = "user", Id = "retention-test" };

    [Fact]
    public async Task Batch_purges_only_dual_time_expired_bodies_and_keeps_cutoff_equal()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 10);

        await InsertBodyAsync(dataSource, lease.Options, "expired", "tenant-a", Snapshot.AddDays(-40), Snapshot.AddDays(-40));
        await InsertBodyAsync(dataSource, lease.Options, "recent-accepted", "tenant-a", Snapshot.AddDays(-40), Snapshot.AddDays(-1));
        await InsertBodyAsync(dataSource, lease.Options, "recent-occurred", "tenant-a", Snapshot.AddDays(-1), Snapshot.AddDays(-40));
        await InsertBodyAsync(dataSource, lease.Options, "exact-cutoff", "tenant-a", Snapshot.AddDays(-30), Snapshot.AddDays(-30));

        var request = RunRequest(scope, "run-time", policy);
        await store.CreateOrReadRunAsync(request);
        var batch = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });

        batch.DeletedCount.Should().Be(1);
        batch.RunState.Should().Be(AuditRetentionRunState.Completed);
        (await ReadBodyStateAsync(dataSource, lease.Options, "expired")).Should().Be("Purged");
        (await ReadBodyStateAsync(dataSource, lease.Options, "recent-accepted")).Should().Be("BodyPresent");
        (await ReadBodyStateAsync(dataSource, lease.Options, "recent-occurred")).Should().Be("BodyPresent");
        (await ReadBodyStateAsync(dataSource, lease.Options, "exact-cutoff")).Should().Be("BodyPresent");
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(3);
    }

    [Fact]
    public async Task Tenant_scope_and_host_scope_never_cross_partitions()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var policy = Policy(days: 30, batch: 10);
        var old = Snapshot.AddDays(-90);

        await InsertBodyAsync(dataSource, lease.Options, "a-row", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "b-row", "tenant-b", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "host-row", null, old, old);

        var tenantRequest = RunRequest(AuditRetentionScope.ForTenant("tenant-a"), "run-tenant", policy);
        await store.CreateOrReadRunAsync(tenantRequest);
        var tenantBatch = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = tenantRequest.Key, BatchOrdinal = 1 });
        tenantBatch.DeletedCount.Should().Be(1);
        (await ReadBodyStateAsync(dataSource, lease.Options, "a-row")).Should().Be("Purged");
        (await ReadBodyStateAsync(dataSource, lease.Options, "b-row")).Should().Be("BodyPresent");
        (await ReadBodyStateAsync(dataSource, lease.Options, "host-row")).Should().Be("BodyPresent");

        var hostRequest = RunRequest(AuditRetentionScope.Host, "run-host", policy);
        await store.CreateOrReadRunAsync(hostRequest);
        var hostBatch = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = hostRequest.Key, BatchOrdinal = 1 });
        hostBatch.DeletedCount.Should().Be(1);
        (await ReadBodyStateAsync(dataSource, lease.Options, "host-row")).Should().Be("Purged");
        (await ReadBodyStateAsync(dataSource, lease.Options, "b-row")).Should().Be("BodyPresent");
    }

    [Fact]
    public async Task Active_hold_blocks_purge_until_released_and_quarantine_is_never_purged()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 10);
        var old = Snapshot.AddDays(-90);

        await InsertBodyAsync(dataSource, lease.Options, "held-row", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "protected-row", "tenant-a", old, old, protection: "governance");
        await InsertQuarantinedAsync(dataSource, lease.Options, "quarantined-row", old);

        var hold = await store.PlaceHoldAsync(new AuditHoldPlacement
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            AuditId = "held-row",
            ReasonCode = "retention.legal",
            Actor = Actor,
            CorrelationId = "corr-hold"
        });

        var request = RunRequest(scope, "run-protection", policy);
        await store.CreateOrReadRunAsync(request);
        var batch = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });

        batch.DeletedCount.Should().Be(0);
        batch.RunState.Should().Be(AuditRetentionRunState.Completed);
        (await ReadBodyStateAsync(dataSource, lease.Options, "held-row")).Should().Be("BodyPresent");
        (await ReadBodyStateAsync(dataSource, lease.Options, "protected-row")).Should().Be("BodyPresent");
        (await ReadBodyStateAsync(dataSource, lease.Options, "quarantined-row")).Should().Be("Quarantined");

        var preview = await store.PreviewAsync(new AuditRetentionPreviewRequest
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot)
        });
        preview.HeldInSample.Should().Be(1);
        preview.ProtectedInSample.Should().Be(1);
        preview.QuarantinedInSample.Should().Be(0);
        preview.EligibleInSample.Should().Be(0);

        var hostPreview = await store.PreviewAsync(new AuditRetentionPreviewRequest
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = AuditRetentionScope.Host,
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot)
        });
        hostPreview.QuarantinedInSample.Should().Be(1);
        hostPreview.Sample.Should().ContainSingle(candidate =>
            candidate.AuditId == "quarantined-row"
            && candidate.Disposition == AuditRetentionCandidateDisposition.Quarantined);

        await store.ReleaseHoldAsync(new AuditHoldRelease
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            HoldId = hold.HoldId,
            ExpectedVersion = 1,
            ReasonCode = "retention.legal-cleared",
            Actor = Actor,
            CorrelationId = "corr-release"
        });

        var second = RunRequest(scope, "run-after-release", policy);
        await store.CreateOrReadRunAsync(second);
        var secondBatch = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = second.Key, BatchOrdinal = 1 });
        secondBatch.DeletedCount.Should().Be(1);
        (await ReadBodyStateAsync(dataSource, lease.Options, "held-row")).Should().Be("Purged");
        (await ReadBodyStateAsync(dataSource, lease.Options, "protected-row")).Should().Be("BodyPresent");
    }

    [Fact]
    public async Task Hold_version_conflict_and_already_purged_are_rejected()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var old = Snapshot.AddDays(-90);
        await InsertBodyAsync(dataSource, lease.Options, "hold-version", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "purged-first", "tenant-a", old, old);

        var hold = await store.PlaceHoldAsync(new AuditHoldPlacement
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            AuditId = "hold-version",
            ReasonCode = "retention.legal",
            Actor = Actor,
            CorrelationId = "corr-hold"
        });

        var badRelease = () => store.ReleaseHoldAsync(new AuditHoldRelease
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            HoldId = hold.HoldId,
            ExpectedVersion = 2,
            ReasonCode = "retention.legal-cleared",
            Actor = Actor,
            CorrelationId = "corr-release"
        }).AsTask();
        await badRelease.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.HoldVersionConflict);

        var policy = Policy(days: 30, batch: 10);
        var request = RunRequest(scope, "run-purge", policy);
        await store.CreateOrReadRunAsync(request);
        await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });
        (await ReadBodyStateAsync(dataSource, lease.Options, "purged-first")).Should().Be("Purged");
        (await ReadBodyStateAsync(dataSource, lease.Options, "hold-version")).Should().Be("BodyPresent");

        var afterPurge = () => store.PlaceHoldAsync(new AuditHoldPlacement
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            AuditId = "purged-first",
            ReasonCode = "retention.legal",
            Actor = Actor,
            CorrelationId = "corr-hold-late"
        }).AsTask();
        await afterPurge.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.HoldAlreadyPurged);
    }

    [Fact]
    public async Task Batch_budget_is_persisted_and_remaining_is_reported()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 2, maxBatches: 2);
        var old = Snapshot.AddDays(-90);
        for (var index = 0; index < 5; index++)
            await InsertBodyAsync(dataSource, lease.Options, $"budget-{index}", "tenant-a", old, old);

        var request = RunRequest(scope, "run-budget", policy);
        await store.CreateOrReadRunAsync(request);
        var first = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });
        first.DeletedCount.Should().Be(2);
        first.RemainingMayExist.Should().BeTrue();
        first.RunState.Should().Be(AuditRetentionRunState.Running);

        var second = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 2 });
        second.DeletedCount.Should().Be(2);
        second.RemainingMayExist.Should().BeTrue();
        second.RunState.Should().Be(AuditRetentionRunState.BudgetReached);
        second.CommittedDeletedCount.Should().Be(4);
        second.CommittedBatchCount.Should().Be(2);

        var third = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 3 });
        third.DeletedCount.Should().Be(0);
        third.RunState.Should().Be(AuditRetentionRunState.BudgetReached);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(1);
    }

    [Fact]
    public async Task Preview_is_read_only_bounded_and_does_not_create_a_run()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 2);
        var old = Snapshot.AddDays(-90);
        for (var index = 0; index < 4; index++)
            await InsertBodyAsync(dataSource, lease.Options, $"preview-{index}", "tenant-a", old, old);

        var preview = await store.PreviewAsync(new AuditRetentionPreviewRequest
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot)
        });

        preview.Sample.Should().HaveCount(2);
        preview.HasMore.Should().BeTrue();
        preview.EligibleInSample.Should().Be(2);
        preview.PolicyHash.Should().Be(policy.ComputeHash());
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(4);
        (await CountRunsAsync(dataSource, lease.Options)).Should().Be(0);
    }

    [Fact]
    public async Task Repeated_batch_key_returns_the_committed_receipt_without_deleting_again()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 1);
        var old = Snapshot.AddDays(-90);
        await InsertBodyAsync(dataSource, lease.Options, "repeat-1", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "repeat-2", "tenant-a", old, old);

        var request = RunRequest(scope, "run-repeat", policy);
        await store.CreateOrReadRunAsync(request);
        var first = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });
        first.DeletedCount.Should().Be(1);
        first.FromExistingReceipt.Should().BeFalse();

        var repeated = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });
        repeated.FromExistingReceipt.Should().BeTrue();
        repeated.DeletedCount.Should().Be(1);
        repeated.CommittedDeletedCount.Should().Be(1);
        repeated.CommittedBatchCount.Should().Be(1);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_same_batch_serializes_on_the_scope_lock()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 1);
        var old = Snapshot.AddDays(-90);
        await InsertBodyAsync(dataSource, lease.Options, "concurrent-1", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "concurrent-2", "tenant-a", old, old);

        var request = RunRequest(scope, "run-concurrent", policy);
        await store.CreateOrReadRunAsync(request);
        var outcomes = await Task.WhenAll(
            store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 }).AsTask(),
            store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 }).AsTask());

        outcomes.Should().ContainSingle(outcome => !outcome.FromExistingReceipt);
        outcomes.Should().ContainSingle(outcome => outcome.FromExistingReceipt);
        outcomes.Should().OnlyContain(outcome => outcome.DeletedCount == 1);
        outcomes.Should().OnlyContain(outcome => outcome.CommittedDeletedCount == 1);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(1);
    }

    [Fact]
    public async Task Receipt_failure_rolls_back_the_whole_batch()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 10);
        var old = Snapshot.AddDays(-90);
        await InsertBodyAsync(dataSource, lease.Options, "rollback-row", "tenant-a", old, old);
        await InstallReceiptFailureTriggerAsync(dataSource, lease.Options, failOrdinal: 1);

        var request = RunRequest(scope, "run-rollback", policy);
        await store.CreateOrReadRunAsync(request);
        var act = () => store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 }).AsTask();
        await act.Should().ThrowAsync<PostgresException>();

        (await ReadBodyStateAsync(dataSource, lease.Options, "rollback-row")).Should().Be("BodyPresent");
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(1);
        (await CountBatchesAsync(dataSource, lease.Options)).Should().Be(0);
        var run = await store.ReadRunAsync(request.Key);
        run!.CommittedDeletedCount.Should().Be(0);
        run.CommittedBatchCount.Should().Be(0);
        run.State.Should().Be(AuditRetentionRunState.Running);
    }

    [Fact]
    public async Task Second_batch_failure_keeps_the_first_committed_batch()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 1, maxBatches: 5);
        var old = Snapshot.AddDays(-90);
        await InsertBodyAsync(dataSource, lease.Options, "first-row", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "second-row", "tenant-a", old, old);
        await InstallReceiptFailureTriggerAsync(dataSource, lease.Options, failOrdinal: 2);

        var request = RunRequest(scope, "run-partial", policy);
        await store.CreateOrReadRunAsync(request);
        var first = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });
        first.DeletedCount.Should().Be(1);

        var act = () => store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 2 }).AsTask();
        await act.Should().ThrowAsync<PostgresException>();

        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(1);
        (await CountBatchesAsync(dataSource, lease.Options)).Should().Be(1);
        var run = await store.ReadRunAsync(request.Key);
        run!.CommittedDeletedCount.Should().Be(1);
        run.CommittedBatchCount.Should().Be(1);
    }

    [Fact]
    public async Task Sink_dedup_survives_purge_and_old_writer_is_rejected()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var sink = new PostgreSqlAuditSink(dataSource, lease.Options);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 10);
        var envelope = CreateEnvelope("sink-row", "tenant-a", Snapshot.AddDays(-90), "integrity-1");

        var accepted = await sink.WriteAsync(envelope);
        accepted.Status.Should().Be(AuditSinkWriteStatus.Accepted);
        await ShiftAcceptedAtIntoThePastAsync(dataSource, lease.Options, "sink-row", Snapshot.AddDays(-90));

        var request = RunRequest(scope, "run-sink", policy);
        await store.CreateOrReadRunAsync(request);
        var batch = await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });
        batch.DeletedCount.Should().Be(1);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(0);

        var duplicate = await sink.WriteAsync(envelope);
        duplicate.Status.Should().Be(AuditSinkWriteStatus.Duplicate);
        duplicate.FirstAcceptedAt.Should().Be(Snapshot.AddDays(-90));
        var duplicateAgain = await sink.WriteAsync(envelope);
        duplicateAgain.FirstAcceptedAt.Should().Be(duplicate.FirstAcceptedAt);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(0);

        var conflicting = await sink.WriteAsync(CreateEnvelope("sink-row", "tenant-a", Snapshot.AddDays(-90), "integrity-2"));
        conflicting.Status.Should().Be(AuditSinkWriteStatus.Conflict);
        conflicting.ExistingIntegrity!.Value.Should().Be("integrity-1");

        var oldWriter = () => InsertLegacyEnvelopeWithoutLedgerAsync(dataSource, lease.Options, "sink-row");
        await oldWriter.Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Sink_dedup_does_not_allow_cross_tenant_duplicate_identity()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var sink = new PostgreSqlAuditSink(dataSource, lease.Options);
        var first = await sink.WriteAsync(CreateEnvelope("shared-id", "tenant-a", Snapshot.AddDays(-1), "shared-integrity"));
        first.Status.Should().Be(AuditSinkWriteStatus.Accepted);

        var second = await sink.WriteAsync(CreateEnvelope("shared-id", "tenant-b", Snapshot.AddDays(-1), "shared-integrity"));
        second.Status.Should().Be(AuditSinkWriteStatus.Duplicate);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-a")).Should().Be(1);
        (await CountBodiesAsync(dataSource, lease.Options, "tenant-b")).Should().Be(0);
    }

    [Fact]
    public async Task Backfill_projects_valid_rows_quarantines_invalid_rows_and_is_repeatable()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var envelopes = lease.Options.SchemaQuotedTable("runtime_audit_envelopes");
        var ledger = lease.Options.SchemaQuotedTable("runtime_audit_retention_ledger");
        await using (var command = new NpgsqlCommand(
            $"alter table {envelopes} drop constraint fk_runtime_audit_envelopes_ledger;"
            + $"delete from {ledger};", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        var old = Snapshot.AddDays(-90);
        await InsertLegacyEnvelopeAsync(connection, lease.Options, "legacy-host", null, old, CreateEnvelopeJson("legacy-host", null, old, "legacy-1", "test"));
        await InsertLegacyEnvelopeAsync(connection, lease.Options, "legacy-tenant", "tenant-a", old, CreateEnvelopeJson("legacy-tenant", "tenant-a", old, "legacy-2", "test"));
        await InsertLegacyEnvelopeAsync(connection, lease.Options, "legacy-governance", "tenant-a", old, CreateEnvelopeJson("legacy-governance", "tenant-a", old, "legacy-3", AuditRetentionSemanticNames.RunTargetKind));
        await InsertLegacyEnvelopeAsync(connection, lease.Options, "legacy-bad-json", "tenant-a", old, "{}");
        await InsertLegacyEnvelopeAsync(connection, lease.Options, "legacy-mismatch", "tenant-a", old, CreateEnvelopeJson("different-id", "tenant-a", old, "legacy-4", "test"));

        await PostgreSqlAuditRetentionLedgerBackfill.RunAsync(connection, lease.Options, default);

        (await ReadLedgerScopeAsync(connection, lease.Options, "legacy-host")).Should().Be("host");
        (await ReadLedgerScopeAsync(connection, lease.Options, "legacy-tenant")).Should().Be("tenant");
        (await ReadLedgerProtectionAsync(connection, lease.Options, "legacy-governance")).Should().Be("governance");
        (await ReadLedgerBodyStateAsync(connection, lease.Options, "legacy-bad-json")).Should().Be("Quarantined");
        (await ReadBodyStateDirectAsync(connection, lease.Options, "legacy-bad-json")).Should().Be("Quarantined");
        (await ReadLedgerQuarantineReasonAsync(connection, lease.Options, "legacy-bad-json")).Should().Be("invalid_envelope");
        (await ReadLedgerBodyStateAsync(connection, lease.Options, "legacy-mismatch")).Should().Be("Quarantined");
        (await ReadLedgerQuarantineReasonAsync(connection, lease.Options, "legacy-mismatch")).Should().Be("audit_id_mismatch");

        var before = await ReadLedgerCountAsync(connection, lease.Options);
        await PostgreSqlAuditRetentionLedgerBackfill.RunAsync(connection, lease.Options, default);
        (await ReadLedgerCountAsync(connection, lease.Options)).Should().Be(before);
    }

    [Fact]
    public async Task Finalize_is_first_terminal_winner_and_retry_resumes_the_same_budget()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var dataSource = CreateDataSource(fixture.ConnectionString);
        var store = new PostgreSqlAuditRetentionStore(dataSource, lease.Options);
        var scope = AuditRetentionScope.ForTenant("tenant-a");
        var policy = Policy(days: 30, batch: 1, maxBatches: 2);
        var old = Snapshot.AddDays(-90);
        await InsertBodyAsync(dataSource, lease.Options, "resume-1", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "resume-2", "tenant-a", old, old);
        await InsertBodyAsync(dataSource, lease.Options, "resume-3", "tenant-a", old, old);

        var request = RunRequest(scope, "run-resume", policy);
        var created = await store.CreateOrReadRunAsync(request);
        await store.ExecuteBatchAsync(new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = 1 });

        var repeated = await store.CreateOrReadRunAsync(request);
        repeated.Key.RequestId.Should().Be(created.Key.RequestId);
        repeated.CommittedBatchCount.Should().Be(1);

        var finalized = await store.FinalizeRunAsync(new AuditRetentionRunFinalization
        {
            Key = request.Key,
            State = AuditRetentionRunState.Cancelled,
            StopReason = AuditRetentionStopReasons.Cancelled
        });
        finalized.State.Should().Be(AuditRetentionRunState.Cancelled);
        finalized.CommittedDeletedCount.Should().Be(1);

        var again = await store.FinalizeRunAsync(new AuditRetentionRunFinalization
        {
            Key = request.Key,
            State = AuditRetentionRunState.Failed,
            StopReason = AuditRetentionStopReasons.Failed
        });
        again.State.Should().Be(AuditRetentionRunState.Cancelled);

        var conflictingBinding = RunRequest(scope, "run-resume", Policy(days: 31, batch: 1, maxBatches: 2));
        var act = () => store.CreateOrReadRunAsync(conflictingBinding).AsTask();
        await act.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.RequestConflict);
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
        => new NpgsqlSlimDataSourceBuilder(connectionString).EnableArrays().Build();

    private static AuditRetentionPolicy Policy(int days, int batch, int maxBatches = 10, int seconds = 60)
        => new()
        {
            RetentionDays = days,
            BatchSize = batch,
            MaxBatchesPerRun = maxBatches,
            MaxRunSeconds = seconds
        };

    private static AuditRetentionRunRequest RunRequest(AuditRetentionScope scope, string requestId, AuditRetentionPolicy policy)
        => new()
        {
            StoreId = PostgreSqlAuditRetentionStore.StoreIdValue,
            SinkId = Sink,
            Scope = scope,
            RequestId = requestId,
            ReasonCode = "retention.test",
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot),
            Actor = Actor,
            CorrelationId = "corr-" + requestId
        };

    private static AuditEnvelope CreateEnvelope(string auditId, string? tenantId, DateTimeOffset occurredAt, string integrityValue)
        => new()
        {
            AuditId = auditId,
            OccurredAt = occurredAt,
            TenantId = tenantId,
            CorrelationId = "corr-" + auditId,
            Actor = new AuditActor { Kind = "system", Id = "test" },
            Action = new AuditAction { Kind = "system", Name = "test" },
            Target = new AuditTarget { Kind = "test", Id = auditId },
            Outcome = new AuditOutcome { Status = "succeeded" },
            Integrity = CreateIntegrity(integrityValue)
        };

    private static string CreateEnvelopeJson(
        string envelopeAuditId,
        string? tenantId,
        DateTimeOffset occurredAt,
        string integrityValue,
        string targetKind)
    {
        var envelope = CreateEnvelope(envelopeAuditId, tenantId, occurredAt, integrityValue) with
        {
            Target = new AuditTarget { Kind = targetKind, Id = envelopeAuditId }
        };
        return PostgreSqlRuntimeStoreSupport.Serialize(envelope, PostgreSqlRuntimeJsonSerializerContext.Default.AuditEnvelope);
    }

    private static CanonicalHash CreateIntegrity(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AccountabilityRecord",
            Scope = "InternalFull",
            Purpose = "AuditEvidence",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "accountability-record-hash-v1"
        };

    private static async Task InsertBodyAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId,
        string? tenantId,
        DateTimeOffset occurredAt,
        DateTimeOffset acceptedAt,
        string protection = "none")
    {
        var envelope = CreateEnvelope(auditId, tenantId, occurredAt, "integrity-" + auditId);
        var envelopeJson = PostgreSqlRuntimeStoreSupport.Serialize(envelope, PostgreSqlRuntimeJsonSerializerContext.Default.AuditEnvelope);
        var integrityJson = PostgreSqlRuntimeStoreSupport.Serialize(envelope.Integrity!, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var ledger = new NpgsqlCommand(
            $"""
             insert into {options.SchemaQuotedTable("runtime_audit_retention_ledger")}
                 (sink_id, audit_id, integrity_json, first_accepted_at, scope_kind, tenant_id, occurred_at, body_state, protection)
             values (@sink, @audit, @integrity, @accepted, @scope, @tenant, @occurred, 'BodyPresent', @protection);
             """,
            connection,
            transaction))
        {
            ledger.Parameters.AddWithValue("sink", Sink);
            ledger.Parameters.AddWithValue("audit", auditId);
            PostgreSqlRuntimeStoreSupport.AddJson(ledger, "integrity", integrityJson);
            ledger.Parameters.AddWithValue("accepted", acceptedAt);
            ledger.Parameters.AddWithValue("scope", tenantId is null ? "host" : "tenant");
            ledger.Parameters.AddWithValue("tenant", tenantId ?? string.Empty);
            ledger.Parameters.AddWithValue("occurred", occurredAt);
            ledger.Parameters.AddWithValue("protection", protection);
            await ledger.ExecuteNonQueryAsync();
        }

        await using (var body = new NpgsqlCommand(
            $"""
             insert into {options.SchemaQuotedTable("runtime_audit_envelopes")}
                 (sink_id, audit_id, integrity_json, envelope_json, body_state)
             values (@sink, @audit, @integrity, @envelope, 'BodyPresent');
             """,
            connection,
            transaction))
        {
            body.Parameters.AddWithValue("sink", Sink);
            body.Parameters.AddWithValue("audit", auditId);
            PostgreSqlRuntimeStoreSupport.AddJson(body, "integrity", integrityJson);
            PostgreSqlRuntimeStoreSupport.AddJson(body, "envelope", envelopeJson);
            await body.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task InsertQuarantinedAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId,
        DateTimeOffset acceptedAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var ledger = new NpgsqlCommand(
            $"""
             insert into {options.SchemaQuotedTable("runtime_audit_retention_ledger")}
                 (sink_id, audit_id, integrity_json, first_accepted_at, scope_kind, tenant_id, occurred_at, body_state, protection, quarantine_reason)
             values (@sink, @audit, null, @accepted, 'quarantine', '', @occurred, 'Quarantined', 'none', 'test');
             """,
            connection,
            transaction))
        {
            ledger.Parameters.AddWithValue("sink", Sink);
            ledger.Parameters.AddWithValue("audit", auditId);
            ledger.Parameters.AddWithValue("accepted", acceptedAt);
            ledger.Parameters.AddWithValue("occurred", acceptedAt);
            await ledger.ExecuteNonQueryAsync();
        }

        await using (var body = new NpgsqlCommand(
            $"""
             insert into {options.SchemaQuotedTable("runtime_audit_envelopes")}
                 (sink_id, audit_id, integrity_json, envelope_json, body_state)
             values (@sink, @audit, @integrity::jsonb, @envelope::jsonb, 'Quarantined');
             """,
            connection,
            transaction))
        {
            body.Parameters.AddWithValue("sink", Sink);
            body.Parameters.AddWithValue("audit", auditId);
            body.Parameters.AddWithValue("integrity", "{}");
            body.Parameters.AddWithValue("envelope", "{}");
            await body.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task InstallReceiptFailureTriggerAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        long failOrdinal)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"""
             create or replace function {options.SchemaQuotedTable("fail_retention_receipt")}() returns trigger as $$
             begin
                 if new.batch_ordinal = {failOrdinal} then
                     raise exception 'injected retention receipt failure';
                 end if;
                 return new;
             end;
             $$ language plpgsql;
             create trigger trg_fail_retention_receipt before insert on {options.SchemaQuotedTable("runtime_audit_retention_batches")}
                 for each row execute function {options.SchemaQuotedTable("fail_retention_receipt")}();
             """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ShiftAcceptedAtIntoThePastAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId,
        DateTimeOffset acceptedAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"update {options.SchemaQuotedTable("runtime_audit_retention_ledger")} set first_accepted_at=@accepted, occurred_at=@accepted where audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("accepted", acceptedAt);
        command.Parameters.AddWithValue("audit", auditId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacyEnvelopeWithoutLedgerAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"insert into {options.SchemaQuotedTable("runtime_audit_envelopes")} (sink_id, audit_id, integrity_json, envelope_json) values (@sink, @audit, '{{}}'::jsonb, '{{}}'::jsonb);",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacyEnvelopeAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId,
        string? tenantId,
        DateTimeOffset acceptedAt,
        string envelopeJson)
    {
        await using var command = new NpgsqlCommand(
            $"""
             insert into {options.SchemaQuotedTable("runtime_audit_envelopes")}
                 (sink_id, audit_id, integrity_json, envelope_json, accepted_at)
             values (@sink, @audit, @integrity, @envelope, @accepted);
             """,
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity",
            PostgreSqlRuntimeStoreSupport.Serialize(CreateIntegrity("integrity-" + auditId), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash));
        PostgreSqlRuntimeStoreSupport.AddJson(command, "envelope", envelopeJson);
        command.Parameters.AddWithValue("accepted", acceptedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadBodyStateAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"select body_state from {options.SchemaQuotedTable("runtime_audit_retention_ledger")} where sink_id=@sink and audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountBodiesAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options,
        string tenantId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"""
             select count(*) from {options.SchemaQuotedTable("runtime_audit_envelopes")} e
             join {options.SchemaQuotedTable("runtime_audit_retention_ledger")} l
               on l.sink_id = e.sink_id and l.audit_id = e.audit_id
             where e.body_state = 'BodyPresent' and l.scope_kind = 'tenant' and l.tenant_id = @tenant;
             """,
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountRunsAsync(NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"select count(*) from {options.SchemaQuotedTable("runtime_audit_retention_runs")};", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountBatchesAsync(NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"select count(*) from {options.SchemaQuotedTable("runtime_audit_retention_batches")};", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ReadLedgerScopeAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var command = new NpgsqlCommand(
            $"select scope_kind from {options.SchemaQuotedTable("runtime_audit_retention_ledger")} where sink_id=@sink and audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<string?> ReadLedgerProtectionAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var command = new NpgsqlCommand(
            $"select protection from {options.SchemaQuotedTable("runtime_audit_retention_ledger")} where sink_id=@sink and audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<string?> ReadLedgerBodyStateAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var command = new NpgsqlCommand(
            $"select body_state from {options.SchemaQuotedTable("runtime_audit_retention_ledger")} where sink_id=@sink and audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<string?> ReadBodyStateDirectAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var command = new NpgsqlCommand(
            $"select body_state from {options.SchemaQuotedTable("runtime_audit_envelopes")} where sink_id=@sink and audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<string?> ReadLedgerQuarantineReasonAsync(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string auditId)
    {
        await using var command = new NpgsqlCommand(
            $"select quarantine_reason from {options.SchemaQuotedTable("runtime_audit_retention_ledger")} where sink_id=@sink and audit_id=@audit;",
            connection);
        command.Parameters.AddWithValue("sink", Sink);
        command.Parameters.AddWithValue("audit", auditId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<long> ReadLedgerCountAsync(NpgsqlConnection connection, PostgreSqlRuntimePersistenceOptions options)
    {
        await using var command = new NpgsqlCommand(
            $"select count(*) from {options.SchemaQuotedTable("runtime_audit_retention_ledger")};", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
