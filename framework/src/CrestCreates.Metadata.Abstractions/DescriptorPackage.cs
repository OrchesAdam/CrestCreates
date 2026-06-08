namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorPackage
{
    public string PackageId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<IDescriptor> Descriptors { get; init; } = Array.Empty<IDescriptor>();
}