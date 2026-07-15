namespace CrestCreates.Metadata.Mcp;

public sealed record McpToolAnnotationOverrides
{
    public bool? DestructiveHint { get; init; }

    public bool? IdempotentHint { get; init; }

    public bool? OpenWorldHint { get; init; }
}
