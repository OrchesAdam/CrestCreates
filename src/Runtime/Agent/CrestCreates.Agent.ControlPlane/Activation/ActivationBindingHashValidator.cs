using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Validates that binding hashes are complete, consistent, and semantically correct.
/// Checks: non-empty values, ArtifactKind/Purpose/Scope per slot, non-empty required
/// metadata fields, AlgorithmVersion and ContractVersion consistency across all hash slots.
/// </summary>
public sealed class ActivationBindingHashValidator
{
    /// <summary>
    /// Expected metadata per binding hash slot.
    /// </summary>
    private static readonly (string Slot, string ExpectedArtifactKind, string ExpectedPurpose, string ExpectedScope)[] SlotMetadata =
    [
        (nameof(BindingHashes.SourceReviewHash), CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.SourceBinding, CanonicalHashScopeNames.InternalFull),
        (nameof(BindingHashes.ReviewManifestHash), CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.Integrity, CanonicalHashScopeNames.InternalFull),
        (nameof(BindingHashes.PackageManifestHash), CanonicalHashArtifactNames.PackageManifest, CanonicalHashPurposeNames.Integrity, CanonicalHashScopeNames.InternalFull),
        (nameof(BindingHashes.PackageEvidenceHash), CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence, CanonicalHashScopeNames.InternalFull),
        (nameof(BindingHashes.PackageEvidenceEnvelopeHash), CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence, CanonicalHashScopeNames.InternalFull),
        (nameof(BindingHashes.ContractHash), CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Contract, CanonicalHashScopeNames.InternalFull),
        (nameof(BindingHashes.DefinitionHash), CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Definition, CanonicalHashScopeNames.InternalFull),
    ];

    /// <summary>
    /// Validates the binding hashes for completeness, semantic correctness, and metadata consistency.
    /// Returns a list of validation issues (empty = valid).
    /// </summary>
    public IReadOnlyList<BindingHashValidationIssue> Validate(BindingHashes hashes)
    {
        var issues = new List<BindingHashValidationIssue>();

        var slots = GetSlots(hashes);

        // Check all slots have non-empty values
        foreach (var (name, hash, _, _, _) in slots)
        {
            if (string.IsNullOrWhiteSpace(hash.Value))
                issues.Add(new(name, $"{name} value is empty", BindingHashValidationSeverity.Error));
        }

        // Check ArtifactKind/Purpose/Scope per slot + required metadata non-empty
        foreach (var (name, hash, expectedKind, expectedPurpose, expectedScope) in slots)
        {
            if (!string.IsNullOrWhiteSpace(hash.Value))
            {
                if (hash.ArtifactKind != expectedKind)
                    issues.Add(new(name, $"{name} has ArtifactKind '{hash.ArtifactKind}', expected '{expectedKind}'", BindingHashValidationSeverity.Error));
                if (hash.Purpose != expectedPurpose)
                    issues.Add(new(name, $"{name} has Purpose '{hash.Purpose}', expected '{expectedPurpose}'", BindingHashValidationSeverity.Error));
                if (hash.Scope != expectedScope)
                    issues.Add(new(name, $"{name} has Scope '{hash.Scope}', expected '{expectedScope}'", BindingHashValidationSeverity.Error));

                // Required metadata fields must be non-empty
                if (string.IsNullOrWhiteSpace(hash.Algorithm))
                    issues.Add(new(name, $"{name} Algorithm is empty", BindingHashValidationSeverity.Error));
                if (string.IsNullOrWhiteSpace(hash.AlgorithmVersion))
                    issues.Add(new(name, $"{name} AlgorithmVersion is empty", BindingHashValidationSeverity.Error));
                if (string.IsNullOrWhiteSpace(hash.ContractVersion))
                    issues.Add(new(name, $"{name} ContractVersion is empty", BindingHashValidationSeverity.Error));
                if (string.IsNullOrWhiteSpace(hash.CanonicalShapeVersion))
                    issues.Add(new(name, $"{name} CanonicalShapeVersion is empty", BindingHashValidationSeverity.Error));
            }
        }

        // Check AlgorithmVersion consistency
        var algorithmVersions = new HashSet<string>();
        foreach (var (_, hash, _, _, _) in slots)
            AddIfNonEmpty(algorithmVersions, hash.AlgorithmVersion);
        if (algorithmVersions.Count > 1)
            issues.Add(new("AlgorithmVersion", $"AlgorithmVersion mismatch: [{string.Join(", ", algorithmVersions)}]", BindingHashValidationSeverity.Error));

        // Check ContractVersion consistency
        var contractVersions = new HashSet<string>();
        foreach (var (_, hash, _, _, _) in slots)
            AddIfNonEmpty(contractVersions, hash.ContractVersion);
        if (contractVersions.Count > 1)
            issues.Add(new("ContractVersion", $"ContractVersion mismatch: [{string.Join(", ", contractVersions)}]", BindingHashValidationSeverity.Warning));

        return issues.AsReadOnly();
    }

    private static (string Name, CanonicalHash Hash, string ExpectedArtifactKind, string ExpectedPurpose, string ExpectedScope)[] GetSlots(BindingHashes hashes)
    {
        return
        [
            (SlotMetadata[0].Slot, hashes.SourceReviewHash, SlotMetadata[0].ExpectedArtifactKind, SlotMetadata[0].ExpectedPurpose, SlotMetadata[0].ExpectedScope),
            (SlotMetadata[1].Slot, hashes.ReviewManifestHash, SlotMetadata[1].ExpectedArtifactKind, SlotMetadata[1].ExpectedPurpose, SlotMetadata[1].ExpectedScope),
            (SlotMetadata[2].Slot, hashes.PackageManifestHash, SlotMetadata[2].ExpectedArtifactKind, SlotMetadata[2].ExpectedPurpose, SlotMetadata[2].ExpectedScope),
            (SlotMetadata[3].Slot, hashes.PackageEvidenceHash, SlotMetadata[3].ExpectedArtifactKind, SlotMetadata[3].ExpectedPurpose, SlotMetadata[3].ExpectedScope),
            (SlotMetadata[4].Slot, hashes.PackageEvidenceEnvelopeHash, SlotMetadata[4].ExpectedArtifactKind, SlotMetadata[4].ExpectedPurpose, SlotMetadata[4].ExpectedScope),
            (SlotMetadata[5].Slot, hashes.ContractHash, SlotMetadata[5].ExpectedArtifactKind, SlotMetadata[5].ExpectedPurpose, SlotMetadata[5].ExpectedScope),
            (SlotMetadata[6].Slot, hashes.DefinitionHash, SlotMetadata[6].ExpectedArtifactKind, SlotMetadata[6].ExpectedPurpose, SlotMetadata[6].ExpectedScope),
        ];
    }

    private static void AddIfNonEmpty(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            set.Add(value);
    }
}

/// <summary>
/// A single validation issue found in binding hashes.
/// </summary>
public sealed record BindingHashValidationIssue(
    string Slot,
    string Description,
    BindingHashValidationSeverity Severity);

/// <summary>
/// Severity of a binding hash validation issue.
/// </summary>
public enum BindingHashValidationSeverity
{
    Warning = 0,
    Error = 1
}
