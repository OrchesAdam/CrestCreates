using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Bootstrap;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

/// <summary>
/// Registration split proofs: AddAgentMemoryReadRuntime owns the read side
/// (stores, identity, producer, hashing) and AddAgentMemoryCuration owns the
/// promotion service plus a fail-closed composition validator gated on the
/// formal curation marker. The aggregate AddAgentMemoryRuntime composes both.
/// </summary>
public sealed class AgentMemoryRuntimeRegistrationTests
{
    private const string TenantId = "tenant-1";

    /// <summary>
    /// Registers a minimal <see cref="ICanonicalHashComputer"/> so hash-dependent
    /// services (projector, promotion) can be resolved in DI proof tests.
    /// </summary>
    private static void RegisterMinimalHashComputer(IServiceCollection services)
    {
        var hashComputer = new Mock<ICanonicalHashComputer>();
        hashComputer
            .Setup(h => h.ComputeFromProjection(It.IsAny<CanonicalHashProjectionResult>()))
            .Returns((CanonicalHashProjectionResult p) => new CanonicalHash
            {
                Value = "test-hash",
                Algorithm = "SHA-256",
                AlgorithmVersion = p.Metadata.AlgorithmVersion,
                ArtifactKind = p.Metadata.ArtifactKind,
                Scope = p.Metadata.Scope,
                Purpose = p.Metadata.Purpose,
                ContractVersion = p.Metadata.ContractVersion,
                CanonicalShapeVersion = p.Metadata.CanonicalShapeVersion
            });
        services.AddSingleton(hashComputer.Object);
    }

    [Fact]
    public void ReadRuntime_Should_ResolveIdentityFactoryAndNullProducer()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryReadRuntime();

        using var provider = services.BuildServiceProvider();
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var factory = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        producer.Should().BeOfType<NullAgentMemoryAccountabilityProducer>();
        factory.Should().BeOfType<DefaultAgentMemoryOperationIdentityFactory>();

        provider.GetRequiredService<IAgentMemoryAccountabilityProducer>().Should().BeSameAs(producer);
        provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>().Should().BeSameAs(factory);
    }

    [Fact]
    public void ReadRuntime_Should_NotResolveFormalCurationMarker()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryReadRuntime();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IAgentMemoryFormalCurationMarker>().Should().BeNull();
    }

    [Fact]
    public void Curation_RegistersMarkerAndValidator()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryReadRuntime();
        RegisterMinimalHashComputer(services);
        services.AddAgentMemoryCuration();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAgentMemoryFormalCurationMarker>().Should().NotBeNull();

        var validator = provider.GetRequiredService<IBootstrapValidator>()
            .Should().BeOfType<AgentMemoryCurationCompositionValidator>().Subject;
        provider.GetRequiredService<IHostedService>()
            .Should().BeOfType<AgentMemoryCurationCompositionValidator>()
            .And.BeSameAs(validator);
    }

    [Fact]
    public void Aggregate_ComposesReadAndCuration()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryRuntime();
        RegisterMinimalHashComputer(services);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAgentMemoryFormalCurationMarker>().Should().NotBeNull();
        provider.GetRequiredService<IBootstrapValidator>()
            .Should().BeOfType<AgentMemoryCurationCompositionValidator>();
        provider.GetRequiredService<IAgentMemoryPromotionService>().Should().NotBeNull();
    }

    [Fact]
    public async Task Validator_FailsClosed_OnNonConditionalStore()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryReadRuntime();
        services.AddSingleton<IAgentMemoryStore>(new Mock<IAgentMemoryStore>().Object);
        services.AddAgentMemoryCuration();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<AgentMemoryCurationCompositionValidator>();
        validator.Validate().HasErrors.Should().BeTrue();

        var hosted = (IHostedService)validator;
        var act = async () => await hosted.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validator_Passes_OnConditionalStore()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryRuntime();
        RegisterMinimalHashComputer(services);

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<AgentMemoryCurationCompositionValidator>();
        validator.Validate().HasErrors.Should().BeFalse();

        var hosted = (IHostedService)validator;
        await hosted.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancelledBeforeTransition_Should_NotCommit()
    {
        var (store, promotion) = MemoryTestFixture.CreateCurationFixture();
        var hashes = MemoryTestFixture.CreateTestHashProjector();
        var memory = await MemoryTestFixture.PromoteActiveMemoryAsync(store, promotion, hashes, TenantId, "c-1", "m-1");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await promotion.ArchiveAsync(
            TenantId, memory.MemoryId, MemoryTestFixture.CreateOperationRequest(TenantId), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.GetMemoryAsync(TenantId, memory.MemoryId))!.Status.Should().Be(AgentMemoryStatus.Active);
    }
}
