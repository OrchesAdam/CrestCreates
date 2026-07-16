using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolEffectiveGovernanceDeriver
{
    public AgentToolEffectiveGovernance Derive(
        AgentCapabilityToolDescriptor tool,
        CapabilityDescriptor capability)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(capability);

        var sideEffect = DeriveSideEffect(tool.SideEffectKind, capability.CapabilityKind);
        var risk = DeriveRisk(tool.RiskFloor, capability.RiskLevel);
        var strongGovernance = risk is CapabilityRiskLevel.High or CapabilityRiskLevel.Critical
            || sideEffect is AgentToolSideEffectKind.ExternalWrite or AgentToolSideEffectKind.Destructive;
        var approval = strongGovernance ? AgentToolApprovalMode.Required : tool.ApprovalMode;

        if (!IsKnownApprovalMode(tool.ApprovalMode)
            || !IsKnownAuditMode(tool.AuditMode)
            || strongGovernance && tool.AuditMode != AgentToolAuditMode.Required
            || tool.AuditMode == AgentToolAuditMode.BestEffort
                && (risk is CapabilityRiskLevel.High or CapabilityRiskLevel.Critical
                    || sideEffect != AgentToolSideEffectKind.ReadOnly))
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.UnsafeGovernance,
                "Agent Tool approval or audit policy is lower than the effective governance floor.");
        }

        return new AgentToolEffectiveGovernance(
            tool.SelectionPolicy,
            sideEffect,
            risk,
            approval,
            tool.Budget,
            tool.AuditMode);
    }

    private static AgentToolSideEffectKind DeriveSideEffect(
        AgentToolSideEffectKind declared,
        CapabilityKind capabilityKind)
    {
        if (capabilityKind is not CapabilityKind.Query and not CapabilityKind.Command)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidSideEffectClassification,
                "Capability kind is unknown.");
        }

        return capabilityKind switch
        {
            CapabilityKind.Query when declared == AgentToolSideEffectKind.Unknown =>
                AgentToolSideEffectKind.ReadOnly,
            CapabilityKind.Query when declared == AgentToolSideEffectKind.ReadOnly =>
                AgentToolSideEffectKind.ReadOnly,
            CapabilityKind.Command when declared is AgentToolSideEffectKind.InternalWrite
                or AgentToolSideEffectKind.ExternalWrite
                or AgentToolSideEffectKind.Destructive => declared,
            _ => throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidSideEffectClassification,
                "Agent Tool side-effect classification contradicts its Capability kind.")
        };
    }

    private static CapabilityRiskLevel DeriveRisk(
        CapabilityRiskLevel? floor,
        CapabilityRiskLevel capabilityRisk)
    {
        if (!IsKnownRisk(capabilityRisk)
            || floor is { } value && !IsKnownRisk(value)
            || floor is { } lower && lower < capabilityRisk)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidRiskFloor,
                "Agent Tool risk floor is invalid or attempts to lower Capability risk.");
        }

        return floor is { } configured && configured > capabilityRisk
            ? configured
            : capabilityRisk;
    }

    private static bool IsKnownApprovalMode(AgentToolApprovalMode value)
        => value is AgentToolApprovalMode.PolicyDriven
            or AgentToolApprovalMode.Required
            or AgentToolApprovalMode.None;

    private static bool IsKnownAuditMode(AgentToolAuditMode value)
        => value is AgentToolAuditMode.Required or AgentToolAuditMode.BestEffort;

    private static bool IsKnownRisk(CapabilityRiskLevel value)
        => value is CapabilityRiskLevel.Low
            or CapabilityRiskLevel.Medium
            or CapabilityRiskLevel.High
            or CapabilityRiskLevel.Critical;
}
