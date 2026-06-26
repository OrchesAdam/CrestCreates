namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

/// <summary>
/// Canonical shape version constants for review result artifact hashing.
/// Shape version changes indicate a breaking change to the canonical JSON format.
/// </summary>
public static class DescriptorDraftReviewCanonicalShapeVersions
{
    public const string SourceBindingV1 = "descriptor-draft-review-source-binding-v1";
    public const string IntegrityV1 = "descriptor-draft-review-integrity-v1";

    public const string SourceBindingV2 = "descriptor-draft-review-source-binding-v2";
    public const string IntegrityV2 = "descriptor-draft-review-integrity-v2";
}
