using CrestCreates.Agent.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Shared authority for validating governance contexts and their component
/// facts. Lives in Abstractions so every durable provider (InMemory, PostgreSQL)
/// applies the exact same validation instead of diverging per implementation.
/// </summary>
public static class AgentToolGovernanceGuard
{
    public static bool IsValid(AgentToolGovernanceContext context)
        => context is not null
            && IsValid(context.LogicalInvocationKey)
            && !string.IsNullOrWhiteSpace(context.AttemptId)
            && !string.IsNullOrWhiteSpace(context.InvocationFingerprint)
            && !string.IsNullOrWhiteSpace(context.ArgumentsHash)
            && IsValid(context.ExecutionContext)
            && string.Equals(
                context.ExecutionContext.ExecutionId,
                context.LogicalInvocationKey.ExecutionId,
                StringComparison.Ordinal)
            && string.Equals(
                context.ExecutionContext.InvocationId,
                context.LogicalInvocationKey.InvocationId,
                StringComparison.Ordinal)
            && string.Equals(
                context.ExecutionContext.AgentId,
                context.LogicalInvocationKey.AgentId,
                StringComparison.Ordinal)
            && IsValid(context.ToolContract)
            && IsValid(context.CapabilityContract)
            && IsValid(context.InputSchemaContract)
            && IsValid(context.OutputSchemaContract)
            && IsValid(context.Governance);

    public static bool IsValid(AgentToolLogicalInvocationKey key)
        => !string.IsNullOrWhiteSpace(key.UserId)
            && !string.IsNullOrWhiteSpace(key.AgentId)
            && !string.IsNullOrWhiteSpace(key.ExecutionId)
            && !string.IsNullOrWhiteSpace(key.InvocationId);

    public static bool IsValid(AgentToolContractIdentity identity)
        => identity is not null
            && !string.IsNullOrWhiteSpace(identity.Id)
            && identity.Version > 0
            && !string.IsNullOrWhiteSpace(identity.ContractHash);

    public static bool IsValid(AgentToolSchemaContractIdentity? identity)
        => identity is null
            || !string.IsNullOrWhiteSpace(identity.Id)
                && identity.Version > 0
                && !string.IsNullOrWhiteSpace(identity.ContractHash);

    public static bool IsValid(AgentToolEffectiveGovernance governance)
        => governance is not null
            && governance.SelectionPolicy is AgentToolSelectionPolicy.ExplicitOnly
                or AgentToolSelectionPolicy.AutomaticAllowed
            && governance.SideEffectKind is AgentToolSideEffectKind.ReadOnly
                or AgentToolSideEffectKind.InternalWrite
                or AgentToolSideEffectKind.ExternalWrite
                or AgentToolSideEffectKind.Destructive
            && governance.EffectiveRisk is CapabilityRiskLevel.Low
                or CapabilityRiskLevel.Medium
                or CapabilityRiskLevel.High
                or CapabilityRiskLevel.Critical
            && governance.EffectiveApprovalMode is AgentToolApprovalMode.PolicyDriven
                or AgentToolApprovalMode.Required
                or AgentToolApprovalMode.None
            && governance.EffectiveAuditMode is AgentToolAuditMode.Required
                or AgentToolAuditMode.BestEffort
            && governance.Budget is not null
            && !string.IsNullOrWhiteSpace(governance.Budget.Category)
            && governance.Budget.CostUnits > 0
            && governance.Budget.MaxCallsPerExecution is null or > 0;

    private static bool IsValid(AgentExecutionContext context)
        => context is not null
            && !string.IsNullOrWhiteSpace(context.ExecutionId)
            && !string.IsNullOrWhiteSpace(context.InvocationId)
            && !string.IsNullOrWhiteSpace(context.AgentId)
            && context.AgentRoles is { Count: > 0 }
            && context.AgentRoles.All(role => !string.IsNullOrWhiteSpace(role))
            && context.CallOrigin is AgentToolCallOrigin.ExplicitRequest
                or AgentToolCallOrigin.AutomaticSelection;
}
