using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

/// <summary>Immutable evidence projection linked to one exact package preview.</summary>
public sealed record AgentEvidenceArtifactEnvelope
{
    public const int CurrentVersion = 1;

    public required int Version { get; init; }
    public required string TenantId { get; init; }
    public required string EvidencePreviewId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required string PackagePreviewId { get; init; }
    public required AgentPackageArtifactOwner Owner { get; init; }
    public required string ScopeFingerprint { get; init; }
    public required string? DraftVersion { get; init; }
    public required PackageEvidencePreview ProjectedEvidence { get; init; }
    public required CanonicalHash ContentIntegrityHash { get; init; }

    public void ValidateBinding()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException($"Evidence artifact version '{Version}' is not supported.");
        Require(TenantId, nameof(TenantId));
        Require(EvidencePreviewId, nameof(EvidencePreviewId));
        Require(PackagePreviewId, nameof(PackagePreviewId));
        Require(ScopeFingerprint, nameof(ScopeFingerprint));
        ArgumentNullException.ThrowIfNull(Owner);
        ArgumentNullException.ThrowIfNull(ProjectedEvidence);
        ArgumentNullException.ThrowIfNull(ProjectedEvidence.Diagnostics);
        if (!StringComparer.Ordinal.Equals(TenantId, Owner.TenantId) ||
            !StringComparer.Ordinal.Equals(TenantId, ProjectedEvidence.TenantId) ||
            !StringComparer.Ordinal.Equals(Owner.DraftId, ProjectedEvidence.DraftId) ||
            !StringComparer.Ordinal.Equals(PackagePreviewId, ProjectedEvidence.PackagePreviewId) ||
            !StringComparer.Ordinal.Equals(EvidencePreviewId, ProjectedEvidence.EvidencePreviewId))
            throw new ArgumentException("Evidence artifact key, owner, or package association is inconsistent.");
        ArgumentNullException.ThrowIfNull(ContentIntegrityHash);
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Evidence artifact {name} is required.", name);
    }
}
