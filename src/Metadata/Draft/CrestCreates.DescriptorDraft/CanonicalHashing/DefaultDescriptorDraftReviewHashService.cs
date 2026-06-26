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

    public CanonicalHash ComputeSourceReviewHash(DescriptorDraftReviewResult reviewResult)
    {
        var projection = CreateSourceBindingProjection(reviewResult);
        return _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashPurposeNames.SourceBinding, DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV1),
                writer => ReviewResultSourceBindingCanonicalHashWriter.WritePayload(writer, projection)));
    }

    public CanonicalHash ComputeReviewManifestHash(DescriptorDraftReviewResult reviewResult)
    {
        var projection = CreateIntegrityProjection(reviewResult);
        return _hashComputer.ComputeFromProjection(
            CanonicalHashProjectionResult.Create(
                CreateMetadata(CanonicalHashPurposeNames.Integrity, DescriptorDraftReviewCanonicalShapeVersions.IntegrityV1),
                writer => ReviewResultIntegrityCanonicalHashWriter.WritePayload(writer, projection)));
    }

    private static ReviewResultSourceBindingProjection CreateSourceBindingProjection(DescriptorDraftReviewResult reviewResult)
    {
        string? governanceDecision = null;
        string? impactSeverity = null;

        // Extract governance decision if present (MaxDecision from DescriptorLifecycleGovernanceReport)
        if (reviewResult.GovernanceDecision is not null)
        {
            governanceDecision = reviewResult.GovernanceDecision.MaxDecision.ToString();
        }

        // Extract impact severity if present (MaxSeverity from DescriptorImpactAnalysisReport)
        if (reviewResult.ImpactAnalysisResult is not null)
        {
            impactSeverity = reviewResult.ImpactAnalysisResult.MaxSeverity.ToString();
        }

        return new ReviewResultSourceBindingProjection
        {
            TenantId = reviewResult.TenantId,
            DraftId = reviewResult.DraftId,
            IsActivationEligible = reviewResult.IsActivationEligible,
            IsValid = reviewResult.ValidationResult.IsValid,
            Diagnostics = reviewResult.Diagnostics
                .Select(d => new ReviewDiagnosticProjection
                {
                    Code = d.Code,
                    Severity = d.Severity.ToString()
                })
                .ToList()
                .AsReadOnly(),
            GovernanceDecision = governanceDecision,
            ImpactSeverity = impactSeverity
        };
    }

    private static ReviewResultIntegrityProjection CreateIntegrityProjection(DescriptorDraftReviewResult reviewResult)
    {
        return new ReviewResultIntegrityProjection
        {
            TenantId = reviewResult.TenantId,
            DraftId = reviewResult.DraftId,
            IsActivationEligible = reviewResult.IsActivationEligible,
            IsValid = reviewResult.ValidationResult.IsValid,
            DiagnosticCount = reviewResult.Diagnostics.Count
        };
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
