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
        AgentToolInvocationOutcome outcome,
        CancellationToken cancellationToken = default);

    ValueTask PublishCompletionAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);

    [Obsolete("Use PrepareCompletionAsync followed by PublishCompletionAsync.")]
    ValueTask CompleteAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationOutcome outcome,
        CancellationToken cancellationToken = default);

    ValueTask MarkIndeterminateAsync(
        AgentToolInvocationLease lease,
        string reasonCode,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseLeaseAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default);
}
