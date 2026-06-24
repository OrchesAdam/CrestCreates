namespace CrestCreates.Metadata.Abstractions.Registry;

public interface IRegistryValidator<TDescriptor>
    where TDescriptor : IDescriptor
{
    int Order { get; }
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
