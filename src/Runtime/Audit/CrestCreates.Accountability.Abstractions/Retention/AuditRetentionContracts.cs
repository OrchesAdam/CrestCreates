using System;
using System.Collections.Generic;
using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Abstractions.Retention;

public enum AuditRetentionRunState
{
    Running = 1,
    Completed = 2,
    BudgetReached = 3,
    Cancelled = 4,
    Failed = 5,
    Unknown = 6
}

public enum AuditRetentionCommitKnowledge
{
    None = 1,
    Committed = 2,
    Unknown = 3
}

public enum AuditRetentionCandidateDisposition
{
    Eligible = 1,
    ActiveHold = 2,
    Protected = 3,
    Quarantined = 4
}

public static class AuditRetentionStopReasons
{
    public const string NoCandidates = "no-candidates";
    public const string BudgetReached = "budget-reached";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string PolicyRejected = "policy-rejected";
    public const string UnknownCommit = "unknown-commit";
}

public sealed record AuditRetentionRunKey
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required string RequestId { get; init; }

    public void EnsureValid()
    {
        AuditRetentionContract.RequireIdentifier(StoreId, nameof(StoreId));
        AuditRetentionContract.RequireIdentifier(SinkId, nameof(SinkId));
        AuditRetentionContract.RequireIdentifier(RequestId, nameof(RequestId));
        Scope.EnsureValid();
    }

    public string StableSignature()
        => $"{StoreId}\u001f{SinkId}\u001f{Scope.StableSignature()}\u001f{RequestId}";
}

public sealed record AuditRetentionPreviewRequest
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required AuditRetentionPolicy Policy { get; init; }
    public required DateTimeOffset SnapshotAt { get; init; }
    public required DateTimeOffset Cutoff { get; init; }

    public void EnsureValid()
    {
        AuditRetentionContract.RequireIdentifier(StoreId, nameof(StoreId));
        AuditRetentionContract.RequireIdentifier(SinkId, nameof(SinkId));
        Scope.EnsureValid();
        Policy.EnsureValid();
        AuditRetentionContract.RequireCutoff(SnapshotAt, Cutoff, Policy);
    }
}

public sealed record AuditRetentionRunRequest
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required string RequestId { get; init; }
    public required string ReasonCode { get; init; }
    public required AuditRetentionPolicy Policy { get; init; }
    public required DateTimeOffset SnapshotAt { get; init; }
    public required DateTimeOffset Cutoff { get; init; }
    public required AuditActor Actor { get; init; }
    public required string CorrelationId { get; init; }
    public string? ExpectedPolicyHash { get; init; }

    public AuditRetentionRunKey Key => new()
    {
        StoreId = StoreId,
        SinkId = SinkId,
        Scope = Scope,
        RequestId = RequestId
    };

    public void EnsureValid()
    {
        Key.EnsureValid();
        AuditRetentionContract.RequireIdentifier(ReasonCode, nameof(ReasonCode));
        Policy.EnsureValid();
        AuditRetentionContract.RequireCutoff(SnapshotAt, Cutoff, Policy);
        AuditRetentionContract.RequireActor(Actor);
        AuditRetentionContract.RequireIdentifier(CorrelationId, nameof(CorrelationId));
        if (ExpectedPolicyHash is { Length: > AuditRetentionContract.MaxHashLength })
            throw new ArgumentException("ExpectedPolicyHash is too long.", nameof(ExpectedPolicyHash));
    }
}

public sealed record AuditRetentionPreviewCandidate
{
    public required string AuditId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset FirstAcceptedAt { get; init; }
    public required AuditRetentionCandidateDisposition Disposition { get; init; }
}

public sealed record AuditRetentionPreview
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required DateTimeOffset SnapshotAt { get; init; }
    public required DateTimeOffset Cutoff { get; init; }
    public required string PolicyHash { get; init; }
    public required int BatchSize { get; init; }
    public required int MaxBatchesPerRun { get; init; }
    public required bool HasMore { get; init; }
    public required IReadOnlyList<AuditRetentionPreviewCandidate> Sample { get; init; }
    public required int EligibleInSample { get; init; }
    public required int HeldInSample { get; init; }
    public required int ProtectedInSample { get; init; }
    public required int QuarantinedInSample { get; init; }
}

public sealed record AuditRetentionRunRecord
{
    public required AuditRetentionRunKey Key { get; init; }
    public required AuditRetentionPolicy Policy { get; init; }
    public required string PolicyHash { get; init; }
    public required DateTimeOffset SnapshotAt { get; init; }
    public required DateTimeOffset Cutoff { get; init; }
    public required string ReasonCode { get; init; }
    public required AuditActor Actor { get; init; }
    public required string CorrelationId { get; init; }
    public required AuditRetentionRunState State { get; init; }
    public required long CommittedDeletedCount { get; init; }
    public required long CommittedBatchCount { get; init; }
    public string? StopReason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AuditRetentionMirrorOutcome(string Status, string? Code)
{
    public static AuditRetentionMirrorOutcome Succeeded { get; } = new("succeeded", null);
    public static AuditRetentionMirrorOutcome Skipped { get; } = new("skipped", null);
    public static AuditRetentionMirrorOutcome Failed(string code) => new("failed", code);
}

public sealed record AuditRetentionRunResult
{
    public required AuditRetentionRunKey Key { get; init; }
    public required AuditRetentionRunState State { get; init; }
    public required AuditRetentionCommitKnowledge CommitKnowledge { get; init; }
    public required long CommittedDeletedCount { get; init; }
    public required long CommittedBatchCount { get; init; }
    public required bool RemainingMayExist { get; init; }
    public string? StopReason { get; init; }
    public AuditRetentionMirrorOutcome? MirrorOutcome { get; init; }
}

public sealed record AuditRetentionBatchRequest
{    public required AuditRetentionRunKey Key { get; init; }
    public required long BatchOrdinal { get; init; }

    public void EnsureValid()
    {
        Key.EnsureValid();
        if (BatchOrdinal <= 0)
            throw new ArgumentException("BatchOrdinal must be positive.", nameof(BatchOrdinal));
    }
}

public sealed record AuditRetentionBatchOutcome
{
    public required long BatchOrdinal { get; init; }
    public required int DeletedCount { get; init; }
    public required long CommittedDeletedCount { get; init; }
    public required long CommittedBatchCount { get; init; }
    public required bool RemainingMayExist { get; init; }
    public required AuditRetentionRunState RunState { get; init; }
    public string? StopReason { get; init; }
    public required bool FromExistingReceipt { get; init; }
    public required DateTimeOffset CommittedAt { get; init; }
}

public sealed record AuditRetentionRunFinalization
{
    public required AuditRetentionRunKey Key { get; init; }
    public required AuditRetentionRunState State { get; init; }
    public string? StopReason { get; init; }

    public void EnsureValid()
    {
        Key.EnsureValid();
        if (State is AuditRetentionRunState.Running)
            throw new ArgumentException("A finalization requires a terminal run state.", nameof(State));
    }
}

public sealed record AuditHold
{
    public required string HoldId { get; init; }
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required string AuditId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required long Version { get; init; }
    public required string ReasonCode { get; init; }
    public required AuditActor Actor { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }
    public AuditActor? ReleaseActor { get; init; }
    public string? ReleaseCorrelationId { get; init; }
    public string? ReleaseReasonCode { get; init; }
}

public sealed record AuditHoldResult
{
    public required AuditHold Hold { get; init; }
    public AuditRetentionMirrorOutcome? MirrorOutcome { get; init; }
}

public sealed record AuditHoldPlacement
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required string AuditId { get; init; }
    public required string ReasonCode { get; init; }
    public required AuditActor Actor { get; init; }
    public required string CorrelationId { get; init; }

    public void EnsureValid()
    {
        AuditRetentionContract.RequireIdentifier(StoreId, nameof(StoreId));
        AuditRetentionContract.RequireIdentifier(SinkId, nameof(SinkId));
        Scope.EnsureValid();
        AuditRetentionContract.RequireIdentifier(AuditId, nameof(AuditId));
        AuditRetentionContract.RequireIdentifier(ReasonCode, nameof(ReasonCode));
        AuditRetentionContract.RequireActor(Actor);
        AuditRetentionContract.RequireIdentifier(CorrelationId, nameof(CorrelationId));
    }
}

public sealed record AuditHoldRelease
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionScope Scope { get; init; }
    public required string HoldId { get; init; }
    public required long ExpectedVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required AuditActor Actor { get; init; }
    public required string CorrelationId { get; init; }

    public void EnsureValid()
    {
        AuditRetentionContract.RequireIdentifier(StoreId, nameof(StoreId));
        AuditRetentionContract.RequireIdentifier(SinkId, nameof(SinkId));
        Scope.EnsureValid();
        AuditRetentionContract.RequireIdentifier(HoldId, nameof(HoldId));
        if (ExpectedVersion <= 0)
            throw new ArgumentException("ExpectedVersion must be positive.", nameof(ExpectedVersion));
        AuditRetentionContract.RequireIdentifier(ReasonCode, nameof(ReasonCode));
        AuditRetentionContract.RequireActor(Actor);
        AuditRetentionContract.RequireIdentifier(CorrelationId, nameof(CorrelationId));
    }
}

public sealed record AuditRetentionStoreCapabilities
{
    public required bool SupportsDurableRuns { get; init; }
    public required bool SupportsAtomicBatches { get; init; }
    public required bool SupportsReceipts { get; init; }
    public required bool SupportsHolds { get; init; }
    public required bool SupportsPurge { get; init; }

    public bool IsComplete => SupportsDurableRuns && SupportsAtomicBatches && SupportsReceipts
        && SupportsHolds && SupportsPurge;

    public static AuditRetentionStoreCapabilities Full { get; } = new()
    {
        SupportsDurableRuns = true,
        SupportsAtomicBatches = true,
        SupportsReceipts = true,
        SupportsHolds = true,
        SupportsPurge = true
    };
}

public sealed record AuditRetentionStoreIdentity
{
    public required string StoreId { get; init; }
    public required string SinkId { get; init; }
    public required AuditRetentionStoreCapabilities Capabilities { get; init; }

    public void EnsureValid()
    {
        AuditRetentionContract.RequireIdentifier(StoreId, nameof(StoreId));
        AuditRetentionContract.RequireIdentifier(SinkId, nameof(SinkId));
    }
}

public static class AuditRetentionContract
{
    public const int MaxHashLength = 256;
    public const int MaxIdentifierLength = 256;

    public static void RequireIdentifier(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
        if (value.Length > MaxIdentifierLength)
            throw new ArgumentException($"{name} exceeds the maximum length.", name);
    }

    public static void RequireActor(AuditActor? actor)
    {
        if (actor is null)
            throw new ArgumentException("Actor is required.", nameof(actor));
        RequireIdentifier(actor.Kind, "Actor.Kind");
        RequireIdentifier(actor.Id, "Actor.Id");
    }

    public static void RequireCutoff(DateTimeOffset snapshotAt, DateTimeOffset cutoff, AuditRetentionPolicy policy)
    {
        policy.EnsureValid();
        var expected = policy.ComputeCutoff(snapshotAt);
        if (cutoff != expected)
            throw new ArgumentException("Cutoff must equal SnapshotAt minus the policy retention window.", nameof(cutoff));
    }
}
