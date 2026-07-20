using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.Tests;

public sealed class AgentMemoryToolStartupTests
{
    [Fact]
    public void EachHostGetsAnIsolatedSelectedCapabilityProvider()
    {
        var first = new ServiceCollection();
        first.AddCapabilityRuntime();
        first.AddAgentMemoryTools();
        using var firstProvider = first.BuildServiceProvider();

        var second = new ServiceCollection();
        second.AddCapabilityRuntime();
        second.AddAgentMemoryTools();
        using var secondProvider = second.BuildServiceProvider();

        var firstResolver = firstProvider.GetRequiredService<ICapabilityHandlerResolver>();
        var secondResolver = secondProvider.GetRequiredService<ICapabilityHandlerResolver>();
        firstResolver.Should().NotBeSameAs(secondResolver);
        firstResolver.Resolve(AgentMemoryToolCapabilityIds.BuildPack).Should().NotBeNull();
        secondResolver.Resolve(AgentMemoryToolCapabilityIds.BuildPack).Should().NotBeNull();
    }

    [Fact]
    public async Task MemoryToolModuleBuildsSevenEntriesThroughTheSharedStartupPath()
    {
        var builder = Host.CreateApplicationBuilder();
        var schemas = new SchemaRegistry(
            new RegistryValidationEngine<SchemaDescriptor>(Array.Empty<IRegistryValidator<SchemaDescriptor>>()));
        schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddAgentMemoryRuntime();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestAgentTools();
        builder.Services.AddAgentMemoryTools();
        builder.Services.AddSingleton<IAgentToolInvocationGate, DevelopmentInMemoryAgentToolInvocationGate>();
        builder.Services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
            (IAgentToolInvocationLeaseAbandoner)sp.GetRequiredService<IAgentToolInvocationGate>());
        builder.Services.AddSingleton<IAgentToolBudgetGate, DevelopmentInMemoryAgentToolBudgetGate>();
        builder.Services.AddSingleton<IAgentToolGovernanceAuditor, DevelopmentInMemoryAgentToolGovernanceAuditor>();

        using var host = builder.Build();
        await host.StartAsync();
        var snapshot = host.Services.GetRequiredService<AgentToolRuntimeSnapshotProvider>().GetRequired();

        snapshot.Entries.Should().HaveCount(7);
        snapshot.Entries.Values.Select(entry => entry.Descriptor.ToolName)
            .Should().BeEquivalentTo(
                new[]
                {
                    AgentMemoryToolCapabilityIds.BuildPack,
                    AgentMemoryToolCapabilityIds.ExpandSource,
                    AgentMemoryToolCapabilityIds.CompressHistory,
                    AgentMemoryToolCapabilityIds.ExtractCandidates,
                    AgentMemoryToolCapabilityIds.PromoteCandidate,
                    AgentMemoryToolCapabilityIds.RejectCandidate,
                    AgentMemoryToolCapabilityIds.SupersedeItem
                },
                options => options.WithStrictOrdering());

        await host.StopAsync();
    }
}
