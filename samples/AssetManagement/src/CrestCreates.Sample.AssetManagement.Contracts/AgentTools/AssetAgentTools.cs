using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;

namespace CrestCreates.Sample.AssetManagement.Contracts.AgentTools;

[AgentToolSpecs]
public static partial class AssetAgentTools
{
    [AgentToolSpec(
        AssetContractIds.GetCapability,
        DescriptorId = "agent-tool:asset-management.get",
        CapabilityVersion = 1,
        InputType = typeof(AssetQueryInput),
        OutputType = typeof(AssetResult),
        ToolName = AssetContractIds.GetTool,
        Title = "Get asset",
        Description = "Gets one asset visible to the current tenant and organization scope.",
        SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.ReadOnly,
        RiskFloor = AgentToolRiskFloor.Low,
        ApprovalMode = AgentToolApprovalMode.None,
        BudgetCategory = "asset-read",
        CostUnits = 1,
        MaxCallsPerExecution = 20,
        AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "asset-agent" })]
    public sealed class GetAsset;

}
