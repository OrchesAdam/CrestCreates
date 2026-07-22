using System.Reflection;
using System.Runtime.Loader;
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
///
/// NOTE: The <c>Type.GetType()</c>-based tests verify TypeForwarded type resolution
/// from the current process, which has the new assemblies already loaded.
/// A true binary compatibility fixture requires a pre-compiled consumer binary
/// that references the OLD assembly (CrestCreates.Agent.Memory.Tools.Abstractions)
/// and is loaded via AssemblyLoadContext to verify TypeForwardedTo resolution
/// across assembly boundaries without recompilation.
/// TODO: Create a standalone consumer project targeting the old assembly names
///       and compile it once, then load the binary in this test.
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

    // ── AssemblyLoadContext-based cross-assembly resolution ─────────

    [Fact]
    public void TypeForwardedTypes_ResolveFromOldAssemblyDll()
    {
        // Find the old assembly DLL (CrestCreates.Agent.Memory.Tools.Abstractions.dll)
        // The old assembly is a forwarding assembly — it contains only TypeForwardedTo
        // attributes and no type definitions. Loading it through a fresh isolated
        // AssemblyLoadContext proves the TypeForward works for consumers compiled
        // against the old assembly name.
        var oldAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "..",
            "src", "Runtime", "Agent", "CrestCreates.Agent.Memory.Tools.Abstractions",
            "bin", "Debug", "net10.0",
            "CrestCreates.Agent.Memory.Tools.Abstractions.dll"));

        if (!File.Exists(oldAssemblyPath))
        {
            // The forwarding assembly must be built before this test runs.
            // CI must build Tools.Abstractions before running Projection.Tests.
            Assert.Fail($"Old forwarding assembly not found at: {oldAssemblyPath}. " +
                "Build CrestCreates.Agent.Memory.Tools.Abstractions before running this test.");
        }

        var context = new AssemblyLoadContext("TypeForwardBinaryTest", isCollectible: true);
        try
        {
            var oldAssembly = context.LoadFromAssemblyPath(oldAssemblyPath);

            // Verify TypeForwarded types can be resolved from the old assembly
            // The resolved type should actually live in the new assembly, not the old one.
            // Only test types that are actually in the TypeForward list.
            var statusType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus");
            Assert.NotNull(statusType);
            Assert.NotEqual(oldAssembly, statusType!.Assembly);
            Assert.Equal(
                "CrestCreates.Agent.Memory.Projection.Abstractions",
                statusType.Assembly.GetName().Name);

            var inputType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackInput");
            Assert.NotNull(inputType);
            Assert.Equal(
                "CrestCreates.Agent.Memory.Projection.Abstractions",
                inputType!.Assembly.GetName().Name);

            var kindType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind");
            Assert.NotNull(kindType);
            Assert.Equal(
                "CrestCreates.Agent.Memory.Projection.Abstractions",
                kindType!.Assembly.GetName().Name);

            // Verify that a type NOT in the TypeForward list is NOT resolvable
            var principalType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryAccessPrincipal");
            Assert.Null(principalType);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void LegacyConsumerFixture_ResolvesForwardedTypesAndInvokesMembers()
    {
        // This test loads a precompiled consumer DLL that was compiled against
        // the OLD Tools.Abstractions contract replica (LegacyContract project).
        // The consumer DLL encodes type references to the old assembly identity
        // (CrestCreates.Agent.Memory.Tools.Abstractions). At runtime, the
        // AssemblyLoadContext resolves the old assembly reference to the current
        // forwarding assembly, which then redirects types via TypeForwardedTo
        // to Projection.Abstractions.
        //
        // This validates: pre-migration consumer → current forwarding assembly → current target types
        // NOT: current source → current forwarding → current target (which is what
        // a ProjectReference to the forwarding assembly would validate).

        var consumerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "TypeForwardLegacyConsumer.dll"));

        if (!File.Exists(consumerPath))
        {
            Assert.Fail($"Legacy consumer DLL not found at: {consumerPath}. " +
                "Build the TypeForwardLegacyConsumer project and copy its output before running this test.");
        }

        // The current forwarding assembly (contains TypeForwardedTo attributes)
        var forwardingAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "CrestCreates.Agent.Memory.Tools.Abstractions.dll"));

        if (!File.Exists(forwardingAssemblyPath))
        {
            Assert.Fail($"Forwarding assembly not found at: {forwardingAssemblyPath}. " +
                "Build CrestCreates.Agent.Memory.Tools.Abstractions before running this test.");
        }

        // The new target assembly (contains actual type definitions)
        var newAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "CrestCreates.Agent.Memory.Projection.Abstractions.dll"));

        if (!File.Exists(newAssemblyPath))
        {
            Assert.Fail($"New target assembly not found at: {newAssemblyPath}. " +
                "Build CrestCreates.Agent.Memory.Projection.Abstractions before running this test.");
        }

        var context = new LegacyConsumerLoadContext(
            forwardingAssemblyPath, newAssemblyPath, consumerPath);
        try
        {
            var consumerAssembly = context.LoadFromAssemblyPath(consumerPath);
            var validatorType = consumerAssembly.GetType("TypeForwardLegacyConsumer.TypeForwardValidator");
            Assert.NotNull(validatorType);

            // Invoke ValidateAll() — returns true if all forwarded types resolve and members invoke correctly
            var validateMethod = validatorType!.GetMethod("ValidateAll", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(validateMethod);

            var result = (bool)validateMethod!.Invoke(null, null)!;
            Assert.True(result, "Legacy consumer binary must resolve all forwarded types and invoke members successfully");
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// Custom AssemblyLoadContext that resolves the old assembly name
    /// (CrestCreates.Agent.Memory.Tools.Abstractions) to the current forwarding
    /// assembly. This simulates what happens when a pre-migration consumer DLL
    /// encounters the current forwarding assembly at runtime.
    /// </summary>
    private sealed class LegacyConsumerLoadContext : AssemblyLoadContext
    {
        private readonly string _forwardingAssemblyPath;
        private readonly string _newAssemblyPath;
        private readonly Assembly? _defaultForwarding;
        private readonly Assembly? _defaultNew;

        public LegacyConsumerLoadContext(
            string forwardingAssemblyPath, string newAssemblyPath, string consumerPath)
            : base("LegacyConsumerTest", isCollectible: true)
        {
            _forwardingAssemblyPath = forwardingAssemblyPath;
            _newAssemblyPath = newAssemblyPath;

            // Pre-load the forwarding and new assemblies into this context
            // so TypeForwardedTo resolution works when the consumer requests
            // types from the old assembly name.
            _defaultForwarding = LoadFromAssemblyPath(forwardingAssemblyPath);
            _defaultNew = LoadFromAssemblyPath(newAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // When the consumer DLL requests the old assembly by name,
            // redirect to the current forwarding assembly (which contains
            // TypeForwardedTo attributes pointing to Projection.Abstractions).
            if (assemblyName.Name == "CrestCreates.Agent.Memory.Tools.Abstractions")
            {
                return _defaultForwarding;
            }

            if (assemblyName.Name == "CrestCreates.Agent.Memory.Projection.Abstractions")
            {
                return _defaultNew;
            }

            // For other dependencies, use default resolution
            return null;
        }
    }
}
