using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Json;
using CrestCreates.Core.Abstractions.Identity;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class ContractTests
{
    [Fact]
    public void AgentMemoryConfidence_IsClosedEnum_NotFloatingPoint()
    {
        typeof(AgentMemoryConfidence).IsEnum.Should().BeTrue();
        typeof(AgentMemoryItem).GetProperty(nameof(AgentMemoryItem.Confidence))!
            .PropertyType.Should().Be(typeof(AgentMemoryConfidence));
    }

    [Fact]
    public void AgentContextEvidenceRef_IsNotNamedActivationEvidence()
    {
        typeof(AgentContextEvidenceRef).Name.Should().Be("AgentContextEvidenceRef");
        typeof(AgentContextEvidenceRef).Assembly.GetTypes()
            .Select(type => type.Name)
            .Should()
            .NotContain("AgentEvidenceRef");
    }

    [Fact]
    public void JsonContext_ContainsAgentMemoryPack()
    {
        JsonTypeInfo<AgentMemoryPack> typeInfo = AgentMemoryJsonSerializerContext.Default.AgentMemoryPack;
        typeInfo.Type.Should().Be(typeof(AgentMemoryPack));
    }

    [Fact]
    public void AgentMemoryDiagnostic_JsonSerializesDiagnosticCodeAsString()
    {
        var diagnostic = new AgentMemoryDiagnostic
        {
            Code = AgentMemoryDiagnosticCodes.ContentRejected,
            Message = "Rejected",
            Severity = SeverityLevel.Warning
        };

        var json = JsonSerializer.Serialize(
            diagnostic,
            AgentMemoryJsonSerializerContext.Default.AgentMemoryDiagnostic);

        json.Should().Contain("\"code\":\"AGENT_MEMORY_CONTENT_REJECTED\"");
        json.Should().NotContain("\"value\"");
        json.Should().NotContain("\"isEmpty\"");
    }

    [Fact]
    public void AgentMemoryDiagnostic_JsonDeserializesDiagnosticCodeFromString()
    {
        const string json = """
            {
              "code": "AGENT_MEMORY_CONTENT_REJECTED",
              "message": "Rejected",
              "severity": "Warning"
            }
            """;

        var diagnostic = JsonSerializer.Deserialize(
            json,
            AgentMemoryJsonSerializerContext.Default.AgentMemoryDiagnostic);

        diagnostic.Should().NotBeNull();
        diagnostic!.Code.Should().Be(AgentMemoryDiagnosticCodes.ContentRejected);
    }

    [Fact]
    public void Contracts_DoNotExposeMutableCollectionTypes()
    {
        var mutableProperties = typeof(AgentMemoryPack).Assembly.GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(property => property.PropertyType.IsGenericType)
            .Where(property => property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();

        mutableProperties.Should().BeEmpty();
    }
}
