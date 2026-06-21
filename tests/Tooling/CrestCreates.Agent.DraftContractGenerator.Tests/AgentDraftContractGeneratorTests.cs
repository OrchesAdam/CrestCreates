using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.CodeGenerator.AgentDraftContractGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using FluentAssertions;

namespace CrestCreates.Agent.DraftContractGenerator.Tests;

/// <summary>
/// Unit tests for the AgentDraftContractSourceGenerator that verify
/// the generator produces correct output from spec files.
/// </summary>
public class AgentDraftContractGeneratorTests
{
    // ──────────────────────────────────────────────────────────────
    // Attribute source text (mirrors the internal attributes from
    // CrestCreates.Agent.DraftContracts.Specs, which are not
    // accessible as project references in tests)
    // ──────────────────────────────────────────────────────────────

    private const string SpecAttributesSource = """
        using System;
        using CrestCreates.Metadata.Abstractions;

        namespace CrestCreates.Agent.DraftContracts.Specs;

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftContractSpecAttribute : Attribute
        {
            public required DescriptorKind Kind { get; init; }
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftFieldAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftReferenceAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftPreserveAttribute : Attribute
        {
            public required string Reason { get; init; }
            public required PreserveCreateStrategy CreateStrategy { get; init; }
        }

        public enum PreserveCreateStrategy
        {
            CreateDefault,
            KnownDomainDefault,
            CreateUnsupported
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftUnsupportedAttribute : Attribute
        {
            public required string Reason { get; init; }
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftContractNameAttribute : Attribute
        {
            public required string Name { get; init; }
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
        public sealed class AgentDraftRequiredOnCreateAttribute : Attribute;
        """;

    // ──────────────────────────────────────────────────────────────
    // Test spec source: one minimal spec per DescriptorKind
    // ──────────────────────────────────────────────────────────────

    private const string AllSixKindsSource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Event.Abstractions;
        using CrestCreates.HumanTask.Abstractions;
        using CrestCreates.Schema.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        [AgentDraftContractSpec(Kind = DescriptorKind.Capability)]
        public sealed class CapabilityTestSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Event)]
        public sealed class EventTestSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Form)]
        public sealed class FormTestSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.HumanTask)]
        public sealed class HumanTaskTestSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Schema)]
        public sealed class SchemaTestSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Workflow)]
        public sealed class WorkflowTestSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }
        }
        """;

    // ──────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────

    private (List<(string FileName, string SourceText)> GeneratedSources, List<Diagnostic> Diagnostics)
        RunGenerator(string source, string[]? additionalSources = null, string[]? additionalReferences = null)
    {
        var allSources = new List<string> { SpecAttributesSource, source };
        if (additionalSources is not null)
            allSources.AddRange(additionalSources);

        var compilation = CreateCompilation(allSources, additionalReferences);

        var driver = CSharpGeneratorDriver.Create(new AgentDraftContractSourceGenerator().AsSourceGenerator());

        // RunGenerators returns the driver with results stored
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var allDiags = new List<Diagnostic>(runResult.Diagnostics);

        // Get generated trees from the run result
        var generated = new List<(string, string)>();
        foreach (var tree in runResult.GeneratedTrees)
        {
            generated.Add((tree.FilePath, tree.ToString()));
        }

        return (generated, allDiags);
    }

    private Compilation CreateCompilation(List<string> sources, string[]? additionalReferences)
    {
        var references = new List<MetadataReference>();

        // Add standard runtime references
        AddReference(references, "System.Runtime");
        AddReference(references, "netstandard");
        AddReference(references, "System.Collections");
        AddReference(references, "System.Linq");
        AddReference(references, "System.Linq.Expressions");
        AddReference(references, "System.Threading.Tasks");
        AddReference(references, "System.ComponentModel");
        AddReference(references, "System.ComponentModel.Annotations");

        // Core
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        // Framework abstractions used by generated output
        AddReference(references, "CrestCreates.Metadata.Abstractions");
        AddReference(references, "CrestCreates.Metadata");
        AddReference(references, "CrestCreates.Event.Abstractions");
        AddReference(references, "CrestCreates.HumanTask.Abstractions");
        AddReference(references, "CrestCreates.Form.Abstractions");
        AddReference(references, "CrestCreates.Workflow.Abstractions");
        AddReference(references, "CrestCreates.Schema.Abstractions");
        AddReference(references, "CrestCreates.Capability.Abstractions");
        AddReference(references, "CrestCreates.DescriptorDraft.Abstractions");

        if (additionalReferences is not null)
        {
            foreach (var r in additionalReferences)
                AddReference(references, r);
        }

        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s))
            .ToList();

        return CSharpCompilation.Create(
            "TestCompilation",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void AddReference(List<MetadataReference> references, string assemblyName)
    {
        try
        {
            var assembly = System.Reflection.Assembly.Load(assemblyName);
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
        catch
        {
            // Ignore assembly load failures
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 1: Generator produces exactly 5 output files
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Generator_Produces_Five_Output_Files()
    {
        var (generated, _) = RunGenerator(AllSixKindsSource);

        var fileNames = generated.Select(g => System.IO.Path.GetFileName(g.FileName)).ToList();

        fileNames.Should().HaveCount(5, "the generator always produces 5 output files");
        fileNames.Should().Contain("AgentDraftPayloadDtos.g.cs");
        fileNames.Should().Contain("AgentDraftPayloadPatchDtos.g.cs");
        fileNames.Should().Contain("AgentDraftChangedFieldEnums.g.cs");
        fileNames.Should().Contain("AgentDraftContractManifest.g.cs");
        fileNames.Should().Contain("AgentDraftPayloadProjection.g.cs");
    }

    // ──────────────────────────────────────────────────────────────
    // Test 1: Generator produces exactly 5 output files
    // ──────────────────────────────────────────────────────────────
    // Test 2: Payload DTOs contain all 6 per-kind types + one-of wrapper
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void PayloadDto_Contains_All_Six_Kinds()
    {
        var (generated, _) = RunGenerator(AllSixKindsSource);

        var payloadFile = generated
            .First(g => System.IO.Path.GetFileName(g.FileName) == "AgentDraftPayloadDtos.g.cs")
            .SourceText;

        // 6 per-kind DTOs
        payloadFile.Should().Contain("AgentCapabilityDraftPayloadDto");
        payloadFile.Should().Contain("AgentEventDraftPayloadDto");
        payloadFile.Should().Contain("AgentFormDraftPayloadDto");
        payloadFile.Should().Contain("AgentHumanTaskDraftPayloadDto");
        payloadFile.Should().Contain("AgentSchemaDraftPayloadDto");
        payloadFile.Should().Contain("AgentWorkflowDraftPayloadDto");

        // One-of wrapper
        payloadFile.Should().Contain("AgentDraftPayloadDto");
        payloadFile.Should().Contain("DescriptorKind Discriminator");
        payloadFile.Should().Contain("AgentCapabilityDraftPayloadDto? Capability");
        payloadFile.Should().Contain("AgentEventDraftPayloadDto? Event");
        payloadFile.Should().Contain("AgentFormDraftPayloadDto? Form");
        payloadFile.Should().Contain("AgentHumanTaskDraftPayloadDto? HumanTask");
        payloadFile.Should().Contain("AgentSchemaDraftPayloadDto? Schema");
        payloadFile.Should().Contain("AgentWorkflowDraftPayloadDto? Workflow");
    }

    // ──────────────────────────────────────────────────────────────
    // Test 3: ChangedField enums have [Flags] attribute and powers of 2
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ChangedFieldEnums_Have_Flags_Attribute()
    {
        var (generated, _) = RunGenerator(AllSixKindsSource);

        var enumsFile = generated
            .First(g => System.IO.Path.GetFileName(g.FileName) == "AgentDraftChangedFieldEnums.g.cs")
            .SourceText;

        // 6 enums, one per kind
        enumsFile.Should().Contain("AgentCapabilityDraftChangedField");
        enumsFile.Should().Contain("AgentEventDraftChangedField");
        enumsFile.Should().Contain("AgentFormDraftChangedField");
        enumsFile.Should().Contain("AgentHumanTaskDraftChangedField");
        enumsFile.Should().Contain("AgentSchemaDraftChangedField");
        enumsFile.Should().Contain("AgentWorkflowDraftChangedField");

        // Each has [Flags]
        var flagsCount = CountOccurrences(enumsFile, "[Flags]");
        flagsCount.Should().Be(6, "each enum should have a [Flags] attribute");

        // Each has None = 0
        var noneCount = CountOccurrences(enumsFile, "None = 0");
        noneCount.Should().Be(6, "each enum should have None = 0");

        // Values are powers of 2 (spot check: look for None = 0, = 1, = 2)
        enumsFile.Should().Contain("= 1,");
        enumsFile.Should().Contain("= 2,");
    }

    // ──────────────────────────────────────────────────────────────
    // Test 4: Projection contains 4 methods
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Projection_Contains_Four_Methods()
    {
        var (generated, _) = RunGenerator(AllSixKindsSource);

        var projectionFile = generated
            .First(g => System.IO.Path.GetFileName(g.FileName) == "AgentDraftPayloadProjection.g.cs")
            .SourceText;

        // Methods are: FromDomain, Create, Merge, TryValidatePayload
        projectionFile.Should().Contain("FromDomain");
        projectionFile.Should().Contain("Create");
        projectionFile.Should().Contain("Merge");
        projectionFile.Should().Contain("TryValidatePayload");
    }

    // ──────────────────────────────────────────────────────────────
    // Test 5: Manifest contains all 6 SupportedKinds
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Manifest_Contains_All_SupportedKinds()
    {
        var (generated, _) = RunGenerator(AllSixKindsSource);

        var manifestFile = generated
            .First(g => System.IO.Path.GetFileName(g.FileName) == "AgentDraftContractManifest.g.cs")
            .SourceText;

        manifestFile.Should().Contain("SupportedKinds");
        manifestFile.Should().Contain("DescriptorKind.Capability");
        manifestFile.Should().Contain("DescriptorKind.Event");
        manifestFile.Should().Contain("DescriptorKind.Form");
        manifestFile.Should().Contain("DescriptorKind.HumanTask");
        manifestFile.Should().Contain("DescriptorKind.Schema");
        manifestFile.Should().Contain("DescriptorKind.Workflow");
    }

    // ──────────────────────────────────────────────────────────────
    // Test 6: Diagnostic ADP002 for unclassified property
    // ──────────────────────────────────────────────────────────────

    private const string UnclassifiedPropertySource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        [AgentDraftContractSpec(Kind = DescriptorKind.Capability)]
        public sealed class BadSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            // No classification attribute on this property
            public string UnclassifiedProp { get; init; } = string.Empty;
        }
        """;

    [Fact]
    public void Diagnostic_ADPC002_For_Unclassified_Property()
    {
        var (_, diagnostics) = RunGenerator(UnclassifiedPropertySource);

        diagnostics.Should().Contain(d =>
            d.Id == "ADP002" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("UnclassifiedProp"));
    }

    // ──────────────────────────────────────────────────────────────
    // Test 7: Diagnostic ADP004 for Preserve without Reason
    // ──────────────────────────────────────────────────────────────

    private const string PreserveWithoutReasonSource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        [AgentDraftContractSpec(Kind = DescriptorKind.Capability)]
        public sealed class BadPreserveSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftPreserve(Reason = "", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
            public string? SupersededById { get; init; }
        }
        """;

    [Fact]
    public void Diagnostic_ADPC004_For_Preserve_Without_Reason()
    {
        var (_, diagnostics) = RunGenerator(PreserveWithoutReasonSource);

        diagnostics.Should().Contain(d =>
            d.Id == "ADP004" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("SupersededById"));
    }

    // ──────────────────────────────────────────────────────────────
    // Test 8: No spec classes → no output
    // ──────────────────────────────────────────────────────────────

    private const string NoSpecClassesSource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        // Just a regular class, no [AgentDraftContractSpec]
        public sealed class NotASpec
        {
            public string Name { get; init; } = string.Empty;
        }
        """;

    [Fact]
    public void No_Spec_Classes_No_Output()
    {
        var (generated, _) = RunGenerator(NoSpecClassesSource);

        generated.Should().BeEmpty("no spec classes means no generated output");
    }

    // ──────────────────────────────────────────────────────────────
    // Test 9: ADP001 — missing spec for a known kind
    // ──────────────────────────────────────────────────────────────

    private const string MissingSchemaKindSource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Event.Abstractions;
        using CrestCreates.HumanTask.Abstractions;
        using CrestCreates.Form.Abstractions;
        using CrestCreates.Workflow.Abstractions;
        using CrestCreates.Capability.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        [AgentDraftContractSpec(Kind = DescriptorKind.Capability)]
        public sealed class CapabilityTestSpec2
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftPreserve(Reason = "test", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
            public int Version { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Event)]
        public sealed class EventTestSpec2
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftPreserve(Reason = "test", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
            public int Version { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Form)]
        public sealed class FormTestSpec2
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftPreserve(Reason = "test", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
            public int Version { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.HumanTask)]
        public sealed class HumanTaskTestSpec2
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftPreserve(Reason = "test", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
            public int Version { get; init; }
        }

        [AgentDraftContractSpec(Kind = DescriptorKind.Workflow)]
        public sealed class WorkflowTestSpec2
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftPreserve(Reason = "test", CreateStrategy = PreserveCreateStrategy.CreateDefault)]
            public int Version { get; init; }
        }
        """;

    [Fact]
    public void Missing_Kind_Triggers_ADP001()
    {
        var (_, diagnostics) = RunGenerator(MissingSchemaKindSource);

        // Schema kind is missing — ADP001 should fire
        diagnostics.Should().Contain(d =>
            d.Id == "ADP001" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("Schema"));

        // Other kinds should NOT have ADP001
        diagnostics.Where(d => d.Id == "ADP001").Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────
    // Test 10: ADP002 closure validation — descriptor property not in spec
    // ──────────────────────────────────────────────────────────────

    private const string MissingDescriptorPropertySource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Event.Abstractions;
        using CrestCreates.Schema.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        [AgentDraftContractSpec(Kind = DescriptorKind.Event)]
        public sealed class IncompleteEventSpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }

            [AgentDraftField]
            public EventCategory Category { get; init; }
        }
        """;

    [Fact]
    public void Descriptor_Property_Not_In_Spec_Triggers_ADP002()
    {
        var (_, diagnostics) = RunGenerator(MissingDescriptorPropertySource);

        // The real EventDescriptor has many more properties than the spec classifies.
        // Closure validation should emit ADP002 for each unclassified descriptor property.
        var adp002Diags = diagnostics.Where(d => d.Id == "ADP002").ToList();

        adp002Diags.Should().NotBeEmpty("descriptor properties missing from spec should trigger ADP002");

        // Should include at least one known descriptor property like "Importance" or "Version"
        adp002Diags.Should().Contain(d => d.GetMessage().Contains("Version"),
            "Version is on EventDescriptor but not classified in the incomplete spec");

        // The spec-level ADP002 for UnclassifiedProp should NOT be present (we don't have one)
        adp002Diags.Should().NotContain(d => d.GetMessage().Contains("UnclassifiedProp"));
    }

    // ──────────────────────────────────────────────────────────────
    // Test 11: ADP010 — spec property not on descriptor type
    // ──────────────────────────────────────────────────────────────

    private const string SpecPropertyNotOnDescriptorSource = """
        using CrestCreates.Metadata.Abstractions;
        using CrestCreates.Event.Abstractions;
        using CrestCreates.Schema.Abstractions;
        using CrestCreates.Agent.DraftContracts.Specs;

        namespace TestSpecs;

        [AgentDraftContractSpec(Kind = DescriptorKind.Event)]
        public sealed class ExtraPropertySpec
        {
            [AgentDraftField]
            public string Name { get; init; } = string.Empty;

            [AgentDraftField]
            public DescriptorState State { get; init; }

            // This property exists on the spec but NOT on EventDescriptor
            [AgentDraftField]
            public string NotARealDescriptorProperty { get; init; } = string.Empty;
        }
        """;

    [Fact]
    public void Spec_Property_Not_On_Descriptor_Triggers_ADP010()
    {
        var (_, diagnostics) = RunGenerator(SpecPropertyNotOnDescriptorSource);

        // ADP010 should fire for NotARealDescriptorProperty which is classified
        // in the spec but doesn't exist on EventDescriptor
        diagnostics.Should().Contain(d =>
            d.Id == "ADP010" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("NotARealDescriptorProperty"));
    }

    // ──────────────────────────────────────────────────────────────
    // Utility
    // ──────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
