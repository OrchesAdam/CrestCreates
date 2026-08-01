using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.State;
using CrestCreates.Runtime.Persistence.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.Tests.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.State;

public sealed class RuntimeStateContractRegistryTests
{
    [Fact]
    public void RuntimeStateContractRegistry_Should_RejectDuplicateTypeId()
    {
        var builder = new RuntimeStateContractBuilder();
        var roots = TestRuntimeStateJsonSerializerContext.TestRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes;
        builder.Add("duplicate", TestRuntimeStateJsonSerializerContext.Default.MutableNestedRuntimeState, roots);
        builder.Add("duplicate", TestRuntimeStateJsonSerializerContext.Default.MutableNestedRuntimeState, roots);

        var act = () => builder.Build();

        act.Should().Throw<RuntimeStateContractException>();
    }

    [Fact]
    public void RuntimeStateContractRegistry_Should_RequireGeneratedRootManifest()
    {
        var builder = new RuntimeStateContractBuilder();

        var act = () => builder.Add(
            "missing-root",
            TestRuntimeStateJsonSerializerContext.Default.MutableNestedRuntimeState,
            new HashSet<Type>());

        act.Should().Throw<RuntimeStateContractException>();
    }

    [Fact]
    public void RegisteredStatePayload_ShouldRoundTripWithExactClrType()
    {
        var registry = CreateRegistry();
        var value = new MutableNestedRuntimeState { Name = "one", Values = ["a"] };

        var envelope = registry.Capture(value);
        var restored = registry.Restore<MutableNestedRuntimeState>(envelope);

        envelope.TypeId.Should().Be("test/runtime/mutable-state/v1");
        restored.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void RegisteredStatePayload_ShouldPreserveStableTypeIdAcrossClrRename()
    {
        var registry = CreateRegistry();
        var envelope = registry.Capture(new MutableNestedRuntimeState { Name = "stable" });

        envelope.TypeId.Should().Be("test/runtime/mutable-state/v1");
    }

    [Fact]
    public void UnregisteredStatePayload_ShouldFailBeforeTransaction()
    {
        var registry = CreateRegistry();

        var act = () => registry.Capture(new UnregisteredState());

        act.Should().Throw<RuntimeStateContractException>();
    }

    [Fact]
    public void UntypedNullStatePayload_ShouldFailBeforeTransaction()
    {
        var registry = CreateRegistry();

        var act = () => registry.Capture((object?)null);

        act.Should().Throw<RuntimeStateContractException>();
    }

    [Fact]
    public void TypedNullStatePayload_ShouldRoundTripWithTypeId()
    {
        var registry = CreateRegistry();

        var envelope = registry.Capture<MutableNestedRuntimeState>(null!);
        var restored = registry.Restore<MutableNestedRuntimeState>(envelope);

        envelope.TypeId.Should().Be("test/runtime/mutable-state/v1");
        restored.Should().BeNull();
    }

    [Fact]
    public void Snapshot_Should_DeepCopyRegisteredStatePayload()
    {
        var registry = CreateRegistry();
        var state = new MutableNestedRuntimeState { Name = "before", Values = ["original"] };
        var envelope = registry.Capture(state);

        state.Values[0] = "mutated";
        var restored = registry.Restore<MutableNestedRuntimeState>(envelope);

        restored!.Values.Should().ContainSingle().Which.Should().Be("original");
    }

    [Fact]
    public void OversizedStatePayload_ShouldFailBeforeSql()
    {
        var registry = CreateRegistry();
        var state = new MutableNestedRuntimeState
        {
            Name = new string('x', RuntimeStateLimits.MaxJsonPayloadCharacters + 1)
        };

        var act = () => registry.Capture(state);

        act.Should().Throw<RuntimeStateContractException>();
    }

    private static RuntimeStateContractRegistry CreateRegistry()
    {
        var builder = new RuntimeStateContractBuilder();
        new TestRuntimeStateContractContributor().Contribute(builder);
        return builder.Build();
    }

    private sealed class UnregisteredState
    {
    }
}
