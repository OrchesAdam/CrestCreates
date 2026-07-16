using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;

namespace CrestCreates.Metadata.AgentTool;

public sealed class AgentCapabilityToolDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "agent-tool";

    public DescriptorKind Kind => DescriptorKind.AgentTool;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Version { get; init; } = 1;

    public DescriptorState State { get; init; } = DescriptorState.Active;

    public string? SupersededById { get; init; }

    public required CapabilityProjectionReference Capability { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public AgentToolSelectionPolicy SelectionPolicy { get; init; }
        = AgentToolSelectionPolicy.ExplicitOnly;

    public AgentToolSideEffectKind SideEffectKind { get; init; }
        = AgentToolSideEffectKind.Unknown;

    public CapabilityRiskLevel? RiskFloor { get; init; }

    public AgentToolApprovalMode ApprovalMode { get; init; }
        = AgentToolApprovalMode.PolicyDriven;

    public required AgentToolBudgetRequirement Budget { get; init; }

    public AgentToolAuditMode AuditMode { get; init; }
        = AgentToolAuditMode.Required;

    public IReadOnlyList<string> AllowedAgentRoles { get; init; }
        = Array.Empty<string>();
}
