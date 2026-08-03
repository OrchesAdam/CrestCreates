namespace CrestCreates.Agent.Tools;

/// <summary>
/// Reconciliation status returned by the pre-dispatch reconciler.
/// </summary>
public enum AgentToolPreDispatchReconciliationStatus
{
    Unknown = 0,
    Released,
    AlreadyReleased,
    StillPending,
    Conflict,
    PostDispatchUnknown,
    Missing
}

/// <summary>
/// Mutable retryable observation. Only StillPending or transient unavailability
/// produces a mutable observation. It may update bounded last-observed/attempt/
/// revision metadata and later progress to a terminal receipt.
/// </summary>
public sealed record AgentToolPreDispatchReconciliationObservation
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    public required AgentToolPreDispatchReconciliationStatus Status { get; init; }

    public string? ReasonCode { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required long Revision { get; init; }
}

/// <summary>
/// Immutable terminal reconciliation receipt. Released, Conflict/terminal
/// Indeterminate, and PostDispatchUnknown create at most one immutable terminal
/// receipt. AlreadyReleased projects the existing Released receipt. Missing has
/// no receipt.
/// </summary>
public sealed record AgentToolPreDispatchReconciliationReceipt
{
    public required AgentToolPreDispatchIdentity Identity { get; init; }

    public required AgentToolPreDispatchReconciliationStatus Status { get; init; }

    public string? ReasonCode { get; init; }

    public required DateTimeOffset TerminalAt { get; init; }

    public required string IntegrityValue { get; init; }
}

/// <summary>
/// Result returned by the reconciler. Contains either a mutable observation or
/// an immutable terminal receipt, never both.
/// </summary>
public sealed record AgentToolPreDispatchReconciliationResult
{
    public required AgentToolPreDispatchReconciliationStatus Status { get; init; }

    public AgentToolPreDispatchReconciliationObservation? Observation { get; init; }

    public AgentToolPreDispatchReconciliationReceipt? Receipt { get; init; }
}

/// <summary>
/// Runtime-owned orchestrator that reads Gate, Budget, and checkpoint in the
/// Spec order. Never dispatches, never evaluates approval, and never creates
/// budget reservations.
/// </summary>
public interface IAgentToolPreDispatchReconciler
{
    ValueTask<AgentToolPreDispatchReconciliationResult> ReconcileAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Durable persistence for reconciliation observations and terminal receipts.
/// Observations are mutable (CAS on revision); terminal receipts are immutable
/// (first-write CAS). Both are keyed by <see cref="AgentToolPreDispatchIdentity"/>.
/// </summary>
public interface IAgentToolPreDispatchReconciliationStore
{
    ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpsertObservationAsync(
        AgentToolPreDispatchReconciliationObservation observation,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryInsertReceiptAsync(
        AgentToolPreDispatchReconciliationReceipt receipt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reports provider durability capability without coupling the generic Runtime
/// Persistence capability contract to Agent Tool types.
/// </summary>
public enum AgentToolPreDispatchPersistenceCapability
{
    FullSemantic,
    FullDurable
}

public interface IAgentToolPreDispatchPersistenceCapabilities
{
    AgentToolPreDispatchPersistenceCapability Capability { get; }
}
