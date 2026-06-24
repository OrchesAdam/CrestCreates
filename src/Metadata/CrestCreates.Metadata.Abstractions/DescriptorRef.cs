namespace CrestCreates.Metadata.Abstractions;

public readonly record struct DescriptorRef<TDescriptor>(string Id)
    where TDescriptor : IDescriptor;

public readonly record struct DescriptorRef(
    string Namespace,
    string Id,
    int? Version = null) : IDescriptorRef
{
    public string FullId => $"{Namespace}.{Id}";
}
