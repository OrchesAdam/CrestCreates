namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorPackageDiffer
{
    DescriptorPackageDiff Diff(
        DescriptorPackage before,
        DescriptorPackage after,
        DescriptorPackageDiffOptions? options = null);
}
