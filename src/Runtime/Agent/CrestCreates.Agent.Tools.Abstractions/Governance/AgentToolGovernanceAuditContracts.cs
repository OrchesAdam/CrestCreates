using CrestCreates.Agent.Abstractions;

namespace CrestCreates.Agent.Tools;

public enum AgentToolInvocationTerminalState
{
    Unknown = 0,
    Completed = 1,
    Indeterminate = 2
}

public enum AgentToolGovernanceAttemptFinalState
{
    Unknown = 0,
    Released = 1,
    Completed = 2,
    Indeterminate = 3
}

public sealed record AgentToolGovernanceAuditContext
{
    public required AgentToolLogicalInvocationKey LogicalInvocationKey { get; init; }

    public required string AttemptId { get; init; }

    public required string InvocationFingerprint { get; init; }

    public required string ArgumentsHash { get; init; }

    public required AgentToolCallOrigin CallOrigin { get; init; }

    public required string AgentRolesHash { get; init; }

    public required AgentToolContractIdentity ToolContract { get; init; }

    public required AgentToolContractIdentity CapabilityContract { get; init; }

    public AgentToolSchemaContractIdentity? InputSchemaContract { get; init; }

    public AgentToolSchemaContractIdentity? OutputSchemaContract { get; init; }

    public required AgentToolEffectiveGovernance Governance { get; init; }
}

public sealed record AgentToolGovernancePreDispatchRecord
{
    public required AgentToolGovernanceAuditContext Context { get; init; }

    public required AgentToolInvocationLease Lease { get; init; }

    public required AgentToolApprovalResult Approval { get; init; }

    public required AgentToolBudgetReservation BudgetReservation { get; init; }
}

public sealed record AgentToolGovernanceAuditHandle
{
    public required string AuditId { get; init; }

    public required DateTimeOffset AcceptedAt { get; init; }
}

public sealed record AgentToolGovernanceFinalizationRecord
{
    public required string AuditId { get; init; }

    public required AgentToolGovernanceAuditContext Context { get; init; }

    public required AgentToolInvocationLease Lease { get; init; }

    public required bool DispatchStarted { get; init; }

    public required AgentToolBudgetReservation BudgetReservation { get; init; }

    public required AgentToolGovernanceAttemptFinalState AttemptState { get; init; }

    public AgentToolInvocationTerminalState? InvocationState { get; init; }

    public required AgentToolInvocationOutcome Outcome { get; init; }

    public required string ReasonCode { get; init; }
}

public interface IAgentToolGovernanceAuditor
{
    ValueTask<AgentToolGovernanceAuditHandle> RecordPreDispatchAsync(
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken = default);

    ValueTask FinalizeAsync(
        AgentToolGovernanceFinalizationRecord record,
        CancellationToken cancellationToken = default);
}
