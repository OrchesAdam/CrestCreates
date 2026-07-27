using CrestCreates.Mcp;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.McpTools;

[McpToolSpecs]
public static partial class ProcurementMcpTools
{
    [McpToolSpec("procurement.submit-request",
        InputType = typeof(SubmitProcurementRequestInput),
        OutputType = typeof(SubmitProcurementRequestResult),
        ToolName = "submit_procurement_request",
        Title = "Submit Procurement Request",
        Description = "Submits a new procurement request via MCP",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.False,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class SubmitRequest { }
}
