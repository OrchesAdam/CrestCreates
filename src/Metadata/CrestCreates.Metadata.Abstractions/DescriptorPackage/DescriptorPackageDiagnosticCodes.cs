using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public static class DescriptorPackageDiagnosticCodes
{
    private const string DuplicateDescriptorRefValue = "PACKAGE_DUPLICATE_DESCRIPTOR_REF";
    public static DiagnosticCode DuplicateDescriptorRef { get; } = new(DuplicateDescriptorRefValue);

    private const string DescriptorHashMismatchValue = "PACKAGE_DESCRIPTOR_HASH_MISMATCH";
    public static DiagnosticCode DescriptorHashMismatch { get; } = new(DescriptorHashMismatchValue);

    private const string ManifestRefMismatchValue = "PACKAGE_MANIFEST_REF_MISMATCH";
    public static DiagnosticCode ManifestRefMismatch { get; } = new(ManifestRefMismatchValue);

    private const string EvidenceSubjectOutsideInventoryValue = "PACKAGE_EVIDENCE_SUBJECT_OUTSIDE_INVENTORY";
    public static DiagnosticCode EvidenceSubjectOutsideInventory { get; } = new(EvidenceSubjectOutsideInventoryValue);

    private const string TopologyNodeOutsidePackageValue = "PACKAGE_TOPOLOGY_NODE_OUTSIDE_PACKAGE";
    public static DiagnosticCode TopologyNodeOutsidePackage { get; } = new(TopologyNodeOutsidePackageValue);

    private const string TopologyEdgeOutsidePackageValue = "PACKAGE_TOPOLOGY_EDGE_OUTSIDE_PACKAGE";
    public static DiagnosticCode TopologyEdgeOutsidePackage { get; } = new(TopologyEdgeOutsidePackageValue);

    private const string ImpactChangeOutsidePackageValue = "PACKAGE_IMPACT_CHANGE_OUTSIDE_PACKAGE";
    public static DiagnosticCode ImpactChangeOutsidePackage { get; } = new(ImpactChangeOutsidePackageValue);

    private const string CompatibilitySubjectOutsidePackageValue = "PACKAGE_COMPATIBILITY_SUBJECT_OUTSIDE_PACKAGE";
    public static DiagnosticCode CompatibilitySubjectOutsidePackage { get; } = new(CompatibilitySubjectOutsidePackageValue);

    private const string LifecycleTransitionOutsideInventoryValue = "PACKAGE_LIFECYCLE_TRANSITION_OUTSIDE_INVENTORY";
    public static DiagnosticCode LifecycleTransitionOutsideInventory { get; } = new(LifecycleTransitionOutsideInventoryValue);

    private const string HashMismatchValue = "PACKAGE_HASH_MISMATCH";
    public static DiagnosticCode HashMismatch { get; } = new(HashMismatchValue);

    private const string FormatUnsupportedValue = "PACKAGE_FORMAT_UNSUPPORTED";
    public static DiagnosticCode FormatUnsupported { get; } = new(FormatUnsupportedValue);

    private const string TopologyNotProvidedValue = "PACKAGE_TOPOLOGY_NOT_PROVIDED";
    public static DiagnosticCode TopologyNotProvided { get; } = new(TopologyNotProvidedValue);

    private const string SeverityErrorValue = "Error";
    public static SeverityLevel SeverityError { get; } = SeverityLevel.Error;

    private const string SeverityWarningValue = "Warning";
    public static SeverityLevel SeverityWarning { get; } = SeverityLevel.Warning;

    private const string SeverityInfoValue = "Info";
    public static SeverityLevel SeverityInfo { get; } = SeverityLevel.Info;
}
