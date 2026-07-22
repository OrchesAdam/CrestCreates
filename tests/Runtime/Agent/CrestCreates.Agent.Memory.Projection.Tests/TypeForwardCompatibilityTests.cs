using System.Text.Json;
using CrestCreates.Agent.Memory.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests;

/// <summary>
/// Verifies that TypeForwarded types are accessible from their original namespace
/// and that their public shape is unchanged.
/// </summary>
public class TypeForwardCompatibilityTests
{
    [Fact]
    public void AgentMemoryToolOperationStatus_ForwardedAccessible()
    {
        var status = AgentMemoryToolOperationStatus.Completed;
        status.Should().Be(AgentMemoryToolOperationStatus.Completed);
        ((int)status).Should().Be(1);
    }

    [Fact]
    public void AgentMemoryToolKind_ForwardedAccessible()
    {
        var kind = AgentMemoryToolKind.Preference;
        kind.Should().Be(AgentMemoryToolKind.Preference);
        ((int)kind).Should().Be(1);
    }

    [Fact]
    public void BuildAgentMemoryPackInput_ForwardedAccessible()
    {
        var input = new BuildAgentMemoryPackInput
        {
            MaximumCount = 10,
            CharacterBudget = 5000
        };
        input.MaximumCount.Should().Be(10);
        input.CharacterBudget.Should().Be(5000);
    }

    [Fact]
    public void ExpandAgentMemorySourceInput_ForwardedAccessible()
    {
        var input = new ExpandAgentMemorySourceInput
        {
            GrantId = "g1",
            MaximumCharacters = 1000
        };
        input.GrantId.Should().Be("g1");
        input.MaximumCharacters.Should().Be(1000);
    }

    [Fact]
    public void AgentMemoryToolCanonicalHashDto_ForwardedAccessible()
    {
        var dto = new AgentMemoryToolCanonicalHashDto
        {
            Value = "abc",
            AlgorithmVersion = "v1",
            ContractVersion = "v1",
            CanonicalShapeVersion = "v1"
        };
        dto.Value.Should().Be("abc");
    }

    [Fact]
    public void EnumConverter_ForwardedAccessible()
    {
        // Verify the converter base class is accessible
        var converter = new AgentMemoryToolOperationStatusJsonConverter();
        converter.Should().NotBeNull();
    }

    [Fact]
    public void AgentMemoryResourceKind_ForwardedAccessible()
    {
        var kind = AgentMemoryResourceKind.Context;
        kind.Should().Be(AgentMemoryResourceKind.Context);
        ((int)kind).Should().Be(1);
    }

    [Fact]
    public void AgentMemorySecurityArtifactState_ForwardedAccessible()
    {
        var state = AgentMemorySecurityArtifactState.Active;
        state.Should().Be(AgentMemorySecurityArtifactState.Active);
        ((int)state).Should().Be(1);
    }

    // ----- Runtime TypeForward verification tests -----

    [Fact]
    public void ForwardedType_ResolvesFromNewAssembly()
    {
        // Type.GetType with assembly-qualified name should resolve to Projection.Abstractions
        var type = Type.GetType("CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, CrestCreates.Agent.Memory.Tools.Abstractions");
        type.Should().NotBeNull("TypeForward should make the type resolvable from the old assembly name");
        type!.Assembly.GetName().Name.Should().Be("CrestCreates.Agent.Memory.Projection.Abstractions",
            "TypeForward should redirect to the new assembly");
    }

    [Fact]
    public void ForwardedType_AllEnumValuesPreserved()
    {
        var type = Type.GetType("CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, CrestCreates.Agent.Memory.Tools.Abstractions");
        var values = Enum.GetValues(type!);
        values.Length.Should().Be(6); // Unknown, Completed, Unavailable, Conflict, Redacted, NotExpandable
        var names = Enum.GetNames(type!);
        names.Should().Contain(new[] { "Unknown", "Completed", "Unavailable", "Conflict", "Redacted", "NotExpandable" });
    }

    [Fact]
    public void ForwardedDto_CanBeSerializedAndDeserialized()
    {
        var input = new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 5000 };
        var json = JsonSerializer.Serialize(input);
        var deserialized = JsonSerializer.Deserialize<BuildAgentMemoryPackInput>(json);
        deserialized.Should().NotBeNull();
        deserialized!.MaximumCount.Should().Be(10);
        deserialized.CharacterBudget.Should().Be(5000);
    }

    [Fact]
    public void ForwardedEnumConverter_WorksInJsonSerialization()
    {
        var status = AgentMemoryToolOperationStatus.Completed;
        var json = JsonSerializer.Serialize(status);
        json.Should().Be("\"completed\"");
        var deserialized = JsonSerializer.Deserialize<AgentMemoryToolOperationStatus>(json);
        deserialized.Should().Be(AgentMemoryToolOperationStatus.Completed);
    }

    [Fact]
    public void AllForwardedTypes_ResolveCorrectly()
    {
        // Exact list from TypeForwards.cs — all 26 TypeForwardedTo declarations
        var forwardedTypes = new[]
        {
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolMemoryStatus",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolConfidence",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSourceKind",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverity",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolCanonicalHashDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemorySourceGrantDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolItemDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolBlockDto",
            "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackInput",
            "CrestCreates.Agent.Memory.Tools.ExpandAgentMemorySourceInput",
            "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackResult",
            "CrestCreates.Agent.Memory.Tools.ExpandAgentMemorySourceResult",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolEnumConverter`1",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolMemoryStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKindJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolConfidenceJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSourceKindJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverityJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryResourceKind",
            "CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactState",
            "CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactKind",
            "CrestCreates.Agent.Memory.Tools.PreparedArtifactDisposition",
        };

        foreach (var typeName in forwardedTypes)
        {
            var type = Type.GetType($"{typeName}, CrestCreates.Agent.Memory.Tools.Abstractions");
            type.Should().NotBeNull($"TypeForward should make {typeName} resolvable from old assembly name");
            type!.Assembly.GetName().Name.Should().Be("CrestCreates.Agent.Memory.Projection.Abstractions",
                $"{typeName} should resolve to Projection.Abstractions via TypeForward");
        }
    }
}
