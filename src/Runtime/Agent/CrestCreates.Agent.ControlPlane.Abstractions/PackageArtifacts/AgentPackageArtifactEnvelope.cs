using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

/// <summary>Immutable package preview content and its exact official package serialization.</summary>
public sealed record AgentPackageArtifactEnvelope
{
    public const int CurrentVersion = 1;

    public required int Version { get; init; }
    public required string TenantId { get; init; }
    public required string PackagePreviewId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required DateTimeOffset PackageCreatedAt { get; init; }
    public required AgentPackageArtifactOwner Owner { get; init; }
    public required string ScopeFingerprint { get; init; }
    public required string? DraftVersion { get; init; }
    public required string VisibleCatalogFingerprint { get; init; }
    public required DescriptorPackagePreview ProjectedPreview { get; init; }
    public required string PackageJson { get; init; }
    public required CanonicalHash ContentIntegrityHash { get; init; }

    public void ValidateBinding()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException($"Package artifact version '{Version}' is not supported.");
        Require(TenantId, nameof(TenantId));
        Require(PackagePreviewId, nameof(PackagePreviewId));
        Require(ScopeFingerprint, nameof(ScopeFingerprint));
        Require(VisibleCatalogFingerprint, nameof(VisibleCatalogFingerprint));
        Require(PackageJson, nameof(PackageJson));
        ArgumentNullException.ThrowIfNull(Owner);
        ArgumentNullException.ThrowIfNull(ProjectedPreview);
        ArgumentNullException.ThrowIfNull(ProjectedPreview.DescriptorIds);
        ArgumentNullException.ThrowIfNull(ContentIntegrityHash);
        if (!StringComparer.Ordinal.Equals(TenantId, Owner.TenantId))
            throw new ArgumentException("Package artifact owner tenant does not match its key.");
        if (!StringComparer.Ordinal.Equals(DraftVersion, Owner.ProposedVersion))
            throw new ArgumentException("Package artifact draft version does not match its captured owner.");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Package artifact {name} is required.", name);
    }
}
