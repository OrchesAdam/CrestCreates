using System.Text.Json;
using CrestCreates.Agent.Memory.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.Tests;

public sealed class AgentMemoryToolContractTests
{
    [Fact]
    public void ToolEnumsUseStableLowercaseWireValues()
    {
        var json = JsonSerializer.Serialize(
            new BuildAgentMemoryPackResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                IsAuthoritative = false
            },
            AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult);

        json.Should().Contain("\"OperationStatus\":\"completed\"");
        json.Should().Contain("\"IsAuthoritative\":false");
        json.Should().NotContain("\"OperationStatus\":0");
    }

    [Fact]
    public void UnknownConfidenceHasAnExplicitWireValueDistinctFromUnknownZero()
    {
        var json = JsonSerializer.Serialize(
            AgentMemoryToolConfidence.Unspecified,
            AgentMemoryToolJsonSerializerContext.Default.AgentMemoryToolConfidence);

        json.Should().Be("\"unknown\"");
    }

    [Fact]
    public void IntegerEnumInputIsRejected()
    {
        var action = () => JsonSerializer.Deserialize(
            "1",
            AgentMemoryToolJsonSerializerContext.Default.AgentMemoryToolOperationStatus);

        action.Should().Throw<JsonException>();
    }
}
