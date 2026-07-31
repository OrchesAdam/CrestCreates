using CrestCreates.Runtime.Persistence.Abstractions.State;
using FluentAssertions;
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
}
