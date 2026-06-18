using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class CompletionOutcome
{
    public CompletionCondition Condition { get; init; }
    public VersionedDescriptorRef<IVersionedDescriptor>? Capability { get; init; }
}
