using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public interface IDescriptorDraftReviewService
{
    Task<DescriptorDraftReviewResult> ReviewAsync(
        DescriptorDraft draft,
        IReadOnlyList<IDescriptor> currentInventory,
        CancellationToken ct = default);
}
