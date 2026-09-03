using System.Text.Json;
using CrestCreates.Agent.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

public sealed class AgentToolPreDispatchFinalizerSemanticEqualityTests
{
    [Fact]
    public void Object_property_reordering_is_equivalent()
    {
        Equivalent("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}")
            .Should().BeTrue();
    }

    [Fact]
    public void Structured_value_change_is_not_equivalent()
    {
        Equivalent("{\"a\":1}", "{\"a\":2}")
            .Should().BeFalse();
    }

    [Fact]
    public void Array_reordering_is_not_equivalent()
    {
        Equivalent("[1,2]", "[2,1]")
            .Should().BeFalse();
    }

    private static bool Equivalent(string leftJson, string rightJson)
    {
        using var leftDocument = JsonDocument.Parse(leftJson);
        using var rightDocument = JsonDocument.Parse(rightJson);
        var left = Outcome(leftDocument.RootElement.Clone());
        var right = Outcome(rightDocument.RootElement.Clone());
        return AgentToolPreDispatchFinalizer.EquivalentOutcome(left, right);
    }

    private static AgentToolInvocationOutcome Outcome(JsonElement output)
        => new()
        {
            Kind = AgentToolInvocationOutcomeKind.Succeeded,
            Code = "ok",
            Message = "ok",
            StructuredOutput = output
        };
}
