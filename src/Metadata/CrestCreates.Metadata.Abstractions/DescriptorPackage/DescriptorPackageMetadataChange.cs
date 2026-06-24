namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed record DescriptorPackageMetadataChange
{
    public required string Field { get; init; }
    public string? BeforeValue { get; init; }
    public string? AfterValue { get; init; }
}
