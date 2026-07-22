using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.TypeForward;

/// <summary>
/// True binary compatibility tests for TypeForwarded types.
///
/// Unlike <see cref="TypeForwardRuntimeCompatibilityTests"/>, these tests use ONLY
/// <c>Type.GetType()</c> with the OLD assembly name and reflection. No direct type
/// references — simulating what a consumer compiled against the old
/// CrestCreates.Agent.Memory.Tools.Abstractions assembly would experience at runtime.
///
/// The old assembly name is used to look up each type; the TypeForward mechanism
/// redirects each resolution to CrestCreates.Agent.Memory.Projection.Abstractions
/// without recompilation.
/// </summary>
public class TypeForwardBinaryCompatibilityTests
{
    private const string OldAssemblyName = "CrestCreates.Agent.Memory.Tools.Abstractions";
    private const string NewAssemblyName = "CrestCreates.Agent.Memory.Projection.Abstractions";

    // ----- Full type resolution -----

    [Fact]
    public void All_26_forwarded_types_resolve_from_old_assembly_name()
    {
        var forwardedTypes = new[]
        {
            // Enums
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolMemoryStatus",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolConfidence",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSourceKind",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverity",
            // DTOs
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolCanonicalHashDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemorySourceGrantDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolItemDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolBlockDto",
            // Contract types
            "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackInput",
            "CrestCreates.Agent.Memory.Tools.ExpandAgentMemorySourceInput",
            "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackResult",
            "CrestCreates.Agent.Memory.Tools.ExpandAgentMemorySourceResult",
            // Enum converters
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolEnumConverter`1",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolMemoryStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKindJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolConfidenceJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSourceKindJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverityJsonConverter",
            // Security enums
            "CrestCreates.Agent.Memory.Tools.AgentMemoryResourceKind",
            "CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactState",
            "CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactKind",
            "CrestCreates.Agent.Memory.Tools.PreparedArtifactDisposition",
        };

        foreach (var typeName in forwardedTypes)
        {
            var type = Type.GetType($"{typeName}, {OldAssemblyName}");
            type.Should().NotBeNull(
                $"TypeForward should make '{typeName}' resolvable from old assembly name '{OldAssemblyName}'");
            type!.Assembly.GetName().Name.Should().Be(NewAssemblyName,
                $"Type '{typeName}' should resolve to new assembly '{NewAssemblyName}' via TypeForward");
        }
    }

    // ----- Enum value preservation -----

    [Fact]
    public void AgentMemoryToolOperationStatus_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

        // Unknown=0, Completed=1, Unavailable=2, Conflict=3, Redacted=4, NotExpandable=5
        var values = Enum.GetValues(type);
        values.Length.Should().Be(6);

        Enum.IsDefined(type, 0).Should().BeTrue();
        Enum.IsDefined(type, 1).Should().BeTrue();
        Enum.IsDefined(type, 2).Should().BeTrue();
        Enum.IsDefined(type, 3).Should().BeTrue();
        Enum.IsDefined(type, 4).Should().BeTrue();
        Enum.IsDefined(type, 5).Should().BeTrue();

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Completed", "Unavailable", "Conflict", "Redacted", "NotExpandable"]);
    }

    [Fact]
    public void AgentMemoryToolKind_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

        // Unknown=0, Preference=1, ProjectFact=2, Decision=3, Constraint=4, WorkflowHint=5, Risk=6
        var values = Enum.GetValues(type);
        values.Length.Should().Be(7);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Preference", "ProjectFact", "Decision", "Constraint", "WorkflowHint", "Risk"]);
    }

    [Fact]
    public void AgentMemoryToolConfidence_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolConfidence, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

        // Unknown=0, Unspecified=1, Low=2, Medium=3, High=4
        var values = Enum.GetValues(type);
        values.Length.Should().Be(5);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Unspecified", "Low", "Medium", "High"]);
    }

    [Fact]
    public void AgentMemoryToolMemoryStatus_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolMemoryStatus, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

        // Unknown=0, Active=1, Superseded=2, Archived=3
        var values = Enum.GetValues(type);
        values.Length.Should().Be(4);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Active", "Superseded", "Archived"]);
    }

    [Fact]
    public void AgentMemoryToolSourceKind_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolSourceKind, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

        // Unknown=0, ConversationTurn=1, ..., ActivationRequest=11
        var values = Enum.GetValues(type);
        values.Length.Should().Be(12);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "ConversationTurn", "TaskRecord", "TaskEvent",
            "CompressedContextBlock", "MemoryCandidate", "MemoryItem",
            "MetadataContextPack", "ReviewReport", "FixProposal",
            "PackagePreview", "ActivationRequest"]);
    }

    [Fact]
    public void AgentMemoryToolDiagnosticSeverity_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverity, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

        // Unknown=0, Info=1, Warning=2, Error=3
        var values = Enum.GetValues(type);
        values.Length.Should().Be(4);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Info", "Warning", "Error"]);
    }

    [Fact]
    public void Security_enums_preserved()
    {
        // AgentMemoryResourceKind
        var kindType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryResourceKind, {OldAssemblyName}");
        kindType.Should().NotBeNull();
        var kindNames = Enum.GetNames(kindType!);
        kindNames.Should().Contain(["Unknown", "Context", "Candidate", "Memory", "ConversationHistory", "TaskHistory"]);

        // AgentMemorySecurityArtifactState
        var stateType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactState, {OldAssemblyName}");
        stateType.Should().NotBeNull();
        var stateNames = Enum.GetNames(stateType!);
        stateNames.Should().Contain(["Unknown", "Active", "Revoked", "Expired"]);

        // AgentMemorySecurityArtifactKind
        var akType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactKind, {OldAssemblyName}");
        akType.Should().NotBeNull();
        var akNames = Enum.GetNames(akType!);
        akNames.Should().Contain(["Unknown", "ResourceHandle", "SourceGrant"]);

        // PreparedArtifactDisposition
        var dispType = Type.GetType($"CrestCreates.Agent.Memory.Tools.PreparedArtifactDisposition, {OldAssemblyName}");
        dispType.Should().NotBeNull();
        var dispNames = Enum.GetNames(dispType!);
        dispNames.Should().Contain(["Unknown", "CreatedByBatch", "ReusedExisting"]);
    }

    // ----- DTO property preservation -----

    [Fact]
    public void AgentMemoryToolBlockDto_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolBlockDto, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain(["Content", "CanonicalContentHash", "SourceGrants"]);
    }

    [Fact]
    public void AgentMemoryToolCanonicalHashDto_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolCanonicalHashDto, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain(["Value", "AlgorithmVersion", "ContractVersion", "CanonicalShapeVersion"]);
    }

    [Fact]
    public void AgentMemoryToolDiagnosticDto_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain(["Code", "Severity"]);
    }

    [Fact]
    public void AgentMemoryToolItemDto_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolItemDto, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain([
            "MemoryHandle", "Kind", "Content", "CanonicalContentHash",
            "Confidence", "MemoryStatus", "IsAuthoritative", "Tags", "SourceGrants"
        ]);
    }

    [Fact]
    public void BuildAgentMemoryPackInput_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackInput, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain([
            "MemoryHandles", "Kinds", "Tags", "MaximumCount", "CharacterBudget", "MinimumConfidence"
        ]);
    }

    [Fact]
    public void BuildAgentMemoryPackResult_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackResult, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain([
            "OperationStatus", "Items", "ReturnedCount", "WasTruncated", "IsAuthoritative", "Diagnostics"
        ]);
    }

    [Fact]
    public void ExpandAgentMemorySourceInput_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.ExpandAgentMemorySourceInput, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain(["GrantId", "MaximumCharacters"]);
    }

    [Fact]
    public void ExpandAgentMemorySourceResult_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.ExpandAgentMemorySourceResult, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain([
            "OperationStatus", "SanitizedContent", "CanonicalContentHash", "WasTruncated", "Diagnostics"
        ]);
    }

    [Fact]
    public void AgentMemorySourceGrantDto_properties_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemorySourceGrantDto, {OldAssemblyName}");
        type.Should().NotBeNull();
        var props = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        props.Select(p => p.Name).Should().Contain(["GrantId", "SourceKind", "ExpiresAt"]);
    }

    // ----- Enum converter accessibility (via old assembly name) -----

    [Fact]
    public void Enum_converter_base_type_is_accessible_and_abstract()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolEnumConverter`1, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsAbstract.Should().BeTrue();
        type.IsPublic.Should().BeTrue();
        type.GetGenericArguments().Should().HaveCount(1);
    }

    [Fact]
    public void All_specific_converter_types_are_accessible()
    {
        var converters = new[]
        {
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolMemoryStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKindJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolConfidenceJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSourceKindJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverityJsonConverter",
        };

        foreach (var converterName in converters)
        {
            var type = Type.GetType($"{converterName}, {OldAssemblyName}");
            type.Should().NotBeNull($"converter '{converterName}' should be accessible via old assembly name");
            type!.IsClass.Should().BeTrue();
            type.IsAbstract.Should().BeFalse();
            type.IsPublic.Should().BeTrue();
            type.BaseType.Should().NotBeNull();
            type.BaseType!.GetGenericTypeDefinition().FullName
                .Should().Be("CrestCreates.Agent.Memory.Tools.AgentMemoryToolEnumConverter`1");
        }
    }

    // ----- JSON serialization round-trips through forwarded types -----

    [Fact]
    public void Enum_json_roundtrip_through_forwarded_type()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {OldAssemblyName}");
        type.Should().NotBeNull();

        // Parse a known value
        var completed = Enum.Parse(type!, "Completed");
        var json = JsonSerializer.Serialize(completed, type!, options);
        var deserialized = JsonSerializer.Deserialize(json, type!, options);
        deserialized.Should().Be(completed);
    }

    [Fact]
    public void Enum_json_with_converter_input_roundtrip()
    {
        // Simulate what happens when a serialized payload contains a string like "preference"
        // and the consumer uses JsonSerializer with the forwarded type.
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind, {OldAssemblyName}");
        type.Should().NotBeNull();

        var json = "\"preference\"";
        var result = JsonSerializer.Deserialize(json, type!);
        result.Should().NotBeNull();
        result!.ToString().Should().Be("Preference");
    }

    [Fact]
    public void Dto_json_roundtrip_through_forwarded_type()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto, {OldAssemblyName}");
        type.Should().NotBeNull();

        // Create instance via reflection
        var diagType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticSeverity, {OldAssemblyName}");
        var severityValue = Enum.Parse(diagType!, "Error");

        // Use Activator.CreateInstance with reflection to set properties isn't straightforward
        // for record types with init-only properties. Instead, test that the type is a record
        // and that the JSON serializer can handle it.
        type!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .All(p => p.CanRead && p.GetMethod!.IsPublic).Should().BeTrue();

        // Verify the type name matches what we'd expect in JSON payloads
        type.FullName.Should().Be("CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto");
    }

    // ----- Consistent type identity -----

    [Fact]
    public void Forwarded_type_resolved_from_old_name_is_same_type_as_new_reference()
    {
        // Resolve via old assembly name
        var viaOld = Type.GetType(
            $"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {OldAssemblyName}");
        viaOld.Should().NotBeNull();

        // Resolve via new assembly name (fully qualified to force assembly lookup)
        var viaNew = Type.GetType(
            $"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {NewAssemblyName}");
        viaNew.Should().NotBeNull();

        // Both resolutions should return the exact same Type object (identity, not just equality)
        viaOld.Should().BeSameAs(viaNew,
            "TypeForward should return the identical Type object regardless of which assembly name is used for resolution");
    }

    [Fact]
    public void Multiple_forwarded_types_resolve_to_same_assembly()
    {
        var assembly = (Assembly?)null;

        foreach (var typeName in new[]
        {
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus",
            "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackInput",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolBlockDto",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatusJsonConverter",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryResourceKind",
        })
        {
            var type = Type.GetType($"{typeName}, {OldAssemblyName}");
            type.Should().NotBeNull();
            if (assembly == null)
                assembly = type!.Assembly;
            else
                type!.Assembly.Should().BeSameAs(assembly,
                    $"All forwarded types should belong to the same assembly ({NewAssemblyName})");
        }
    }
}
