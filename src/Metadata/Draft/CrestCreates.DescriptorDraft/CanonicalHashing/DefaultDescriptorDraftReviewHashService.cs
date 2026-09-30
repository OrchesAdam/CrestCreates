using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.CanonicalHashing;

/// <summary>
/// Computes canonical hashes for DescriptorDraftReviewResult.
/// Uses ICanonicalHashComputer.ComputeFromProjection with dedicated canonical writers.
/// </summary>
public sealed class DefaultDescriptorDraftReviewHashService : IDescriptorDraftReviewHashService
{
    private readonly ICanonicalHashComputer _hashComputer;

    public DefaultDescriptorDraftReviewHashService(ICanonicalHashComputer hashComputer)
        => _hashComputer = hashComputer;

    public DescriptorDraftReviewHashInput CaptureInput(DescriptorDraftReviewResult reviewResult)
        => DescriptorDraftReviewHashInput.Capture(reviewResult);

    public CanonicalHash ComputeSourceReviewHash(DescriptorDraftReviewResult reviewResult)
        => ComputeSourceReviewHash(CaptureInput(reviewResult));

    public CanonicalHash ComputeReviewManifestHash(DescriptorDraftReviewResult reviewResult)
        => ComputeReviewManifestHash(CaptureInput(reviewResult));

    public CanonicalHash ComputeSourceReviewHash(DescriptorDraftReviewHashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        return _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashPurposeNames.SourceBinding, DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV2),
                writer => ReviewResultSourceBindingCanonicalHashWriter.WritePayload(writer, input.SourceBinding)));
    }

    public CanonicalHash ComputeReviewManifestHash(DescriptorDraftReviewHashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var sourceBinding = input.SourceBinding;
        var projection = new ReviewResultIntegrityProjection
        {
            TenantId = sourceBinding.TenantId,
            DraftId = sourceBinding.DraftId,
            IsActivationEligible = sourceBinding.IsActivationEligible,
            IsValid = sourceBinding.IsValid,
            DiagnosticCount = sourceBinding.Diagnostics.Count
        };
        return _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashPurposeNames.Integrity, DescriptorDraftReviewCanonicalShapeVersions.IntegrityV2),
                writer => ReviewResultIntegrityCanonicalHashWriter.WritePayload(writer, projection)));
    }

    private static CanonicalHashMetadata CreateMetadata(string purpose, string shapeVersion) => new()
    {
        ArtifactKind = CanonicalHashArtifactNames.ReviewResult,
        Purpose = purpose,
        Scope = CanonicalHashScopeNames.InternalFull,
        AlgorithmVersion = "sha256-canonical-json-v1",
        ContractVersion = CanonicalHashContractVersions.DescriptorHash,
        CanonicalShapeVersion = shapeVersion
    };
}
