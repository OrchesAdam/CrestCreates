using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Exposure.Abstractions;

public sealed class AgentToolDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public string Description { get; init; } = string.Empty;
    public ToolCallMode ToolCallMode { get; init; } = ToolCallMode.Auto;
    public int? BudgetLimit { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
