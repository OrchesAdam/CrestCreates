namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorRef
{
    string Namespace { get; }
    string Id { get; }
    int? Version { get; }
    string FullId => $"{Namespace}.{Id}";
}
