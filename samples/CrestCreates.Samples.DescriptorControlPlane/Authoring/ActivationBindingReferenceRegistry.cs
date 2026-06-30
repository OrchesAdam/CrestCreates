namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

/// <summary>
/// Sample-level registry for tracking activation binding references
/// and their DraftId associations. Provides equivalent validation
/// to Control Plane's internal _reviewResults/_packagePreviews/_evidencePreviews checks.
/// </summary>
public sealed class ActivationBindingReferenceRegistry
{
    private readonly Dictionary<(string TenantId, string ResourceId), BindingReference> _references = new();

    public void RegisterReviewResult(string tenantId, string reviewResultId, string draftId)
        => _references[(tenantId, reviewResultId)] = new("ReviewResult", reviewResultId, draftId);

    public void RegisterPackagePreview(string tenantId, string packagePreviewId, string draftId)
        => _references[(tenantId, packagePreviewId)] = new("PackagePreview", packagePreviewId, draftId);

    public void RegisterEvidencePreview(string tenantId, string evidencePreviewId, string draftId)
        => _references[(tenantId, evidencePreviewId)] = new("EvidencePreview", evidencePreviewId, draftId);

    public BindingReferenceValidationResult ValidateReferences(
        string tenantId, string draftId,
        string reviewResultId, string packagePreviewId, string evidencePreviewId)
    {
        var errors = new List<string>();

        if (!_references.TryGetValue((tenantId, reviewResultId), out var reviewRef))
            errors.Add($"Review result '{reviewResultId}' not found for tenant '{tenantId}'.");
        else if (reviewRef.DraftId != draftId)
            errors.Add($"Review result '{reviewResultId}' belongs to draft '{reviewRef.DraftId}', not '{draftId}'.");

        if (!_references.TryGetValue((tenantId, packagePreviewId), out var packageRef))
            errors.Add($"Package preview '{packagePreviewId}' not found for tenant '{tenantId}'.");
        else if (packageRef.DraftId != draftId)
            errors.Add($"Package preview '{packagePreviewId}' belongs to draft '{packageRef.DraftId}', not '{draftId}'.");

        if (!_references.TryGetValue((tenantId, evidencePreviewId), out var evidenceRef))
            errors.Add($"Evidence preview '{evidencePreviewId}' not found for tenant '{tenantId}'.");
        else if (evidenceRef.DraftId != draftId)
            errors.Add($"Evidence preview '{evidencePreviewId}' belongs to draft '{evidenceRef.DraftId}', not '{draftId}'.");

        return new(errors.Count == 0, errors);
    }

    private sealed record BindingReference(string Kind, string ResourceId, string DraftId);
}

public sealed record BindingReferenceValidationResult(bool IsValid, IReadOnlyList<string> Errors);
