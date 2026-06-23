using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record HumanTaskDescriptorDraftPayload(
    HumanTaskDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.HumanTask;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload CreateClone() => this with
    {
        Descriptor = new HumanTaskDescriptor
        {
            Id = Descriptor.Id,
            Name = Descriptor.Name,
            State = Descriptor.State,
            SupersededById = Descriptor.SupersededById,
            Version = Descriptor.Version,
            Interaction = Descriptor.Interaction,
            InputSchema = Descriptor.InputSchema,
            OutputSchema = Descriptor.OutputSchema,
            AssigneeStrategy = Descriptor.AssigneeStrategy,
            Timeout = Descriptor.Timeout,
            Permissions = Descriptor.Permissions,
            Outcomes = Descriptor.Outcomes.Select(CloneOutcome).ToArray()
        }
    };

    private static CompletionOutcome CloneOutcome(CompletionOutcome outcome) => new()
    {
        Condition = outcome.Condition,
        Capability = outcome.Capability
    };
}
