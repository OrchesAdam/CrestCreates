namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

public sealed record AgentPackageArtifactKey(string TenantId, string PackagePreviewId);

public sealed record AgentEvidenceArtifactKey(string TenantId, string EvidencePreviewId);
