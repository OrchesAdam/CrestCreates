namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

/// <summary>Single asynchronous authority for immutable package and linked evidence previews.</summary>
public interface IAgentPackageArtifactStore
{
    Task InsertPackageAsync(AgentPackageArtifactEnvelope package, CancellationToken ct = default);

    /// <summary>Inserts both records atomically; either both are visible or neither is.</summary>
    Task InsertPackageAndEvidenceAsync(
        AgentPackageArtifactEnvelope package,
        AgentEvidenceArtifactEnvelope evidence,
        CancellationToken ct = default);

    /// <summary>Inserts evidence only when its exact parent package already exists.</summary>
    Task InsertEvidenceAsync(AgentEvidenceArtifactEnvelope evidence, CancellationToken ct = default);

    Task<AgentPackageArtifactEnvelope?> GetPackageAsync(
        AgentPackageArtifactKey key,
        CancellationToken ct = default);

    Task<AgentEvidenceArtifactEnvelope?> GetEvidenceAsync(
        AgentEvidenceArtifactKey key,
        CancellationToken ct = default);

    Task<AgentPackageArtifactEnvelope?> GetLatestReusablePackageAsync(
        string tenantId,
        string draftId,
        string scopeFingerprint,
        string? capturedDraftVersion,
        string visibleCatalogFingerprint,
        CancellationToken ct = default);
}
