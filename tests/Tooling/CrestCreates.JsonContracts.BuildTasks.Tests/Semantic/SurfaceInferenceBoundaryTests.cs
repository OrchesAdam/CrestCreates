using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: B01-B13</summary>
public class SurfaceInferenceBoundaryTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Build_SkipsCancellationTokenParameter()
    {
        var source = (Path: "CtSkip.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading;
using System.Threading.Tasks;

public interface ICtService
{
    Task<string> GetAsync(CancellationToken ct);
}

[JsonContractSurface(typeof(ICtService))]
public partial class CtContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::System.String");
        rootNames.Should().NotContain(n => n.Contains("CancellationToken"));
    }

    [Fact]
    public void Build_NullableReferenceTypeNormalization()
    {
        var source = (Path: "NullableNorm.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public record Dto(string Value);

public interface INullableService
{
    Task<Dto?> GetAsync(System.Threading.CancellationToken ct);
    Task UpdateAsync(Dto input, System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(INullableService))]
public partial class NullableContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::Dto");
        rootNames.Count.Should().Be(1);
    }

    [Fact]
    public void Build_DiamondInheritanceNoDuplicateRoots()
    {
        var source = JsonContractTestSources.DiamondSurface();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Count(n => n == "global::System.String").Should().Be(1);
    }

    [Fact]
    public void Build_SameRootFromParamAndReturn()
    {
        var source = JsonContractTestSources.MultipleParameterSurface();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::RequestDto");
        rootNames.Should().Contain("global::ResultDto");
    }
}
