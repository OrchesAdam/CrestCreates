using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record EventDescriptorDraftPayload(
    EventDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Event;
    public override IDescriptor GetDescriptor() => Descriptor;
    // EventDescriptor has no IReadOnlyList properties — all scalars/value types. Clone is identity-safe.
    public override DescriptorDraftPayload CreateClone() => this with
    {
        Descriptor = new EventDescriptor
        {
            Id = Descriptor.Id,
            Name = Descriptor.Name,
            State = Descriptor.State,
            SupersededById = Descriptor.SupersededById,
            Version = Descriptor.Version,
            PayloadSchema = Descriptor.PayloadSchema,
            Category = Descriptor.Category,
            Semantic = Descriptor.Semantic,
            Importance = Descriptor.Importance,
            ChangeKind = Descriptor.ChangeKind
        }
    };
}
