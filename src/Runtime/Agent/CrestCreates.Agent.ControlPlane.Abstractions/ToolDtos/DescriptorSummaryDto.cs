using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of a descriptor, replacing direct IDescriptor exposure.
/// LifecycleState is string (not enum) because different descriptor kinds
/// may use different lifecycle enums.
/// </summary>
public sealed record DescriptorSummaryDto
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? LifecycleState { get; init; }
}
