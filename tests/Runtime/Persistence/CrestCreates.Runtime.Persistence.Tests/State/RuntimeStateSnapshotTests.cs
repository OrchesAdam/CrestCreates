using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.State;

public sealed class RuntimeStateSnapshotTests
{
    [Fact]
    public void RuntimeStateBag_ShouldRoundTripOrdinallyWithoutObjectPayload()
    {
        var bag = new RuntimeStateBag(
        [
            new KeyValuePair<string, RuntimeStateValue>("z", new RuntimeStateValue { TypeId = "z", JsonPayload = "1" }),
            new KeyValuePair<string, RuntimeStateValue>("a", new RuntimeStateValue { TypeId = "a", JsonPayload = "2" })
        ]);

        bag.Values.Keys.Should().Equal("a", "z");
        bag.Values.Values.Should().OnlyContain(value => value is RuntimeStateValue);
    }

    [Fact]
    public void BuiltInRuntimeStateContract_ShouldRoundTripRuntimeStateBag()
    {
        using var provider = new ServiceCollection()
            .AddRuntimePersistence()
            .BuildServiceProvider();
        var registry = provider.GetRequiredService<IRuntimeStateContractRegistry>();
        var original = new RuntimeStateBag(
        [
            new KeyValuePair<string, RuntimeStateValue>(
                "approval",
                new RuntimeStateValue { TypeId = "crest.runtime/string/v1", JsonPayload = "\"pending\"" })
        ]);

        var restored = registry.Restore(registry.Capture(original)) as RuntimeStateBag;

        restored.Should().NotBeNull();
        restored!.Values.Should().ContainKey("approval");
        restored.Values["approval"].JsonPayload.Should().Be("\"pending\"");
    }
}
