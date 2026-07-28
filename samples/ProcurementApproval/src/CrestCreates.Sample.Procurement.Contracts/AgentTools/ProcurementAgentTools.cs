using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Contracts.AgentTools;

[AgentToolSpecs]
public static partial class ProcurementAgentTools
{
    [AgentToolSpec(
        ProcurementContractIds.GetCapability,
        DescriptorId = "agent-tool:procurement.get-request",
        CapabilityVersion = 1,
        InputType = typeof(GetProcurementRequestInput),
        OutputType = typeof(ProcurementRequestResult),
        ToolName = ProcurementContractIds.GetTool,
        Title = "Get procurement request",
        Description = "Gets one procurement request in the current tenant.",
        SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.ReadOnly,
        RiskFloor = AgentToolRiskFloor.Low,
        ApprovalMode = AgentToolApprovalMode.None,
        BudgetCategory = "procurement-read",
        CostUnits = 1,
        MaxCallsPerExecution = 10,
        AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "procurement-agent" })]
    public sealed class GetRequest;

    [AgentToolSpec(
        ProcurementContractIds.SubmitCapability,
        DescriptorId = "agent-tool:procurement.submit-request",
        CapabilityVersion = 1,
        InputType = typeof(SubmitProcurementRequestInput),
        OutputType = typeof(SubmitProcurementRequestResult),
        ToolName = ProcurementContractIds.SubmitTool,
        Title = "Submit procurement request",
        Description = "Creates and submits a procurement request for the current user.",
        SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.InternalWrite,
        RiskFloor = AgentToolRiskFloor.High,
        ApprovalMode = AgentToolApprovalMode.Required,
        BudgetCategory = "procurement",
        CostUnits = 1,
        MaxCallsPerExecution = 1,
        AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "procurement-agent" })]
    public sealed class SubmitRequest;
}
