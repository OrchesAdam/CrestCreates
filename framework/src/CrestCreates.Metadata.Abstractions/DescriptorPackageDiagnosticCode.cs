namespace CrestCreates.Metadata.Abstractions;

public static class DescriptorPackageDiagnosticCode
{
    public const string DuplicateDescriptorRef = "PACKAGE_DUPLICATE_DESCRIPTOR_REF";
    public const string DescriptorHashMismatch = "PACKAGE_DESCRIPTOR_HASH_MISMATCH";
    public const string ManifestRefMismatch = "PACKAGE_MANIFEST_REF_MISMATCH";
    public const string EvidenceSubjectOutsideInventory = "PACKAGE_EVIDENCE_SUBJECT_OUTSIDE_INVENTORY";
    public const string TopologyNodeOutsidePackage = "PACKAGE_TOPOLOGY_NODE_OUTSIDE_PACKAGE";
    public const string TopologyEdgeOutsidePackage = "PACKAGE_TOPOLOGY_EDGE_OUTSIDE_PACKAGE";
    public const string ImpactChangeOutsidePackage = "PACKAGE_IMPACT_CHANGE_OUTSIDE_PACKAGE";
    public const string CompatibilitySubjectOutsidePackage = "PACKAGE_COMPATIBILITY_SUBJECT_OUTSIDE_PACKAGE";
    public const string LifecycleTransitionOutsideInventory = "PACKAGE_LIFECYCLE_TRANSITION_OUTSIDE_INVENTORY";
    public const string HashMismatch = "PACKAGE_HASH_MISMATCH";
    public const string FormatUnsupported = "PACKAGE_FORMAT_UNSUPPORTED";
    public const string TopologyNotProvided = "PACKAGE_TOPOLOGY_NOT_PROVIDED";

    public const string SeverityError = "Error";
    public const string SeverityWarning = "Warning";
    public const string SeverityInfo = "Info";
}
