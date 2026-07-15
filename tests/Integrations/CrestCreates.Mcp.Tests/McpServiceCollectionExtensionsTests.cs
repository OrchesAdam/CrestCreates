using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCrestMcpToolProjection_registers_core_services_once()
    {
        var services = new ServiceCollection();

        services.AddCrestMcpToolProjection();
        services.AddCrestMcpToolProjection();

        services.Count(descriptor => descriptor.ServiceType == typeof(IMcpToolRegistry)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IMcpToolInvoker)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IMcpToolDiscoveryService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IMcpToolExposurePolicy)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IMcpIdempotencyKeyBuilder)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(ISchemaValidator)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDescriptorRelationshipExtractor)
            && descriptor.ImplementationType == typeof(McpToolRelationshipExtractor)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Name == "McpToolProjectionStartupValidator").Should().Be(1);
        services.Single(descriptor => descriptor.ServiceType == typeof(IMcpToolInvoker))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Single(descriptor => descriptor.ServiceType == typeof(IMcpToolDiscoveryService))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Projection_services_pass_scope_validation_with_scoped_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICapabilityDispatcher>(_ => Mock.Of<ICapabilityDispatcher>());
        services.AddCrestMcpToolProjection();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>().Should().NotBeNull();
    }

    [Fact]
    public async Task Host_start_eagerly_builds_snapshot_and_propagates_configuration_failure()
    {
        var builder = Host.CreateApplicationBuilder();
        var tools = new Mock<IMcpToolRegistry>();
        var capabilities = new Mock<ICapabilityRegistry>();
        var schemas = new Mock<ISchemaRegistry>();
        tools.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        capabilities.As<IRegistryState>().SetupGet(registry => registry.State).Returns(RegistryState.Built);
        schemas.As<IRegistryState>().SetupGet(registry => registry.State).Returns(RegistryState.Built);
        builder.Services.AddSingleton(tools.Object);
        builder.Services.AddSingleton(capabilities.Object);
        builder.Services.AddSingleton(schemas.Object);
        builder.Services.AddSingleton(Mock.Of<ICanonicalHashComputer>());
        builder.Services.AddCrestMcpToolProjection();
        using var host = builder.Build();

        var action = async () => await host.StartAsync();

        var exception = await action.Should().ThrowAsync<McpToolConfigurationException>();
        exception.Which.Code.Should().Be("MCP114");
    }

    [Fact]
    public async Task Host_start_builds_mcp_registry_before_publishing_snapshot()
    {
        var builder = Host.CreateApplicationBuilder();
        var capabilities = new Mock<ICapabilityRegistry>();
        var schemas = new Mock<ISchemaRegistry>();
        capabilities.As<IRegistryState>().SetupGet(registry => registry.State).Returns(RegistryState.Built);
        schemas.As<IRegistryState>().SetupGet(registry => registry.State).Returns(RegistryState.Built);
        builder.Services.AddSingleton(capabilities.Object);
        builder.Services.AddSingleton(schemas.Object);
        builder.Services.AddSingleton(Mock.Of<ICanonicalHashComputer>());
        builder.Services.AddCrestMcpToolProjection(options =>
            options.SerializerOptions.TypeInfoResolver = McpTestJsonContext.Default);
        using var host = builder.Build();

        await host.StartAsync();

        schemas.Verify(registry => registry.Build(
            It.IsAny<IEnumerable<IDescriptorProvider<SchemaDescriptor>>>()), Times.Once);
        capabilities.Verify(registry => registry.Build(
            It.IsAny<IEnumerable<IDescriptorProvider<CapabilityDescriptor>>>()), Times.Once);
        host.Services.GetRequiredService<McpToolRegistry>().State.Should().Be(RegistryState.Built);
    }
}
