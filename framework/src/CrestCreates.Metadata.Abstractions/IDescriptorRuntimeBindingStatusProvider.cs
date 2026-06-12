namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Consumer-facing query API. Runs AFTER registries are built.
/// Does not trigger registry.Build() or mutate descriptors.
/// </summary>
public interface IDescriptorRuntimeBindingStatusProvider
{
    DescriptorBindingReport GetStatus(IDescriptor descriptor);
    RuntimeBindingReport GetAllStatuses();
}
