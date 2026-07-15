using CrestCreates.Metadata;
using CrestCreates.Metadata.Mcp;

namespace CrestCreates.Mcp;

public enum McpToolExposurePhase
{
    Discovery = 0,
    Invocation = 1
}

public sealed record McpToolExposureContext(
    McpToolHostContext Host,
    McpToolDescriptor Tool,
    CapabilityDescriptor Capability,
    McpToolExposurePhase Phase);

public sealed record McpToolExposureDecision(bool IsAllowed)
{
    public static McpToolExposureDecision Allow { get; } = new(true);

    public static McpToolExposureDecision Deny { get; } = new(false);
}

public interface IMcpToolExposurePolicy
{
    ValueTask<McpToolExposureDecision> EvaluateAsync(
        McpToolExposureContext context,
        CancellationToken cancellationToken = default);
}
