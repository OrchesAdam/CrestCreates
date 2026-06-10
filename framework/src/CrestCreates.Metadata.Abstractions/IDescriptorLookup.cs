namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Read-only query interface for bootstrap phase.
/// Implemented by registries or constructed by BootstrapCoordinator.
/// </summary>
public interface IDescriptorLookup
{
    bool Exists(DescriptorRef descriptorRef);
}
