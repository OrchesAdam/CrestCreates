using System.Reflection;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.TypeForward;

/// <summary>
/// Binary-compatibility tests for TypeForwarded types.
///
/// Verifies that:
/// <list type="bullet">
/// <item>Forwarded types resolve to the Projection.Abstractions assembly at runtime</item>
/// <item>TypeForwardedTo attributes exist in the old Tools.Abstractions assembly</item>
/// <item>Forwarded enum values are preserved</item>
/// <item>Forwarded DTO properties are preserved</item>
/// <item>Enum converter types remain accessible</item>
/// </list>
///
/// These are distinct from <see cref="TypeForwardCompatibilityTests"/>
/// which primarily test source compatibility (type instantiation and basic
/// member access from the original namespace).
/// </summary>
public class TypeForwardRuntimeCompatibilityTests
{
    // ----- Assembly resolution verification -----

    [Fact]
    public void Forwarded_types_resolve_to_Projection_Abstractions_assembly()
    {
        const string expectedAssemblyName = "CrestCreates.Agent.Memory.Projection.Abstractions";

        // BuildAgentMemoryPackInput is TypeForwarded: physically in Projection.Abstractions
        typeof(BuildAgentMemoryPackInput).Assembly.GetName().Name.Should().Be(expectedAssemblyName);

        // Enum types
        typeof(AgentMemoryToolOperationStatus).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolKind).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolConfidence).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolMemoryStatus).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolSourceKind).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolDiagnosticSeverity).Assembly.GetName().Name.Should().Be(expectedAssemblyName);

        // DTO types
        typeof(AgentMemoryToolCanonicalHashDto).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemorySourceGrantDto).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolDiagnosticDto).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolItemDto).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolBlockDto).Assembly.GetName().Name.Should().Be(expectedAssemblyName);

        // Contract types
        typeof(BuildAgentMemoryPackResult).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(ExpandAgentMemorySourceInput).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(ExpandAgentMemorySourceResult).Assembly.GetName().Name.Should().Be(expectedAssemblyName);

        // Converter types
        typeof(AgentMemoryToolEnumConverter<>).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemoryToolOperationStatusJsonConverter).Assembly.GetName().Name.Should().Be(expectedAssemblyName);

        // Security enums
        typeof(AgentMemoryResourceKind).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemorySecurityArtifactState).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(AgentMemorySecurityArtifactKind).Assembly.GetName().Name.Should().Be(expectedAssemblyName);
        typeof(PreparedArtifactDisposition).Assembly.GetName().Name.Should().Be(expectedAssemblyName);

        // Verify Assembly instances are identity-equal to the Projection.Abstractions assembly
        var projectionAssembly = typeof(AgentMemoryAccessPrincipal).Assembly;
        typeof(BuildAgentMemoryPackInput).Assembly.Should().BeSameAs(projectionAssembly);
    }

    [Fact]
    public void Forwarded_types_resolve_via_assembly_qualified_name_from_old_assembly()
    {
        // Binary compatibility: Type.GetType with the OLD assembly name should
        // resolve to the NEW assembly via TypeForwardedTo attributes.
        var type = Type.GetType(
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, CrestCreates.Agent.Memory.Tools.Abstractions");
        type.Should().NotBeNull("TypeForward should make the type resolvable from the old assembly name");
        type!.Assembly.GetName().Name.Should().Be("CrestCreates.Agent.Memory.Projection.Abstractions",
            "TypeForward should redirect to the new assembly");

        // Verify the DTO also resolves
        var blockType = Type.GetType(
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolBlockDto, CrestCreates.Agent.Memory.Tools.Abstractions");
        blockType.Should().NotBeNull();
        blockType!.Assembly.GetName().Name.Should().Be("CrestCreates.Agent.Memory.Projection.Abstractions");
    }

    // ----- TypeForwardedTo attribute verification -----

    [Fact]
    public void All_26_forwarded_types_resolve_from_old_assembly_name()
    {
        // TypeForwardedTo is a pseudo-custom attribute stored in assembly metadata
        // tables and not accessible via GetCustomAttributes<T>() or CustomAttributeData.
        //
        // The true binary compatibility test is: can a consumer compiled against the
        // OLD assembly still resolve types at runtime?
        //
        // This test verifies that ALL 26 forwarded types (matching TypeForwards.cs)
        // resolve from the old assembly name to the new Projection.Abstractions assembly.

        const string oldAssembly = "CrestCreates.Agent.Memory.Tools.Abstractions";
        const string newAssembly = "CrestCreates.Agent.Memory.Projection.Abstractions";

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
            var type = Type.GetType($"{typeName}, {oldAssembly}");
            type.Should().NotBeNull(
                $"TypeForward should make {typeName} resolvable from old assembly name '{oldAssembly}'");
            type!.Assembly.GetName().Name.Should().Be(newAssembly,
                $"{typeName} should resolve to '{newAssembly}' via TypeForward");
        }
    }

    // ----- Enum value preservation -----

    [Fact]
    public void Forwarded_enum_values_are_preserved()
    {
        // AgentMemoryToolOperationStatus
        ((int)AgentMemoryToolOperationStatus.Unknown).Should().Be(0);
        ((int)AgentMemoryToolOperationStatus.Completed).Should().Be(1);
        ((int)AgentMemoryToolOperationStatus.Unavailable).Should().Be(2);
        ((int)AgentMemoryToolOperationStatus.Conflict).Should().Be(3);
        ((int)AgentMemoryToolOperationStatus.Redacted).Should().Be(4);
        ((int)AgentMemoryToolOperationStatus.NotExpandable).Should().Be(5);

        // AgentMemoryToolKind
        ((int)AgentMemoryToolKind.Unknown).Should().Be(0);
        ((int)AgentMemoryToolKind.Preference).Should().Be(1);
        ((int)AgentMemoryToolKind.ProjectFact).Should().Be(2);
        ((int)AgentMemoryToolKind.Decision).Should().Be(3);
        ((int)AgentMemoryToolKind.Constraint).Should().Be(4);
        ((int)AgentMemoryToolKind.WorkflowHint).Should().Be(5);
        ((int)AgentMemoryToolKind.Risk).Should().Be(6);

        // AgentMemoryToolConfidence
        ((int)AgentMemoryToolConfidence.Unknown).Should().Be(0);
        ((int)AgentMemoryToolConfidence.Unspecified).Should().Be(1);
        ((int)AgentMemoryToolConfidence.Low).Should().Be(2);
        ((int)AgentMemoryToolConfidence.Medium).Should().Be(3);
        ((int)AgentMemoryToolConfidence.High).Should().Be(4);

        // AgentMemoryToolMemoryStatus
        ((int)AgentMemoryToolMemoryStatus.Unknown).Should().Be(0);
        ((int)AgentMemoryToolMemoryStatus.Active).Should().Be(1);
        ((int)AgentMemoryToolMemoryStatus.Superseded).Should().Be(2);
        ((int)AgentMemoryToolMemoryStatus.Archived).Should().Be(3);

        // AgentMemoryResourceKind
        ((int)AgentMemoryResourceKind.Unknown).Should().Be(0);
        ((int)AgentMemoryResourceKind.Context).Should().Be(1);
        ((int)AgentMemoryResourceKind.Candidate).Should().Be(2);
        ((int)AgentMemoryResourceKind.Memory).Should().Be(3);
        ((int)AgentMemoryResourceKind.ConversationHistory).Should().Be(4);
        ((int)AgentMemoryResourceKind.TaskHistory).Should().Be(5);
    }

    [Fact]
    public void Forwarded_enum_has_all_expected_members()
    {
        // Verify that all expected enum members exist (not just values)
        Enum.GetNames(typeof(AgentMemoryToolOperationStatus))
            .Should().Contain(["Unknown", "Completed", "Unavailable", "Conflict", "Redacted", "NotExpandable"]);

        Enum.GetNames(typeof(AgentMemoryToolKind))
            .Should().Contain(["Unknown", "Preference", "ProjectFact", "Decision", "Constraint", "WorkflowHint", "Risk"]);

        Enum.GetNames(typeof(AgentMemoryToolConfidence))
            .Should().Contain(["Unknown", "Unspecified", "Low", "Medium", "High"]);
    }

    // ----- DTO property preservation -----

    [Fact]
    public void Forwarded_DTO_properties_are_preserved()
    {
        var blockDtoType = typeof(AgentMemoryToolBlockDto);
        blockDtoType.GetProperty("Content").Should().NotBeNull();
        blockDtoType.GetProperty("CanonicalContentHash").Should().NotBeNull();
        blockDtoType.GetProperty("SourceGrants").Should().NotBeNull();

        var diagnosticDtoType = typeof(AgentMemoryToolDiagnosticDto);
        diagnosticDtoType.GetProperty("Code").Should().NotBeNull();
        diagnosticDtoType.GetProperty("Severity").Should().NotBeNull();

        var canonHashDtoType = typeof(AgentMemoryToolCanonicalHashDto);
        canonHashDtoType.GetProperty("Value").Should().NotBeNull();
        canonHashDtoType.GetProperty("AlgorithmVersion").Should().NotBeNull();
        canonHashDtoType.GetProperty("ContractVersion").Should().NotBeNull();
        canonHashDtoType.GetProperty("CanonicalShapeVersion").Should().NotBeNull();

        var buildInputType = typeof(BuildAgentMemoryPackInput);
        buildInputType.GetProperty("MemoryHandles").Should().NotBeNull();
        buildInputType.GetProperty("Kinds").Should().NotBeNull();
        buildInputType.GetProperty("Tags").Should().NotBeNull();
        buildInputType.GetProperty("MaximumCount").Should().NotBeNull();
        buildInputType.GetProperty("CharacterBudget").Should().NotBeNull();
        buildInputType.GetProperty("MinimumConfidence").Should().NotBeNull();

        var expandInputType = typeof(ExpandAgentMemorySourceInput);
        expandInputType.GetProperty("GrantId").Should().NotBeNull();
        expandInputType.GetProperty("MaximumCharacters").Should().NotBeNull();
    }

    [Fact]
    public void Forwarded_DTO_can_be_constructed_and_properties_set()
    {
        var block = new AgentMemoryToolBlockDto
        {
            Content = "test content",
            CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
            {
                Value = "abc123",
                AlgorithmVersion = "v1",
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1"
            },
            SourceGrants = []
        };
        block.Content.Should().Be("test content");
        block.CanonicalContentHash.Value.Should().Be("abc123");

        var diag = new AgentMemoryToolDiagnosticDto
        {
            Code = "ERR001",
            Severity = AgentMemoryToolDiagnosticSeverity.Error
        };
        diag.Code.Should().Be("ERR001");
        diag.Severity.Should().Be(AgentMemoryToolDiagnosticSeverity.Error);

        var buildResult = new BuildAgentMemoryPackResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            Items = [],
            ReturnedCount = 0,
            WasTruncated = false,
            IsAuthoritative = true,
            Diagnostics = []
        };
        buildResult.OperationStatus.Should().Be(AgentMemoryToolOperationStatus.Completed);
        buildResult.IsAuthoritative.Should().BeTrue();
    }

    // ----- Enum converter type accessibility -----

    [Fact]
    public void Enum_converter_base_class_is_accessible_and_abstract()
    {
        var converterBaseType = typeof(AgentMemoryToolEnumConverter<>);
        converterBaseType.Should().NotBeNull();
        converterBaseType.IsAbstract.Should().BeTrue();
        converterBaseType.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void Enum_converter_specific_types_are_accessible()
    {
        typeof(AgentMemoryToolOperationStatusJsonConverter).Should().NotBeNull();
        typeof(AgentMemoryToolMemoryStatusJsonConverter).Should().NotBeNull();
        typeof(AgentMemoryToolKindJsonConverter).Should().NotBeNull();
        typeof(AgentMemoryToolConfidenceJsonConverter).Should().NotBeNull();
        typeof(AgentMemoryToolSourceKindJsonConverter).Should().NotBeNull();
        typeof(AgentMemoryToolDiagnosticSeverityJsonConverter).Should().NotBeNull();
    }

    [Fact]
    public void Enum_converter_can_be_instantiated()
    {
        var converter = new AgentMemoryToolOperationStatusJsonConverter();
        converter.Should().NotBeNull();
        converter.Should().BeOfType<AgentMemoryToolOperationStatusJsonConverter>();
    }

    [Fact]
    public void Enum_converter_base_has_expected_generic_parameter()
    {
        var converterBaseType = typeof(AgentMemoryToolEnumConverter<>);
        converterBaseType.GetGenericArguments().Should().HaveCount(1);

        // The specific converter should inherit from the generic base
        typeof(AgentMemoryToolOperationStatusJsonConverter).BaseType
            .Should().NotBeNull();
        typeof(AgentMemoryToolOperationStatusJsonConverter).BaseType!
            .GetGenericTypeDefinition()
            .Should().Be(typeof(AgentMemoryToolEnumConverter<>));
    }
}
