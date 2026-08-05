namespace CrestCreates.Agent.Tools;

/// <summary>
/// Attempt-level pre-dispatch state tracked by the Invocation Gate.
/// </summary>
public enum AgentToolInvocationPreDispatchState
{
    Unknown = 0,
    Pending,
    Ready,
    Accepted,
    DispatchStarted,
    Abandoned,
    ReleasePending,
    Released,
    CompletionPending,
    Completed,
    Indeterminate,
    ReconciliationPending
}

/// <summary>
/// Frozen snapshot of the invocation intent stored by
/// <c>PreparePreDispatchIntent</c>. Contains the full lease identity, invocation
/// fingerprint, governance context, and safe approval result. The opaque
/// approval input is never stored. This snapshot is the restart authority used
/// to validate every Context, contract, governance, lease, and approval field
/// in the later checkpoint. Renewals update only Gate ownership expiry, not
/// this snapshot.
/// </summary>
public sealed record AgentToolInvocationPreDispatchIntentSnapshot
{
    public required AgentToolInvocationLease FrozenLease { get; init; }

    public required string InvocationFingerprint { get; init; }

    public required AgentToolGovernanceAuditContext Context { get; init; }

    public required AgentToolApprovalResult Approval { get; init; }
}

/// <summary>
/// Immutable Attempt-scoped receipt for a confirmed budget denial. Contains the
/// stable safe denial outcome/reason. The logical invocation remains bound to
/// its original fingerprint but is not logically Completed. A later Acquire with
/// the same fingerprint creates a new Attempt and re-evaluates approval/budget.
/// </summary>
public sealed record AgentToolInvocationAbandonedReceipt
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    public required AgentToolInvocationOutcome Outcome { get; init; }

    public required string ReasonCode { get; init; }

    public required DateTimeOffset AbandonedAt { get; init; }
}

/// <summary>
/// Request to prepare the pre-dispatch intent in the Gate. Stores the full
/// lease identity plus a detached snapshot of the governance audit context and
/// the safe approval result before budget reservation.
/// </summary>
public sealed record AgentToolInvocationPreparePreDispatchIntentRequest
{
    public required AgentToolInvocationPreDispatchIntentSnapshot Intent { get; init; }
}

/// <summary>
/// Request to bind a budget reservation in the Gate. Transitions Pending to
/// Ready. Idempotent for the same reservation; conflicts on changed
/// identity/content.
/// </summary>
public sealed record AgentToolInvocationBindReservationRequest
{
    public required string ReservationId { get; init; }

    public required AgentToolBudgetReservation Reservation { get; init; }
}

/// <summary>
/// Request to bind an accepted pre-dispatch receipt in the Gate. Permits only a
/// matching <c>PreDispatchReady</c> Attempt.
/// </summary>
public sealed record AgentToolInvocationBindPreDispatchRequest
{
    public required AgentToolGovernancePreDispatchReceipt Receipt { get; init; }
}

/// <summary>
/// Result of a Gate pre-dispatch operation. Contains the current state and, when
/// applicable, the frozen intent snapshot, bound reservation, accepted receipt,
/// or abandoned receipt.
/// </summary>
public sealed record AgentToolInvocationPreDispatchResult
{
    public required AgentToolInvocationPreDispatchState State { get; init; }

    /// <summary>
    /// Logical/operational indeterminate marker. When set, the attempt is
    /// fenced from further progress but the underlying <see cref="State"/>
    /// (Pending/Ready/Accepted/ReleasePending/CompletionPending) is preserved so
    /// a reconciler can still converge the attempt instead of losing the
    /// recovery substate.
    /// </summary>
    public bool Indeterminate { get; init; }

    public AgentToolInvocationPreDispatchIntentSnapshot? Intent { get; init; }

    public string? BoundReservationId { get; init; }

    public AgentToolGovernancePreDispatchReceipt? AcceptedReceipt { get; init; }

    public AgentToolInvocationAbandonedReceipt? AbandonedReceipt { get; init; }

    /// <summary>
    /// Current revision of the Attempt row. Observed by the reconciler and
    /// replayed as <c>ExpectedRevision</c> when claiming reconciliation
    /// ownership; a mismatch means another participant transitioned the Attempt.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Claim token of a granted reconciliation claim. Set when
    /// <see cref="State"/> is <see cref="AgentToolInvocationPreDispatchState.ReconciliationPending"/>.
    /// Lets a reconciler that lost its claim response recover the exact claim
    /// token and complete the reconciliation without a second claim.
    /// </summary>
    public string? ReconciliationClaimToken { get; init; }

    /// <summary>
    /// The pre-claim substate preserved by a granted reconciliation claim.
    /// </summary>
    public AgentToolInvocationPreDispatchState? ReconciliationClaimedState { get; init; }

    /// <summary>
    /// When the reconciliation claim was granted. Recovery evidence for a
    /// reconciler reconstructing a claim after its own crash.
    /// </summary>
    public DateTimeOffset? ReconciliationClaimedAt { get; init; }

    public string? ReasonCode { get; init; }
}

/// <summary>
/// Request to publish a budget denial as an immutable Abandoned receipt.
/// Repeating this transition with the same denial content returns the same
/// receipt; changed content conflicts.
/// </summary>
public sealed record AgentToolInvocationPublishDenialRequest
{
    public required AgentToolInvocationOutcome Outcome { get; init; }

    public required string ReasonCode { get; init; }
}

/// <summary>
/// Test-support snapshot of stored pre-dispatch state. This is test-support data
/// only; provider-private rows do not escape through production contracts.
/// </summary>
public sealed record StoredAgentToolPreDispatchSnapshot
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    public required AgentToolInvocationPreDispatchState State { get; init; }

    public AgentToolInvocationPreDispatchIntentSnapshot? Intent { get; init; }

    public string? BoundReservationId { get; init; }

    public AgentToolGovernancePreDispatchReceipt? AcceptedReceipt { get; init; }

    public AgentToolInvocationAbandonedReceipt? AbandonedReceipt { get; init; }

    public bool DispatchStarted { get; init; }

    public string? ReasonCode { get; init; }
}

/// <summary>
/// Crash window identifiers for deterministic crash-test scenarios.
/// </summary>
public enum AgentToolPreDispatchCrashWindow
{
    CW02,
    CW04,
    CW06,
    CW08,
    CW12,
    CW13,
    CW15,
    CW16
}
