namespace CrestCreates.Agent.Tools;

public readonly record struct AgentToolLogicalInvocationKey(
    string? TenantId,
    string UserId,
    string AgentId,
    string ExecutionId,
    string InvocationId);

public sealed record AgentToolInvocationAcquireRequest(
    AgentToolLogicalInvocationKey Key,
    string InvocationFingerprint);

public enum AgentToolInvocationAcquireStatus
{
    Unknown = 0,
    Acquired = 1,
    InProgress = 2,
    Completed = 3,
    Indeterminate = 4,
    Conflict = 5
}

public sealed record AgentToolInvocationLease
{
    public required string AttemptId { get; init; }

    public required string LeaseId { get; init; }

    public required long FencingToken { get; init; }

    public required DateTimeOffset AcquiredAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record AgentToolInvocationAcquireResult
{
    public required AgentToolInvocationAcquireStatus Status { get; init; }

    public AgentToolInvocationLease? Lease { get; init; }

    public AgentToolInvocationOutcome? CompletedOutcome { get; init; }

    public string? ReasonCode { get; init; }
}

public enum AgentToolInvocationCompletionState
{
    Unknown = 0,
    CompletionPending = 1,
    Completed = 2,
    Indeterminate = 3
}

public sealed record AgentToolInvocationPrepareCompletionRequest
{
    public required AgentToolInvocationOutcome Outcome { get; init; }

    public string? AuditId { get; init; }

    public required string BudgetReservationId { get; init; }

    public required string ReasonCode { get; init; }
}

public sealed record AgentToolInvocationCompletionResult
{
    public required AgentToolInvocationCompletionState State { get; init; }

    public AgentToolInvocationOutcome? Outcome { get; init; }

    public DateTimeOffset? PreparedAt { get; init; }

    public string? AuditId { get; init; }

    public string? BudgetReservationId { get; init; }

    public string? ReasonCode { get; init; }
}

public enum AgentToolInvocationReleaseState
{
    Unknown = 0,
    ReleasePending = 1,
    Released = 2,
    Indeterminate = 3
}

public sealed record AgentToolInvocationReleaseResult
{
    public required AgentToolInvocationReleaseState State { get; init; }

    public DateTimeOffset? PreparedAt { get; init; }

    public string? ReasonCode { get; init; }
}

public interface IAgentToolInvocationGate
{
    ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
        AgentToolInvocationAcquireRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolInvocationLease> RenewAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryMarkDispatchStartedAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask PrepareCompletionAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPrepareCompletionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask PrepareReleaseAsync(
        AgentToolInvocationLease lease,
        string reasonCode,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    ValueTask MarkIndeterminateAsync(
        AgentToolInvocationLease lease,
        string reasonCode,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseLeaseAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);
}
