using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryWorkflowContinuationAcceptanceStore : IWorkflowContinuationAcceptanceStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    public InMemoryWorkflowContinuationAcceptanceStore(InMemoryRuntimeTransactionCoordinator coordinator) => _coordinator = coordinator;

    public Task<WorkflowContinuationAcceptanceWriteResult> AddAsync(WorkflowContinuationAcceptance acceptance, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ =>
        {
            var state = _coordinator.RequireAmbientState();
            var key = (acceptance.TenantScope, acceptance.CompletionEventId);
            if (state.ContinuationAcceptances.TryGetValue(key, out var existing))
            {
                if (existing.HumanTaskKey != acceptance.HumanTaskKey || existing.WorkflowKey != acceptance.WorkflowKey || existing.Integrity.Value != acceptance.Integrity.Value)
                    return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Conflict);
                return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Duplicate);
            }
            var byTask = state.ContinuationAcceptances.Values.FirstOrDefault(item => item.TenantScope == acceptance.TenantScope && item.HumanTaskKey == acceptance.HumanTaskKey);
            if (byTask is not null && byTask.CompletionEventId != acceptance.CompletionEventId)
                return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Conflict);
            state.ContinuationAcceptances[key] = acceptance;
            return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Accepted);
        }, cancellationToken).AsTask();

    public Task<WorkflowContinuationAcceptance?> GetAsync(RuntimeTenantScope scope, string completionEventId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync<WorkflowContinuationAcceptance?>(_ =>
        {
            _coordinator.RequireAmbientState().ContinuationAcceptances.TryGetValue((scope, completionEventId), out var value);
            return ValueTask.FromResult(value);
        }, cancellationToken).AsTask();

    public Task<(WorkflowContinuationResumeClaimResult Result, WorkflowContinuationResumeClaim? Claim)> TryClaimResumeAsync(
        WorkflowContinuationAcceptance acceptance, WorkflowContinuationResumeLease lease,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ =>
        {
            ValidateLease(lease);
            var state = _coordinator.RequireAmbientState();
            var key = (acceptance.TenantScope, acceptance.CompletionEventId);
            if (!state.ContinuationAcceptances.ContainsKey(key))
                return ValueTask.FromResult((WorkflowContinuationResumeClaimResult.AcceptanceMissing, (WorkflowContinuationResumeClaim?)null));
            if (!state.Workflows.TryGetValue(acceptance.WorkflowKey, out var workflow))
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                    "The workflow bound to the continuation acceptance no longer exists.");
            if (workflow.Revision != acceptance.WorkflowToRevision)
                return ValueTask.FromResult((workflow.Revision > acceptance.WorkflowToRevision
                    ? WorkflowContinuationResumeClaimResult.NoResumeRequired
                    : WorkflowContinuationResumeClaimResult.RevisionMismatch, (WorkflowContinuationResumeClaim?)null));
            if (workflow.Status != WorkflowInstanceStatus.Running || workflow.WaitingHumanTaskKey is not null)
                return ValueTask.FromResult((WorkflowContinuationResumeClaimResult.NoResumeRequired, (WorkflowContinuationResumeClaim?)null));

            if (!state.ContinuationResumeClaims.TryGetValue(key, out var claim))
            {
                claim = new InMemoryContinuationResumeClaim();
                state.ContinuationResumeClaims[key] = claim;
            }
            if (claim.CompletedAt is not null)
                return ValueTask.FromResult((WorkflowContinuationResumeClaimResult.NoResumeRequired, (WorkflowContinuationResumeClaim?)null));
            if (claim.OwnerId is not null && claim.ExpiresAt > DateTimeOffset.UtcNow)
                return ValueTask.FromResult((WorkflowContinuationResumeClaimResult.AlreadyInProgress, (WorkflowContinuationResumeClaim?)null));

            claim.Version++;
            claim.OwnerId = lease.OwnerId;
            claim.ExpiresAt = lease.ExpiresAt;
            return ValueTask.FromResult((WorkflowContinuationResumeClaimResult.Acquired,
                (WorkflowContinuationResumeClaim?)new WorkflowContinuationResumeClaim(claim.Version)));
        }, cancellationToken).AsTask();

    public Task<bool> CompleteResumeAsync(WorkflowContinuationAcceptance acceptance,
        WorkflowContinuationResumeClaim claim, CancellationToken cancellationToken = default)
        => UpdateResumeAsync(acceptance, claim, completed: true, cancellationToken);

    public Task<bool> ReleaseResumeAsync(WorkflowContinuationAcceptance acceptance,
        WorkflowContinuationResumeClaim claim, CancellationToken cancellationToken = default)
        => UpdateResumeAsync(acceptance, claim, completed: false, cancellationToken);

    private Task<bool> UpdateResumeAsync(WorkflowContinuationAcceptance acceptance,
        WorkflowContinuationResumeClaim claim, bool completed, CancellationToken cancellationToken)
        => _coordinator.ExecuteAsync(_ =>
        {
            var key = (acceptance.TenantScope, acceptance.CompletionEventId);
            var state = _coordinator.RequireAmbientState();
            if (!state.ContinuationResumeClaims.TryGetValue(key, out var current) || current.Version != claim.Version)
                return ValueTask.FromResult(false);
            if (completed) current.CompletedAt = DateTimeOffset.UtcNow;
            else
            {
                current.OwnerId = null;
                current.ExpiresAt = null;
            }
            return ValueTask.FromResult(true);
        }, cancellationToken).AsTask();

    private static void ValidateLease(WorkflowContinuationResumeLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.OwnerId);
        if (lease.FencingToken <= 0 || lease.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(lease));
    }
}
