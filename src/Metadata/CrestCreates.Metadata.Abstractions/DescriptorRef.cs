namespace CrestCreates.Metadata.Abstractions;

public readonly record struct DescriptorRef<TDescriptor>(string Id)
    where TDescriptor : IDescriptor;
