namespace CrestCreates.Mcp;

internal sealed class McpUnknownToolException : McpToolProtocolException
{
    internal McpUnknownToolException()
        : base(McpToolProtocolFailureKind.UnknownTool, "MCP_TOOL_UNKNOWN", "Unknown tool.")
    {
    }
}

internal sealed class McpInvalidRequestException : McpToolProtocolException
{
    internal McpInvalidRequestException(string code, string message)
        : base(McpToolProtocolFailureKind.InvalidRequest, code, message)
    {
    }
}

internal sealed class McpToolContractViolationException : McpToolProtocolException
{
    internal McpToolContractViolationException(
        string internalCode,
        string safeMessage,
        Exception? innerException = null)
        : base(McpToolProtocolFailureKind.InternalServer, internalCode, safeMessage, innerException)
    {
    }
}
