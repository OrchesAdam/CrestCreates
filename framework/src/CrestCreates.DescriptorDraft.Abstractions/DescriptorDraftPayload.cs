using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public abstract record DescriptorDraftPayload
{
    public abstract DescriptorKind DescriptorKind { get; }
    public abstract IDescriptor GetDescriptor();
    public abstract DescriptorDraftPayload CreateClone();
}
