using System.Collections.Generic;
using System.Linq;
using CrestCreates.CodeGenerator.CanonicalHashGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CanonicalHashGenerator;

public sealed class CanonicalHashSourceGeneratorTests
{
    private static class TestSources
    {
        /// <summary>
        /// Minimal copies of canonical hash attribute declarations so that
        /// source generator tests work without a project reference to Metadata.Abstractions.
        /// </summary>
        private const string AttributeDeclarations = @"
using System;

namespace CrestCreates.Metadata.Abstractions.CanonicalHashing
{
    public enum CanonicalHashArtifactKind { Descriptor = 0, ReviewResult = 1, Package = 2, Report = 3 }
    public enum DescriptorKind { Unknown = 0, Schema = 1, Capability = 2 }
    public enum CanonicalHashFieldClassification { Contract = 1, DefinitionOnly = 2, Excluded = 3 }
    public enum CanonicalHashCollectionOrderMode { None = 0, SourceOrder = 1, OrdinalByValue = 2, OrdinalByProperty = 3, OrderedKeyValue = 4 }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CanonicalHashProfileAttribute : Attribute
    {
        public CanonicalHashArtifactKind ArtifactKind { get; init; } = CanonicalHashArtifactKind.Descriptor;
        public DescriptorKind DescriptorKind { get; init; } = DescriptorKind.Unknown;
        public Type? TargetType { get; init; }
        public string? ContractShapeVersion { get; init; }
        public string? DefinitionShapeVersion { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CanonicalHashUnionProfileAttribute : Attribute
    {
        public Type? TargetType { get; init; }
        public string? Discriminator { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CanonicalHashUnionCaseAttribute : Attribute
    {
        public CanonicalHashUnionCaseAttribute(Type caseType, string discriminatorValue)
        {
            CaseType = caseType;
            DiscriminatorValue = discriminatorValue;
        }
        public Type CaseType { get; }
        public string DiscriminatorValue { get; }
        public Type? ValueProfile { get; init; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class CanonicalHashFieldAttribute : Attribute
    {
        public CanonicalHashFieldAttribute(string propertyName, CanonicalHashFieldClassification classification)
        {
            PropertyName = propertyName;
            Classification = classification;
        }
        public string PropertyName { get; }
        public CanonicalHashFieldClassification Classification { get; }
        public int Order { get; init; }
        public Type? ElementProfile { get; init; }
        public Type? ValueProfile { get; init; }
        public Type? Filter { get; init; }
        public CanonicalHashCollectionOrderMode CollectionOrderMode { get; init; } = CanonicalHashCollectionOrderMode.None;
        public string? OrderByProperty { get; init; }
        public string? Reason { get; init; }
        public Type? CustomWriter { get; init; }
    }
}
";

        public static string WithProfiles(string body)
        {
            var lines = body.Split('\n');
            var usings = new List<string>();
            var restLines = new List<string>();
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("using ") && !line.TrimStart().StartsWith("using ("))
                    usings.Add(line);
                else
                    restLines.Add(line);
            }
            return string.Join("\n", usings) + "\n" + AttributeDeclarations + "\n" + string.Join("\n", restLines);
        }
    }

    /// <summary>
    /// Source for a union profile with two cases (CapabilityTarget and SchemaTarget)
    /// and their corresponding normal profiles. Used by union writer generation tests.
    /// </summary>
    private const string UnionProfileSource = """
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using System.Collections.Generic;

namespace TestNamespace
{
    public abstract class InteractionTarget { }

    public sealed class CapabilityTarget : InteractionTarget
    {
        public string KindCap { get; init; } = "";
        public string Id { get; init; } = "";
    }

    public sealed class SchemaTarget : InteractionTarget
    {
        public string KindSch { get; init; } = "";
        public string Name { get; init; } = "";
    }

    [CanonicalHashProfile(
        TargetType = typeof(CapabilityTarget),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class CapabilityTargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(CapabilityTarget.KindCap), CanonicalHashFieldClassification.Contract)]
        [CanonicalHashField(nameof(CapabilityTarget.Id), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashProfile(
        TargetType = typeof(SchemaTarget),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class SchemaTargetCanonicalHashProfile
    {
        [CanonicalHashField(nameof(SchemaTarget.KindSch), CanonicalHashFieldClassification.Contract)]
        [CanonicalHashField(nameof(SchemaTarget.Name), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashUnionProfile(TargetType = typeof(InteractionTarget), Discriminator = "Kind")]
    [CanonicalHashUnionCase(typeof(CapabilityTarget), "Capability", ValueProfile = typeof(CapabilityTargetCanonicalHashProfile))]
    [CanonicalHashUnionCase(typeof(SchemaTarget), "Schema", ValueProfile = typeof(SchemaTargetCanonicalHashProfile))]
    internal sealed class InteractionTargetCanonicalHashProfile { }
}
""";

    /// <summary>
    /// Source for a profile with a filtered collection of sub-structure items.
    /// </summary>
    private const string FilteredCollectionSource = """
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using System.Collections.Generic;

namespace TestNamespace
{
    public sealed class SchemaDescriptor
    {
        public string Name { get; init; } = "";
        public List<SchemaField> Fields { get; init; } = new();
    }

    public sealed class SchemaField
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
    }

    public static class RequiredSchemaFieldCanonicalHashFilter
    {
        public static bool Include(SchemaField field) => field.Type != "optional";
    }

    [CanonicalHashProfile(
        TargetType = typeof(SchemaField),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class SchemaFieldCanonicalHashProfile
    {
        [CanonicalHashField(nameof(SchemaField.Name), CanonicalHashFieldClassification.Contract)]
        [CanonicalHashField(nameof(SchemaField.Type), CanonicalHashFieldClassification.Contract)]
        private static void Fields() { }
    }

    [CanonicalHashProfile(
        TargetType = typeof(SchemaDescriptor),
        ContractShapeVersion = "v1",
        DefinitionShapeVersion = "v1")]
    internal sealed class SchemaDescriptorCanonicalHashProfile
    {
        [CanonicalHashField(nameof(SchemaDescriptor.Name), CanonicalHashFieldClassification.Contract)]
        [CanonicalHashField(nameof(SchemaDescriptor.Fields), CanonicalHashFieldClassification.Contract,
            CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
            OrderByProperty = "Name",
            ElementProfile = typeof(SchemaFieldCanonicalHashProfile),
            Filter = typeof(RequiredSchemaFieldCanonicalHashFilter))]
        private static void Fields() { }
    }
}
""";

    [Fact]
    public void GeneratedSource_ContainsUnionWriterClass()
    {
        var source = TestSources.WithProfiles(UnionProfileSource);
        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);

        // Verify source generator produced output (don't check CompilationSuccess — test environment
        // lacks CrestCreates.Metadata.Abstractions assembly references needed for generated code to compile).
        Assert.NotEmpty(result.GeneratedSources);

        var allSources = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText));

        // Debug: dump the writer source
        var writerSource = result.GeneratedSources.FirstOrDefault(s => s.FileName.Contains("CanonicalHashWriters"));
        Assert.NotNull(writerSource);

        var ws = writerSource!.SourceText;

        // Union writer class declaration
        Assert.Contains("internal static class InteractionTargetCanonicalHashWriter", ws);

        // Switch on target
        Assert.Contains("switch (target)", ws);

        // Case with discriminator and value dispatch (uses fully-qualified type names)
        Assert.Contains("case global::TestNamespace.CapabilityTarget value:", ws);
        Assert.Contains("w.WriteString(\"Kind\", \"Capability\");", ws);
        Assert.Contains("w.WritePropertyName(\"Value\");", ws);
        Assert.Contains("CapabilityTargetCanonicalHashWriter.WriteContractPayload(w, value);", ws);

        // Second case
        Assert.Contains("case global::TestNamespace.SchemaTarget value:", ws);
        Assert.Contains("w.WriteString(\"Kind\", \"Schema\");", ws);
        Assert.Contains("SchemaTargetCanonicalHashWriter.WriteContractPayload(w, value);", ws);

        // Default case
        Assert.Contains("default:", ws);
        Assert.Contains("throw new InvalidOperationException", ws);

        // Both WriteContractPayload and WriteDefinitionPayload exist
        Assert.Contains("public static void WriteContractPayload", ws);
        Assert.Contains("public static void WriteDefinitionPayload", ws);
    }

    [Fact]
    public void GeneratedSource_ContainsFilteredCollectionCode()
    {
        var source = TestSources.WithProfiles(FilteredCollectionSource);
        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);

        Assert.NotEmpty(result.GeneratedSources);

        var allSources = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText));

        // Filter should be used with .Include
        Assert.Contains("RequiredSchemaFieldCanonicalHashFilter.Include", allSources);

        // .Where(...) should come before .OrderBy(...) for filtered collections
        var writerSource = result.GeneratedSources
            .FirstOrDefault(s => s.FileName.Contains("CanonicalHashWriters"));
        Assert.NotNull(writerSource);

        var writerText = writerSource!.SourceText;
        var whereIdx = writerText.IndexOf(".Where(", System.StringComparison.Ordinal);
        var orderByIdx = writerText.IndexOf(".OrderBy(", System.StringComparison.Ordinal);

        Assert.True(whereIdx >= 0, "Should contain .Where(");
        Assert.True(orderByIdx >= 0, "Should contain .OrderBy(");
        Assert.True(whereIdx < orderByIdx, ".Where() should appear before .OrderBy()");
    }

    [Fact]
    public void GeneratedSource_DoesNotContainForbiddenPatterns()
    {
        var source = TestSources.WithProfiles(UnionProfileSource);
        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);

        Assert.NotEmpty(result.GeneratedSources);

        var writerSource = result.GeneratedSources.FirstOrDefault(s => s.FileName.Contains("CanonicalHashWriters"));
        Assert.NotNull(writerSource);
        var generatedText = writerSource!.SourceText;

        // Must not contain any reflection-based patterns in generated writer main path.
        // .GetType() is allowed only in default/error branches (unreachable in normal operation).
        Assert.DoesNotContain("JsonSerializer", generatedText);
        Assert.DoesNotContain("JsonTypeInfo", generatedText);
        Assert.DoesNotContain("GetTypeInfo", generatedText);
        Assert.DoesNotContain("CustomWriter", generatedText); // Active CustomWriter paths should be removed
    }

    [Fact]
    public void GeneratedSource_NoJsonSerializerInFilteredCollection()
    {
        var source = TestSources.WithProfiles(FilteredCollectionSource);
        var result = SourceGeneratorTestHelper.RunGenerator<CanonicalHashSourceGenerator>(source);

        Assert.NotEmpty(result.GeneratedSources);

        var writerSource = result.GeneratedSources.FirstOrDefault(s => s.FileName.Contains("CanonicalHashWriters"));
        Assert.NotNull(writerSource);
        var generatedText = writerSource!.SourceText;

        Assert.DoesNotContain("JsonSerializer", generatedText);
        Assert.DoesNotContain("JsonTypeInfo", generatedText);
        Assert.DoesNotContain("GetTypeInfo", generatedText);
    }
}
