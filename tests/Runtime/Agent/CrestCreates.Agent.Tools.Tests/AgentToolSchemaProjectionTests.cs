using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests;

public sealed class AgentToolSchemaProjectionTests
{
    [Fact]
    public void Projector_IsAThinParityPreservingAdapterOverSharedKernel()
    {
        var schema = AgentToolRuntimeTestFixture.Schema("schema");
        var shared = new SchemaJsonContractProjector();
        var agent = new AgentToolJsonSchemaProjector(shared);

        var sharedJson = shared.ProjectObject(schema).GetRawText();
        var agentJson = agent.ProjectInput(schema).GetRawText();

        agentJson.Should().Be(sharedJson);
    }

    [Fact]
    public void ParityAdapter_PreservesSuccessfulDirectionalValidation()
    {
        var schema = AgentToolRuntimeTestFixture.Schema("schema");
        var typeInfo = AgentToolTestJsonContext.Default.GetTypeInfo(typeof(TestDto));
        typeInfo.Should().NotBeNull();
        var adapter = new AgentToolSchemaParityValidator(
            new SchemaJsonTypeInfoParityValidator());

        var action = () => adapter.ValidateInput(schema, typeInfo!);

        action.Should().NotThrow();
    }

    [Fact]
    public void ParityAdapter_MapsNonObjectRootToAtp114()
    {
        var schema = AgentToolRuntimeTestFixture.Schema("schema");
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = AgentToolPrimitiveJsonContext.Default
        };
        var typeInfo = options.GetTypeInfo(typeof(int[]));
        typeInfo.Kind.Should().Be(JsonTypeInfoKind.Enumerable);
        var adapter = new AgentToolSchemaParityValidator(
            new SchemaJsonTypeInfoParityValidator());

        var action = () => adapter.ValidateInput(schema, typeInfo);

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.JsonRootNotObject);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(int[]))]
internal partial class AgentToolPrimitiveJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
