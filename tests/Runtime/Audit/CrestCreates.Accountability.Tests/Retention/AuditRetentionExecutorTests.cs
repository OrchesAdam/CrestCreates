using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Retention;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Retention;

public sealed class AuditRetentionExecutorTests
{
    private static readonly DateTimeOffset Snapshot = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ContinuesUntilStoreReportsNoRemaining_AndMirrorsEachBatch()
    {
        var request = Request();
        var store = new FakeStore();
        var time = new MutableTimeProvider { Now = Snapshot };
        store.NextBatch = batch => ValueTask.FromResult(
            batch.BatchOrdinal == 1
                ? Outcome(1, deleted: 40, totalDeleted: 40, remaining: true, committedAt: Snapshot)
                : Outcome(2, deleted: 10, totalDeleted: 50, remaining: false, committedAt: Snapshot.AddMinutes(1)));
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, time);

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.CommitKnowledge.Should().Be(AuditRetentionCommitKnowledge.Committed);
        result.CommittedDeletedCount.Should().Be(50);
        result.CommittedBatchCount.Should().Be(2);
        result.RemainingMayExist.Should().BeFalse();
        result.StopReason.Should().Be(AuditRetentionStopReasons.NoCandidates);
        result.MirrorOutcome.Should().Be(AuditRetentionMirrorOutcome.Succeeded);
        store.BatchOrdinals.Should().Equal(1L, 2L);
        store.Finalizations.Should().BeEmpty();
        recorder.Envelopes.Should().HaveCount(2);
        recorder.Envelopes.Select(envelope => envelope.Target.Version).Should().Equal("1", "2");
        recorder.Envelopes.Should().OnlyContain(envelope => envelope.Target.Kind == AuditRetentionSemanticNames.BatchTargetKind);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAtMaxBatches_AndFinalizesBudgetReached()
    {
        var request = Request(Policy(maxBatches: 2));
        var store = new FakeStore();
        store.NextBatch = batch => ValueTask.FromResult(
            Outcome(batch.BatchOrdinal, deleted: 100, totalDeleted: 100 * batch.BatchOrdinal, remaining: true, committedAt: Snapshot));
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.BudgetReached);
        result.CommittedDeletedCount.Should().Be(200);
        result.CommittedBatchCount.Should().Be(2);
        result.RemainingMayExist.Should().BeTrue();
        result.StopReason.Should().Be(AuditRetentionStopReasons.BudgetReached);
        store.BatchOrdinals.Should().Equal(1L, 2L);
        store.Finalizations.Should().ContainSingle()
            .Which.State.Should().Be(AuditRetentionRunState.BudgetReached);
        recorder.Envelopes.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAtTimeBudget_EvenWhenBatchesRemain()
    {
        var request = Request(Policy(maxSeconds: 60));
        var store = new FakeStore();
        var time = new MutableTimeProvider { Now = Snapshot };
        store.NextBatch = batch =>
        {
            time.Now = time.Now.AddSeconds(61);
            return ValueTask.FromResult(
                Outcome(batch.BatchOrdinal, deleted: 5, totalDeleted: 5, remaining: true, committedAt: time.Now));
        };
        var executor = CreateExecutor(store, new FakeRecorder(), time);

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.BudgetReached);
        result.CommittedBatchCount.Should().Be(1);
        result.RemainingMayExist.Should().BeTrue();
        store.BatchOrdinals.Should().Equal(1L);
        store.Finalizations.Should().ContainSingle()
            .Which.StopReason.Should().Be(AuditRetentionStopReasons.BudgetReached);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_FinalizesCancelled_AndKeepsCommittedBatches()
    {
        var request = Request();
        using var cancellation = new CancellationTokenSource();
        var store = new FakeStore();
        store.NextBatch = batch =>
        {
            store.Run = NewRun(request, AuditRetentionRunState.Running, deleted: 40, batches: 1);
            cancellation.Cancel();
            return ValueTask.FromResult(
                Outcome(batch.BatchOrdinal, deleted: 40, totalDeleted: 40, remaining: true, committedAt: Snapshot));
        };
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request, cancellation.Token);

        result.State.Should().Be(AuditRetentionRunState.Cancelled);
        result.CommitKnowledge.Should().Be(AuditRetentionCommitKnowledge.Committed);
        result.CommittedDeletedCount.Should().Be(40);
        result.CommittedBatchCount.Should().Be(1);
        result.RemainingMayExist.Should().BeTrue();
        result.StopReason.Should().Be(AuditRetentionStopReasons.Cancelled);
        store.Finalizations.Should().ContainSingle()
            .Which.State.Should().Be(AuditRetentionRunState.Cancelled);
        recorder.Envelopes.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousBatchFailure_RecoversFromCommittedReceipt()
    {
        var request = Request();
        var store = new FakeStore();
        store.NextBatch = _ =>
        {
            store.Run = NewRun(
                request,
                AuditRetentionRunState.Completed,
                deleted: 40,
                batches: 1,
                stopReason: AuditRetentionStopReasons.NoCandidates,
                updatedAt: Snapshot.AddMinutes(1));
            throw new IOException("connection reset after commit");
        };
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.CommitKnowledge.Should().Be(AuditRetentionCommitKnowledge.Committed);
        result.CommittedDeletedCount.Should().Be(40);
        result.CommittedBatchCount.Should().Be(1);
        result.RemainingMayExist.Should().BeFalse();
        result.MirrorOutcome.Should().Be(AuditRetentionMirrorOutcome.Succeeded);
        store.Finalizations.Should().BeEmpty();
        recorder.Envelopes.Should().ContainSingle()
            .Which.Target.Id.Should().EndWith("/1");
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousBatchFailure_WithoutReceipt_ReportsUnknownCommit()
    {
        var request = Request();
        var store = new FakeStore();
        store.NextBatch = _ => throw new IOException("connection reset before commit");
        store.Read = _ => ValueTask.FromResult<AuditRetentionRunRecord?>(null);
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Unknown);
        result.CommitKnowledge.Should().Be(AuditRetentionCommitKnowledge.Unknown);
        result.CommittedDeletedCount.Should().Be(0);
        result.CommittedBatchCount.Should().Be(0);
        result.RemainingMayExist.Should().BeTrue();
        result.StopReason.Should().Be(AuditRetentionStopReasons.UnknownCommit);
        result.MirrorOutcome.Should().BeNull();
        store.Finalizations.Should().BeEmpty();
        recorder.Envelopes.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousBatchFailure_WithRunningReceipt_Continues()
    {
        var request = Request();
        var store = new FakeStore();
        store.NextBatch = batch =>
        {
            if (batch.BatchOrdinal == 1)
            {
                store.Run = NewRun(request, AuditRetentionRunState.Running, deleted: 40, batches: 1);
                throw new IOException("connection reset after commit");
            }

            return ValueTask.FromResult(
                Outcome(batch.BatchOrdinal, deleted: 10, totalDeleted: 50, remaining: false, committedAt: Snapshot.AddMinutes(1)));
        };
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.CommittedDeletedCount.Should().Be(50);
        result.CommittedBatchCount.Should().Be(2);
        store.BatchOrdinals.Should().Equal(1L, 2L);
        recorder.Envelopes.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingTerminalRun_ReturnsStoredResultWithoutExecutingBatches()
    {
        var request = Request();
        var store = new FakeStore
        {
            Run = NewRun(
                request,
                AuditRetentionRunState.Completed,
                deleted: 7,
                batches: 1,
                stopReason: AuditRetentionStopReasons.NoCandidates)
        };
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.CommittedDeletedCount.Should().Be(7);
        result.CommittedBatchCount.Should().Be(1);
        result.MirrorOutcome.Should().BeNull();
        store.BatchOrdinals.Should().BeEmpty();
        store.Finalizations.Should().BeEmpty();
        recorder.Envelopes.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ExpectedPolicyHashMismatch_RejectsBeforeStoreAccess()
    {
        var policy = Policy();
        var request = Request(policy) with { ExpectedPolicyHash = new string('0', 64) };
        var store = new FakeStore();
        var executor = CreateExecutor(store, new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var act = () => executor.ExecuteAsync(request).AsTask();

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.PolicyHashMismatch);
        store.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_PersistedRunCreatedWithDifferentPolicy_Rejects()
    {
        var request = Request();
        var store = new FakeStore
        {
            Run = NewRun(
                request,
                AuditRetentionRunState.Running,
                policyHash: Policy(batch: 250).ComputeHash())
        };
        var executor = CreateExecutor(store, new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var act = () => executor.ExecuteAsync(request).AsTask();

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.PolicyHashMismatch);
        store.BatchOrdinals.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CreateRunAmbiguousFailure_ReconcilesExistingRun()
    {
        var request = Request();
        var store = new FakeStore();
        store.CreateOrRead = _ => throw new IOException("connection reset after insert");
        store.Read = _ => ValueTask.FromResult<AuditRetentionRunRecord?>(
            NewRun(request, AuditRetentionRunState.Running, deleted: 0, batches: 0));
        store.NextBatch = batch => ValueTask.FromResult(
            Outcome(batch.BatchOrdinal, deleted: 3, totalDeleted: 3, remaining: false, committedAt: Snapshot));
        var executor = CreateExecutor(store, new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.CommittedDeletedCount.Should().Be(3);
        store.ReadCalls.Should().Be(1);
        store.BatchOrdinals.Should().Equal(1L);
    }

    [Fact]
    public async Task ExecuteAsync_MirrorRejection_IsReportedWithoutChangingRunState()
    {
        var request = Request();
        var store = new FakeStore();
        store.NextBatch = batch => ValueTask.FromResult(
            Outcome(batch.BatchOrdinal, deleted: 2, totalDeleted: 2, remaining: false, committedAt: Snapshot));
        var recorder = new FakeRecorder
        {
            OnRecord = envelope => new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Rejected,
                ProcessedAt = Snapshot,
                Issues = [new AuditRecordIssue("mirror-rejected")]
            }
        };
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.MirrorOutcome.Should().Be(AuditRetentionMirrorOutcome.Failed("mirror-rejected"));
    }

    [Fact]
    public async Task ExecuteAsync_MirrorFailure_IsReportedWithoutChangingRunState()
    {
        var request = Request();
        var store = new FakeStore();
        store.NextBatch = batch => ValueTask.FromResult(
            Outcome(batch.BatchOrdinal, deleted: 2, totalDeleted: 2, remaining: false, committedAt: Snapshot));
        var recorder = new FakeRecorder { OnRecord = _ => throw new InvalidOperationException("sink down") };
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ExecuteAsync(request);

        result.State.Should().Be(AuditRetentionRunState.Completed);
        result.MirrorOutcome.Should().Be(AuditRetentionMirrorOutcome.Failed("AUDIT_RETENTION_MIRROR_FAILURE"));
    }

    [Fact]
    public async Task PreviewAsync_PolicyHashMismatch_Rejects()
    {
        var policy = Policy();
        var request = new AuditRetentionPreviewRequest
        {
            StoreId = FakeStore.StoreIdValue,
            SinkId = FakeStore.SinkIdValue,
            Scope = AuditRetentionScope.Host,
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot)
        };
        var store = new FakeStore
        {
            Preview = previewRequest => ValueTask.FromResult(NewPreview(previewRequest, new string('a', 64)))
        };
        var executor = CreateExecutor(store, new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var act = () => executor.PreviewAsync(request).AsTask();

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.PolicyHashMismatch);
    }

    [Fact]
    public async Task PreviewAsync_MatchingHash_ReturnsStorePreview()
    {
        var policy = Policy();
        var request = new AuditRetentionPreviewRequest
        {
            StoreId = FakeStore.StoreIdValue,
            SinkId = FakeStore.SinkIdValue,
            Scope = AuditRetentionScope.Host,
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot)
        };
        var store = new FakeStore
        {
            Preview = previewRequest => ValueTask.FromResult(NewPreview(previewRequest, policy.ComputeHash()))
        };
        var executor = CreateExecutor(store, new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var preview = await executor.PreviewAsync(request);

        preview.PolicyHash.Should().Be(policy.ComputeHash());
        preview.EligibleInSample.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownStore_Rejects()
    {
        var request = Request() with { StoreId = "other-store" };
        var executor = CreateExecutor(new FakeStore(), new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var act = () => executor.ExecuteAsync(request).AsTask();

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.StoreUnknown);
    }

    [Fact]
    public async Task ExecuteAsync_SinkMismatch_Rejects()
    {
        var request = Request() with { SinkId = "other-sink" };
        var executor = CreateExecutor(new FakeStore(), new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var act = () => executor.ExecuteAsync(request).AsTask();

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.StoreMismatch);
    }

    [Fact]
    public async Task ExecuteAsync_StoreWithoutFullCapabilities_Rejects()
    {
        var store = new FakeStore(IdentityWith(capabilities => capabilities with { SupportsHolds = false }));
        var executor = CreateExecutor(store, new FakeRecorder(), new MutableTimeProvider { Now = Snapshot });

        var act = () => executor.ExecuteAsync(Request()).AsTask();

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.CapabilityMissing);
    }

    [Fact]
    public void Constructor_DuplicateStoreId_Rejects()
    {
        var act = () => new AuditRetentionExecutor([new FakeStore(), new FakeStore()]);

        act.Should().Throw<AuditRetentionException>()
            .Which.Code.Should().Be(AuditRetentionErrorCodes.StoreDuplicate);
    }

    [Fact]
    public void GetStoreIdentity_ReturnsDeclaredIdentity()
    {
        var executor = new AuditRetentionExecutor([new FakeStore()]);

        var identity = executor.GetStoreIdentity(FakeStore.StoreIdValue);

        identity.SinkId.Should().Be(FakeStore.SinkIdValue);
        identity.Capabilities.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task PlaceHoldAsync_MirrorsPlacementFact()
    {
        var store = new FakeStore
        {
            PlaceHold = placement => ValueTask.FromResult(NewHold(placement, version: 1))
        };
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.PlaceHoldAsync(Placement());

        result.Hold.Version.Should().Be(1);
        result.MirrorOutcome.Should().Be(AuditRetentionMirrorOutcome.Succeeded);
        recorder.Envelopes.Should().ContainSingle()
            .Which.Action.Name.Should().Be(AuditRetentionSemanticNames.HoldPlaceActionName);
    }

    [Fact]
    public async Task ReleaseHoldAsync_MirrorsReleaseFact()
    {
        var store = new FakeStore
        {
            ReleaseHold = release => ValueTask.FromResult(NewHold(
                new AuditHoldPlacement
                {
                    StoreId = release.StoreId,
                    SinkId = release.SinkId,
                    Scope = release.Scope,
                    AuditId = "audit-1",
                    ReasonCode = release.ReasonCode,
                    Actor = release.Actor,
                    CorrelationId = release.CorrelationId
                },
                version: release.ExpectedVersion + 1,
                released: true))
        };
        var recorder = new FakeRecorder();
        var executor = CreateExecutor(store, recorder, new MutableTimeProvider { Now = Snapshot });

        var result = await executor.ReleaseHoldAsync(new AuditHoldRelease
        {
            StoreId = FakeStore.StoreIdValue,
            SinkId = FakeStore.SinkIdValue,
            Scope = AuditRetentionScope.ForTenant("tenant-1"),
            HoldId = "hold-1",
            ExpectedVersion = 1,
            ReasonCode = "investigation.closed",
            Actor = new AuditActor { Kind = "user", Id = "bob" },
            CorrelationId = "corr-release"
        });

        result.Hold.Version.Should().Be(2);
        result.MirrorOutcome.Should().Be(AuditRetentionMirrorOutcome.Succeeded);
        recorder.Envelopes.Should().ContainSingle()
            .Which.Action.Name.Should().Be(AuditRetentionSemanticNames.HoldReleaseActionName);
    }

    private static AuditRetentionExecutor CreateExecutor(FakeStore store, IAuditRecorder recorder, TimeProvider time)
        => new([store], recorder, time);

    private static AuditRetentionPolicy Policy(
        int retentionDays = 90,
        int batch = 100,
        int maxBatches = 5,
        int maxSeconds = 60)
        => new()
        {
            RetentionDays = retentionDays,
            BatchSize = batch,
            MaxBatchesPerRun = maxBatches,
            MaxRunSeconds = maxSeconds
        };

    private static AuditRetentionRunRequest Request(AuditRetentionPolicy? policy = null)
    {
        policy ??= Policy();
        return new AuditRetentionRunRequest
        {
            StoreId = FakeStore.StoreIdValue,
            SinkId = FakeStore.SinkIdValue,
            Scope = AuditRetentionScope.ForTenant("tenant-1"),
            RequestId = "req-1",
            ReasonCode = "retention.manual",
            Policy = policy,
            SnapshotAt = Snapshot,
            Cutoff = policy.ComputeCutoff(Snapshot),
            Actor = new AuditActor { Kind = "user", Id = "alice" },
            CorrelationId = "corr-1"
        };
    }

    private static AuditHoldPlacement Placement() => new()
    {
        StoreId = FakeStore.StoreIdValue,
        SinkId = FakeStore.SinkIdValue,
        Scope = AuditRetentionScope.ForTenant("tenant-1"),
        AuditId = "audit-1",
        ReasonCode = "investigation",
        Actor = new AuditActor { Kind = "user", Id = "alice" },
        CorrelationId = "corr-hold"
    };

    private static AuditHold NewHold(AuditHoldPlacement placement, long version, bool released = false) => new()
    {
        HoldId = "hold-1",
        StoreId = placement.StoreId,
        SinkId = placement.SinkId,
        AuditId = placement.AuditId,
        Scope = placement.Scope,
        Version = version,
        ReasonCode = placement.ReasonCode,
        Actor = placement.Actor,
        CorrelationId = placement.CorrelationId,
        CreatedAt = Snapshot,
        ReleasedAt = released ? Snapshot.AddMinutes(5) : null,
        ReleaseActor = released ? new AuditActor { Kind = "user", Id = "bob" } : null,
        ReleaseCorrelationId = released ? "corr-release" : null,
        ReleaseReasonCode = released ? "investigation.closed" : null
    };

    private static AuditRetentionRunRecord NewRun(
        AuditRetentionRunRequest request,
        AuditRetentionRunState state,
        long deleted = 0,
        long batches = 0,
        string? stopReason = null,
        string? policyHash = null,
        DateTimeOffset? updatedAt = null)
        => new()
        {
            Key = request.Key,
            Policy = request.Policy,
            PolicyHash = policyHash ?? request.Policy.ComputeHash(),
            SnapshotAt = request.SnapshotAt,
            Cutoff = request.Cutoff,
            ReasonCode = request.ReasonCode,
            Actor = request.Actor,
            CorrelationId = request.CorrelationId,
            State = state,
            CommittedDeletedCount = deleted,
            CommittedBatchCount = batches,
            StopReason = stopReason,
            CreatedAt = Snapshot,
            UpdatedAt = updatedAt ?? Snapshot
        };

    private static AuditRetentionBatchOutcome Outcome(
        long ordinal,
        int deleted,
        long totalDeleted,
        bool remaining,
        DateTimeOffset committedAt,
        AuditRetentionRunState state = AuditRetentionRunState.Running,
        string? stopReason = null)
        => new()
        {
            BatchOrdinal = ordinal,
            DeletedCount = deleted,
            CommittedDeletedCount = totalDeleted,
            CommittedBatchCount = ordinal,
            RemainingMayExist = remaining,
            RunState = state,
            StopReason = stopReason,
            FromExistingReceipt = false,
            CommittedAt = committedAt
        };

    private static AuditRetentionPreview NewPreview(AuditRetentionPreviewRequest request, string policyHash)
        => new()
        {
            StoreId = request.StoreId,
            SinkId = request.SinkId,
            Scope = request.Scope,
            SnapshotAt = request.SnapshotAt,
            Cutoff = request.Cutoff,
            PolicyHash = policyHash,
            BatchSize = request.Policy.BatchSize,
            MaxBatchesPerRun = request.Policy.MaxBatchesPerRun,
            HasMore = false,
            Sample =
            [
                new AuditRetentionPreviewCandidate
                {
                    AuditId = "audit-1",
                    OccurredAt = Snapshot.AddDays(-120),
                    FirstAcceptedAt = Snapshot.AddDays(-119),
                    Disposition = AuditRetentionCandidateDisposition.Eligible
                }
            ],
            EligibleInSample = 1,
            HeldInSample = 0,
            ProtectedInSample = 0,
            QuarantinedInSample = 0
        };

    private static AuditRetentionStoreIdentity IdentityWith(
        Func<AuditRetentionStoreCapabilities, AuditRetentionStoreCapabilities> mutate)
        => new()
        {
            StoreId = FakeStore.StoreIdValue,
            SinkId = FakeStore.SinkIdValue,
            Capabilities = mutate(AuditRetentionStoreCapabilities.Full)
        };

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeRecorder : IAuditRecorder
    {
        public List<AuditEnvelope> Envelopes { get; } = [];

        public Func<AuditEnvelope, AuditRecordResult>? OnRecord { get; set; }

        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            if (OnRecord is not null)
                return ValueTask.FromResult(OnRecord(envelope));

            return ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Recorded,
                ProcessedAt = Snapshot,
                SinkResults =
                [
                    new AuditSinkWriteResult
                    {
                        SinkId = FakeStore.SinkIdValue,
                        AuditId = envelope.AuditId,
                        Integrity = new CanonicalHash
                        {
                            Value = new string('a', 64),
                            Algorithm = "SHA-256",
                            AlgorithmVersion = "sha256-canonical-json-v1",
                            ArtifactKind = "AuditEnvelope",
                            Scope = "InternalFull",
                            Purpose = "Integrity",
                            ContractVersion = "canonical-hash-v1",
                            CanonicalShapeVersion = "audit-envelope-v1"
                        },
                        Status = AuditSinkWriteStatus.Accepted
                    }
                ]
            });
        }
    }

    private sealed class FakeStore : IAuditRetentionStore
    {
        public const string StoreIdValue = "test-store";
        public const string SinkIdValue = "test-sink";

        public FakeStore(AuditRetentionStoreIdentity? identity = null)
            => Identity = identity ?? new AuditRetentionStoreIdentity
            {
                StoreId = StoreIdValue,
                SinkId = SinkIdValue,
                Capabilities = AuditRetentionStoreCapabilities.Full
            };

        public AuditRetentionStoreIdentity Identity { get; }

        public AuditRetentionRunRecord? Run { get; set; }

        public Func<AuditRetentionRunRequest, ValueTask<AuditRetentionRunRecord>>? CreateOrRead { get; set; }

        public Func<AuditRetentionBatchRequest, ValueTask<AuditRetentionBatchOutcome>>? NextBatch { get; set; }

        public Func<AuditRetentionRunKey, ValueTask<AuditRetentionRunRecord?>>? Read { get; set; }

        public Func<AuditRetentionRunFinalization, ValueTask<AuditRetentionRunRecord>>? Finalize { get; set; }

        public Func<AuditRetentionPreviewRequest, ValueTask<AuditRetentionPreview>>? Preview { get; set; }

        public Func<AuditHoldPlacement, ValueTask<AuditHold>>? PlaceHold { get; set; }

        public Func<AuditHoldRelease, ValueTask<AuditHold>>? ReleaseHold { get; set; }

        public List<long> BatchOrdinals { get; } = [];

        public List<AuditRetentionRunFinalization> Finalizations { get; } = [];

        public int CreateCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public ValueTask<AuditRetentionRunRecord> CreateOrReadRunAsync(
            AuditRetentionRunRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            if (CreateOrRead is not null)
                return CreateOrRead(request);
            Run ??= NewRun(request, AuditRetentionRunState.Running);
            return ValueTask.FromResult(Run);
        }

        public ValueTask<AuditRetentionPreview> PreviewAsync(
            AuditRetentionPreviewRequest request,
            CancellationToken cancellationToken = default)
            => Preview is null
                ? throw new InvalidOperationException("Preview is not scripted.")
                : Preview(request);

        public ValueTask<AuditRetentionBatchOutcome> ExecuteBatchAsync(
            AuditRetentionBatchRequest request,
            CancellationToken cancellationToken = default)
        {
            BatchOrdinals.Add(request.BatchOrdinal);
            return NextBatch is null
                ? throw new InvalidOperationException("No batch is scripted.")
                : NextBatch(request);
        }

        public ValueTask<AuditRetentionRunRecord?> ReadRunAsync(
            AuditRetentionRunKey key,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Read is null
                ? ValueTask.FromResult(Run)
                : Read(key);
        }

        public ValueTask<AuditRetentionRunRecord> FinalizeRunAsync(
            AuditRetentionRunFinalization finalization,
            CancellationToken cancellationToken = default)
        {
            Finalizations.Add(finalization);
            if (Finalize is not null)
                return Finalize(finalization);

            Run = Run! with
            {
                State = finalization.State,
                StopReason = finalization.StopReason
            };
            return ValueTask.FromResult(Run);
        }

        public ValueTask<AuditHold> PlaceHoldAsync(
            AuditHoldPlacement placement,
            CancellationToken cancellationToken = default)
            => PlaceHold is null
                ? throw new InvalidOperationException("PlaceHold is not scripted.")
                : PlaceHold(placement);

        public ValueTask<AuditHold> ReleaseHoldAsync(
            AuditHoldRelease release,
            CancellationToken cancellationToken = default)
            => ReleaseHold is null
                ? throw new InvalidOperationException("ReleaseHold is not scripted.")
                : ReleaseHold(release);
    }
}
