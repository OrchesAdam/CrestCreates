using CrestCreates.Metadata.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public abstract record DescriptorDraftPayload : ISnapshotable<DescriptorDraftPayload>
{
    public abstract DescriptorKind DescriptorKind { get; }
    public abstract IDescriptor GetDescriptor();
    public abstract DescriptorDraftPayload Snapshot();
}
