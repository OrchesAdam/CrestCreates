namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorResolver
{
    TDescriptor? Resolve<TDescriptor>(string id) where TDescriptor : IDescriptor;
    TDescriptor? Resolve<TDescriptor>(IDescriptorRef reference) where TDescriptor : IDescriptor;
    IReadOnlyList<TDescriptor> Query<TDescriptor>(DescriptorQuery query) where TDescriptor : IDescriptor;
}
