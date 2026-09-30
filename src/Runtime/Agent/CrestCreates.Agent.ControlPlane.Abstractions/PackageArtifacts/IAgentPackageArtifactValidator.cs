namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

/// <summary>Validates captured package content, hashes, scope and evidence-parent binding.</summary>
public interface IAgentPackageArtifactValidator
{
    void ValidatePackage(AgentPackageArtifactEnvelope package);
    void ValidateEvidence(AgentEvidenceArtifactEnvelope evidence, AgentPackageArtifactEnvelope package);
}

/// <summary>Captures official package/evidence content and generates its integrity digest.</summary>
public interface IAgentPackageArtifactFactory
{
    AgentPackageEvidenceArtifactPair CreateProjectedPair(
        string packagePreviewId,
        string evidencePreviewId,
        DateTimeOffset capturedAt,
        CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft owner,
        CrestCreates.Agent.ControlPlane.Abstractions.AgentToolAuthorizationOptions authorizationOptions,
        IReadOnlyList<CrestCreates.Metadata.Abstractions.IDescriptor> exactCatalogInventory,
        CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage package,
        IReadOnlyList<CrestCreates.Agent.ControlPlane.Abstractions.AgentToolDiagnostic>? diagnostics = null);

    AgentPackageArtifactEnvelope CreatePackage(
        string packagePreviewId,
        DateTimeOffset capturedAt,
        CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft owner,
        string scopeFingerprint,
        string visibleCatalogFingerprint,
        CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview projectedPreview,
        CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage package);

    AgentEvidenceArtifactEnvelope CreateEvidence(
        string evidencePreviewId,
        DateTimeOffset capturedAt,
        AgentPackageArtifactEnvelope package,
        CrestCreates.Agent.ControlPlane.Abstractions.PackageEvidencePreview projectedEvidence);

    CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage ReadPackageContent(
        AgentPackageArtifactEnvelope package);
}
