namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorPackage
{
    public DescriptorManifest Manifest { get; init; } = new();
    public DescriptorSnapshot Snapshot { get; init; } = new();
    public DescriptorPackageEvidence Evidence { get; init; } = new();
    public IReadOnlyList<DescriptorPackageDiagnostic> Diagnostics { get; init; }
        = Array.Empty<DescriptorPackageDiagnostic>();

    public string PackageId => Manifest.PackageId;
    public string PackageVersion => Manifest.PackageVersion;
    public string ContentHash => Manifest.ContentHash;
}
