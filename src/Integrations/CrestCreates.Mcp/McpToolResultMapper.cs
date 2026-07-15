using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolResultMapper
{
    public McpToolInvocationOutcome MapFailure(CapabilityExecutionResult result)
        => new(
            true,
            [new McpToolTextContent("The operation could not be completed.")],
            StructuredContent: null,
            ErrorCode: result.ErrorCode ?? StatusCode(result.Status));

    public McpToolInvocationOutcome MapInputError(string code, string safeMessage)
        => new(true, [new McpToolTextContent(safeMessage)], null, code);

    public McpToolInvocationOutcome MapVoidSuccess()
        => new(false, [new McpToolTextContent("Operation completed successfully.")]);

    public McpToolInvocationOutcome MapStructuredSuccess(System.Text.Json.JsonElement output)
        => new(
            false,
            [new McpToolTextContent(output.GetRawText())],
            output);

    private static string StatusCode(CapabilityExecutionStatus status) => status switch
    {
        CapabilityExecutionStatus.TimedOut => "TIMEOUT",
        _ => "CAPABILITY_FAILED"
    };
}
