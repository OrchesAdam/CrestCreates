using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests;

public sealed class AgentToolProjectionStartupBuilderTests
{
    [Fact]
    public async Task BuildAndPublish_IsConcurrentAndRepeatCallIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var capability = AgentToolRuntimeTestFixture.Capability("startup-capability-" + suffix);
        var tool = AgentToolRuntimeTestFixture.Tool(
            "startup-tool-" + suffix,
            capability.Id,
            "startup." + suffix);
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        DescriptorProviderRegistry.Register(
            new TestDescriptorProvider<CapabilityDescriptor>(capability));
        DescriptorProviderRegistry.Register(
            new TestDescriptorProvider<AgentCapabilityToolDescriptor>(tool));

        var schemas = new SchemaRegistry(
            new RegistryValidationEngine<SchemaDescriptor>(
                Array.Empty<IRegistryValidator<SchemaDescriptor>>()));
        var capabilities = new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        var tools = new AgentToolRegistry(
            new RegistryValidationEngine<AgentCapabilityToolDescriptor>(
                new[] { new AgentToolDescriptorValidator() }));
        var provider = new AgentToolRuntimeSnapshotProvider();
        var snapshotBuilder = AgentToolRuntimeTestFixture.SnapshotBuilder(
            tools,
            capabilities,
            schemas);
        var startup = new AgentToolProjectionStartupBuilder(
            schemas,
            capabilities,
            tools,
            snapshotBuilder,
            provider);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(startup.BuildAndPublish)));

        results.Should().OnlyContain(snapshot => ReferenceEquals(snapshot, results[0]));
        startup.BuildAndPublish().Should().BeSameAs(results[0]);
        provider.GetRequired().Should().BeSameAs(results[0]);
    }
}
