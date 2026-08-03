namespace CrestCreates.Agent.Tools;

public enum AgentToolBudgetReserveStatus
{
    Unknown = 0,
    Denied = 1,
    Reserved = 2
}

public enum AgentToolBudgetReservationState
{
    Unknown = 0,
    Reserved = 1,
    Released = 2,
    Committed = 3,
    Indeterminate = 4
}

public sealed record AgentToolBudgetReserveRequest
{
    public required AgentToolGovernanceContext Context { get; init; }
}

public sealed record AgentToolBudgetReservation
{
    public required string ReservationId { get; init; }

    public required string AttemptId { get; init; }

    public required string InvocationFingerprint { get; init; }

    public required string Category { get; init; }

    public required long CostUnits { get; init; }

    public int? MaxCallsPerExecution { get; init; }

    public required AgentToolBudgetReservationState State { get; init; }
}

public sealed record AgentToolBudgetReserveResult
{
    public required AgentToolBudgetReserveStatus Status { get; init; }

    public AgentToolBudgetReservation? Reservation { get; init; }

    public string? ReasonCode { get; init; }
}

public sealed record AgentToolBudgetFinalizeRequest
{
    public required string ReservationId { get; init; }

    public required string AttemptId { get; init; }

    public required string InvocationFingerprint { get; init; }

    public required AgentToolBudgetReservationState RequestedState { get; init; }

    public required string ReasonCode { get; init; }
}

public interface IAgentToolBudgetGate
{
    ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
        AgentToolBudgetReserveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolBudgetReservation> FinalizeAsync(
        AgentToolBudgetFinalizeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default);
}
