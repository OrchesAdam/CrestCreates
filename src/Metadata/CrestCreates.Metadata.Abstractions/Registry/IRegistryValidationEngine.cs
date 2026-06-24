namespace CrestCreates.Metadata.Abstractions.Registry;

public interface IRegistryValidationEngine<TDescriptor>
    where TDescriptor : IDescriptor
{
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
