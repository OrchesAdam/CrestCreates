namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public interface IDescriptorChangeSetBuilder
{
    DescriptorChangeSet Build(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after);
}
