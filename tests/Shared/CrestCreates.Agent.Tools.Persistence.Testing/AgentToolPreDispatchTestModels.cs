namespace CrestCreates.Agent.Tools.Persistence.Testing;

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
