using CrestCreates.Mcp;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Contracts.McpTools;

[McpToolSpecs]
public static partial class ProcurementMcpTools
{
    [McpToolSpec(
        ProcurementContractIds.GetCapability,
        DescriptorId = "mcp-tool:procurement.get-request",
        CapabilityVersion = 1,
        InputType = typeof(GetProcurementRequestInput),
        OutputType = typeof(ProcurementRequestResult),
        ToolName = ProcurementContractIds.GetTool,
        Title = "Get procurement request",
        Description = "Gets one procurement request in the current tenant.",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class GetRequest;
}
