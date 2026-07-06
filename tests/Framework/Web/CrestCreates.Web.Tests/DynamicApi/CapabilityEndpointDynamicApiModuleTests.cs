using System.Linq;
using CrestCreates.DynamicApi;
using CrestCreates.DynamicApi.Modules;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointDynamicApiModuleTests
{
    [Fact]
    public void OnConfigureServices_Registers_CapabilityEndpoint_Metadata_Components()
    {
        var services = new ServiceCollection();
        var module = new DynamicApiModule();

        module.OnConfigureServices(services);

        services.Should().Contain(d =>
            d.ServiceType == typeof(ICapabilityEndpointRegistry)
            && d.ImplementationType == typeof(CapabilityEndpointRegistry));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IRegistryValidationEngine<CapabilityEndpointDescriptor>));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IRegistryValidator<CapabilityEndpointDescriptor>)
            && d.ImplementationType == typeof(CapabilityEndpointDescriptorValidator));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDescriptorRelationshipExtractor)
            && d.ImplementationType == typeof(CapabilityEndpointRelationshipExtractor));
    }

    [Fact]
    public void OnConfigureServices_Validator_Requires_ICapabilityRegistry_For_DI_Resolution()
    {
        // DynamicApiModule registers CapabilityEndpointDescriptorValidator which requires ICapabilityRegistry.
        // Without ICapabilityRegistry in the container, building the provider must fail (fail-closed).
        var services = new ServiceCollection();
        var module = new DynamicApiModule();

        module.OnConfigureServices(services);

        var act = () => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OnConfigureServices_Validator_Resolves_When_ICapabilityRegistry_Registered()
    {
        var services = new ServiceCollection();
        var module = new DynamicApiModule();

        module.OnConfigureServices(services);

        // Register ICapabilityRegistry (normally done by CrestCreates.Capability module)
        var registry = new Mock<ICapabilityRegistry>().Object;
        services.AddSingleton<ICapabilityRegistry>(registry);

        var sp = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        sp.GetRequiredService<IRegistryValidator<CapabilityEndpointDescriptor>>()
            .Should().BeOfType<CapabilityEndpointDescriptorValidator>();
    }
}
