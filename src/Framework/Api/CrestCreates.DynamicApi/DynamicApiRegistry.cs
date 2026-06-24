namespace CrestCreates.DynamicApi;

public sealed class DynamicApiRegistry
{
    public DynamicApiRegistry(IReadOnlyList<DynamicApiServiceDescriptor> services)
    {
        Services = services;
    }

    public IReadOnlyList<DynamicApiServiceDescriptor> Services { get; }
}
