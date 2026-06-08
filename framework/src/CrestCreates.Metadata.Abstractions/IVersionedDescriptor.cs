namespace CrestCreates.Metadata.Abstractions;

public interface IVersionedDescriptor : IDescriptor
{
    int Version { get; }
}
