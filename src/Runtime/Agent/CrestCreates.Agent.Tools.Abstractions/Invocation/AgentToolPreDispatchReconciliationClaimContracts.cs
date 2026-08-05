namespace CrestCreates.Agent.Tools;

/// <summary>
/// Status of a reconciliation-claim attempt against the Invocation Gate.
/// Claimed grants exclusive reconciliation ownership of the Attempt;
/// NotClaimable and RevisionConflict grant nothing.
/// </summary>
public enum AgentToolPreDispatchReconciliationClaimStatus
{
    Unknown = 0,
    Claimed = 1,
    NotClaimable = 2,
    RevisionConflict = 3
}

/// <summary>
/// Request to claim reconciliation ownership of an Attempt. The Gate grants
/// the claim in a single CAS only when the Attempt is Pending/Ready/Accepted,
/// dispatch has not started, the observed revision still matches, and at least
/// one durable ownership-loss condition holds (indeterminate marker, expired
/// lease, or an explicit control-plane ownership assertion).
/// </summary>
public sealed record AgentToolPreDispatchReconciliationClaimRequest
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    /// <summary>
    /// Revision observed by the reconciler when it read the Attempt. A mismatch
    /// means another participant (the live Invoker or another reconciler)
    /// transitioned the Attempt since the read; the claim is rejected so the
    /// reconciler re-reads and re-decides.
    /// </summary>
    public required long ExpectedRevision { get; init; }

    /// <summary>
    /// Durable control-plane assertion that the worker holding the Attempt's
    /// lease is no longer alive. When false, the claim requires the Attempt to
    /// carry an indeterminate marker or an expired lease.
    /// </summary>
    public bool OwnershipLost { get; init; }

    /// <summary>
    /// Human/operator readable evidence recorded with the claim when
    /// <see cref="OwnershipLost"/> is asserted (for example a process-tree kill
    /// observation, orchestrator worker-death event, or heartbeat timeout).
    /// </summary>
    public string? OwnershipEvidence { get; init; }
}

/// <summary>
/// Immutable reconciliation claim granted by the Gate. The claim token is the
/// fencing proof used by <c>CompletePreDispatchReconciliationAsync</c> to
/// publish the terminal Gate outcome; the frozen substate (lease, reservation,
/// receipt, intent) is the recovery evidence preserved for governance
/// finalization.
/// </summary>
public sealed record AgentToolPreDispatchReconciliationClaim
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    /// <summary>
    /// Revision of the Attempt after the claim transition.
    /// </summary>
    public required long Revision { get; init; }

    public required string ClaimToken { get; init; }

    public required DateTimeOffset ClaimedAt { get; init; }

    /// <summary>
    /// The pre-claim substate (Pending/Ready/Accepted) preserved for recovery.
    /// </summary>
    public required AgentToolInvocationPreDispatchState ClaimedState { get; init; }

    public required bool Indeterminate { get; init; }

    /// <summary>
    /// The lease that owned the Attempt before the claim; frozen as recovery
    /// evidence. Null when the Attempt was marked indeterminate and its lease
    /// was already invalidated.
    /// </summary>
    public AgentToolInvocationLease? FrozenLease { get; init; }

    public string? BoundReservationId { get; init; }

    public AgentToolGovernancePreDispatchReceipt? AcceptedReceipt { get; init; }

    public AgentToolInvocationPreDispatchIntentSnapshot? Intent { get; init; }

    public string? LastReasonCode { get; init; }

    public string? OwnershipEvidence { get; init; }
}

/// <summary>
/// Result of a reconciliation-claim attempt. Claimed carries the immutable
/// claim; NotClaimable and RevisionConflict carry the current reason.
/// </summary>
public sealed record AgentToolPreDispatchReconciliationClaimResult
{
    public required AgentToolPreDispatchReconciliationClaimStatus Status { get; init; }

    public AgentToolPreDispatchReconciliationClaim? Claim { get; init; }

    public string? ReasonCode { get; init; }
}

/// <summary>
/// Terminal outcome kind for a claimed reconciliation. Released preserves the
/// governance record as a released (no-dispatch) finalization; Abandoned closes
/// an unrecorded Attempt with an abandoned receipt.
/// </summary>
public enum AgentToolPreDispatchReconciliationCompletionKind
{
    Unknown = 0,
    Released = 1,
    Abandoned = 2
}
