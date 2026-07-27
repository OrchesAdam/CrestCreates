using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

public sealed class ToolSpecSurfaceTests : JsonContractCompilationTestBase
{
    [Fact]
    public void AgentToolSpec_ProducesExactInputAndOutputRoots()
    {
        var result = Build(AgentSource("typeof(InputDto)", "typeof(OutputDto)"));

        result.Diagnostics.Should().BeEmpty();
        var context = result.Model!.Contexts.Single();
        context.SurfaceRoots.Select(root => root.FullMetadataName)
            .Should().BeEquivalentTo("global::InputDto", "global::OutputDto");
        context.BindingRoots.Select(root => root.FullMetadataName)
            .Should().BeEquivalentTo("global::InputDto", "global::OutputDto");
    }

    [Fact]
    public void McpToolSpec_ProducesExactInputAndOutputRoots()
    {
        var result = Build(McpSource());

        result.Diagnostics.Should().BeEmpty();
        result.Model!.Contexts.Single().BindingRoots.Select(root => root.FullMetadataName)
            .Should().BeEquivalentTo("global::InputDto", "global::OutputDto");
    }

    [Fact]
    public void SharedRoots_AreDeduplicated_AndRetainAllSpecProvenance()
    {
        var result = Build(McpSource(includeSharedSpec: true));

        var roots = result.Model!.Contexts.Single().BindingRoots;
        roots.Should().HaveCount(2);
        roots.Single(root => root.FullMetadataName == "global::InputDto")
            .Provenance.Declarations.Should().HaveCount(2);
    }

    [Fact]
    public void NestedMemberTypes_AreNotBindingRoots()
    {
        var result = Build(AgentSource("typeof(InputDto)", "typeof(OutputDto)",
            "public sealed record NestedDto(string Value); public sealed record InputDto(NestedDto Nested);"));

        result.Model!.Contexts.Single().BindingRoots.Select(root => root.FullMetadataName)
            .Should().NotContain("global::NestedDto");
    }

    [Fact]
    public void MissingInputOrOutput_ContributesOnlyPresentRoot()
    {
        var result = Build(AgentSource("null", "typeof(OutputDto)"));

        result.Diagnostics.Should().BeEmpty();
        result.Model!.Contexts.Single().BindingRoots.Select(root => root.FullMetadataName)
            .Should().Equal("global::OutputDto");
    }

    [Fact]
    public void RemovingSpec_RemovesStaleRoot()
    {
        var before = Build(AgentSource("typeof(InputDto)", "typeof(OutputDto)",
            additionalDeclarations: "public sealed record RemovedDto(string Value);",
            additionalSpecDeclaration:
                "[CrestCreates.Agent.Tools.AgentToolSpec(\"removed\", InputType = typeof(RemovedDto))] public sealed class Removed { }"));
        var after = Build(AgentSource("typeof(InputDto)", "typeof(OutputDto)"));

        before.Model!.Contexts.Single().BindingRoots.Select(root => root.FullMetadataName)
            .Should().Contain("global::RemovedDto");
        after.Model!.Contexts.Single().BindingRoots.Select(root => root.FullMetadataName)
            .Should().NotContain("global::RemovedDto");
        JsonContractSourceWriter.WriteContextSource(after.Model).Should().NotContain("RemovedDto");
    }

    [Fact]
    public void GeneratedBindingManifest_IsOrdinalStable()
    {
        var result = Build(AgentSource("typeof(OutputDto)", "typeof(InputDto)"));

        result.Model!.Contexts.Single().BindingRoots.Select(root => root.FullMetadataName)
            .Should().Equal("global::InputDto", "global::OutputDto");
    }

    [Fact]
    public void MixedInterfaceAndToolSpecSurfaces_KeepBindingRoleExact()
    {
        var result = Build(AgentSource("typeof(InputDto)", "typeof(OutputDto)", additionalContextAttribute:
            "[JsonContractSurface(typeof(IService))]", additionalDeclarations:
            "public interface IService { System.Threading.Tasks.Task<InputDto> GetAsync(); System.Threading.Tasks.Task<InterfaceDto> OtherAsync(); } public sealed record InterfaceDto(int Value);"));

        var context = result.Model!.Contexts.Single();
        context.SurfaceRoots.Select(root => root.FullMetadataName).Should().Contain("global::InterfaceDto");
        context.BindingRoots.Select(root => root.FullMetadataName).Should().NotContain("global::InterfaceDto");
        context.BindingRoots.Single(root => root.FullMetadataName == "global::InputDto")
            .Provenance.MethodSignatures.Should().BeEmpty("binding provenance must not absorb interface provenance");
    }

    [Fact]
    public void Fail_UnsupportedSurfaceAdapter()
    {
        var source = (Path: "Unsupported.cs", Text: """
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;
            public sealed class Unsupported { }
            [JsonContractSurface(typeof(Unsupported))]
            public partial class TestContext : JsonSerializerContext { }
            """);

        Build(source).Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == JsonContractDiagnosticIds.InvalidSurface);
    }

    [Fact]
    public void Fail_OpenGenericToolSpecRoot()
    {
        Build(AgentSource("typeof(GenericDto<>)", "typeof(OutputDto)",
                "public sealed class GenericDto<T> { }"))
            .Diagnostics.Should().Contain(diagnostic => diagnostic.Id == JsonContractDiagnosticIds.InvalidRoot);
    }

    [Fact]
    public void Fail_UnresolvedToolSpecRoot()
    {
        Build(AgentSource("typeof(MissingDto)", "typeof(OutputDto)"))
            .Diagnostics.Should().Contain(diagnostic => diagnostic.Id == JsonContractDiagnosticIds.UnresolvedPreCoreCompileRoot);
    }

    private static JsonContractTestCompilation Build((string Path, string Text) source)
        => JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());

    private static (string Path, string Text) AgentSource(
        string inputExpression,
        string outputExpression,
        string? additionalDeclarations = null,
        string? additionalContextAttribute = null,
        string? additionalSpecDeclaration = null)
        => ("AgentToolSpec.cs", $$"""
            using System;
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;

            namespace CrestCreates.Agent.Tools
            {
                [AttributeUsage(AttributeTargets.Class)] public sealed class AgentToolSpecsAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class)] public sealed class AgentToolSpecAttribute : Attribute
                {
                    public AgentToolSpecAttribute(string id) { }
                    public Type? InputType { get; set; }
                    public Type? OutputType { get; set; }
                }
            }

            public sealed record InputDto(string Value);
            public sealed record OutputDto(int Value);
            {{additionalDeclarations}}

            [CrestCreates.Agent.Tools.AgentToolSpecs]
            public static class AgentSpecs
            {
                [CrestCreates.Agent.Tools.AgentToolSpec("one", InputType = {{inputExpression}}, OutputType = {{outputExpression}})]
                public sealed class One { }
                {{additionalSpecDeclaration}}
            }

            {{additionalContextAttribute}}
            [JsonContractSurface(typeof(AgentSpecs))]
            public partial class TestContext : JsonSerializerContext { }
            """);

    private static (string Path, string Text) McpSource(bool includeSharedSpec = false)
        => ("McpToolSpec.cs", $$"""
            using System;
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;

            namespace CrestCreates.Mcp
            {
                [AttributeUsage(AttributeTargets.Class)] public sealed class McpToolSpecsAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class)] public sealed class McpToolSpecAttribute : Attribute
                {
                    public McpToolSpecAttribute(string id) { }
                    public Type? InputType { get; set; }
                    public Type? OutputType { get; set; }
                }
            }

            public sealed record InputDto(string Value);
            public sealed record OutputDto(int Value);

            [CrestCreates.Mcp.McpToolSpecs]
            public static class McpSpecs
            {
                [CrestCreates.Mcp.McpToolSpec("one", InputType = typeof(InputDto), OutputType = typeof(OutputDto))]
                public sealed class One { }
                {{(includeSharedSpec ? "[CrestCreates.Mcp.McpToolSpec(\"two\", InputType = typeof(InputDto), OutputType = typeof(OutputDto))] public sealed class Two { }" : string.Empty)}}
            }

            [JsonContractSurface(typeof(McpSpecs))]
            public partial class TestContext : JsonSerializerContext { }
            """);
}
