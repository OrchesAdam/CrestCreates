using CrestCreates.Agent.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed record AgentToolGovernanceContext
{
    public required AgentToolLogicalInvocationKey LogicalInvocationKey { get; init; }

    public required string AttemptId { get; init; }

    public required string InvocationFingerprint { get; init; }

    public required string ArgumentsHash { get; init; }

    public required AgentExecutionContext ExecutionContext { get; init; }

    public required AgentToolContractIdentity ToolContract { get; init; }

    public required AgentToolContractIdentity CapabilityContract { get; init; }

    public AgentToolSchemaContractIdentity? InputSchemaContract { get; init; }

    public AgentToolSchemaContractIdentity? OutputSchemaContract { get; init; }

    public required AgentToolEffectiveGovernance Governance { get; init; }
}
