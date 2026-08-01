namespace CrestCreates.Metadata.Abstractions.Runtime;

public interface IRuntimeDescriptorPinResolver<TDescriptor>
    where TDescriptor : IVersionedDescriptor
{
    ResolvedRuntimeDescriptor<TDescriptor> Capture(TDescriptor descriptor);

    ResolvedRuntimeDescriptor<TDescriptor> Resolve(RuntimeDescriptorPin pin);
}
