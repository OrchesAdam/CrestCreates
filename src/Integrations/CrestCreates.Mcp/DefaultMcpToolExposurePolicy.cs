namespace CrestCreates.Mcp;

public sealed class DefaultMcpToolExposurePolicy : IMcpToolExposurePolicy
{
    public ValueTask<McpToolExposureDecision> EvaluateAsync(
        McpToolExposureContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(McpToolExposureDecision.Allow);
}
