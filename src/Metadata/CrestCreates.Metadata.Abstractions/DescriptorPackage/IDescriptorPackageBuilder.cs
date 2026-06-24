namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public interface IDescriptorPackageBuilder
{
    DescriptorPackage Build(DescriptorPackageBuildRequest request);
}
