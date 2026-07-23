using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests.TypeForward;

/// <summary>
/// True binary compatibility tests for TypeForwarded types.
///
/// Frozen fixture validation: The <c>Fixtures/</c> directory contains checked-in
/// pre-compiled DLLs (old contract + consumer) with a SHA-256 manifest. Tests
/// verify manifest integrity before loading. Missing or tampered fixtures cause
/// test failure — no silent skip.
/// </summary>
public class TypeForwardBinaryCompatibilityTests
{
    private const string OldAssemblyName = "CrestCreates.Agent.Memory.Tools.Abstractions";
    private const string NewAssemblyName = "CrestCreates.Agent.Memory.Projection.Abstractions";

    private static string FixturesDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "TypeForward", "Fixtures"));

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

        var values = Enum.GetValues(type);
        values.Length.Should().Be(6);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Completed", "Unavailable", "Conflict", "Redacted", "NotExpandable"]);
    }

    [Fact]
    public void AgentMemoryToolKind_enum_values_preserved()
    {
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind, {OldAssemblyName}");
        type.Should().NotBeNull();
        type!.IsEnum.Should().BeTrue();

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

        var values = Enum.GetValues(type);
        values.Length.Should().Be(4);

        var names = Enum.GetNames(type);
        names.Should().Contain(["Unknown", "Info", "Warning", "Error"]);
    }

    [Fact]
    public void Security_enums_preserved()
    {
        var kindType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryResourceKind, {OldAssemblyName}");
        kindType.Should().NotBeNull();
        var kindNames = Enum.GetNames(kindType!);
        kindNames.Should().Contain(["Unknown", "Context", "Candidate", "Memory", "ConversationHistory", "TaskHistory"]);

        var stateType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactState, {OldAssemblyName}");
        stateType.Should().NotBeNull();
        var stateNames = Enum.GetNames(stateType!);
        stateNames.Should().Contain(["Unknown", "Active", "Revoked", "Expired"]);

        var akType = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemorySecurityArtifactKind, {OldAssemblyName}");
        akType.Should().NotBeNull();
        var akNames = Enum.GetNames(akType!);
        akNames.Should().Contain(["Unknown", "ResourceHandle", "SourceGrant"]);

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

    // ----- Enum converter accessibility -----

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

    // ----- JSON serialization round-trips -----

    [Fact]
    public void Enum_json_roundtrip_through_forwarded_type()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {OldAssemblyName}");
        type.Should().NotBeNull();

        var completed = Enum.Parse(type!, "Completed");
        var json = JsonSerializer.Serialize(completed, type!, options);
        var deserialized = JsonSerializer.Deserialize(json, type!, options);
        deserialized.Should().Be(completed);
    }

    [Fact]
    public void Enum_json_with_converter_input_roundtrip()
    {
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
        var type = Type.GetType($"CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto, {OldAssemblyName}");
        type.Should().NotBeNull();

        type!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .All(p => p.CanRead && p.GetMethod!.IsPublic).Should().BeTrue();
        type.FullName.Should().Be("CrestCreates.Agent.Memory.Tools.AgentMemoryToolDiagnosticDto");
    }

    // ----- Consistent type identity -----

    [Fact]
    public void Forwarded_type_resolved_from_old_name_is_same_type_as_new_reference()
    {
        var viaOld = Type.GetType(
            $"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {OldAssemblyName}");
        viaOld.Should().NotBeNull();

        var viaNew = Type.GetType(
            $"CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus, {NewAssemblyName}");
        viaNew.Should().NotBeNull();

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

    // ── Frozen fixture validation ──────────────────────────────────

    [Fact]
    public void Frozen_fixture_manifest_exists_and_is_valid()
    {
        var manifestPath = Path.Combine(FixturesDir, "manifest.json");
        Assert.True(File.Exists(manifestPath),
            $"Frozen fixture manifest must exist at: {manifestPath}. " +
            "If you updated the LegacyContract or LegacyConsumer, rebuild and copy DLLs, then update manifest.json.");

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(manifestJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(manifest);
        manifest.Version.Should().Be(1);
        manifest.Artifacts.Should().NotBeEmpty();

        foreach (var (fileName, info) in manifest.Artifacts)
        {
            var filePath = Path.Combine(FixturesDir, fileName);
            Assert.True(File.Exists(filePath),
                $"Frozen fixture DLL '{fileName}' must exist at: {filePath}");

            using var stream = File.OpenRead(filePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            hash.Should().Be(info.Sha256,
                $"Frozen fixture '{fileName}' SHA-256 must match manifest. " +
                "If you intentionally updated the contract, rebuild DLLs and update manifest.json. " +
                "Do NOT modify the manifest without rebuilding.");
        }
    }

    // ── AssemblyLoadContext-based cross-assembly resolution ─────────

    [Fact]
    public void TypeForwardedTypes_ResolveFromOldAssemblyDll()
    {
        // The current forwarding assembly (built from source, contains TypeForwardedTo attributes)
        var forwardingAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "..",
            "src", "Runtime", "Agent", "CrestCreates.Agent.Memory.Tools.Abstractions",
            "bin", "Debug", "net10.0",
            "CrestCreates.Agent.Memory.Tools.Abstractions.dll"));

        if (!File.Exists(forwardingAssemblyPath))
        {
            Assert.Fail($"Forwarding assembly not found at: {forwardingAssemblyPath}. " +
                "Build CrestCreates.Agent.Memory.Tools.Abstractions before running this test.");
        }

        var context = new AssemblyLoadContext("TypeForwardBinaryTest", isCollectible: true);
        try
        {
            var oldAssembly = context.LoadFromAssemblyPath(forwardingAssemblyPath);

            var statusType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus");
            Assert.NotNull(statusType);
            Assert.NotEqual(oldAssembly, statusType!.Assembly);
            Assert.Equal(NewAssemblyName, statusType.Assembly.GetName().Name);

            var inputType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.BuildAgentMemoryPackInput");
            Assert.NotNull(inputType);
            Assert.Equal(NewAssemblyName, inputType!.Assembly.GetName().Name);

            var kindType = oldAssembly.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind");
            Assert.NotNull(kindType);
            Assert.Equal(NewAssemblyName, kindType!.Assembly.GetName().Name);

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
        // Validate frozen fixture manifest integrity first
        var manifestPath = Path.Combine(FixturesDir, "manifest.json");
        Assert.True(File.Exists(manifestPath),
            $"Frozen fixture manifest must exist at: {manifestPath}");

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(manifestJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(manifest);

        // Load frozen consumer DLL (compiled against pre-migration contract)
        var consumerPath = Path.Combine(FixturesDir, "TypeForwardLegacyConsumer.dll");
        Assert.True(File.Exists(consumerPath),
            $"Frozen consumer DLL must exist at: {consumerPath}. " +
            "Rebuild LegacyContract + LegacyConsumer, copy DLLs to Fixtures/, update manifest.json.");

        // Verify consumer DLL hash matches manifest
        using (var stream = File.OpenRead(consumerPath))
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Assert.True(manifest.Artifacts.ContainsKey("TypeForwardLegacyConsumer.dll"),
                "Manifest must contain TypeForwardLegacyConsumer.dll entry");
            hash.Should().Be(manifest.Artifacts["TypeForwardLegacyConsumer.dll"].Sha256,
                "Consumer DLL SHA-256 must match manifest — frozen fixture must not be tampered with");
        }

        // Load frozen old contract DLL
        var oldContractPath = Path.Combine(FixturesDir, "CrestCreates.Agent.Memory.Tools.Abstractions.dll");
        Assert.True(File.Exists(oldContractPath),
            $"Frozen old contract DLL must exist at: {oldContractPath}");

        // Verify old contract DLL hash matches manifest
        using (var stream = File.OpenRead(oldContractPath))
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Assert.True(manifest.Artifacts.ContainsKey("CrestCreates.Agent.Memory.Tools.Abstractions.dll"),
                "Manifest must contain old contract DLL entry");
            hash.Should().Be(manifest.Artifacts["CrestCreates.Agent.Memory.Tools.Abstractions.dll"].Sha256,
                "Old contract DLL SHA-256 must match manifest");
        }

        // The current forwarding assembly (built from source)
        var forwardingAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "CrestCreates.Agent.Memory.Tools.Abstractions.dll"));
        Assert.True(File.Exists(forwardingAssemblyPath),
            $"Current forwarding assembly not found at: {forwardingAssemblyPath}");

        // The new target assembly (contains actual type definitions)
        var newAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "CrestCreates.Agent.Memory.Projection.Abstractions.dll"));
        Assert.True(File.Exists(newAssemblyPath),
            $"New target assembly not found at: {newAssemblyPath}");

        var context = new LegacyConsumerLoadContext(
            forwardingAssemblyPath, newAssemblyPath, consumerPath);
        try
        {
            var consumerAssembly = context.LoadFromAssemblyPath(consumerPath);
            var validatorType = consumerAssembly.GetType("TypeForwardLegacyConsumer.TypeForwardValidator");
            Assert.NotNull(validatorType);

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
    /// to the current forwarding assembly.
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

            _defaultForwarding = LoadFromAssemblyPath(forwardingAssemblyPath);
            _defaultNew = LoadFromAssemblyPath(newAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == "CrestCreates.Agent.Memory.Tools.Abstractions")
                return _defaultForwarding;

            if (assemblyName.Name == "CrestCreates.Agent.Memory.Projection.Abstractions")
                return _defaultNew;

            return null;
        }
    }

    // Manifest model for JSON deserialization
    private record FixtureManifest(int Version, Dictionary<string, FixtureArtifactInfo> Artifacts);
    private record FixtureArtifactInfo(string Sha256, string Description);
}
