using CrestCreates.Mcp;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;

namespace CrestCreates.Sample.AssetManagement.Contracts.McpTools;

[McpToolSpecs]
public static partial class AssetMcpTools
{
    [McpToolSpec(
        AssetContractIds.GetCapability,
        DescriptorId = "mcp-tool:asset-management.get",
        CapabilityVersion = 1,
        InputType = typeof(AssetQueryInput),
        OutputType = typeof(AssetResult),
        ToolName = AssetContractIds.GetTool,
        Title = "Get asset",
        Description = "Gets one asset visible to the current tenant and organization scope.",
        DestructiveHint = McpBooleanHint.False,
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class GetAsset;

}
