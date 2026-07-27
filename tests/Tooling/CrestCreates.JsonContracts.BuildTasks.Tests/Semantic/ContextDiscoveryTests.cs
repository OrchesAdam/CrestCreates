using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: B11, C02, F01, F02, F11</summary>
public class ContextDiscoveryTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Discover_NoMarkedContextProducesEmptyGenerationModel()
    {
        var source = JsonContractTestSources.NoMarkedContext();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        result.Model!.Contexts.Should().BeEmpty();
    }

    [Fact]
    public void Discover_MultipleContextsAreIsolated()
    {
        var source = JsonContractTestSources.MultipleContextProject();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Model!.Contexts.Should().HaveCount(2);
        result.Diagnostics.Should().BeEmpty();

        var names = result.Model.Contexts.Select(c => c.ContextSimpleName).ToList();
        names.Should().Contain(["FirstContext", "SecondContext"]);
    }

    [Fact]
    public void Discover_ContextsAreOrdinalSorted()
    {
        var source = JsonContractTestSources.MultipleContextProject();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        var names = result.Model!.Contexts.Select(c => c.FullMetadataName).ToList();
        names.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Fail_NonPartialContext()
    {
        var source = JsonContractTestSources.InvalidContext();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidContext,
            contextMetadataName: "global::NonPartialContext");

        result.Model!.Contexts.Should().BeEmpty();
    }

    [Fact]
    public void Fail_NonJsonSerializerContext()
    {
        var source = (Path: "NotAContext.cs", Text: """
            using CrestCreates.Core.Abstractions.Serialization;

            public interface IService { }

            [JsonContractSurface(typeof(IService))]
            public partial class NotAJsonSerializerContext { }
            """);
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidContext,
            contextMetadataName: "global::NotAJsonSerializerContext");
        result.Model!.Contexts.Should().BeEmpty();
    }

    [Fact]
    public void Fail_NestedOrGenericContext()
    {
        var source = JsonContractTestSources.InvalidContext();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidContext,
            contextMetadataName: "global::OuterClass.NestedContext");

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidContext,
            contextMetadataName: "global::GenericContext<T>");
    }

    [Fact]
    public void Fail_NonPartialOrNestedContext()
    {
        var source = JsonContractTestSources.InvalidContext();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        var invalidContextDiagnostics = result.Diagnostics
            .Where(d => d.Id == JsonContractDiagnosticIds.InvalidContext)
            .ToList();

        invalidContextDiagnostics.Should().HaveCount(3);
        invalidContextDiagnostics.Should().OnlyContain(d => d.Severity == JsonContractDiagnosticSeverity.Error);
    }

    [Fact]
    public void Fail_NonInterfaceOrUnboundSurface()
    {
        var source = JsonContractTestSources.NonInterfaceSurface();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidSurface,
            contextMetadataName: "global::NonInterfaceContext");
    }

    [Fact]
    public void Fail_UnresolvedMarkerOrStjIdentity()
    {
        var compilation = CreateCompilation(
            "TestAssembly",
            [("Minimal.cs", "public class Dummy {}")],
            referencePaths: []);

        var builder = new JsonContractSurfaceModelBuilder();
        var model = builder.Build(compilation);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            model.Diagnostics,
            JsonContractDiagnosticIds.RequiredSymbolUnresolved);

        model.Contexts.Should().BeEmpty();
    }
}
