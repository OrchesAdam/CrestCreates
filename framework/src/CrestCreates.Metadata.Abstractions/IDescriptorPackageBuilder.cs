namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorPackageBuilder
{
    DescriptorPackage Build(DescriptorPackageBuildRequest request);
}
