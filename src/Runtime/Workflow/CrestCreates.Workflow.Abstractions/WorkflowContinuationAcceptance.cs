using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Workflow.Abstractions;

public sealed record WorkflowContinuationAcceptance
{
    public required RuntimeTenantScope TenantScope { get; init; }
    public required string CompletionEventId { get; init; }
    public required RuntimeInstanceKey HumanTaskKey { get; init; }
    public required RuntimeInstanceKey WorkflowKey { get; init; }
    public required string Outcome { get; init; }
    public RuntimeStateValue? Result { get; init; }
    public required long WorkflowFromRevision { get; init; }
    public required long WorkflowToRevision { get; init; }
    public required CanonicalHash Integrity { get; init; }
    public DateTimeOffset AcceptedAt { get; init; } = DateTimeOffset.UtcNow;
    public string RunOperationId => CompletionEventId;
}

public sealed record WorkflowContinuationResumeLease(string OwnerId, long FencingToken, DateTimeOffset ExpiresAt);

public enum WorkflowContinuationResumeClaimResult
{
    Acquired,
    AlreadyInProgress,
    NoResumeRequired,
    AcceptanceMissing,
    RevisionMismatch
}

public sealed record WorkflowContinuationResumeClaim(long Version);

public enum WorkflowContinuationAcceptanceWriteResult { Accepted, Duplicate, Conflict }

public interface IWorkflowContinuationAcceptanceStore
{
    Task<WorkflowContinuationAcceptanceWriteResult> AddAsync(WorkflowContinuationAcceptance acceptance, CancellationToken cancellationToken = default);
    Task<WorkflowContinuationAcceptance?> GetAsync(RuntimeTenantScope scope, string completionEventId, CancellationToken cancellationToken = default);
    Task<(WorkflowContinuationResumeClaimResult Result, WorkflowContinuationResumeClaim? Claim)> TryClaimResumeAsync(
        WorkflowContinuationAcceptance acceptance,
        WorkflowContinuationResumeLease lease,
        CancellationToken cancellationToken = default);
    Task<bool> CompleteResumeAsync(
        WorkflowContinuationAcceptance acceptance,
        WorkflowContinuationResumeClaim claim,
        CancellationToken cancellationToken = default);
    Task<bool> ReleaseResumeAsync(
        WorkflowContinuationAcceptance acceptance,
        WorkflowContinuationResumeClaim claim,
        CancellationToken cancellationToken = default);
}
