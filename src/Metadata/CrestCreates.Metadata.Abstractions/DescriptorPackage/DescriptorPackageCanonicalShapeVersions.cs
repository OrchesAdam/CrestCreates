namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

/// <summary>
/// Canonical shape version constants for package artifact hashing.
/// Shape version changes indicate a breaking change to the canonical JSON format.
/// </summary>
public static class DescriptorPackageCanonicalShapeVersions
{
    public const string PackageManifestV1 = "descriptor-package-manifest-v1";
    public const string PackageEvidenceV1 = "descriptor-package-evidence-v1";
    public const string PackageEvidenceEnvelopeV1 = "descriptor-package-evidence-envelope-v1";

    public const string PackageManifestV2 = "descriptor-package-manifest-v2";
    public const string PackageEvidenceV2 = "descriptor-package-evidence-v2";
    public const string PackageEvidenceEnvelopeV2 = "descriptor-package-evidence-envelope-v2";
}
