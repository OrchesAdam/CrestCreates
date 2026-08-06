namespace CrestCreates.Agent.Tools;

/// <summary>
/// The sole public pre-dispatch recovery identity. Combines the logical
/// invocation key with the AttemptId. No other identifier (AuditId, LeaseId,
/// ReservationId, CorrelationId, Capability idempotency key) may substitute.
/// </summary>
public readonly record struct AgentToolPreDispatchIdentity(
    AgentToolLogicalInvocationKey LogicalInvocationKey,
    string AttemptId);

/// <summary>
/// Immutable receipt issued by the governance auditor on first durable
/// acceptance. The provider-issued AuditId and AcceptedAt are frozen at first
/// acceptance and returned unchanged for all identical retries, concurrent
/// retries, lookups, and restart recovery.
/// </summary>
public sealed record AgentToolGovernancePreDispatchReceipt
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    public required string AuditId { get; init; }

    public required DateTimeOffset AcceptedAt { get; init; }
}

/// <summary>
/// Typed result of a pre-dispatch checkpoint write. Accepted and Duplicate
/// require an exact receipt. Conflict carries no receipt. Unknown is never a
/// successful return.
/// </summary>
public enum AgentToolGovernancePreDispatchWriteStatus
{
    Unknown = 0,
    Accepted = 1,
    Duplicate = 2,
    Conflict = 3
}

public sealed record AgentToolGovernancePreDispatchWriteResult
{
    public required AgentToolGovernancePreDispatchWriteStatus Status { get; init; }

    public AgentToolGovernancePreDispatchReceipt? Receipt { get; init; }
}

/// <summary>
/// Typed result of a pre-dispatch checkpoint read. Accepted requires both
/// Receipt and a detached complete Checkpoint. Missing requires both null.
/// Unknown is invalid as a returned provider result.
/// </summary>
public enum AgentToolGovernancePreDispatchReadStatus
{
    Unknown = 0,
    Missing = 1,
    Accepted = 2
}

public sealed record AgentToolGovernancePreDispatchReadResult
{
    public required AgentToolGovernancePreDispatchReadStatus Status { get; init; }

    public AgentToolGovernancePreDispatchReceipt? Receipt { get; init; }

    public AgentToolGovernancePreDispatchRecord? Checkpoint { get; init; }
}

/// <summary>
/// Provider-neutral budget reservation read result. Missing, Reserved,
/// Released, Committed, and Indeterminate remain distinct. Availability failure
/// is not Missing.
/// </summary>
public enum AgentToolBudgetReadStatus
{
    Unknown = 0,
    Missing = 1,
    Reserved = 2,
    Released = 3,
    Committed = 4,
    Indeterminate = 5
}

public sealed record AgentToolBudgetReservationReadResult
{
    public required AgentToolBudgetReadStatus Status { get; init; }

    public AgentToolBudgetReservation? Reservation { get; init; }
}
