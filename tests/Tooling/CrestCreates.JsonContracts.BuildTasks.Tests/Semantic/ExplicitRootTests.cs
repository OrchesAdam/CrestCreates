using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: B10, C03</summary>
public class ExplicitRootTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Build_DoesNotDuplicateExplicitJsonSerializableRoots()
    {
        var source = (Path: "ExplicitDup.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public record SharedDto(string Value);

public interface ISharedService
{
    Task<SharedDto> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(ISharedService))]
[JsonContractExplicitRoot(typeof(SharedDto))]
public partial class ExplicitDupContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var ctx = result.Model!.Contexts[0];

        var allNames = ctx.AllDirectRoots.Select(r => r.FullMetadataName).ToList();
        allNames.Count(n => n == "global::SharedDto").Should().Be(1);

        ctx.SurfaceRoots.Should().Contain(r => r.FullMetadataName == "global::SharedDto");
        ctx.ExplicitRoots.Should().Contain(r => r.FullMetadataName == "global::SharedDto");
    }

    [Fact]
    public void Build_DoesNotDuplicateExplicitExtras()
    {
        var source = (Path: "ExplicitExtra.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public record SurfaceDto(string Value);
public record ExtraDto(int Count);

public interface ISurfaceService
{
    Task<SurfaceDto> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(ISurfaceService))]
[JsonContractExplicitRoot(typeof(ExtraDto))]
public partial class ExplicitExtraContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var ctx = result.Model!.Contexts[0];

        ctx.SurfaceRoots.Select(r => r.FullMetadataName).Should().Contain("global::SurfaceDto");
        ctx.SurfaceRoots.Select(r => r.FullMetadataName).Should().NotContain("global::ExtraDto");

        ctx.ExplicitRoots.Select(r => r.FullMetadataName).Should().Contain("global::ExtraDto");
        ctx.ExplicitRoots.Select(r => r.FullMetadataName).Should().NotContain("global::SurfaceDto");

        ctx.AllDirectRoots.Select(r => r.FullMetadataName).Should().Contain(["global::ExtraDto", "global::SurfaceDto"]);
    }

    [Fact]
    public void Build_ExplicitRootStillAppearsInManifestUnion()
    {
        var source = (Path: "ExplicitUnion.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public record DtoA(string Value);
public record DtoB(int Count);

public interface IUnionService
{
    Task<DtoA> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IUnionService))]
[JsonContractExplicitRoot(typeof(DtoB))]
public partial class UnionContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var allNames = result.Model!.Contexts[0].AllDirectRoots.Select(r => r.FullMetadataName).ToList();
        allNames.Should().Contain(["global::DtoA", "global::DtoB"]);
    }

    [Fact]
    public void AllDirectRoots_EqualSurfaceUnionExplicit()
    {
        var source = (Path: "AllDirectRoots.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public record Root1(string A);
public record Root2(int B);
public record Root3(double C);

public interface IAllRootsService
{
    Task<Root1> GetAsync(Root2 input, System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IAllRootsService))]
[JsonContractExplicitRoot(typeof(Root3))]
public partial class AllRootsContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var ctx = result.Model!.Contexts[0];

        var surfaceNames = ctx.SurfaceRoots.Select(r => r.FullMetadataName).ToHashSet();
        var explicitNames = ctx.ExplicitRoots.Select(r => r.FullMetadataName).ToHashSet();
        var allNames = ctx.AllDirectRoots.Select(r => r.FullMetadataName).ToHashSet();

        allNames.Should().Equal(surfaceNames.Union(explicitNames).OrderBy(n => n, StringComparer.Ordinal));
    }
}
