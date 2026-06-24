namespace CrestCreates.Metadata.Abstractions;

public readonly record struct DescriptorRef(
    string Namespace,
    string Id,
    int? Version = null) : IDescriptorRef
{
    public string FullId => $"{Namespace}.{Id}";
}
