using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: C01-C04</summary>
public class SurfaceCompositionTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Build_MultipleSurfaceAttributesProduceMergedRoots()
    {
        var source = JsonContractTestSources.ExplicitDuplicateSurface();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::SharedDto");
        rootNames.Count(n => n == "global::SharedDto").Should().Be(1);
    }

    [Fact]
    public void Build_RootsAreOrdinalSorted()
    {
        var source = JsonContractTestSources.ExplicitDuplicateSurface();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }
}
