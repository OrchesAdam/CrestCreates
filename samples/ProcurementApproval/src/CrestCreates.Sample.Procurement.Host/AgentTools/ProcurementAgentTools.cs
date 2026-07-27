using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.AgentTools;

[AgentToolSpecs]
public static partial class ProcurementAgentTools
{
    [AgentToolSpec("procurement.submit-request",
        InputType = typeof(SubmitProcurementRequestInput),
        OutputType = typeof(SubmitProcurementRequestResult),
        ToolName = "submit_procurement_request",
        Title = "Submit Procurement Request",
        Description = "Submits a new procurement request via Agent",
        SideEffectKind = AgentToolSideEffectKind.InternalWrite,
        RiskFloor = AgentToolRiskFloor.Medium,
        AuditMode = AgentToolAuditMode.Required,
        BudgetCategory = "procurement",
        AllowedAgentRoles = new[] { "procurement-agent" })]
    public sealed class SubmitRequest { }
}
