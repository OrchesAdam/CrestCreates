using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleTransition
{
    public required DescriptorRef Subject { get; init; }
    public required DescriptorLifecycleOperation Operation { get; init; }
    public DescriptorState? FromState { get; init; }
    public DescriptorState? ToState { get; init; }
    public string? Reason { get; init; }
}
