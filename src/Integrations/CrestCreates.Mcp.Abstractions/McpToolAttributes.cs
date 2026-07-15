namespace CrestCreates.Mcp;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpToolSpecsAttribute : Attribute;

public enum McpBooleanHint
{
    Unspecified = 0,
    False = 1,
    True = 2
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpToolSpecAttribute : Attribute
{
    public McpToolSpecAttribute(string capabilityId)
        => CapabilityId = capabilityId;

    public string CapabilityId { get; }

    public string? DescriptorId { get; set; }

    public int DescriptorVersion { get; set; } = 1;

    public int CapabilityVersion { get; set; }

    public Type? InputType { get; set; }

    public Type? OutputType { get; set; }

    public string? ToolName { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public McpBooleanHint DestructiveHint { get; set; }

    public McpBooleanHint IdempotentHint { get; set; }

    public McpBooleanHint OpenWorldHint { get; set; }
}
