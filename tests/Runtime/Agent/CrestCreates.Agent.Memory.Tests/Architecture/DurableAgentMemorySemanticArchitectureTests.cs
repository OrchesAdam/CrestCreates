using System.Linq;
using System.Reflection;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Curation;
using CrestCreates.Agent.Memory.Abstractions.Persistence;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Curation;
using CrestCreates.Agent.Memory.Persistence;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests.Architecture;

/// <summary>
/// Locks the shared semantic surface: one hash projector, one curation
/// projector/state machine, one exact comparer, no copied projection logic in
/// the Promotion Service or Stores, and no Accountability dependency in the
/// InMemory Store.
/// </summary>
public sealed class DurableAgentMemorySemanticArchitectureTests
{
    [Fact]
    public void CanonicalHashProjector_Should_ImplementStateHashInterface()
    {
        typeof(AgentMemoryCanonicalHashProjector)
            .GetInterfaces()
            .Should().Contain(typeof(IAgentMemoryStateHashProjector));
    }

    [Fact]
    public void PromotionService_Should_Not_DeclarePrivatePromotedMemoryProjection()
    {
        var projection = typeof(DefaultAgentMemoryPromotionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name.Contains("CreatePromotedMemory", StringComparison.Ordinal));

        projection.Should().BeNull("Promotion Service must consume IAgentMemoryCurationProjector, not copy projection logic.");
    }

    [Fact]
    public void InMemoryStore_Should_Not_DeclarePrivateProjectionOrEquivalentPayloadCopies()
    {
        var methods = typeof(InMemoryAgentMemoryStore)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        methods.Should().NotContain(name => name.Contains("CreatePromotedMemory", StringComparison.Ordinal));
        methods.Should().NotContain(name => name.Contains("EquivalentMemoryPayload", StringComparison.Ordinal));
    }

    [Fact]
    public void BothStores_Should_ConsumeSharedStateMachineAndComparerSurfaces()
    {
        var inMemoryConstructor = typeof(InMemoryAgentMemoryStore)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        inMemoryConstructor.Should().Contain(typeof(IAgentMemoryStateHashProjector));
        inMemoryConstructor.Should().Contain(typeof(IAgentMemoryCurationStateMachine));
        inMemoryConstructor.Should().Contain(typeof(IAgentMemoryPersistenceComparer));
    }

    [Fact]
    public void StateMachine_Should_ConsumeBothSharedProjectors()
    {
        var constructor = typeof(DefaultAgentMemoryCurationStateMachine)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructor.Should().Contain(typeof(IAgentMemoryStateHashProjector));
        constructor.Should().Contain(typeof(IAgentMemoryCurationProjector));
    }

    [Fact]
    public void InMemoryStore_Should_Not_ReferenceAccountabilityTypes()
    {
        var constructorTypes = typeof(InMemoryAgentMemoryStore)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName ?? string.Empty)
            .ToArray();

        constructorTypes.Should().NotContain(name => name.Contains("Accountability", StringComparison.Ordinal));
    }

    [Fact]
    public void InMemoryStore_CurationOutcomeGuarantee_Should_BeConfirmedAtomic()
    {
        var store = new InMemoryAgentMemoryStore();
        ((IAgentMemoryStoreCapabilities)store).CurationOutcomeGuarantee
            .Should().Be(AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic);
    }
}
