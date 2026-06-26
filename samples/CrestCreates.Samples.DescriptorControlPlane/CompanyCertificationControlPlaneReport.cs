using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Output of a <see cref="CompanyCertificationControlPlaneRunner"/>.Run call.
/// Exposes the full analysis evidence (topology, change set, impact,
/// compatibility, governance, descriptor package) plus convenience properties
/// for pass/fail, governance decision, and package hashes.
/// </summary>
public sealed record CompanyCertificationControlPlaneReport
{
    public required string ScenarioName { get; init; }

    public required DescriptorTopologySnapshot Topology { get; init; }

    public required DescriptorChangeSet ChangeSet { get; init; }

    public required DescriptorImpactAnalysisReport Impact { get; init; }

    public required DescriptorCompatibilityReport Compatibility { get; init; }

    public required DescriptorLifecycleGovernanceReport Governance { get; init; }

    public required DescriptorPackage Package { get; init; }

    /// <summary>
    /// <c>true</c> when topology, lifecycle governance, and package self-checks
    /// did not produce a blocking or error-level result. Review-required outcomes
    /// are still considered passing for this flag because activation can continue
    /// only through an explicit review gate.
    /// </summary>
    public bool ControlPlanePassed =>
        Topology.Diagnostics.IsHealthy
        && !Governance.IsBlocked
        && !Package.Diagnostics.Any(d =>
            string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The most severe lifecycle decision across all transitions.
    /// </summary>
    public DescriptorLifecycleDecisionKind GovernanceDecision => Governance.MaxDecision;

    /// <summary>
    /// SHA-256 canonical hash of the package manifest.
    /// </summary>
    public string PackageManifestHash =>
        Package.Hashes?.PackageManifestHash.Value ?? string.Empty;

    /// <summary>
    /// SHA-256 canonical hash of the evidence payload (topology + impact + compatibility
    /// + governance). May be empty when the package builder does not produce
    /// a separate evidence hash.
    /// </summary>
    public string PackageEvidenceHash =>
        Package.Hashes?.PackageEvidenceHash.Value ?? string.Empty;

    /// <summary>
    /// SHA-256 canonical hash of the full package envelope (manifest + evidence).
    /// May be empty when the package builder does not produce an envelope hash.
    /// </summary>
    public string PackageEnvelopeHash =>
        Package.Hashes?.PackageEvidenceEnvelopeHash.Value ?? string.Empty;
}
