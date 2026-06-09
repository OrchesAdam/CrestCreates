namespace CrestCreates.Metadata.Abstractions;

public interface IRegistryValidationEngine<TDescriptor>
    where TDescriptor : IDescriptor
{
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
