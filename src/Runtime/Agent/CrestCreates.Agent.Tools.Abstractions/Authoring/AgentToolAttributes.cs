using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentToolSpecsAttribute : Attribute;

public enum AgentToolRiskFloor
{
    Inherit = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentToolSpecAttribute : Attribute
{
    public AgentToolSpecAttribute(string capabilityId)
        => CapabilityId = capabilityId;

    public string CapabilityId { get; }

    public string? DescriptorId { get; set; }

    public int DescriptorVersion { get; set; } = 1;

    public int CapabilityVersion { get; set; }

    public string? ExpectedCapabilityContractHash { get; set; }

    public Type? InputType { get; set; }

    public Type? OutputType { get; set; }

    public string? ToolName { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public AgentToolSelectionPolicy SelectionPolicy { get; set; }
        = AgentToolSelectionPolicy.ExplicitOnly;

    public AgentToolSideEffectKind SideEffectKind { get; set; }
        = AgentToolSideEffectKind.Unknown;

    public AgentToolRiskFloor RiskFloor { get; set; }
        = AgentToolRiskFloor.Inherit;

    public AgentToolApprovalMode ApprovalMode { get; set; }
        = AgentToolApprovalMode.PolicyDriven;

    public string? BudgetCategory { get; set; }

    public long CostUnits { get; set; } = 1;

    public int MaxCallsPerExecution { get; set; }

    public AgentToolAuditMode AuditMode { get; set; }
        = AgentToolAuditMode.Required;

    public string[] AllowedAgentRoles { get; set; } = Array.Empty<string>();
}
