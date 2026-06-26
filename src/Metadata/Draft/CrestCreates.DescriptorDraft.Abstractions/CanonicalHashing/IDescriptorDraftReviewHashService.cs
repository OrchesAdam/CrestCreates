using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

/// <summary>
/// Computes canonical hashes for DescriptorDraftReviewResult artifacts.
/// Two independent views: SourceBinding (for activation binding) and Integrity (for manifest verification).
/// </summary>
public interface IDescriptorDraftReviewHashService
{
    CanonicalHash ComputeSourceReviewHash(DescriptorDraftReviewResult reviewResult);
    CanonicalHash ComputeReviewManifestHash(DescriptorDraftReviewResult reviewResult);
}
