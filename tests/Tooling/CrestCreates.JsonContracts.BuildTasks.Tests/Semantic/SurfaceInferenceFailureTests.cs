using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: F01-F06</summary>
public class SurfaceInferenceFailureTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Fail_GenericMethodOnSurface()
    {
        var source = (Path: "GenericMethod.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface IGenericService
{
    Task<T> GetAsync<T>(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IGenericService))]
public partial class GenericMethodContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.GenericMethod);
    }

    [Fact]
    public void Fail_OpenGenericRootType()
    {
        var source = (Path: "OpenGenericRoot.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IOpenGenericService
{
    Task<List<object>> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IOpenGenericService))]
public partial class OpenGenericRootContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain(n => n.Contains("List"));
    }

    [Fact]
    public void Fail_UnresolvedPreCoreCompileRoot()
    {
        var source = JsonContractTestSources.SameProjectUnresolvedType();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.UnresolvedPreCoreCompileRoot);
    }
}
