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
    }

    [Fact]
    public async Task Host_start_eagerly_builds_snapshot_and_propagates_configuration_failure()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(Mock.Of<IMcpToolRegistry>());
        builder.Services.AddSingleton(Mock.Of<ICapabilityRegistry>());
        builder.Services.AddSingleton(Mock.Of<ISchemaRegistry>());
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
