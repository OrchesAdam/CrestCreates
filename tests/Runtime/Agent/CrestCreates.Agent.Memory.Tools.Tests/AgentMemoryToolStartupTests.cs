using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;
using CrestCreates.Accountability.Bootstrap;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.Tests;

public sealed class AgentMemoryToolStartupTests
{
    [Fact]
    public void EachHostGetsAnIsolatedSelectedCapabilityProvider()
    {
        var first = new ServiceCollection();
        first.AddCapabilityRuntime();
        first.AddAccountability();
        first.AddAgentMemoryTools();
        using var firstProvider = first.BuildServiceProvider();

        var second = new ServiceCollection();
        second.AddCapabilityRuntime();
        second.AddAccountability();
        second.AddAgentMemoryTools();
        using var secondProvider = second.BuildServiceProvider();

        var firstResolver = firstProvider.GetRequiredService<ICapabilityHandlerResolver>();
        var secondResolver = secondProvider.GetRequiredService<ICapabilityHandlerResolver>();
        firstResolver.Should().NotBeSameAs(secondResolver);
        firstResolver.Resolve(AgentMemoryToolCapabilityIds.BuildPack).Should().NotBeNull();
        secondResolver.Resolve(AgentMemoryToolCapabilityIds.BuildPack).Should().NotBeNull();
    }

    /// <summary>
    /// The formal-curation marker is what gates the composition validator, not the
    /// Accountability producer. Even with the null producer registered by
    /// AddAgentMemoryRuntime, a store that is only IAgentMemoryStore must fail
    /// closed at startup.
    /// </summary>
    [Fact]
    public async Task Startup_FailsClosed_WhenStoreIsReadOnlyEvenWithNullProducer()
    {
        var builder = Host.CreateApplicationBuilder();
        var schemas = new SchemaRegistry(
            new RegistryValidationEngine<SchemaDescriptor>(Array.Empty<IRegistryValidator<SchemaDescriptor>>()));
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddAgentMemoryRuntime();
        // Override the InMemory store with a read-only store that is not conditional.
        builder.Services.RemoveAll<IAgentMemoryStore>();
        builder.Services.AddSingleton<IAgentMemoryStore>(new Mock<IAgentMemoryStore>().Object);
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddAccountability();
        builder.Services.AddCrestAgentTools();
        builder.Services.AddAgentMemoryTools();
        schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        builder.Services.AddSingleton<IAgentToolInvocationGate, DevelopmentInMemoryAgentToolInvocationGate>();
        builder.Services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
            (IAgentToolInvocationLeaseAbandoner)sp.GetRequiredService<IAgentToolInvocationGate>());
        builder.Services.AddSingleton<IAgentToolBudgetGate, DevelopmentInMemoryAgentToolBudgetGate>();
        builder.Services.AddSingleton<IAgentToolGovernanceAuditor, DevelopmentInMemoryAgentToolGovernanceAuditor>();

        using var host = builder.Build();
        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*" + AgentMemoryDiagnosticCodes.CurationCompositionInvalid + "*");
    }

    /// <summary>
    /// A store that implements the conditional interface but advertises Unknown
    /// outcome semantics is still a partial provider: it must not pass startup.
    /// </summary>
    [Fact]
    public async Task Startup_FailsClosed_WhenConditionalStoreAdvertisesUnknownGuarantee()
    {
        var builder = Host.CreateApplicationBuilder();
        var schemas = new SchemaRegistry(
            new RegistryValidationEngine<SchemaDescriptor>(Array.Empty<IRegistryValidator<SchemaDescriptor>>()));
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddAgentMemoryRuntime();
        // A store that is IAgentMemoryStore + IAgentMemoryConditionalCurationStore
        // but advertises Unknown (partial conditional provider).
        var store = new Mock<IAgentMemoryStore>();
        store.As<IAgentMemoryConditionalCurationStore>();
        store.As<IAgentMemoryStoreCapabilities>()
            .Setup(c => c.CurationOutcomeGuarantee)
            .Returns(AgentMemoryCurationOutcomeGuarantee.Unknown);
        builder.Services.RemoveAll<IAgentMemoryStore>();
        builder.Services.AddSingleton<IAgentMemoryStore>(store.Object);
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddAccountability();
        builder.Services.AddCrestAgentTools();
        builder.Services.AddAgentMemoryTools();
        schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        builder.Services.AddSingleton<IAgentToolInvocationGate, DevelopmentInMemoryAgentToolInvocationGate>();
        builder.Services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
            (IAgentToolInvocationLeaseAbandoner)sp.GetRequiredService<IAgentToolInvocationGate>());
        builder.Services.AddSingleton<IAgentToolBudgetGate, DevelopmentInMemoryAgentToolBudgetGate>();
        builder.Services.AddSingleton<IAgentToolGovernanceAuditor, DevelopmentInMemoryAgentToolGovernanceAuditor>();

        using var host = builder.Build();
        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*" + AgentMemoryDiagnosticCodes.CurationCompositionInvalid + "*");
    }

    [Fact]
    public async Task MemoryToolModuleBuildsSevenEntriesThroughTheSharedStartupPath()
    {
        var builder = Host.CreateApplicationBuilder();
        var schemas = new SchemaRegistry(
            new RegistryValidationEngine<SchemaDescriptor>(Array.Empty<IRegistryValidator<SchemaDescriptor>>()));
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddAgentMemoryRuntime();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddAccountability();
        builder.Services.AddCrestAgentTools();
        builder.Services.AddAgentMemoryTools();
        schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
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
