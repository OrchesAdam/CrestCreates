namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public interface IDescriptorPackageDiffer
{
    DescriptorPackageDiff Diff(
        DescriptorPackage before,
        DescriptorPackage after,
        DescriptorPackageDiffOptions? options = null);
}
