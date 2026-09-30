namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

/// <summary>The immutable package and its evidence preview captured against that exact package.</summary>
public sealed record AgentPackageEvidenceArtifactPair
{
    public required AgentPackageArtifactEnvelope Package { get; init; }
    public required AgentEvidenceArtifactEnvelope Evidence { get; init; }
}
