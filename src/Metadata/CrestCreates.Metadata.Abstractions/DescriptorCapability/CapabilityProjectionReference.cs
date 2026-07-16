namespace CrestCreates.Metadata.Abstractions.DescriptorCapability;

/// <summary>
/// Identifies a Capability selected by a metadata-owned runtime projection.
/// </summary>
public readonly record struct CapabilityProjectionReference(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null);
