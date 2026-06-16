using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DraftQuery
{
    public DescriptorKind? DescriptorKind { get; init; }
    public DescriptorDraftOperation? Operation { get; init; }
    public DescriptorDraftAuthorKind? AuthorKind { get; init; }
    public DescriptorDraftStatus? Status { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
}
