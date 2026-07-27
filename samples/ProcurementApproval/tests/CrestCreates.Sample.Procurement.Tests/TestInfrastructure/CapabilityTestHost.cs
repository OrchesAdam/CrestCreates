using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Sample.Procurement.Application.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

internal sealed class CapabilityTestHost
{
    private readonly ServiceProvider _serviceProvider;

    public CapabilityTestHost()
    {
        var services = new ServiceCollection();

        var handlerResolver = new CapabilityHandlerResolver();
        var module = new ProcurementCapabilityModule();
        module.Apply(handlerResolver);
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddCapabilityRuntime();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([]);
        services.AddSingleton<ICapabilityRegistry>(registry);

        _serviceProvider = services.BuildServiceProvider();
    }

    public ICapabilityPipeline CreatePipeline()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();
    }
}
