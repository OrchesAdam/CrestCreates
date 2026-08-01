namespace CrestCreates.Metadata.Abstractions.Runtime;

public sealed record ResolvedRuntimeDescriptor<TDescriptor>
    where TDescriptor : IVersionedDescriptor
{
    public required TDescriptor Descriptor { get; init; }

    public required RuntimeDescriptorPin Pin { get; init; }
}
